import type { CrosswordProgressWrite } from '../api/types';

export const OFFLINE_QUEUE_SCHEMA_VERSION = 1;

export const OFFLINE_QUEUE_STORAGE_KEY = 'qz:offline-queue:v1';

export type OfflineQueueKind = 'forum.reply' | 'message.reply' | 'message.compose' | 'crossword.progress';

export type OfflineQueueState = 'queued' | 'sending' | 'needs_attention';

export type OfflineQueueTarget =
  | { topicId: number }
  | { conversationId: string }
  | { recipientMemberId: string }
  | { crosswordId: string };

export type OfflineQueueItem = {
  schemaVersion: number;
  operationId: string;
  memberId: string;
  kind: OfflineQueueKind;
  target: OfflineQueueTarget;
  payload: { body: string; crossword?: CrosswordProgressWrite };
  createdAt: string;
  updatedAt: string;
  attemptCount: number;
  nextRetryAt: string;
  state: OfflineQueueState;
  lastError: string | null;
};

export type OfflineQueueAuth = {
  getAccessToken: () => string | null;
  getMemberId: () => string | null;
  refreshAccessToken: () => Promise<string | null>;
};
