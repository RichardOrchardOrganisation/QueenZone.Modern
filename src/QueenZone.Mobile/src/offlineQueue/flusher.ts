import type { CrosswordProgressWrite } from '../api/types';
import { classifyQueueFailure, exhaustedRetries, nextRetryAt } from './retry';
import {
  isOfflineQueueItemDiscarded,
  listOfflineQueue,
  removeOfflineItem,
  updateOfflineItem,
} from './store';
import type { OfflineQueueAuth, OfflineQueueItem } from './types';

type QueueSenders = {
  saveCrosswordProgress?: (id: string, progress: CrosswordProgressWrite, accessToken: string, signal?: AbortSignal) => Promise<unknown>;
  createForumReply: (
    topicId: number,
    input: { body: string },
    accessToken: string,
    signal?: AbortSignal,
    idempotencyKey?: string,
  ) => Promise<unknown>;
  replyToConversation: (
    accessToken: string,
    conversationId: string,
    body: string,
    signal?: AbortSignal,
    idempotencyKey?: string,
  ) => Promise<unknown>;
  composeMessage: (
    accessToken: string,
    recipientMemberId: string,
    body: string,
    signal?: AbortSignal,
    idempotencyKey?: string,
  ) => Promise<unknown>;
};

let auth: OfflineQueueAuth | null = null;
let flushGeneration = 0;
let activeFlush: AbortController | null = null;
let senders: QueueSenders | null = null;
let retryTimer: ReturnType<typeof setTimeout> | null = null;

export function clearOfflineQueueRetryTimer(): void {
  if (retryTimer) {
    clearTimeout(retryTimer);
    retryTimer = null;
  }
}

async function armRetryTimer(memberId: string, isCurrent: () => boolean): Promise<void> {
  if (!isCurrent()) return;
  clearOfflineQueueRetryTimer();
  const upcoming = (await listOfflineQueue(memberId))
    .filter((item) => item.state === 'queued')
    .map((item) => Date.parse(item.nextRetryAt))
    .filter((stamp) => Number.isFinite(stamp))
    .sort((a, b) => a - b)[0];
  if (upcoming == null || !isCurrent()) {
    return;
  }
  const delay = Math.min(Math.max(0, upcoming - Date.now()), 5 * 60_000);
  retryTimer = setTimeout(() => {
    retryTimer = null;
    if (isCurrent()) void flushOfflineQueue();
  }, delay);
}

export function setOfflineQueueSendersForTests(next: QueueSenders | null): void {
  senders = next;
}

async function resolveSenders(): Promise<QueueSenders> {
  if (senders) {
    return senders;
  }
  const forum = await import('../api/forum');
  const messages = await import('../api/messages');
  const crosswords = await import('../api/crosswords');
  return {
    createForumReply: forum.createForumReply,
    replyToConversation: messages.replyToConversation,
    composeMessage: messages.composeMessage,
    saveCrosswordProgress: crosswords.saveCrosswordProgress,
  };
}

export function configureOfflineQueueAuth(next: OfflineQueueAuth | null): void {
  invalidateOfflineQueueFlush();
  auth = next;
}

/** Invalidate synchronously at every session boundary, while retaining auth getters. */
export function invalidateOfflineQueueFlush(): void {
  flushGeneration += 1;
  activeFlush?.abort();
  activeFlush = null;
  clearOfflineQueueRetryTimer();
}

function targetKey(item: OfflineQueueItem): string {
  if ('crosswordId' in item.target) return `crossword:${item.target.crosswordId}`;
  if ('topicId' in item.target) {
    return `forum:${item.target.topicId}`;
  }
  if ('conversationId' in item.target) {
    return `conversation:${item.target.conversationId}`;
  }
  return `compose:${item.target.recipientMemberId}`;
}

async function sendItem(
  item: OfflineQueueItem,
  accessToken: string,
  signal: AbortSignal,
  isCurrent: () => boolean,
): Promise<void> {
  const body = item.payload.body;
  const active = await resolveSenders();
  if (!isCurrent() || isOfflineQueueItemDiscarded(item)) return;
  if (item.kind === 'crossword.progress' && 'crosswordId' in item.target) {
    if (!item.payload.crossword || !active.saveCrosswordProgress) throw new Error('Crossword progress sender is unavailable.');
    await active.saveCrosswordProgress(item.target.crosswordId, item.payload.crossword, accessToken, signal);
    return;
  }
  if (item.kind === 'forum.reply' && 'topicId' in item.target) {
    await active.createForumReply(
      item.target.topicId,
      { body },
      accessToken,
      signal,
      item.operationId,
    );
    return;
  }
  if (item.kind === 'message.reply' && 'conversationId' in item.target) {
    await active.replyToConversation(
      accessToken,
      item.target.conversationId,
      body,
      signal,
      item.operationId,
    );
    return;
  }
  if (item.kind === 'message.compose' && 'recipientMemberId' in item.target) {
    await active.composeMessage(
      accessToken,
      item.target.recipientMemberId,
      body,
      signal,
      item.operationId,
    );
  }
}

export async function flushOfflineQueue(): Promise<void> {
  if (activeFlush) {
    return;
  }
  const currentAuth = auth;
  if (!currentAuth) {
    return;
  }

  const generation = flushGeneration;
  const controller = new AbortController();
  activeFlush = controller;
  let memberId: string | null = null;
  const isCurrent = () => generation === flushGeneration && auth === currentAuth &&
    !controller.signal.aborted && memberId !== null && currentAuth.getMemberId() === memberId;
  try {
    memberId = currentAuth.getMemberId();
    if (!memberId) {
      return;
    }

    const refreshedToken = await currentAuth.refreshAccessToken();
    if (!isCurrent()) return;
    const accessToken = refreshedToken ?? currentAuth.getAccessToken();
    if (!accessToken) {
      return;
    }

    const now = new Date().toISOString();
    const items = (await listOfflineQueue(memberId))
      .filter((item) => item.state !== 'needs_attention' && item.nextRetryAt <= now)
      .sort((a, b) => a.createdAt.localeCompare(b.createdAt));
    if (!isCurrent()) return;

    const blockedTargets = new Set<string>();

    for (const item of items) {
      if (!isCurrent()) return;
      if (isOfflineQueueItemDiscarded(item)) continue;
      const key = targetKey(item);
      if (blockedTargets.has(key)) {
        continue;
      }

      const sending = await updateOfflineItem(item.operationId, { state: 'sending' }, isCurrent);
      if (!isCurrent()) return;
      if (!sending || isOfflineQueueItemDiscarded(sending)) continue;
      try {
        await sendItem(sending, accessToken, controller.signal, isCurrent);
        if (!isCurrent()) return;
        await removeOfflineItem(item.operationId, isCurrent);
      } catch (err) {
        if (!isCurrent()) return;
        if (isOfflineQueueItemDiscarded(item)) continue;
        const kind = classifyQueueFailure(err);
        const attemptCount = item.attemptCount + 1;
        if (kind === 'permanent' || exhaustedRetries(attemptCount)) {
          await updateOfflineItem(item.operationId, {
            state: 'needs_attention',
            attemptCount,
            lastError: err instanceof Error ? err.message : 'Send failed.',
          }, isCurrent);
          continue;
        }

        await updateOfflineItem(item.operationId, {
          state: 'queued',
          attemptCount,
          nextRetryAt: nextRetryAt(attemptCount, err),
          lastError: err instanceof Error ? err.message : 'Send failed.',
        }, isCurrent);
        blockedTargets.add(key);
        if (kind === 'auth' || kind === 'systemic' || kind === 'retry') {
          return;
        }
      }
    }
  } finally {
    if (activeFlush === controller) activeFlush = null;
    if (memberId && isCurrent()) {
      // Storage may be temporarily unavailable; foreground/enqueue triggers retry.
      void armRetryTimer(memberId, isCurrent).catch(() => {});
    }
  }
}
