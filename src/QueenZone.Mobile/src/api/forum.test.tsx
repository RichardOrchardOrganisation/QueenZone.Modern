import {
  closeForumTopicPoll,
  createForumReply,
  createForumTopic,
  fetchForumCategories,
  fetchForumCategory,
  fetchForumCategoryTopics,
  fetchForumRecentThreads,
  fetchForumStats,
  fetchForumTopic,
  fetchForumTopicPoll,
  fetchForumTopicPosts,
  fetchForumTopicWatch,
  unwatchForumTopic,
  voteForumTopicPoll,
  watchForumTopic,
  blockForumPostAuthor,
  unblockForumPostAuthor,
} from './forum';
import {
  ContentCache,
  createMemoryStorage,
  forumTopicCacheKey,
  forumTopicPostsCacheKey,
  setContentCacheForTests,
} from '../cache';
import { jsonResponse } from '../test/fixtures';
import { reportApiFailure } from '../config/sentry';

jest.mock('../config', () => ({
  apiV1Url: (path: string) => `http://qz.test/api/v1${path.startsWith('/') ? path : `/${path}`}`,
}));

jest.mock('../config/sentry', () => ({
  reportApiFailure: jest.fn(),
}));

const fetchMock = jest.fn<Promise<Response>, [RequestInfo | URL, RequestInit?]>();
const reportApiFailureMock = reportApiFailure as jest.MockedFunction<typeof reportApiFailure>;

beforeEach(() => {
  fetchMock.mockReset();
  reportApiFailureMock.mockReset();
  global.fetch = fetchMock as unknown as typeof fetch;
});

afterEach(() => {
  setContentCacheForTests(null);
});

function lastCall() {
  const call = fetchMock.mock.calls.at(-1);
  if (!call) {
    throw new Error('fetch was not called');
  }
  return { url: String(call[0]), init: call[1] ?? {} };
}

describe('read endpoints', () => {
  it('builds recent-threads, category, and topic URLs', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse([]));
    await fetchForumRecentThreads(5);
    expect(lastCall().url).toBe('http://qz.test/api/v1/forum/recent-threads?count=5');

    const cache = new ContentCache({ storage: createMemoryStorage() });
    const cached = [{ id: 1, title: 'cached thread' }];
    await cache.put('home:forum-threads', cached);
    setContentCacheForTests(cache);
    fetchMock.mockResolvedValueOnce(jsonResponse([])); // background revalidation, result unused by this test
    const threads = await fetchForumRecentThreads(3, undefined, {
      cacheKey: 'home:forum-threads',
      ttlMs: 60_000,
    });
    expect(threads).toEqual(cached);
    setContentCacheForTests(null);

    fetchMock.mockResolvedValueOnce(jsonResponse({ boardCount: 6, threadCount: 12600, postCount: 1 }));
    await fetchForumStats();
    expect(lastCall().url).toBe('http://qz.test/api/v1/forum/stats');

    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [] }));
    await fetchForumCategories({ page: 2 });
    expect(lastCall().url).toBe('http://qz.test/api/v1/forum/categories?page=2');

    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 1 }));
    await fetchForumCategory(1);
    expect(lastCall().url).toBe('http://qz.test/api/v1/forum/categories/1');

    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [] }));
    await fetchForumCategoryTopics(1, { page: 3 });
    expect(lastCall().url).toBe('http://qz.test/api/v1/forum/categories/1/topics?page=3');

    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 10 }));
    await fetchForumTopic(10);
    expect(lastCall().url).toBe('http://qz.test/api/v1/forum/topics/10');

    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [] }));
    await fetchForumTopicPosts(10, { page: 2 });
    expect(lastCall().url).toBe('http://qz.test/api/v1/forum/topics/10/posts?page=2');
  });

  it('caches topic headers and opened post pages under public keys', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    fetchMock.mockResolvedValueOnce(
      jsonResponse({ id: 10, title: 'Hello', forumId: 1, forumName: 'The Music', postCount: 2, hasPoll: false, isLocked: false }),
    );
    await fetchForumTopic(10, undefined, { cache });
    expect(await cache.get(forumTopicCacheKey(10))).toMatchObject({ id: 10, title: 'Hello' });

    const page1 = { items: [{ id: 1 }, { id: 2 }], page: 1, pageSize: 15, totalCount: 2, totalPages: 1 };
    fetchMock.mockResolvedValueOnce(jsonResponse(page1));
    await fetchForumTopicPosts(10, { page: 1, cache });
    expect(await cache.get(forumTopicPostsCacheKey(10, 1))).toMatchObject(page1);
  });
});

function pdfBlobResponse() {
  return new Response(new Uint8Array([0x25, 0x50, 0x44, 0x46]), {
    status: 200,
    headers: { 'Content-Type': 'application/pdf' },
  });
}

describe('createForumTopic and createForumReply', () => {
  it('POSTs JSON title/body when no file is attached', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 1, starterPostId: 2, title: 'Hi', detailPath: '/t/1' }));
    await createForumTopic(4, { title: 'Hi', body: 'Hello' }, 'tok');
    const { url, init } = lastCall();
    expect(url).toBe('http://qz.test/api/v1/forum/categories/4/topics');
    expect(init.method).toBe('POST');
    expect(init.body).toBe(JSON.stringify({ title: 'Hi', body: 'Hello' }));
    expect(init.headers).toMatchObject({ Authorization: 'Bearer tok', 'Content-Type': 'application/json' });

    fetchMock.mockResolvedValueOnce(jsonResponse({ id: 9, topicId: 10, detailPath: '/t/10' }));
    await createForumReply(10, { body: 'Reply' }, 'tok');
    const reply = lastCall();
    expect(reply.url).toBe('http://qz.test/api/v1/forum/topics/10/posts');
    expect(reply.init.body).toBe(JSON.stringify({ body: 'Reply' }));
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('POSTs multipart when a file is attached', async () => {
    fetchMock
      .mockResolvedValueOnce(pdfBlobResponse())
      .mockResolvedValueOnce(jsonResponse({ id: 9, topicId: 10, detailPath: '/t/10' }));

    await createForumReply(
      10,
      {
        body: 'Reply',
        file: { uri: 'file:///tmp/notes.pdf', name: 'notes.pdf', type: 'application/pdf' },
      },
      'tok',
    );

    expect(String(fetchMock.mock.calls[0]?.[0])).toBe('file:///tmp/notes.pdf');
    const { url, init } = lastCall();
    expect(url).toBe('http://qz.test/api/v1/forum/topics/10/posts');
    expect(init.method).toBe('POST');
    expect(init.headers).toMatchObject({ Authorization: 'Bearer tok' });
    expect(init.headers).not.toHaveProperty('Content-Type');
    const form = init.body as FormData;
    expect(form.get('body')).toBe('Reply');
    expect(form.get('file')).toBeInstanceOf(Blob);
    expect(form.get('title')).toBeNull();
  });

  it('POSTs multipart title/body/file for a new topic', async () => {
    fetchMock
      .mockResolvedValueOnce(pdfBlobResponse())
      .mockResolvedValueOnce(jsonResponse({ id: 1, starterPostId: 2, title: 'Hi', detailPath: '/t/1' }));

    await createForumTopic(
      4,
      {
        title: 'Hi',
        body: 'Hello',
        file: { uri: 'file:///tmp/notes.pdf', name: 'notes.pdf', type: 'application/pdf' },
      },
      'tok',
    );

    const { url, init } = lastCall();
    expect(url).toBe('http://qz.test/api/v1/forum/categories/4/topics');
    const form = init.body as FormData;
    expect(form.get('title')).toBe('Hi');
    expect(form.get('body')).toBe('Hello');
    expect(form.get('file')).toBeInstanceOf(Blob);
    expect(init.headers).not.toHaveProperty('Content-Type');
  });

  it('maps a failed local file read to local-file and reports it', async () => {
    const cause = new TypeError('Network request failed');
    fetchMock.mockRejectedValueOnce(cause);

    await expect(
      createForumReply(
        10,
        {
          body: 'Reply',
          file: { uri: 'file:///tmp/notes.pdf', name: 'notes.pdf', type: 'application/pdf' },
        },
        'tok',
      ),
    ).rejects.toMatchObject({
      kind: 'local-file',
      message: 'Could not read the selected photo. Try choosing it again.',
    });

    expect(reportApiFailureMock).toHaveBeenCalledWith({
      kind: 'local-file',
      status: 0,
      method: 'POST',
      path: '/forum/topics/10/posts',
      cause,
    });
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});

describe('poll endpoints', () => {
  it('fetches the poll with an optional viewer token', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ pollId: 'p1' }));
    await fetchForumTopicPoll(10, 'tok');
    const { url, init } = lastCall();
    expect(url).toBe('http://qz.test/api/v1/forum/topics/10/poll');
    expect(init.headers).toMatchObject({ Authorization: 'Bearer tok' });
  });

  it('votes a single option as optionId and multiple as optionIds', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ pollId: 'p1' }));
    await voteForumTopicPoll(10, ['opt-1'], 'tok');
    expect(lastCall().init.body).toBe(JSON.stringify({ optionId: 'opt-1' }));

    fetchMock.mockResolvedValueOnce(jsonResponse({ pollId: 'p1' }));
    await voteForumTopicPoll(10, ['opt-1', 'opt-2'], 'tok');
    expect(lastCall().init.body).toBe(JSON.stringify({ optionIds: ['opt-1', 'opt-2'] }));
  });

  it('closes the poll with a Bearer token and no body', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ pollId: 'p1', isClosed: true }));
    await closeForumTopicPoll(10, 'tok');
    const { url, init } = lastCall();
    expect(url).toBe('http://qz.test/api/v1/forum/topics/10/poll/close');
    expect(init.method).toBe('POST');
    expect(init.headers).toMatchObject({ Authorization: 'Bearer tok' });
  });
});

describe('post author block and unblock', () => {
  it('POSTs block and unblock with a Bearer token and no body', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse(null, 204));
    await blockForumPostAuthor('tok', 42);
    expect(lastCall().url).toBe('http://qz.test/api/v1/me/forum/posts/42/block');
    expect(lastCall().init.method).toBe('POST');
    expect(lastCall().init.headers).toMatchObject({ Authorization: 'Bearer tok' });
    expect(lastCall().init.body).toBeUndefined();

    fetchMock.mockResolvedValueOnce(jsonResponse(null, 204));
    await unblockForumPostAuthor('tok', 42);
    expect(lastCall().url).toBe('http://qz.test/api/v1/me/forum/posts/42/unblock');
    expect(lastCall().init.method).toBe('POST');
    expect(lastCall().init.headers).toMatchObject({ Authorization: 'Bearer tok' });
    expect(lastCall().init.body).toBeUndefined();
  });
});

describe('topic watch endpoints', () => {
  it('gets, watches, and unwatches with a Bearer token', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ watching: false }));
    await fetchForumTopicWatch(10, 'tok');
    expect(lastCall().url).toBe('http://qz.test/api/v1/forum/topics/10/watch');
    expect(lastCall().init.headers).toMatchObject({ Authorization: 'Bearer tok' });

    fetchMock.mockResolvedValueOnce(jsonResponse({ watching: true }));
    await watchForumTopic(10, 'tok');
    expect(lastCall().url).toBe('http://qz.test/api/v1/forum/topics/10/watch');
    expect(lastCall().init.method).toBe('POST');

    fetchMock.mockResolvedValueOnce(jsonResponse({ watching: false }));
    await unwatchForumTopic(10, 'tok');
    expect(lastCall().init.method).toBe('DELETE');
  });
});
