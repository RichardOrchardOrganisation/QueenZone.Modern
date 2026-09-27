import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { ApiError, fetchJson, sendJson, sendMultipart } from '../src/api/client.ts';
import {
  fetchArticleDetail,
  fetchArticlesPage,
  fetchFanPerformancesPage,
  fetchNewsDetail,
  fetchNewsPage,
  fetchPhotoCategoryItems,
  fetchPhotoDetail,
} from '../src/api/content.ts';
import { fetchSearchPage } from '../src/api/search.ts';
import {
  createForumReply,
  fetchForumTopic,
  fetchForumTopicPoll,
  fetchForumTopicPosts,
  fetchForumTopicWatch,
  unwatchForumTopic,
  voteForumTopicPoll,
  watchForumTopic,
} from '../src/api/forum.ts';
import { parseMemberProfile } from '../src/api/me.ts';
import {
  fetchNotificationPreferences,
  patchNotificationPreferences,
} from '../src/api/notificationPreferences.ts';
import {
  composeMessage,
  fetchConversation,
  fetchInbox,
} from '../src/api/messages.ts';
import { parseNewsSuggestionCreated, newsSuggestionsPath } from '../src/api/newsSuggestionResponse.ts';
import { parsePhotoSubmissionCreated, photoSubmissionsPath } from '../src/api/photoSubmissionForm.ts';
import {
  conversationDetailSchema,
  expectedField,
  expectedStatus,
  fanPerformanceSchema,
  forumPollSchema,
  forumPostCreatedSchema,
  forumPostSchema,
  forumTopicDetailSchema,
  forumTopicWatchSchema,
  inboxConversationSchema,
  memberProfileSchema,
  articleDetailSchema,
  articleListItemSchema,
  newsDetailSchema,
  newsListItemSchema,
  newsSuggestionCreatedSchema,
  notificationPreferencesSchema,
  pagedSchema,
  parseContract,
  photoDetailSchema,
  photoListItemSchema,
  photoSubmissionCreatedSchema,
  problemDetailsSchema,
  searchResultSchema,
} from '../src/api/schemas.ts';
import { isPhotoCdnUrl } from '../src/screens/photos/photoGalleryMeta.ts';
import { resolveMediaUrl } from '../src/api/submissions.ts';
import { loadContractFixture, pngPixel } from './host.ts';

const fixture = loadContractFixture();
const token = fixture.member.accessToken;

async function expectApiError(
  endpoint: string,
  expected: number,
  run: () => Promise<unknown>,
): Promise<ApiError> {
  try {
    await run();
  } catch (err) {
    if (err instanceof ApiError) {
      expectedStatus(endpoint, err.status, expected);
      if (err.problem) {
        parseContract(endpoint, problemDetailsSchema, err.problem);
      }
      return err;
    }
    throw err;
  }

  throw new Error(`Contract ${endpoint} failed: expected status ${expected}, but the request succeeded.`);
}

describe('mobile API consumer contracts', { concurrency: false }, () => {
  it('reads a paged news list and a published news detail', async () => {
    const page = parseContract(
      'GET /api/v1/content/news',
      pagedSchema(newsListItemSchema),
      await fetchNewsPage({ page: 1, pageSize: 5 }),
    );
    expectedField('GET /api/v1/content/news', 'items', page.items, Array.isArray, 'a non-empty array');
    assert.ok(page.items.length > 0, 'Contract GET /api/v1/content/news failed: expected field items to be non-empty');
    assert.equal(page.page, 1);
    assert.equal(page.pageSize, 5);
    assert.ok(page.totalCount >= page.items.length);
    assert.ok(page.totalPages >= 1);

    const detail = parseContract(
      'GET /api/v1/content/news/1003',
      newsDetailSchema,
      await fetchNewsDetail(1003),
    );
    assert.equal(detail.id, 1003);
    assert.match(detail.body, /ugc\/news\//);
    const relativeMedia = resolveMediaUrl(fixture.baseUrl, '/ugc/news/sample-crest.jpg');
    assert.equal(relativeMedia, `${fixture.baseUrl}/ugc/news/sample-crest.jpg`);
  });

  it('reads a paged articles list and a published article detail', async () => {
    const page = parseContract(
      'GET /api/v1/content/articles',
      pagedSchema(articleListItemSchema),
      await fetchArticlesPage({ page: 1, pageSize: 20 }),
    );
    expectedField('GET /api/v1/content/articles', 'items', page.items, Array.isArray, 'a non-empty array');
    assert.ok(page.items.length > 0, 'Contract GET /api/v1/content/articles failed: expected field items to be non-empty');
    assert.equal(page.page, 1);
    assert.equal(page.pageSize, 20);
    assert.ok(page.totalCount >= page.items.length);
    assert.ok(page.totalPages >= 1);
    assert.equal(page.items[0]?.id, 101);
    assert.match(page.items[0]?.detailPath ?? '', /^\/articles\/101\//);
    assert.ok(!page.items.some((item) => item.id === 9001));

    const detail = parseContract(
      'GET /api/v1/content/articles/101',
      articleDetailSchema,
      await fetchArticleDetail(101),
    );
    assert.equal(detail.id, 101);
    assert.equal(detail.title, 'Inside the Making of Bohemian Rhapsody');
    assert.match(detail.body, /studios/i);
  });

  it('searches the shared SearchDocument index', async () => {
    const page = parseContract(
      'GET /api/v1/search',
      pagedSchema(searchResultSchema),
      await fetchSearchPage({ q: 'modernisation', page: 1, pageSize: 20 }),
    );
    expectedField('GET /api/v1/search', 'items', page.items, Array.isArray, 'an array');
    assert.equal(page.page, 1);
    assert.equal(page.pageSize, 20);
    const news = page.items.find((item) => item.sourceKey === 'news:1003');
    assert.ok(news, 'Contract GET /api/v1/search failed: expected a news:1003 hit for modernisation');
    assert.equal(news.id, 1003);
    assert.equal(news.contentType, 'news');
  });

  it('reads photo category items at pageSize 24 with CDN media URLs', async () => {
    const page = parseContract(
      'GET /api/v1/content/photos/categories/brian-may/items',
      pagedSchema(photoListItemSchema),
      await fetchPhotoCategoryItems('brian-may'),
    );
    assert.equal(page.pageSize, 24, 'Contract GET /api/v1/content/photos/categories/brian-may/items failed: expected field pageSize to be 24');
    assert.ok(page.items.length > 0);
    assert.ok(
      isPhotoCdnUrl(page.items[0]?.thumbnailUrl),
      `Contract GET /api/v1/content/photos/categories/brian-may/items failed: expected field thumbnailUrl to be a cdn.queenzone.org URL, received ${page.items[0]?.thumbnailUrl}`,
    );

    const photo = parseContract(
      'GET /api/v1/content/photos/categories/brian-may/items/101',
      photoDetailSchema,
      await fetchPhotoDetail('brian-may', 101),
    );
    assert.ok(
      isPhotoCdnUrl(photo.imageUrl),
      `Contract GET /api/v1/content/photos/categories/brian-may/items/101 failed: expected field imageUrl to be a cdn.queenzone.org URL, received ${photo.imageUrl}`,
    );
    assert.equal(resolveMediaUrl(fixture.baseUrl, photo.imageUrl), photo.imageUrl);
  });

  it('reads a forum topic with posts and a fan-performances list including empty pages', async () => {
    const topic = parseContract(
      'GET /api/v1/forum/topics/1002',
      forumTopicDetailSchema,
      await fetchForumTopic(1002),
    );
    assert.equal(topic.id, 1002);

    const watch = parseContract(
      'GET /api/v1/forum/topics/1002/watch',
      forumTopicWatchSchema,
      await fetchForumTopicWatch(1002, token),
    );
    assert.equal(watch.watching, false);
    const watched = parseContract(
      'POST /api/v1/forum/topics/1002/watch',
      forumTopicWatchSchema,
      await watchForumTopic(1002, token),
    );
    assert.equal(watched.watching, true);
    const unwatched = parseContract(
      'DELETE /api/v1/forum/topics/1002/watch',
      forumTopicWatchSchema,
      await unwatchForumTopic(1002, token),
    );
    assert.equal(unwatched.watching, false);

    const posts = parseContract(
      'GET /api/v1/forum/topics/1002/posts',
      pagedSchema(forumPostSchema),
      await fetchForumTopicPosts(1002),
    );
    assert.ok(posts.items.length > 0, 'Contract GET /api/v1/forum/topics/1002/posts failed: expected field items to be non-empty');
    assert.equal(posts.pageSize, 15, 'forum post default must match forumPostsPageSize');
    const withAttachment = posts.items.find((item) => item.attachments.length > 0);
    assert.ok(withAttachment, 'Contract GET /api/v1/forum/topics/1002/posts failed: expected an attachment');
    assert.match(withAttachment.attachments[0].url, /^\/forum\/attachment\//);
    assert.match(withAttachment.attachments[0].downloadUrl, /^\/api\/v1\/forum\/attachments\//);
    assert.notEqual(withAttachment.attachments[0].url, withAttachment.attachments[0].downloadUrl);

    const denied = await fetch(`${fixture.baseUrl}/api/v1/forum/attachments/legacy/1002`, {
      redirect: 'manual',
    });
    expectedStatus('GET /api/v1/forum/attachments/legacy/1002', denied.status, 401);

    const allowed = await fetch(`${fixture.baseUrl}/api/v1/forum/attachments/legacy/1002`, {
      headers: { Authorization: `Bearer ${token}` },
      redirect: 'manual',
    });
    assert.equal(
      allowed.status,
      200,
      `Contract GET /api/v1/forum/attachments/legacy/1002 failed: expected a streamed download for a signed-in member, received ${allowed.status}`,
    );
    assert.equal(allowed.headers.get('location'), null);
    const disposition = allowed.headers.get('content-disposition') ?? '';
    assert.match(disposition, /attachment/i);
    assert.match(disposition, /anoto-setlist-scan\.jpg/);
    const bytes = Buffer.from(await allowed.arrayBuffer());
    assert.equal(bytes.toString('utf8'), 'scan-bytes');

    const performances = parseContract(
      'GET /api/v1/content/fan-performances',
      pagedSchema(fanPerformanceSchema),
      await fetchFanPerformancesPage(),
    );
    assert.ok(performances.items.length > 0);
    assert.ok(
      performances.items[0]?.audioPath.startsWith('/api/v1/content/fan-performances/'),
      `Contract GET /api/v1/content/fan-performances failed: expected field audioPath to be app-relative, received ${performances.items[0]?.audioPath}`,
    );
    assert.equal(
      resolveMediaUrl(fixture.baseUrl, performances.items[0]!.audioPath),
      `${fixture.baseUrl}${performances.items[0]!.audioPath}`,
    );

    const empty = parseContract(
      'GET /api/v1/content/fan-performances?page=9',
      pagedSchema(fanPerformanceSchema),
      await fetchFanPerformancesPage({ page: 9, pageSize: 5 }),
    );
    assert.equal(empty.items.length, 0, 'Contract GET /api/v1/content/fan-performances?page=9 failed: expected field items to be an empty array');
    assert.equal(empty.page, 9);
    assert.equal(empty.pageSize, 5);
    assert.ok(empty.totalCount > 0);
    assert.ok(empty.totalPages >= 1);
  });

  it('reads /me and an empty-then-populated messages inbox/thread', async () => {
    const rawProfile = await fetchJson('/me', { accessToken: token });
    parseContract('GET /api/v1/me', memberProfileSchema, rawProfile);
    const profile = parseMemberProfile(rawProfile);
    assert.equal(profile.memberId, fixture.member.id);
    assert.equal(profile.displayName, fixture.member.displayName);

    const preferences = parseContract(
      'GET /api/v1/me/notification-preferences',
      notificationPreferencesSchema,
      await fetchNotificationPreferences(token),
    );
    assert.equal(typeof preferences.forumReply, 'boolean');
    assert.equal(typeof preferences.privateMessage, 'boolean');
    assert.equal(typeof preferences.news, 'boolean');

    const patched = parseContract(
      'PATCH /api/v1/me/notification-preferences',
      notificationPreferencesSchema,
      await patchNotificationPreferences(token, { news: !preferences.news }),
    );
    assert.equal(patched.news, !preferences.news);
    assert.equal(patched.forumReply, preferences.forumReply);
    assert.equal(patched.privateMessage, preferences.privateMessage);
    await patchNotificationPreferences(token, { news: preferences.news });

    const seededInbox = parseContract(
      'GET /api/v1/me/messages',
      pagedSchema(inboxConversationSchema),
      await fetchInbox(token),
    );
    assert.equal(seededInbox.pageSize, 50, 'inbox default must match inboxPageSize');
    assert.ok(
      seededInbox.items.length >= 1,
      'Contract GET /api/v1/me/messages failed: expected the seeded unread conversation before compose',
    );
    assert.ok(
      seededInbox.items.some((item) => item.otherParticipantDisplayName === fixture.otherMember.displayName),
      'Contract GET /api/v1/me/messages failed: expected a conversation with Contract Other',
    );

    const thread = parseContract(
      'POST /api/v1/me/messages',
      conversationDetailSchema,
      await composeMessage(token, fixture.otherMember.id, 'Hello from the consumer-contract suite.'),
    );
    assert.equal(thread.otherParticipantId, fixture.otherMember.id);
    assert.ok(thread.messages.length > 0);

    const inbox = parseContract(
      'GET /api/v1/me/messages',
      pagedSchema(inboxConversationSchema),
      await fetchInbox(token),
    );
    assert.equal(inbox.pageSize, 50, 'inbox default must match inboxPageSize');
    assert.ok(inbox.items.length > 0, 'Contract GET /api/v1/me/messages failed: expected field items to be non-empty after compose');

    const opened = parseContract(
      `GET /api/v1/me/messages/${thread.conversationId}`,
      conversationDetailSchema,
      await fetchConversation(token, thread.conversationId),
    );
    assert.equal(opened.conversationId, thread.conversationId);
  });

  it('accepts a photo-submission 201 through sendMultipart', async () => {
    const form = new FormData();
    form.append('title', 'Contract Wembley shot');
    form.append('description', 'Seeded by the consumer-contract suite');
    form.append('photo', pngPixel(), 'photo.png');

    const created = parsePhotoSubmissionCreated(
      parseContract(
        'POST /api/v1/member/photo-submissions',
        photoSubmissionCreatedSchema,
        await sendMultipart(photoSubmissionsPath, form, { accessToken: token }),
      ),
    );
    assert.equal(created.title, 'Contract Wembley shot');
    assert.equal(created.status, 'Pending');
  });

  it('accepts a news-suggestion 201 through sendJson', async () => {
    const created = parseNewsSuggestionCreated(
      parseContract(
        'POST /api/v1/member/news-suggestions',
        newsSuggestionCreatedSchema,
        await sendJson(newsSuggestionsPath, {
          method: 'POST',
          body: {
            url: `https://www.bbc.co.uk/news/contract-${Date.now()}`,
            title: 'Contract Queen dates',
            notes: null,
          },
          accessToken: token,
        }),
      ),
    );
    assert.equal(created.status, 'Pending');
    assert.match(created.url, /^https:\/\//);
  });

  it('maps news-suggestion auth 401 through ApiError', async () => {
    const unauthorized = await expectApiError('POST /api/v1/member/news-suggestions', 401, () =>
      sendJson(newsSuggestionsPath, {
        method: 'POST',
        body: { url: 'https://www.bbc.co.uk/news/example' },
      }),
    );
    expectedField(
      'POST /api/v1/member/news-suggestions',
      'problem.title',
      unauthorized.problem?.title,
      (value) => value === 'Unauthorized',
      'Unauthorized',
    );
  });

  it('maps forum reply validation 400 and auth 401 through ApiError', async () => {
    const badReply = await expectApiError(
      'POST /api/v1/forum/topics/1002/posts',
      400,
      () =>
        sendJson('/forum/topics/1002/posts', {
          method: 'POST',
          body: { body: '<script>alert(1)</script>' },
          accessToken: token,
        }),
    );
    assert.match(
      badReply.message,
      /Body is required/i,
      `Contract POST /api/v1/forum/topics/1002/posts failed: expected Problem Details detail about the body, received ${badReply.message}`,
    );
    expectedField(
      'POST /api/v1/forum/topics/1002/posts',
      'problem.title',
      badReply.problem?.title,
      (value) => typeof value === 'string' && value.length > 0,
      'a non-empty RFC 7807 title',
    );

    const unauthorized = await expectApiError('GET /api/v1/me', 401, () => fetchJson('/me'));
    expectedField(
      'GET /api/v1/me',
      'problem.title',
      unauthorized.problem?.title,
      (value) => value === 'Unauthorized',
      'Unauthorized',
    );
  });

  it('maps unauthorized 401 for a suspended reply, not found 404, and poll conflict 409', async () => {
    const unauthorized = await expectApiError(
      'POST /api/v1/forum/topics/1002/posts',
      401,
      () => createForumReply(1002, { body: 'Suspended members cannot post.' }, fixture.suspendedMember.accessToken),
    );
    expectedField(
      'POST /api/v1/forum/topics/1002/posts',
      'problem.title',
      unauthorized.problem?.title,
      (value) => value === 'Unauthorized',
      'Unauthorized',
    );

    const missing = await expectApiError(
      'GET /api/v1/content/news/999999',
      404,
      () => fetchNewsDetail(999999),
    );
    expectedField(
      'GET /api/v1/content/news/999999',
      'problem.title',
      missing.problem?.title,
      (value) => typeof value === 'string' && /not found/i.test(value),
      'Not Found',
    );

    const poll = parseContract(
      `GET /api/v1/forum/topics/${fixture.pollTopicId}/poll`,
      forumPollSchema,
      await fetchForumTopicPoll(fixture.pollTopicId, token),
    );
    parseContract(
      `POST /api/v1/forum/topics/${fixture.pollTopicId}/poll/vote`,
      forumPollSchema,
      await voteForumTopicPoll(fixture.pollTopicId, [fixture.pollOptionId], token),
    );
    const conflict = await expectApiError(
      `POST /api/v1/forum/topics/${fixture.pollTopicId}/poll/vote`,
      409,
      () => voteForumTopicPoll(fixture.pollTopicId, [poll.options[0]!.optionId], token),
    );
    expectedField(
      `POST /api/v1/forum/topics/${fixture.pollTopicId}/poll/vote`,
      'problem.title',
      conflict.problem?.title,
      (value) => value === 'Conflict',
      'Conflict',
    );
  });

  it('maps forum write quota 429 Problem Details', async () => {
    let lastError: ApiError | null = null;
    for (let i = 0; i < 6; i += 1) {
      try {
        parseContract(
          'POST /api/v1/forum/topics/1002/posts',
          forumPostCreatedSchema,
          await createForumReply(1002, { body: `Contract rate-limit reply ${i}` }, token),
        );
      } catch (err) {
        if (err instanceof ApiError && err.status === 429) {
          lastError = err;
          break;
        }
        throw err;
      }
    }

    assert.ok(lastError, 'Contract POST /api/v1/forum/topics/1002/posts failed: expected status 429 after repeated replies');
    expectedStatus('POST /api/v1/forum/topics/1002/posts', lastError.status, 429);
    parseContract('POST /api/v1/forum/topics/1002/posts', problemDetailsSchema, lastError.problem);
    expectedField(
      'POST /api/v1/forum/topics/1002/posts',
      'problem.status',
      lastError.problem?.status,
      (value) => value === 429,
      '429',
    );
  });
});
