import { createMemoryStorage, type KeyValueStorage } from '../cache/storage';
import {
  OFFLINE_QUEUE_SCHEMA_VERSION,
  OFFLINE_QUEUE_STORAGE_KEY,
  type OfflineQueueItem,
} from './types';

let storage: KeyValueStorage | null = null;
let storageWork: Promise<unknown> = Promise.resolve();
const listeners = new Set<() => void>();
let enqueueSequence = 0;
const operationSequences = new Map<string, number>();
const discardedBefore = new Map<string | null, number>();
export const OFFLINE_QUEUE_DISCARD_STORAGE_KEY = `${OFFLINE_QUEUE_STORAGE_KEY}:discarded`;
const durableDiscardedOperations = new Set<string>();
let discardIntentLoaded = false;

type CleanupGuard = () => boolean;

function assertCleanupCurrent(isCurrent: CleanupGuard): void {
  if (!isCurrent()) {
    throw new Error('Offline queue cleanup was superseded.');
  }
}

function getStorage(): KeyValueStorage {
  if (!storage) {
    // eslint-disable-next-line @typescript-eslint/no-require-imports -- lazy so Node unit tests never load React Native AsyncStorage.
    const loaded = require('../cache/asyncStorageAdapter') as typeof import('../cache/asyncStorageAdapter');
    storage = loaded.createAsyncStorageAdapter();
  }
  return storage;
}

export function setOfflineQueueStorageForTests(next: KeyValueStorage | null): void {
  storage = next ?? createMemoryStorage();
  enqueueSequence = 0;
  operationSequences.clear();
  discardedBefore.clear();
  durableDiscardedOperations.clear();
  discardIntentLoaded = false;
}

function matchesDiscard(item: OfflineQueueItem, memberId: string | null, cutoff: number): boolean {
  return (!memberId || item.memberId === memberId) &&
    (operationSequences.get(item.operationId) ?? 0) <= cutoff;
}

export function isOfflineQueueItemDiscarded(item: OfflineQueueItem): boolean {
  const memberCutoff = discardedBefore.get(item.memberId);
  const globalCutoff = discardedBefore.get(null);
  return durableDiscardedOperations.has(item.operationId) ||
    (memberCutoff !== undefined && matchesDiscard(item, item.memberId, memberCutoff)) ||
    (globalCutoff !== undefined && matchesDiscard(item, null, globalCutoff));
}

/**
 * Capture the user's confirmed intent synchronously, before switching sessions.
 * Existing disk rows have sequence zero; enqueues reserve their sequence before
 * awaiting storage. A delayed read or failed cleanup cannot admit old sends, and
 * retries cannot delete genuinely new operations from a later same-member login.
 */
export function prepareOfflineQueueDiscard(memberId: string | null = null): () => Promise<void> {
  const scope = memberId || null;
  const cutoff = enqueueSequence;
  discardedBefore.set(scope, cutoff);
  notify();
  return () => serializeStorage(async (target) => {
    const items = await readAll(target);
    const discardedIds = new Set(durableDiscardedOperations);
    for (const row of items) {
      if (matchesDiscard(row, scope, cutoff)) discardedIds.add(row.operationId);
    }
    // Persist exact IDs before queue deletion. If deletion fails or the process
    // exits, those operations remain suppressed after the next startup.
    try {
      await target.setItem(OFFLINE_QUEUE_DISCARD_STORAGE_KEY, JSON.stringify([...discardedIds]));
    } catch {
      // Physical deletion is still safe to attempt if the intent write fails.
      // Only a successful queue rewrite establishes that the old rows are gone;
      // if it also fails, its error propagates and in-memory intent stays active.
    }
    for (const operationId of discardedIds) durableDiscardedOperations.add(operationId);
    await writeAll(target, items.filter((row) => !discardedIds.has(row.operationId)));
    await target.removeItem(OFFLINE_QUEUE_DISCARD_STORAGE_KEY);
    durableDiscardedOperations.clear();
  });
}

async function loadDiscardIntent(target: KeyValueStorage): Promise<void> {
  if (discardIntentLoaded) return;
  const raw = await target.getItem(OFFLINE_QUEUE_DISCARD_STORAGE_KEY);
  if (raw !== null) {
    let parsed: unknown;
    try {
      parsed = JSON.parse(raw) as unknown;
    } catch {
      throw new Error('Offline queue discard state is invalid.');
    }
    if (!Array.isArray(parsed) || !parsed.every((id): id is string => typeof id === 'string')) {
      throw new Error('Offline queue discard state is invalid.');
    }
    for (const operationId of parsed) durableDiscardedOperations.add(operationId);
  }
  discardIntentLoaded = true;
}

// The entire queue shares one storage key. Serialize reads as well as mutations:
// a read may repair corrupt storage, and a delayed old read must not erase a new send.
function serializeStorage<T>(work: (target: KeyValueStorage) => Promise<T>): Promise<T> {
  const target = getStorage();
  const next = storageWork.then(() => work(target));
  storageWork = next.catch(() => {});
  return next;
}

export function subscribeOfflineQueue(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function notify(): void {
  for (const listener of listeners) {
    listener();
  }
}

function isItem(value: unknown): value is OfflineQueueItem {
  if (!value || typeof value !== 'object') {
    return false;
  }
  const row = value as OfflineQueueItem;
  return (
    row.schemaVersion === OFFLINE_QUEUE_SCHEMA_VERSION &&
    typeof row.operationId === 'string' &&
    typeof row.memberId === 'string' &&
    typeof row.kind === 'string' &&
    typeof row.payload?.body === 'string' &&
    (row.state === 'queued' || row.state === 'sending' || row.state === 'needs_attention')
  );
}

async function readAll(
  target: KeyValueStorage,
  isCurrent: CleanupGuard = () => true,
): Promise<OfflineQueueItem[]> {
  await loadDiscardIntent(target);
  const raw = await target.getItem(OFFLINE_QUEUE_STORAGE_KEY);
  assertCleanupCurrent(isCurrent);
  if (!raw) {
    return [];
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw) as unknown;
  } catch {
    await target.removeItem(OFFLINE_QUEUE_STORAGE_KEY);
    return [];
  }
  if (!Array.isArray(parsed)) {
    await target.removeItem(OFFLINE_QUEUE_STORAGE_KEY);
    return [];
  }
  return parsed.filter(isItem);
}

async function writeAll(target: KeyValueStorage, items: OfflineQueueItem[]): Promise<void> {
  await target.setItem(OFFLINE_QUEUE_STORAGE_KEY, JSON.stringify(items));
  notify();
}

export async function listOfflineQueue(memberId?: string | null): Promise<OfflineQueueItem[]> {
  return serializeStorage(async (target) => {
    const items = await readAll(target);
    return items.filter((item) => (!memberId || item.memberId === memberId) && !isOfflineQueueItemDiscarded(item));
  });
}

export async function enqueueOfflineItem(item: OfflineQueueItem): Promise<void> {
  const sequence = ++enqueueSequence;
  return serializeStorage(async (target) => {
    const items = await readAll(target);
    if (!operationSequences.has(item.operationId)) {
      // Reusing an existing operation id is a retry, not a newly composed send.
      operationSequences.set(item.operationId, items.some((row) => row.operationId === item.operationId) ? 0 : sequence);
    }
    if (isOfflineQueueItemDiscarded(item)) {
      throw new Error('Offline queue operation was discarded.');
    }
    const next = items.filter((row) => row.operationId !== item.operationId);
    next.push(item);
    next.sort((a, b) => a.createdAt.localeCompare(b.createdAt));
    await writeAll(target, next);
  });
}

export async function updateOfflineItem(
  operationId: string,
  patch: Partial<OfflineQueueItem>,
  isCurrent: CleanupGuard = () => true,
): Promise<OfflineQueueItem | null> {
  return serializeStorage(async (target) => {
    if (!isCurrent()) return null;
    const items = await readAll(target);
    if (!isCurrent()) return null;
    const index = items.findIndex((row) => row.operationId === operationId);
    if (index < 0 || isOfflineQueueItemDiscarded(items[index]!)) {
      return null;
    }
    const updated = { ...items[index], ...patch, operationId, updatedAt: new Date().toISOString() };
    items[index] = updated;
    await writeAll(target, items);
    return updated;
  });
}

export async function removeOfflineItem(operationId: string, isCurrent: CleanupGuard = () => true): Promise<void> {
  return serializeStorage(async (target) => {
    if (!isCurrent()) return;
    const items = await readAll(target);
    if (!isCurrent()) return;
    const next = items.filter((row) => row.operationId !== operationId);
    if (next.length === items.length) {
      return;
    }
    await writeAll(target, next);
  });
}

export async function discardOfflineQueue(
  memberId?: string | null,
  isCurrent: CleanupGuard = () => true,
): Promise<void> {
  return serializeStorage(async (target) => {
    assertCleanupCurrent(isCurrent);
    if (!memberId) {
      await target.removeItem(OFFLINE_QUEUE_STORAGE_KEY);
      notify();
      return;
    }
    const items = await readAll(target, isCurrent);
    assertCleanupCurrent(isCurrent);
    await writeAll(target, items.filter((row) => row.memberId !== memberId));
  });
}

export async function countPendingOfflineItems(memberId?: string | null): Promise<number> {
  return (await listOfflineQueue(memberId)).length;
}
