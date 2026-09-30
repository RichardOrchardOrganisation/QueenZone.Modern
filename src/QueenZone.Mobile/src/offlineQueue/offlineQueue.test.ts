import assert from 'node:assert/strict';
import { register } from 'node:module';
import { afterEach, describe, it } from 'node:test';
import { pathToFileURL } from 'node:url';
import { ApiError } from '../api/errors.ts';

register(
  `data:text/javascript,${encodeURIComponent(`
    export async function resolve(specifier, context, nextResolve) {
      if (specifier.startsWith('.') && !/\\\\.(?:[cm]?[jt]s|json)$/.test(specifier)) {
        try {
          return await nextResolve(specifier + '.ts', context);
        } catch {
          return nextResolve(specifier, context);
        }
      }
      return nextResolve(specifier, context);
    }
  `)}`,
  pathToFileURL('./'),
);

const { createMemoryStorage } = await import('../cache/storage.ts');
const { classifyQueueFailure } = await import('./retry.ts');
const {
  discardOfflineQueue,
  listOfflineQueue,
  prepareOfflineQueueDiscard,
  removeOfflineItem,
  setOfflineQueueStorageForTests,
} = await import('./store.ts');
const { enqueueForumReply, enqueueMessageReply } = await import('./index.ts');
const {
  configureOfflineQueueAuth,
  flushOfflineQueue,
  invalidateOfflineQueueFlush,
  setOfflineQueueSendersForTests,
} = await import('./flusher.ts');

const memberA = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const memberB = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

afterEach(async () => {
  configureOfflineQueueAuth(null);
  setOfflineQueueSendersForTests(null);
  setOfflineQueueStorageForTests(createMemoryStorage());
  await discardOfflineQueue();
});

describe('offline queue store', () => {
  it('persists before later reads and isolates members', async () => {
    setOfflineQueueStorageForTests(createMemoryStorage());
    await enqueueForumReply({ memberId: memberA, topicId: 12, body: 'Hello A' });
    await enqueueMessageReply({
      memberId: memberB,
      conversationId: 'c1',
      body: 'Hello B',
    });

    const a = await listOfflineQueue(memberA);
    const b = await listOfflineQueue(memberB);
    assert.equal(a.length, 1);
    assert.equal(a[0]?.payload.body, 'Hello A');
    assert.equal(b.length, 1);
    assert.equal(b[0]?.payload.body, 'Hello B');
    assert.notEqual(a[0]?.operationId, b[0]?.operationId);
  });

  it('drops corrupt payloads', async () => {
    const storage = createMemoryStorage({ 'qz:offline-queue:v1': '{not-json' });
    setOfflineQueueStorageForTests(storage);
    assert.deepEqual(await listOfflineQueue(memberA), []);
  });

  it('discards one member without touching another', async () => {
    setOfflineQueueStorageForTests(createMemoryStorage());
    await enqueueForumReply({ memberId: memberA, topicId: 1, body: 'A' });
    await enqueueForumReply({ memberId: memberB, topicId: 1, body: 'B' });
    await discardOfflineQueue(memberA);
    assert.equal((await listOfflineQueue(memberA)).length, 0);
    assert.equal((await listOfflineQueue(memberB)).length, 1);
  });
});

describe('offline queue flusher', () => {
  it('FlushOfflineQueue_SignOutDuringFirstSend_AbortsAndNeverSendsCapturedSecondRow', async () => {
    setOfflineQueueStorageForTests(createMemoryStorage());
    const started = Promise.withResolvers<void>();
    const release = Promise.withResolvers<void>();
    const sent: string[] = [];
    const signals: AbortSignal[] = [];
    setOfflineQueueSendersForTests({
      createForumReply: async (_topic, input, _token, signal) => {
        sent.push(input.body);
        assert.ok(signal);
        signals.push(signal);
        started.resolve();
        await release.promise;
      },
      replyToConversation: async () => {},
      composeMessage: async () => {},
    });
    configureOfflineQueueAuth({
      getAccessToken: () => 'old-token',
      getMemberId: () => memberA,
      refreshAccessToken: async () => 'old-token',
    });
    await enqueueForumReply({ memberId: memberA, topicId: 1, body: 'first' });
    await enqueueForumReply({ memberId: memberA, topicId: 1, body: 'second' });
    const flushing = flushOfflineQueue();
    await started.promise;
    const cleanup = prepareOfflineQueueDiscard(memberA);
    configureOfflineQueueAuth(null);
    assert.equal(signals[0]?.aborted, true);
    await cleanup();
    release.resolve();
    await flushing;
    assert.deepEqual(sent, ['first']);
    assert.deepEqual(await listOfflineQueue(), []);
  });

  it('FlushOfflineQueue_FailedDiscardThenSameMemberLogin_SendsOnlyNewOperations', async () => {
    const base = createMemoryStorage();
    let fail = false;
    setOfflineQueueStorageForTests({
      ...base,
      async setItem(key, value) {
        if (fail) throw new Error('Cleanup storage failed');
        await base.setItem(key, value);
      },
    });
    await enqueueForumReply({ memberId: memberA, topicId: 1, body: 'discarded' });
    const cleanup = prepareOfflineQueueDiscard(memberA);
    fail = true;
    await assert.rejects(cleanup(), { message: 'Cleanup storage failed' });
    fail = false;
    const sent: string[] = [];
    setOfflineQueueSendersForTests({
      createForumReply: async (_topic, input, token) => { sent.push(`${input.body}:${token}`); },
      replyToConversation: async () => {},
      composeMessage: async () => {},
    });
    configureOfflineQueueAuth({
      getAccessToken: () => 'new-token',
      getMemberId: () => memberA,
      refreshAccessToken: async () => 'new-token',
    });
    await enqueueForumReply({ memberId: memberA, topicId: 1, body: 'new' });
    await flushOfflineQueue();
    await cleanup();
    assert.deepEqual(sent, ['new:new-token']);
    assert.deepEqual(await listOfflineQueue(), []);
  });

  it('FlushOfflineQueue_RemovedCapturedRow_DoesNotSendWhenUpdateReturnsNull', async () => {
    setOfflineQueueStorageForTests(createMemoryStorage());
    const started = Promise.withResolvers<void>();
    const release = Promise.withResolvers<void>();
    const sent: string[] = [];
    setOfflineQueueSendersForTests({
      createForumReply: async (_topic, input) => {
        sent.push(input.body);
        started.resolve();
        await release.promise;
      },
      replyToConversation: async () => {},
      composeMessage: async () => {},
    });
    configureOfflineQueueAuth({
      getAccessToken: () => 'token',
      getMemberId: () => memberA,
      refreshAccessToken: async () => 'token',
    });
    await enqueueForumReply({ memberId: memberA, topicId: 1, body: 'first' });
    const second = await enqueueForumReply({ memberId: memberA, topicId: 1, body: 'removed' });
    const flushing = flushOfflineQueue();
    await started.promise;
    await removeOfflineItem(second.operationId);
    release.resolve();
    await flushing;
    assert.deepEqual(sent, ['first']);
  });

  it('FlushOfflineQueue_SessionInvalidatedDuringRefresh_NewFlushUsesRetainedAuthGetters', async () => {
    setOfflineQueueStorageForTests(createMemoryStorage());
    const refreshStarted = Promise.withResolvers<void>();
    const oldRefresh = Promise.withResolvers<string | null>();
    let currentMember = memberA;
    let currentToken = 'old-token';
    let refreshCalls = 0;
    configureOfflineQueueAuth({
      getAccessToken: () => currentToken,
      getMemberId: () => currentMember,
      refreshAccessToken: async () => {
        refreshCalls += 1;
        if (refreshCalls === 1) {
          refreshStarted.resolve();
          return oldRefresh.promise;
        }
        return currentToken;
      },
    });
    const sent: string[] = [];
    setOfflineQueueSendersForTests({
      createForumReply: async (_topic, input, token) => { sent.push(`${input.body}:${token}`); },
      replyToConversation: async () => {},
      composeMessage: async () => {},
    });
    await enqueueForumReply({ memberId: memberA, topicId: 1, body: 'old' });
    await enqueueForumReply({ memberId: memberB, topicId: 1, body: 'new' });
    const oldFlush = flushOfflineQueue();
    await refreshStarted.promise;
    invalidateOfflineQueueFlush();
    currentMember = memberB;
    currentToken = 'new-token';
    await flushOfflineQueue();
    oldRefresh.resolve('old-token');
    await oldFlush;
    assert.deepEqual(sent, ['new:new-token']);
    assert.equal((await listOfflineQueue(memberA)).length, 1);
  });

  it('FlushOfflineQueue_OldSenderPending_SameMemberNewFlushIsNotBlockedOrClobbered', async () => {
    setOfflineQueueStorageForTests(createMemoryStorage());
    const oldStarted = Promise.withResolvers<void>();
    const oldRelease = Promise.withResolvers<void>();
    const newStarted = Promise.withResolvers<void>();
    const newRelease = Promise.withResolvers<void>();
    let token = 'old-token';
    const sent: string[] = [];
    configureOfflineQueueAuth({
      getAccessToken: () => token,
      getMemberId: () => memberA,
      refreshAccessToken: async () => token,
    });
    setOfflineQueueSendersForTests({
      createForumReply: async (_topic, input, accessToken) => {
        sent.push(`${input.body}:${accessToken}`);
        if (input.body === 'old') {
          oldStarted.resolve();
          await oldRelease.promise;
        } else {
          newStarted.resolve();
          await newRelease.promise;
        }
      },
      replyToConversation: async () => {},
      composeMessage: async () => {},
    });
    await enqueueForumReply({ memberId: memberA, topicId: 1, body: 'old' });
    const oldFlush = flushOfflineQueue();
    await oldStarted.promise;
    const cleanup = prepareOfflineQueueDiscard(memberA);
    invalidateOfflineQueueFlush();
    token = 'new-token';
    await enqueueForumReply({ memberId: memberA, topicId: 1, body: 'new' });
    const newFlush = flushOfflineQueue();
    await newStarted.promise;
    oldRelease.resolve();
    await oldFlush;
    await flushOfflineQueue();
    assert.deepEqual(sent, ['old:old-token', 'new:new-token']);
    newRelease.resolve();
    await newFlush;
    await cleanup();
    assert.deepEqual(await listOfflineQueue(), []);
  });

  it('sends FIFO per target and uses the operation id', async () => {
    setOfflineQueueStorageForTests(createMemoryStorage());
    const sent: string[] = [];
    setOfflineQueueSendersForTests({
      createForumReply: async (_topic, input, _token, _signal, key) => {
        sent.push(`${input.body}:${key}`);
        return { id: 1, topicId: 9, detailPath: '/forum/topic/9' };
      },
      replyToConversation: async () => {
        throw new Error('not used');
      },
      composeMessage: async () => {
        throw new Error('not used');
      },
    });
    configureOfflineQueueAuth({
      getAccessToken: () => 'tok',
      getMemberId: () => memberA,
      refreshAccessToken: async () => 'tok',
    });

    await enqueueForumReply({ memberId: memberA, topicId: 9, body: 'one' });
    await enqueueForumReply({ memberId: memberA, topicId: 9, body: 'two' });
    await flushOfflineQueue();

    assert.deepEqual(
      sent.map((entry) => entry.split(':')[0]),
      ['one', 'two'],
    );
    assert.equal((await listOfflineQueue(memberA)).length, 0);
  });

  it('keeps the item queued on timeout and does not duplicate on replay', async () => {
    setOfflineQueueStorageForTests(createMemoryStorage());
    let calls = 0;
    setOfflineQueueSendersForTests({
      createForumReply: async () => {
        throw new Error('not used');
      },
      replyToConversation: async (_token, _id, _body, _signal, key) => {
        calls += 1;
        if (calls === 1) {
          throw ApiError.timeout();
        }
        assert.ok(key);
        return {
          conversationId: 'c1',
          otherParticipantId: memberB,
          otherParticipantDisplayName: 'Bob',
          messages: [],
          page: 1,
          pageSize: 50,
          totalCount: 0,
          totalPages: 1,
          detailPath: '/messages/c1',
          canSendReply: true,
          hasBlockedOtherParticipant: false,
        };
      },
      composeMessage: async () => {
        throw new Error('not used');
      },
    });
    configureOfflineQueueAuth({
      getAccessToken: () => 'tok',
      getMemberId: () => memberA,
      refreshAccessToken: async () => 'tok',
    });

    const queued = await enqueueMessageReply({
      memberId: memberA,
      conversationId: 'c1',
      body: 'Ping',
    });
    await flushOfflineQueue();
    const waiting = await listOfflineQueue(memberA);
    assert.equal(waiting.length, 1);
    assert.equal(waiting[0]?.operationId, queued.operationId);
    assert.equal(waiting[0]?.state, 'queued');

    waiting[0]!.nextRetryAt = new Date(0).toISOString();
    const { updateOfflineItem } = await import('./store.ts');
    await updateOfflineItem(queued.operationId, { nextRetryAt: new Date(0).toISOString() });
    await flushOfflineQueue();
    assert.equal((await listOfflineQueue(memberA)).length, 0);
    assert.equal(calls, 2);
  });

  it('marks validation errors as needs attention and does not send another member item', async () => {
    setOfflineQueueStorageForTests(createMemoryStorage());
    const sentMembers: string[] = [];
    setOfflineQueueSendersForTests({
      createForumReply: async () => {
        throw new Error('not used');
      },
      replyToConversation: async () => {
        throw ApiError.http(400, 'Write a message.');
      },
      composeMessage: async () => {
        throw new Error('not used');
      },
    });
    configureOfflineQueueAuth({
      getAccessToken: () => 'tok',
      getMemberId: () => memberA,
      refreshAccessToken: async () => {
        sentMembers.push(memberA);
        return 'tok';
      },
    });

    await enqueueMessageReply({ memberId: memberA, conversationId: 'c1', body: '' });
    await enqueueMessageReply({ memberId: memberB, conversationId: 'c1', body: 'nope' });
    await flushOfflineQueue();

    const a = await listOfflineQueue(memberA);
    const b = await listOfflineQueue(memberB);
    assert.equal(a[0]?.state, 'needs_attention');
    assert.equal(b[0]?.state, 'queued');
    assert.deepEqual(sentMembers, [memberA]);
  });
});

describe('classifyQueueFailure', () => {
  it('classifies offline, 429, 401, and 404', () => {
    assert.equal(classifyQueueFailure(ApiError.offline()), 'retry');
    assert.equal(classifyQueueFailure(ApiError.timeout()), 'retry');
    assert.equal(classifyQueueFailure(ApiError.http(429, 'slow')), 'systemic');
    assert.equal(classifyQueueFailure(ApiError.http(401, 'auth')), 'auth');
    assert.equal(classifyQueueFailure(ApiError.http(404, 'gone')), 'permanent');
    assert.equal(classifyQueueFailure(ApiError.http(403, 'no')), 'permanent');
    assert.equal(classifyQueueFailure(ApiError.http(500, 'boom')), 'systemic');
  });
});
