import {
  composeMessage,
  fetchConversation,
  fetchConversationResult,
  fetchInbox,
  fetchUnreadConversationCount,
  replyToConversation,
  reportConversationMessage,
  searchRecipients,
  blockConversationParticipant,
  unblockConversationParticipant,
} from './messages';
import { jsonResponse } from '../test/fixtures';
import { ContentCache, conversationCacheKey, createMemoryStorage, inboxCacheKey } from '../cache';

function accessJwt(payload: object): string {
  const encode = (value: object) => Buffer.from(JSON.stringify(value)).toString('base64url');
  return `${encode({ alg: 'none', typ: 'JWT' })}.${encode(payload)}.sig`;
}

jest.mock('../config', () => ({
  apiV1Url: (path: string) => `http://qz.test/api/v1${path.startsWith('/') ? path : `/${path}`}`,
}));

const fetchMock = jest.fn<Promise<Response>, [RequestInfo | URL, RequestInit?]>();

beforeEach(() => {
  fetchMock.mockReset();
  global.fetch = fetchMock as unknown as typeof fetch;
});

function lastCall() {
  const call = fetchMock.mock.calls.at(-1);
  if (!call) {
    throw new Error('fetch was not called');
  }
  return { url: String(call[0]), init: call[1] ?? {} };
}

describe('fetchInbox', () => {
  it('sends the Bearer token and page params', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ items: [] }));
    await fetchInbox('tok', { page: 2, pageSize: 20 });
    const { url, init } = lastCall();
    expect(url).toBe('http://qz.test/api/v1/me/messages?page=2&pageSize=20');
    expect(init.headers).toMatchObject({ Authorization: 'Bearer tok' });
  });

  it('shares a standard first-page request and persists the complete response', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    const token = accessJwt({ sub: 'member-a' });
    const response = { items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 1 };
    let resolveFetch: (response: Response) => void = () => {};
    fetchMock.mockReturnValueOnce(new Promise((resolve) => { resolveFetch = resolve; }));

    const first = fetchInbox(token, { page: 1, pageSize: 50, cache });
    const second = fetchInbox(token, { page: 1, pageSize: 50, cache });
    expect(fetchMock).toHaveBeenCalledTimes(1);

    resolveFetch(jsonResponse(response));
    await expect(Promise.all([first, second])).resolves.toEqual([response, response]);
    expect(await cache.get(inboxCacheKey('member-a'))).toEqual(response);
  });

  it('retains the cached inbox after a transient network failure', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    const token = accessJwt({ sub: 'member-a' });
    const cached = { items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 1 };
    await cache.put(inboxCacheKey('member-a'), cached);
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));

    await expect(fetchInbox(token, { page: 1, pageSize: 50, cache })).resolves.toEqual(cached);
  });
});

describe('fetchUnreadConversationCount', () => {
  it('clamps a valid count to a non-negative integer', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ unreadConversationCount: 3.7 }));
    await expect(fetchUnreadConversationCount('tok')).resolves.toBe(3);
    expect(lastCall().url).toBe('http://qz.test/api/v1/me/messages/unread-count');
  });

  it('falls back to 0 for a missing or invalid count', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({}));
    await expect(fetchUnreadConversationCount('tok')).resolves.toBe(0);

    fetchMock.mockResolvedValueOnce(jsonResponse({ unreadConversationCount: -5 }));
    await expect(fetchUnreadConversationCount('tok')).resolves.toBe(0);
  });
});

describe('searchRecipients', () => {
  it('sends the query string and returns the items array', async () => {
    fetchMock.mockResolvedValueOnce(
      jsonResponse({ items: [{ memberId: 'm1', displayName: 'Roger' }] }),
    );
    const recipients = await searchRecipients('tok', 'rog');
    expect(lastCall().url).toBe('http://qz.test/api/v1/me/messages/recipients?q=rog');
    expect(recipients).toEqual([{ memberId: 'm1', displayName: 'Roger' }]);
  });

  it('returns an empty array when items is missing', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({}));
    await expect(searchRecipients('tok', 'x')).resolves.toEqual([]);
  });
});

describe('fetchConversation and replyToConversation', () => {
  it('reads a conversation and posts a reply body', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ conversationId: 'c1', messages: [] }));
    await fetchConversation('tok', 'c1', { page: 1 });
    expect(lastCall().url).toBe('http://qz.test/api/v1/me/messages/c1?page=1');

    fetchMock.mockResolvedValueOnce(jsonResponse({ conversationId: 'c1', messages: [] }));
    await replyToConversation('tok', 'c1', 'hello');
    const { url, init } = lastCall();
    expect(url).toBe('http://qz.test/api/v1/me/messages/c1');
    expect(init.method).toBe('POST');
    expect(init.body).toBe(JSON.stringify({ body: 'hello' }));
  });

  it('caches the opened conversation under memberId, never the Bearer token', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    const payload = {
      conversationId: 'c1',
      messages: [],
      page: 1,
      pageSize: 50,
      totalCount: 0,
      totalPages: 0,
    };
    fetchMock.mockResolvedValueOnce(jsonResponse(payload));
    await fetchConversation('secret-token', 'c1', { memberId: 'member-a', cache });
    expect(await cache.get(conversationCacheKey('member-a', 'c1'))).toMatchObject(payload);
    expect(await cache.get(conversationCacheKey('member-b', 'c1'))).toBeNull();
    expect((await cache.listCacheKeys()).join(',')).not.toContain('secret-token');
  });

  it('returns a cached conversation from the JWT sub when /me memberId is missing and the network is offline', async () => {
    const cache = new ContentCache({ storage: createMemoryStorage() });
    const memberId = 'jwt-member';
    const payload = {
      conversationId: 'c1',
      messages: [{ id: 'm1', body: 'hello from cache' }],
      page: 1,
      pageSize: 50,
      totalCount: 1,
      totalPages: 1,
    };
    await cache.put(conversationCacheKey(memberId, 'c1'), payload);
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));

    const result = await fetchConversationResult(accessJwt({ sub: memberId }), 'c1', { cache });

    expect(result.source).toBe('cache');
    expect(result.data).toMatchObject(payload);
  });
});

describe('block and unblock conversation participant', () => {
  it('POSTs the conversation block and unblock paths', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse(null, 204));
    await blockConversationParticipant('tok', 'c1');
    expect(lastCall().url).toBe('http://qz.test/api/v1/me/messages/c1/block');
    expect(lastCall().init.method).toBe('POST');
    expect(lastCall().init.headers).toMatchObject({ Authorization: 'Bearer tok' });

    fetchMock.mockResolvedValueOnce(jsonResponse(null, 204));
    await unblockConversationParticipant('tok', 'c1');
    expect(lastCall().url).toBe('http://qz.test/api/v1/me/messages/c1/unblock');
    expect(lastCall().init.method).toBe('POST');
    expect(lastCall().init.headers).toMatchObject({ Authorization: 'Bearer tok' });
  });
});

describe('composeMessage', () => {
  it('posts recipient and body to the inbox path', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ conversationId: 'c2', messages: [] }));
    await composeMessage('tok', 'member-2', 'hi there');
    const { url, init } = lastCall();
    expect(url).toBe('http://qz.test/api/v1/me/messages');
    expect(init.body).toBe(JSON.stringify({ recipientMemberId: 'member-2', body: 'hi there' }));
  });
});

describe('reportConversationMessage', () => {
  it('trims a provided reason', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ reportId: 'r1', alreadyReported: false }));
    await reportConversationMessage('tok', 'c1', 'm1', '  spam  ');
    expect(lastCall().init.body).toBe(JSON.stringify({ reason: 'spam' }));
  });

  it('sends null when no reason is given', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ reportId: 'r1', alreadyReported: true }));
    await reportConversationMessage('tok', 'c1', 'm1');
    const { url, init } = lastCall();
    expect(url).toBe('http://qz.test/api/v1/me/messages/c1/messages/m1/report');
    expect(init.body).toBe(JSON.stringify({ reason: null }));
  });
});
