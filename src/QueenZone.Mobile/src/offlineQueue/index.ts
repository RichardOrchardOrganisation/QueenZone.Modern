import { useEffect, useState } from 'react';
import { newOperationId } from './ids';
import {
  enqueueOfflineItem,
  listOfflineQueue,
  subscribeOfflineQueue,
} from './store';
import {
  OFFLINE_QUEUE_SCHEMA_VERSION,
  type OfflineQueueItem,
  type OfflineQueueKind,
  type OfflineQueueTarget,
} from './types';

export {
  clearOfflineQueueRetryTimer,
  configureOfflineQueueAuth,
  flushOfflineQueue,
  invalidateOfflineQueueFlush,
  setOfflineQueueSendersForTests,
} from './flusher';
export { newOperationId } from './ids';
export {
  countPendingOfflineItems,
  discardOfflineQueue,
  listOfflineQueue,
  prepareOfflineQueueDiscard,
  removeOfflineItem,
  setOfflineQueueStorageForTests,
  subscribeOfflineQueue,
  updateOfflineItem,
} from './store';
export type { OfflineQueueItem, OfflineQueueKind, OfflineQueueState } from './types';

async function enqueue(input: {
  memberId: string;
  kind: OfflineQueueKind;
  target: OfflineQueueTarget;
  body: string;
  operationId?: string;
}): Promise<OfflineQueueItem> {
  const now = new Date().toISOString();
  const item: OfflineQueueItem = {
    schemaVersion: OFFLINE_QUEUE_SCHEMA_VERSION,
    operationId: input.operationId ?? newOperationId(),
    memberId: input.memberId,
    kind: input.kind,
    target: input.target,
    payload: { body: input.body },
    createdAt: now,
    updatedAt: now,
    attemptCount: 0,
    nextRetryAt: now,
    state: 'queued',
    lastError: null,
  };
  await enqueueOfflineItem(item);
  return item;
}

export function enqueueForumReply(input: {
  memberId: string;
  topicId: number;
  body: string;
}): Promise<OfflineQueueItem> {
  return enqueue({
    memberId: input.memberId,
    kind: 'forum.reply',
    target: { topicId: input.topicId },
    body: input.body,
  });
}

export function enqueueMessageReply(input: {
  memberId: string;
  conversationId: string;
  body: string;
}): Promise<OfflineQueueItem> {
  return enqueue({
    memberId: input.memberId,
    kind: 'message.reply',
    target: { conversationId: input.conversationId },
    body: input.body,
  });
}

export function enqueueMessageCompose(input: {
  memberId: string;
  recipientMemberId: string;
  body: string;
  operationId?: string;
}): Promise<OfflineQueueItem> {
  return enqueue({
    memberId: input.memberId,
    kind: 'message.compose',
    target: { recipientMemberId: input.recipientMemberId },
    body: input.body,
    operationId: input.operationId,
  });
}

export function useOfflineQueue(memberId: string | null): OfflineQueueItem[] {
  const [items, setItems] = useState<OfflineQueueItem[]>([]);

  useEffect(() => {
    let cancelled = false;
    const load = () => {
      void listOfflineQueue(memberId).then((next) => {
        if (!cancelled) {
          setItems(next);
        }
      });
    };
    load();
    const unsubscribe = subscribeOfflineQueue(load);
    return () => {
      cancelled = true;
      unsubscribe();
    };
  }, [memberId]);

  return items;
}
