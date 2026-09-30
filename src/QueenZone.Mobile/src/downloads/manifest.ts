import { createAsyncStorageAdapter } from '../cache/asyncStorageAdapter';
import type { KeyValueStorage } from '../cache/storage';
import { getDownloadFileHost, isDownloadFileForMember, isLegacyDownloadFile } from './files';
import { resolveDownloadAudioExtension } from './audioBytes';
import {
  DOWNLOAD_MANIFEST_SCHEMA_VERSION,
  type DownloadManifest,
  type DownloadManifestEntry,
} from './types';

const MANIFEST_KEY_PREFIX = 'qz:downloads:v1:member:';

function manifestKey(memberId: string): string {
  return `${MANIFEST_KEY_PREFIX}${memberId}`;
}

let storage: KeyValueStorage = createAsyncStorageAdapter();
const mutations = new Map<string, Promise<unknown>>();

type ManifestGuard = () => boolean;

export function assertDownloadCleanupCurrent(isCurrent: ManifestGuard): void {
  if (!isCurrent()) {
    throw new Error('Download cleanup was superseded.');
  }
}

function mutateManifest<T>(memberId: string, operation: () => Promise<T>): Promise<T> {
  const previous = mutations.get(memberId) ?? Promise.resolve();
  const result = previous.catch(() => undefined).then(operation);
  mutations.set(memberId, result);
  void result.finally(() => {
    if (mutations.get(memberId) === result) {
      mutations.delete(memberId);
    }
  }).catch(() => undefined);
  return result;
}

export function setDownloadManifestStorageForTests(next: KeyValueStorage | null): void {
  storage = next ?? createAsyncStorageAdapter();
  mutations.clear();
}

export function emptyManifest(memberId: string): DownloadManifest {
  return { schemaVersion: DOWNLOAD_MANIFEST_SCHEMA_VERSION, memberId, entries: {} };
}

function isEntry(value: unknown): value is DownloadManifestEntry {
  if (value === null || typeof value !== 'object') {
    return false;
  }
  const entry = value as DownloadManifestEntry;
  return (
    typeof entry.performanceId === 'string' &&
    typeof entry.localUri === 'string' &&
    typeof entry.title === 'string' &&
    typeof entry.performedBy === 'string' &&
    typeof entry.completedAt === 'string' &&
    typeof entry.memberId === 'string' &&
    (entry.byteSize === null || typeof entry.byteSize === 'number') &&
    (entry.sourceRevision === null || typeof entry.sourceRevision === 'string')
  );
}

function parseManifest(raw: string | null, memberId: string): DownloadManifest {
  if (!raw) {
    return emptyManifest(memberId);
  }

  try {
    const parsed = JSON.parse(raw) as Partial<DownloadManifest>;
    if (parsed.schemaVersion !== DOWNLOAD_MANIFEST_SCHEMA_VERSION || parsed.memberId !== memberId) {
      return emptyManifest(memberId);
    }

    const entries: Record<string, DownloadManifestEntry> = {};
    for (const [id, entry] of Object.entries(parsed.entries ?? {})) {
      if (isEntry(entry) && entry.performanceId === id && entry.memberId === memberId) {
        entries[id] = entry;
      }
    }
    return { schemaVersion: DOWNLOAD_MANIFEST_SCHEMA_VERSION, memberId, entries };
  } catch {
    return emptyManifest(memberId);
  }
}

export async function readDownloadManifest(memberId: string): Promise<DownloadManifest> {
  return parseManifest(await storage.getItem(manifestKey(memberId)), memberId);
}

export async function writeDownloadManifest(manifest: DownloadManifest): Promise<void> {
  await mutateManifest(manifest.memberId, () => storage.setItem(manifestKey(manifest.memberId), JSON.stringify(manifest)));
}

export async function getCompletedDownload(
  memberId: string,
  performanceId: string,
): Promise<DownloadManifestEntry | null> {
  const manifest = await readDownloadManifest(memberId);
  return manifest.entries[performanceId] ?? null;
}

export async function upsertCompletedDownload(
  entry: DownloadManifestEntry,
  isCurrent: ManifestGuard = () => true,
): Promise<void> {
  await mutateManifest(entry.memberId, async () => {
    assertDownloadCleanupCurrent(isCurrent);
    const manifest = await readDownloadManifest(entry.memberId);
    assertDownloadCleanupCurrent(isCurrent);
    manifest.entries[entry.performanceId] = entry;
    await storage.setItem(manifestKey(entry.memberId), JSON.stringify(manifest));
  });
}

export async function removeCompletedDownload(
  memberId: string,
  performanceId: string,
  isCurrent: ManifestGuard = () => true,
  beforeRemove?: (entry: DownloadManifestEntry | undefined) => void,
): Promise<void> {
  await mutateManifest(memberId, async () => {
    assertDownloadCleanupCurrent(isCurrent);
    const manifest = await readDownloadManifest(memberId);
    assertDownloadCleanupCurrent(isCurrent);
    beforeRemove?.(manifest.entries[performanceId]);
    delete manifest.entries[performanceId];
    await storage.setItem(manifestKey(memberId), JSON.stringify(manifest));
  });
}

export async function clearDownloadManifest(
  memberId?: string | null,
  isCurrent: ManifestGuard = () => true,
  beforeRemove?: (manifest: DownloadManifest) => void,
): Promise<void> {
  if (memberId) {
    await mutateManifest(memberId, async () => {
      assertDownloadCleanupCurrent(isCurrent);
      if (beforeRemove) {
        const manifest = await readDownloadManifest(memberId);
        assertDownloadCleanupCurrent(isCurrent);
        beforeRemove(manifest);
      }
      assertDownloadCleanupCurrent(isCurrent);
      await storage.removeItem(manifestKey(memberId));
    });
    return;
  }

  const keys = await storage.getAllKeys();
  assertDownloadCleanupCurrent(isCurrent);
  await Promise.all(keys.filter((key) => key.startsWith(MANIFEST_KEY_PREFIX)).map((key) =>
    clearDownloadManifest(key.slice(MANIFEST_KEY_PREFIX.length), isCurrent, beforeRemove),
  ));
}

async function migrateDownloadExtension(
  entry: DownloadManifestEntry,
  host: ReturnType<typeof getDownloadFileHost>,
  isCurrent: ManifestGuard,
): Promise<boolean> {
  const leaf = entry.localUri.split('/').pop() ?? '';
  if (leaf.includes('.')) return false;
  const extension = resolveDownloadAudioExtension(await host.readPrefix(entry.localUri, 4));
  assertDownloadCleanupCurrent(isCurrent);
  const migratedUri = `${entry.localUri}.${extension}`;
  await host.promote(entry.localUri, migratedUri);
  if (!isCurrent()) {
    host.deleteIfExists(migratedUri);
    assertDownloadCleanupCurrent(isCurrent);
  }
  entry.localUri = migratedUri;
  return true;
}

/**
 * Drop missing/zero-length completed files and scrub leftover `.part` files.
 * Partial or failed downloads never become completed entries.
 */
export async function reconcileDownloadManifest(
  memberId: string,
  isCurrent: ManifestGuard = () => true,
  canDeletePart: (uri: string) => boolean = () => true,
): Promise<DownloadManifest> {
  return mutateManifest(memberId, async () => {
    assertDownloadCleanupCurrent(isCurrent);
    const host = getDownloadFileHost();
    const manifest = await readDownloadManifest(memberId);
    assertDownloadCleanupCurrent(isCurrent);
    let dirty = false;

    for (const [id, entry] of Object.entries(manifest.entries)) {
      const exists = host.exists(entry.localUri);
      const size = exists ? host.size(entry.localUri) : 0;
      if (!exists || size <= 0 || entry.memberId !== memberId) {
        delete manifest.entries[id];
        if (exists) {
          host.deleteIfExists(entry.localUri);
        }
        dirty = true;
        continue;
      }

      dirty = await migrateDownloadExtension(entry, host, isCurrent) || dirty;
    }

    for (const partUri of host.listPartUris()) {
      if (canDeletePart(partUri) && (isDownloadFileForMember(partUri, memberId) || isLegacyDownloadFile(partUri))) {
        host.deleteIfExists(partUri);
      }
    }

    if (dirty) {
      assertDownloadCleanupCurrent(isCurrent);
      await storage.setItem(manifestKey(memberId), JSON.stringify(manifest));
    }

    return manifest;
  });
}
