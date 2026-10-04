import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import {
  articleDetailSchema,
  crosswordDetailSchema,
  crosswordListItemSchema,
  crosswordCheckResultSchema,
  crosswordRevealResultSchema,
  crosswordProgressSchema,
  crosswordCompletionResultSchema,
  crosswordLeaderboardSchema,
  crosswordHistorySchema,
  articleListItemSchema,
  newsDetailSchema,
  newsListItemSchema,
  notificationPreferencesSchema,
  parseContract,
  photoNavSchema,
  searchResultSchema,
} from './schemas.ts';

describe('parseContract', () => {
  it('validates crossword play status, cell bounds, letters and completion review independently of future fields', () => {
    const playVersion = '11111111-2222-4333-8444-555555555555';
    const check = { playVersion, cells: [{ index: 0, status: 'correct' }], explanations: [], complete: false, futureField: true };
    assert.ok(crosswordCheckResultSchema.safeParse(check).success);
    assert.ok(!crosswordCheckResultSchema.safeParse({ ...check, cells: [{ index: 225, status: 'correct' }] }).success);
    assert.ok(!crosswordCheckResultSchema.safeParse({ ...check, cells: [{ index: 0, status: 'unknown' }] }).success);
    assert.ok(crosswordRevealResultSchema.safeParse({ playVersion, cells: [{ index: 0, letter: 'A' }], explanations: [], clean: false }).success);
    assert.ok(!crosswordRevealResultSchema.safeParse({ playVersion, cells: [{ index: 0, letter: 'AB' }], explanations: [], clean: false }).success);
    const progress = { playVersion, letters: 'BRIAN.#', elapsedSeconds: 12, revealedCells: [0], autoCheckUsed: true,
      updatedAt: '2026-10-04T00:00:00Z', startedAt: '2026-10-04T00:00:00Z' };
    assert.ok(crosswordProgressSchema.safeParse(progress).success);
    assert.ok(!crosswordProgressSchema.safeParse({ ...progress, letters: 'private?' }).success);
    assert.ok(!crosswordProgressSchema.safeParse({ ...progress, elapsedSeconds: -1 }).success);
    const completion = { playVersion: progress.playVersion, correct: true, completion: { elapsedSeconds: 120, clean: false, rankingEligible: false,
      completedAt: progress.updatedAt }, review: [{ number: 1, direction: 'across', answer: 'BRIAN', explanation: null }] };
    assert.ok(crosswordCompletionResultSchema.safeParse(completion).success);
    assert.ok(crosswordCompletionResultSchema.safeParse({ playVersion: progress.playVersion, correct: false, completion: null, review: [] }).success);
    assert.ok(!crosswordCompletionResultSchema.safeParse({ ...completion, review: [{ ...completion.review[0], answer: 'two words' }] }).success);
  });
  it('rejects malformed crossword coordinates and mismatched grid arrays', () => {
    const metadata = { id: '11111111-2222-4333-8444-555555555555', slug: 'a-puzzle', title: 'A puzzle',
      difficulty: 'easy', width: 5, height: 5 };
    assert.ok(crosswordListItemSchema.safeParse({ ...metadata, publishedAt: '2026-10-04T00:00:00Z' }).success);
    const detail = { ...metadata, description: '', style: 'british', archived: true,
      blocks: Array<boolean>(25).fill(false), numbering: Array<number>(25).fill(0),
      clues: [{ number: 1, direction: 'across', row: 0, column: 0, length: 5, clue: 'A clue', enumeration: '(5)' }] };
    assert.ok(crosswordDetailSchema.safeParse(detail).success);
    assert.ok(!crosswordDetailSchema.safeParse({ ...detail, blocks: [] }).success);
    assert.ok(!crosswordDetailSchema.safeParse({ ...detail, clues: [{ ...detail.clues[0], row: 15 }] }).success);
    assert.ok(!crosswordListItemSchema.safeParse({ ...metadata, difficulty: 'unknown', publishedAt: null }).success);
  });
  it('names the endpoint and missing field when a payload is incompatible', () => {
    assert.throws(
      () => parseContract('GET /api/v1/content/news', newsListItemSchema, { id: 1, excerpt: '', publishedAt: '2026-01-01', detailPath: '/news/1' }),
      /Contract GET \/api\/v1\/content\/news failed: title:/,
    );
  });

  it('accepts a complete article list item and detail', () => {
    const item = parseContract('GET /api/v1/content/articles', articleListItemSchema, {
      id: 101,
      title: 'Inside the Making of Bohemian Rhapsody',
      excerpt: 'Excerpt',
      publishedAt: '2024-03-12T00:00:00',
      detailPath: '/articles/101/inside-the-making-of-bohemian-rhapsody',
      categoryName: 'Recording',
    });
    assert.equal(item.id, 101);
    assert.equal(item.categoryName, 'Recording');

    const detail = parseContract('GET /api/v1/content/articles/101', articleDetailSchema, {
      id: 101,
      title: 'Inside the Making of Bohemian Rhapsody',
      excerpt: 'Excerpt',
      body: '<p>Body</p>',
      publishedAt: '2024-03-12T00:00:00',
      source: 'Queenzone archive',
      categoryName: 'Recording',
      detailPath: '/articles/101/inside-the-making-of-bohemian-rhapsody',
    });
    assert.equal(detail.source, 'Queenzone archive');
    assert.equal(detail.body, '<p>Body</p>');
  });

  it('accepts a complete news list item', () => {
    const item = parseContract('GET /api/v1/content/news', newsListItemSchema, {
      id: 1003,
      title: 'QueenZone modernisation begins',
      excerpt: 'Excerpt',
      publishedAt: '2026-06-11T09:00:00',
      detailPath: '/news/1003/queenzone-modernisation-begins',
    });
    assert.equal(item.id, 1003);
    assert.equal(item.imageUrl, undefined);
  });

  it('accepts optional news image urls', () => {
    const withImage = parseContract('GET /api/v1/content/news', newsListItemSchema, {
      id: 1003,
      title: 'QueenZone modernisation begins',
      excerpt: 'Excerpt',
      publishedAt: '2026-06-11T09:00:00',
      detailPath: '/news/1003/queenzone-modernisation-begins',
      imageUrl: '/ugc/articles/editors/me/hero.webp',
      thumbnailUrl: '/ugc/articles/editors/me/hero.webp?size=thumb',
    });
    assert.equal(withImage.imageUrl, '/ugc/articles/editors/me/hero.webp');
    assert.equal(withImage.thumbnailUrl, '/ugc/articles/editors/me/hero.webp?size=thumb');
    const withoutImage = parseContract('GET /api/v1/content/news', newsListItemSchema, {
      id: 1004,
      title: 'No photo',
      excerpt: 'Excerpt',
      publishedAt: '2026-06-11T09:00:00',
      detailPath: '/news/1004/no-photo',
      imageUrl: null,
      thumbnailUrl: null,
    });
    assert.equal(withoutImage.imageUrl, null);
    assert.equal(withoutImage.thumbnailUrl, null);
  });

  it('accepts optional news discussion fields on detail', () => {
    const withoutTopic = parseContract('GET /api/v1/content/news/1003', newsDetailSchema, {
      id: 1003,
      title: 'QueenZone modernisation begins',
      excerpt: 'Excerpt',
      body: '<p>Body</p>',
      publishedAt: '2026-06-11T09:00:00',
      sourceUrl: null,
      detailPath: '/news/1003/queenzone-modernisation-begins',
      topicId: null,
      discussionReplyCount: null,
      discussionPreview: null,
    });
    assert.equal(withoutTopic.topicId, null);

    const withPreview = parseContract('GET /api/v1/content/news/42', newsDetailSchema, {
      id: 42,
      title: 'Linked story',
      excerpt: 'Excerpt',
      body: '<p>Body</p>',
      publishedAt: '2026-08-01T08:00:00Z',
      sourceUrl: null,
      detailPath: '/news/42/linked-story',
      topicId: 1002,
      discussionReplyCount: 2,
      discussionPreview: [
        { authorDisplayName: 'Alice', postedAt: '2026-08-01T10:30:00Z', excerpt: 'First preview excerpt' },
        { authorDisplayName: 'Bob', postedAt: '2026-08-01T11:30:00Z', excerpt: 'Latest preview excerpt' },
      ],
    });
    assert.equal(withPreview.topicId, 1002);
    assert.equal(withPreview.discussionReplyCount, 2);
    assert.equal(withPreview.discussionPreview?.[1]?.authorDisplayName, 'Bob');
  });

  it('accepts a search hit with sourceKey and optional id', () => {
    const item = parseContract('GET /api/v1/search', searchResultSchema, {
      contentType: 'news',
      sourceKey: 'news:1003',
      title: 'QueenZone modernisation begins',
      summary: 'Excerpt',
      url: '/news/1003/queenzone-modernisation-begins',
      publishedAt: '2026-06-11T09:00:00Z',
      imageUrl: null,
      category: null,
      authorDisplayName: null,
      id: 1003,
    });
    assert.equal(item.sourceKey, 'news:1003');
    assert.equal(item.id, 1003);
  });

  it('accepts photo nav with and without prefetch fields', () => {
    const legacy = parseContract('GET /api/v1/content/photos/nav', photoNavSchema, {
      picId: 102,
      detailPath: '/photography/brian-may/102',
    });
    assert.equal(legacy.picId, 102);
    assert.equal(legacy.imageUrl, undefined);

    const withImage = parseContract('GET /api/v1/content/photos/nav', photoNavSchema, {
      picId: 102,
      detailPath: '/photography/brian-may/102',
      imageUrl: 'https://cdn.queenzone.org/brian-may/img-102.jpg',
      pictureWidth: 1600,
      pictureHeight: 1200,
    });
    assert.equal(withImage.imageUrl, 'https://cdn.queenzone.org/brian-may/img-102.jpg');
    assert.equal(withImage.pictureWidth, 1600);

    const nullImage = parseContract('GET /api/v1/content/photos/nav', photoNavSchema, {
      picId: 102,
      detailPath: '/photography/brian-may/102',
      imageUrl: null,
      pictureWidth: null,
      pictureHeight: null,
    });
    assert.equal(nullImage.imageUrl, null);
  });

  it('accepts notification preference toggles', () => {
    const prefs = parseContract('GET /api/v1/me/notification-preferences', notificationPreferencesSchema, {
      forumReply: true,
      privateMessage: true,
      news: false,
    });
    assert.equal(prefs.forumReply, true);
    assert.equal(prefs.news, false);
  });
});

it('validates results contracts and rejects private identity fields or more than 50 leaderboard rows', () => {
  const solve = { rank: 1, displayName: 'Queen fan', elapsedSeconds: 120, completedAt: '2026-10-04T00:00:00Z' };
  assert.ok(crosswordLeaderboardSchema.safeParse({ top: [solve], viewer: { ...solve, rank: 61 }, totalMembers: 61 }).success);
  assert.ok(!crosswordLeaderboardSchema.safeParse({ top: [{ ...solve, memberId: 'private' }], viewer: null, totalMembers: 1 }).success);
  assert.ok(!crosswordLeaderboardSchema.safeParse({ top: Array(51).fill(solve), viewer: null, totalMembers: 51 }).success);
  const item = { id: '11111111-2222-4333-8444-555555555555', slug: null, title: 'Unavailable puzzle', elapsedSeconds: 120, clean: false, completedAt: solve.completedAt, playable: false };
  assert.ok(crosswordHistorySchema.safeParse({ items: [item], totalCompleted: 1, weeklyStreak: 1, weekTimeZone: 'UTC' }).success);
  assert.ok(!crosswordHistorySchema.safeParse({ items: [{ ...item, email: 'private@example.test' }], totalCompleted: 1, weeklyStreak: 1, weekTimeZone: 'UTC' }).success);
});
