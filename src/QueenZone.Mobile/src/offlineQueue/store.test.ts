import assert from 'node:assert/strict';
import { register } from 'node:module';
import { afterEach, describe, it } from 'node:test';
import { pathToFileURL } from 'node:url';
import type { KeyValueStorage } from '../cache/storage.ts';
import type { OfflineQueueItem } from './types.ts';

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
const { OFFLINE_QUEUE_SCHEMA_VERSION, OFFLINE_QUEUE_STORAGE_KEY } = await import('./types.ts');
const {
  OFFLINE_QUEUE_DISCARD_STORAGE_KEY,
  countPendingOfflineItems,
  discardOfflineQueue,
  enqueueOfflineItem,
  listOfflineQueue,
  prepareOfflineQueueDiscard,
  removeOfflineItem,
  setOfflineQueueStorageForTests,
  subscribeOfflineQueue,
  updateOfflineItem,
} = await import('./store.ts');

function item(operationId: string, memberId = 'member-a'): OfflineQueueItem {
  return {
    schemaVersion: OFFLINE_QUEUE_SCHEMA_VERSION,
    operationId,
    memberId,
    kind: 'forum.reply',
    target: { topicId: 1 },
    payload: { body: operationId },
    createdAt: '2026-09-30T00:00:00.000Z',
    updatedAt: '2026-09-30T00:00:00.000Z',
    attemptCount: 0,
    nextRetryAt: '2026-09-30T00:00:00.000Z',
    state: 'queued',
    lastError: null,
  };
}

function pausedStorage(
  method: 'getItem' | 'setItem' | 'removeItem',
  raw: string | null,
) {
  const base = createMemoryStorage(raw === null ? {} : { [OFFLINE_QUEUE_STORAGE_KEY]: raw });
  const started = Promise.withResolvers<void>();
  const resume = Promise.withResolvers<void>();
  const calls: string[] = [];
  let paused = false;
  async function pause(name: string) {
    calls.push(name);
    if (name === method && !paused) {
      paused = true;
      started.resolve();
      await resume.promise;
    }
  }
  const storage: KeyValueStorage = {
    ...base,
    async getItem(key) {
      const snapshot = await base.getItem(key);
      if (key === OFFLINE_QUEUE_STORAGE_KEY) await pause('getItem');
      return snapshot;
    },
    async setItem(key, value) {
      if (key === OFFLINE_QUEUE_STORAGE_KEY) await pause('setItem');
      await base.setItem(key, value);
    },
    async removeItem(key) {
      if (key === OFFLINE_QUEUE_STORAGE_KEY) await pause('removeItem');
      await base.removeItem(key);
    },
  };
  return { storage, base, calls, started: started.promise, resume: resume.resolve };
}

afterEach(() => {
  setOfflineQueueStorageForTests(createMemoryStorage());
});

describe('offline queue storage serialization', () => {
  it('PrepareOfflineQueueDiscard_IntentWriteFailsButQueueWriteSucceeds_DeletesOldRowsAndPreservesNewRows', async () => {
    const base = createMemoryStorage({
      [OFFLINE_QUEUE_STORAGE_KEY]: JSON.stringify([item('old-a'), item('old-b', 'member-b')]),
      [OFFLINE_QUEUE_DISCARD_STORAGE_KEY]: JSON.stringify(['old-b']),
    });
    let markerWrites = 0;
    setOfflineQueueStorageForTests({
      ...base,
      async setItem(key, value) {
        if (key === OFFLINE_QUEUE_DISCARD_STORAGE_KEY) {
          markerWrites += 1;
          throw new Error('Intent write failed');
        }
        // Another member's durable intent remains intact until its rows are deleted.
        assert.deepEqual(JSON.parse((await base.getItem(OFFLINE_QUEUE_DISCARD_STORAGE_KEY))!), ['old-b']);
        await base.setItem(key, value);
      },
    });
    const cleanup = prepareOfflineQueueDiscard('member-a');
    await enqueueOfflineItem(item('new-a'));
    await cleanup();
    assert.equal(markerWrites, 1);
    assert.equal(await base.getItem(OFFLINE_QUEUE_DISCARD_STORAGE_KEY), null);

    setOfflineQueueStorageForTests(base);
    assert.deepEqual((await listOfflineQueue()).map((row) => row.operationId), ['new-a']);
  });

  it('PrepareOfflineQueueDiscard_IntentAndQueueWritesFail_RejectsAndRetainsBothMembersCancellation', async () => {
    const base = createMemoryStorage({
      [OFFLINE_QUEUE_STORAGE_KEY]: JSON.stringify([item('old-a'), item('old-b', 'member-b')]),
      [OFFLINE_QUEUE_DISCARD_STORAGE_KEY]: JSON.stringify(['old-b']),
    });
    const queueFailure = new Error('Queue deletion failed');
    let fail = false;
    const failedKeys: string[] = [];
    setOfflineQueueStorageForTests({
      ...base,
      async setItem(key, value) {
        if (fail) {
          failedKeys.push(key);
          if (key === OFFLINE_QUEUE_DISCARD_STORAGE_KEY) throw new Error('Intent write failed');
          throw queueFailure;
        }
        await base.setItem(key, value);
      },
    });
    const cleanup = prepareOfflineQueueDiscard('member-a');
    await enqueueOfflineItem(item('new-a'));
    fail = true;
    await assert.rejects(cleanup(), (error) => error === queueFailure);
    assert.deepEqual(failedKeys, [OFFLINE_QUEUE_DISCARD_STORAGE_KEY, OFFLINE_QUEUE_STORAGE_KEY]);
    assert.deepEqual((await listOfflineQueue()).map((row) => row.operationId), ['new-a']);
    assert.deepEqual(JSON.parse((await base.getItem(OFFLINE_QUEUE_DISCARD_STORAGE_KEY))!), ['old-b']);
    assert.deepEqual(JSON.parse((await base.getItem(OFFLINE_QUEUE_STORAGE_KEY))!).map((row: OfflineQueueItem) => row.operationId), ['old-a', 'old-b', 'new-a']);
  });

  it('PrepareOfflineQueueDiscard_IntentPersistsButQueueWriteFails_RestartSuppressesOldOperations', async () => {
    const base = createMemoryStorage({ [OFFLINE_QUEUE_STORAGE_KEY]: JSON.stringify([item('old')]) });
    setOfflineQueueStorageForTests({
      ...base,
      async setItem(key, value) {
        if (key === OFFLINE_QUEUE_STORAGE_KEY) throw new Error('Queue deletion failed');
        await base.setItem(key, value);
      },
    });
    const cleanup = prepareOfflineQueueDiscard('member-a');
    await assert.rejects(cleanup(), { message: 'Queue deletion failed' });
    assert.deepEqual(JSON.parse((await base.getItem(OFFLINE_QUEUE_DISCARD_STORAGE_KEY))!), ['old']);

    // Reset process state while retaining the actual device-storage contents.
    setOfflineQueueStorageForTests(base);
    assert.deepEqual(await listOfflineQueue(), []);
    assert.equal(await updateOfflineItem('old', { state: 'sending' }), null);
    await assert.rejects(enqueueOfflineItem(item('old')), { message: 'Offline queue operation was discarded.' });
    await enqueueOfflineItem(item('new'));
    assert.deepEqual((await listOfflineQueue()).map((row) => row.operationId), ['new']);
  });

  it('PrepareOfflineQueueDiscard_SuccessfulDeletion_RemovesDurableIntentAfterQueueWrite', async () => {
    const base = createMemoryStorage({ [OFFLINE_QUEUE_STORAGE_KEY]: JSON.stringify([item('old')]) });
    const writes: string[] = [];
    setOfflineQueueStorageForTests({
      ...base,
      async setItem(key, value) { writes.push(`set:${key}`); await base.setItem(key, value); },
      async removeItem(key) { writes.push(`remove:${key}`); await base.removeItem(key); },
    });
    await prepareOfflineQueueDiscard('member-a')();
    assert.deepEqual(writes, [
      `set:${OFFLINE_QUEUE_DISCARD_STORAGE_KEY}`,
      `set:${OFFLINE_QUEUE_STORAGE_KEY}`,
      `remove:${OFFLINE_QUEUE_DISCARD_STORAGE_KEY}`,
    ]);
    assert.equal(await base.getItem(OFFLINE_QUEUE_DISCARD_STORAGE_KEY), null);
  });

  for (const raw of ['{broken', 'null', '[1]']) {
    it(`ListOfflineQueue_InvalidDurableIntent${raw}_FailsClosed`, async () => {
      setOfflineQueueStorageForTests(createMemoryStorage({
        [OFFLINE_QUEUE_STORAGE_KEY]: JSON.stringify([item('old')]),
        [OFFLINE_QUEUE_DISCARD_STORAGE_KEY]: raw,
      }));
      await assert.rejects(listOfflineQueue(), { message: 'Offline queue discard state is invalid.' });
    });
  }

  it('ListOfflineQueue_DurableIntentReadFails_PropagatesAndRetriesWithoutAdmittingRows', async () => {
    const base = createMemoryStorage({
      [OFFLINE_QUEUE_STORAGE_KEY]: JSON.stringify([item('old')]),
      [OFFLINE_QUEUE_DISCARD_STORAGE_KEY]: JSON.stringify(['old']),
    });
    let fail = true;
    setOfflineQueueStorageForTests({
      ...base,
      async getItem(key) {
        if (key === OFFLINE_QUEUE_DISCARD_STORAGE_KEY && fail) throw new Error('Intent read failed');
        return base.getItem(key);
      },
    });
    await assert.rejects(listOfflineQueue(), { message: 'Intent read failed' });
    fail = false;
    assert.deepEqual(await listOfflineQueue(), []);
  });

  it('PrepareOfflineQueueDiscard_FailedDeletion_SuppressesOldRowsAndRetryPreservesNewRows', async () => {
    const base = createMemoryStorage({ [OFFLINE_QUEUE_STORAGE_KEY]: JSON.stringify([item('old')]) });
    let fail = true;
    setOfflineQueueStorageForTests({
      ...base,
      async setItem(key, value) {
        if (fail) throw new Error('Storage write failed');
        await base.setItem(key, value);
      },
    });
    const cleanup = prepareOfflineQueueDiscard('member-a');
    assert.deepEqual(await listOfflineQueue(), []);
    await assert.rejects(cleanup(), { message: 'Storage write failed' });
    assert.notEqual(await base.getItem(OFFLINE_QUEUE_STORAGE_KEY), '[]');
    assert.equal(await updateOfflineItem('old', { state: 'sending' }), null);
    fail = false;
    await enqueueOfflineItem(item('new'));
    await cleanup();
    await cleanup();
    assert.deepEqual((await listOfflineQueue()).map((row) => row.operationId), ['new']);
    assert.deepEqual(JSON.parse((await base.getItem(OFFLINE_QUEUE_STORAGE_KEY))!).map((row: OfflineQueueItem) => row.operationId), ['new']);
  });

  it('PrepareOfflineQueueDiscard_UnknownIdsAndStalledInspection_PreservesOnlyNewEnqueues', async () => {
    const held = pausedStorage('getItem', JSON.stringify([item('old')]));
    setOfflineQueueStorageForTests(held.storage);
    const inspection = listOfflineQueue();
    await held.started;
    const cleanup = prepareOfflineQueueDiscard('member-a');
    const enqueue = enqueueOfflineItem(item('new'));
    const discard = cleanup();
    held.resume();
    assert.deepEqual(await inspection, []);
    await Promise.all([enqueue, discard]);
    assert.deepEqual((await listOfflineQueue()).map((row) => row.operationId), ['new']);
  });

  it('PrepareOfflineQueueDiscard_PreIntentEnqueueStillPending_DoesNotResurrectOldSend', async () => {
    const held = pausedStorage('getItem', null);
    setOfflineQueueStorageForTests(held.storage);
    const old = enqueueOfflineItem(item('old'));
    const rejected = assert.rejects(old, { message: 'Offline queue operation was discarded.' });
    await held.started;
    const cleanup = prepareOfflineQueueDiscard('member-a');
    const fresh = enqueueOfflineItem(item('new'));
    held.resume();
    await Promise.all([rejected, fresh, cleanup()]);
    assert.deepEqual((await listOfflineQueue()).map((row) => row.operationId), ['new']);
    await assert.rejects(enqueueOfflineItem(item('old')), { message: 'Offline queue operation was discarded.' });
  });

  it('PrepareOfflineQueueDiscard_GlobalIntentAndRepeatedCleanup_PreservesPostIntentMembers', async () => {
    setOfflineQueueStorageForTests(createMemoryStorage());
    await enqueueOfflineItem(item('old-a'));
    await enqueueOfflineItem(item('old-b', 'member-b'));
    const cleanup = prepareOfflineQueueDiscard();
    await enqueueOfflineItem(item('new-a'));
    await enqueueOfflineItem(item('new-b', 'member-b'));
    await cleanup();
    await cleanup();
    assert.deepEqual((await listOfflineQueue()).map((row) => row.operationId), ['new-a', 'new-b']);
  });

  it('EnqueueOfflineItem_ConcurrentMutations_PreservesEachChange', async () => {
    const held = pausedStorage('getItem', JSON.stringify([item('update'), item('remove')]));
    setOfflineQueueStorageForTests(held.storage);
    const update = updateOfflineItem('update', { state: 'needs_attention' });
    await held.started;
    const remove = removeOfflineItem('remove');
    const first = enqueueOfflineItem(item('first'));
    const second = enqueueOfflineItem(item('second', 'member-b'));
    held.resume();
    await Promise.all([update, remove, first, second]);

    const rows = await listOfflineQueue();
    assert.deepEqual(rows.map((row) => row.operationId), ['update', 'first', 'second']);
    assert.equal(rows[0]?.state, 'needs_attention');
    assert.equal(await countPendingOfflineItems('member-b'), 1);
  });

  it('DiscardOfflineQueue_DelayedMemberRead_PreservesLaterEnqueuesForBothMembers', async () => {
    const held = pausedStorage('getItem', JSON.stringify([
      item('old-a'), item('existing-b', 'member-b'),
    ]));
    setOfflineQueueStorageForTests(held.storage);
    const discard = discardOfflineQueue('member-a');
    await held.started;
    const nextMember = enqueueOfflineItem(item('new-b', 'member-b'));
    const sameMember = enqueueOfflineItem(item('new-a'));
    held.resume();
    await Promise.all([discard, nextMember, sameMember]);

    assert.deepEqual((await listOfflineQueue()).map((row) => row.operationId), [
      'existing-b', 'new-b', 'new-a',
    ]);
  });

  it('DiscardOfflineQueue_DelayedWrite_PreservesLaterEnqueuesForBothMembers', async () => {
    const held = pausedStorage('setItem', JSON.stringify([item('old-a')]));
    setOfflineQueueStorageForTests(held.storage);
    const discard = discardOfflineQueue('member-a');
    await held.started;
    const nextMember = enqueueOfflineItem(item('new-b', 'member-b'));
    const sameMember = enqueueOfflineItem(item('new-a'));
    held.resume();
    await Promise.all([discard, nextMember, sameMember]);

    assert.deepEqual((await listOfflineQueue()).map((row) => row.operationId), ['new-b', 'new-a']);
  });

  it('DiscardOfflineQueue_DelayedGlobalRemoval_PreservesLaterEnqueues', async () => {
    const held = pausedStorage('removeItem', JSON.stringify([item('old-a')]));
    setOfflineQueueStorageForTests(held.storage);
    const discard = discardOfflineQueue();
    await held.started;
    const nextMember = enqueueOfflineItem(item('new-b', 'member-b'));
    const sameMember = enqueueOfflineItem(item('new-a'));
    held.resume();
    await Promise.all([discard, nextMember, sameMember]);

    assert.deepEqual((await listOfflineQueue()).map((row) => row.operationId), ['new-b', 'new-a']);
  });

  it('ListOfflineQueue_CorruptReadRepair_PreservesLaterEnqueue', async () => {
    const held = pausedStorage('getItem', '{broken');
    setOfflineQueueStorageForTests(held.storage);
    const read = listOfflineQueue();
    await held.started;
    const enqueue = enqueueOfflineItem(item('new-b', 'member-b'));
    held.resume();
    assert.deepEqual(await read, []);
    await enqueue;

    assert.deepEqual(held.calls, ['getItem', 'removeItem', 'getItem', 'setItem']);
    assert.deepEqual((await listOfflineQueue()).map((row) => row.operationId), ['new-b']);
  });

  it('ListOfflineQueue_EnqueueWritePending_WaitsForCommittedData', async () => {
    const held = pausedStorage('setItem', null);
    setOfflineQueueStorageForTests(held.storage);
    const enqueue = enqueueOfflineItem(item('pending'));
    await held.started;
    const read = listOfflineQueue();
    held.resume();
    await enqueue;
    assert.deepEqual((await read).map((row) => row.operationId), ['pending']);
  });

  for (const memberId of ['member-a', null]) {
    it(`DiscardOfflineQueue_${memberId ?? 'Global'}GuardExpiresInQueue_DoesNotMutate`, async () => {
      const held = pausedStorage('setItem', null);
      setOfflineQueueStorageForTests(held.storage);
      const enqueue = enqueueOfflineItem(item('pending'));
      await held.started;
      let current = true;
      const discard = discardOfflineQueue(memberId, () => current);
      const rejected = assert.rejects(discard, { message: 'Offline queue cleanup was superseded.' });
      current = false;
      held.resume();
      await Promise.all([enqueue, rejected]);

      assert.deepEqual(held.calls, ['getItem', 'setItem']);
      assert.equal((await listOfflineQueue()).length, 1);
    });
  }

  for (const raw of [JSON.stringify([item('old-a')]), '{broken', 'null']) {
    it(`DiscardOfflineQueue_GuardExpiresDuringRead${raw.startsWith('[') ? 'Valid' : raw === 'null' ? 'NonArray' : 'Corrupt'}_DoesNotMutate`, async () => {
      const held = pausedStorage('getItem', raw);
      setOfflineQueueStorageForTests(held.storage);
      let current = true;
      const discard = discardOfflineQueue('member-a', () => current);
      const rejected = assert.rejects(discard, { message: 'Offline queue cleanup was superseded.' });
      await held.started;
      current = false;
      held.resume();
      await rejected;

      assert.deepEqual(held.calls, ['getItem']);
      assert.equal(await held.base.getItem(OFFLINE_QUEUE_STORAGE_KEY), raw);
    });
  }

  for (const raw of ['{broken', 'null']) {
    it(`ListOfflineQueue_${raw === 'null' ? 'NonArray' : 'Corrupt'}RepairFails_PropagatesAndAllowsRetry`, async () => {
      const base = createMemoryStorage({ [OFFLINE_QUEUE_STORAGE_KEY]: raw });
      const failure = new Error('Storage removal failed');
      let removals = 0;
      setOfflineQueueStorageForTests({
        ...base,
        async removeItem(key) {
          removals += 1;
          if (removals === 1) throw failure;
          await base.removeItem(key);
        },
      });

      await assert.rejects(listOfflineQueue(), (error) => error === failure);
      assert.equal(removals, 1);
      await enqueueOfflineItem(item('after-failure'));
      assert.deepEqual((await listOfflineQueue()).map((row) => row.operationId), ['after-failure']);
    });
  }

  it('DiscardOfflineQueue_WriteFails_PropagatesWithoutPoisoningLaterMutations', async () => {
    const base = createMemoryStorage({ [OFFLINE_QUEUE_STORAGE_KEY]: JSON.stringify([item('old-a')]) });
    const failure = new Error('Storage write failed');
    let writes = 0;
    let notifications = 0;
    setOfflineQueueStorageForTests({
      ...base,
      async setItem(key, value) {
        writes += 1;
        if (writes === 1) throw failure;
        await base.setItem(key, value);
      },
    });
    const unsubscribe = subscribeOfflineQueue(() => { notifications += 1; });
    try {
      await assert.rejects(discardOfflineQueue('member-a'), (error) => error === failure);
      assert.equal(notifications, 0);
      await enqueueOfflineItem(item('new-b', 'member-b'));
      await discardOfflineQueue('member-a');
      assert.deepEqual((await listOfflineQueue()).map((row) => row.operationId), ['new-b']);
      assert.equal(notifications, 2);
    } finally {
      unsubscribe();
    }
  });

  it('DiscardOfflineQueue_GlobalRemovalFails_PropagatesAndAllowsRetry', async () => {
    const base = createMemoryStorage({ [OFFLINE_QUEUE_STORAGE_KEY]: JSON.stringify([item('old-a')]) });
    const failure = new Error('Storage removal failed');
    let removals = 0;
    setOfflineQueueStorageForTests({
      ...base,
      async removeItem(key) {
        removals += 1;
        if (removals === 1) throw failure;
        await base.removeItem(key);
      },
    });

    await assert.rejects(discardOfflineQueue(), (error) => error === failure);
    assert.equal((await listOfflineQueue()).length, 1);
    await discardOfflineQueue();
    assert.deepEqual(await listOfflineQueue(), []);
  });
});
