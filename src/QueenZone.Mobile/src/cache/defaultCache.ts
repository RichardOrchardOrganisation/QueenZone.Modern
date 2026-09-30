import { createAsyncStorageAdapter } from './asyncStorageAdapter';
import { CONTENT_CACHE_MAX_ENTRIES, ContentCache } from './contentCache';
import { invalidatePrefix } from './externalStore';
import {
  DOWNLOAD_UI_CACHE_KEY_PREFIX,
  PRIVATE_CACHE_KEY_PREFIX,
  downloadUiCachePrefix,
  privateMemberCachePrefix,
} from './keys';

let shared: ContentCache | null = null;

/** Process-wide content cache backed by AsyncStorage. */
export function getContentCache(): ContentCache {
  if (!shared) {
    shared = new ContentCache({
      storage: createAsyncStorageAdapter(),
      maxEntries: CONTENT_CACHE_MAX_ENTRIES,
    });
  }
  return shared;
}

/** Test helper to replace or clear the singleton. */
export function setContentCacheForTests(cache: ContentCache | null): void {
  shared = cache;
}

/** Drop conversation snapshots. Public forum cache is left in place. */
export async function purgePrivateContentCache(memberId?: string | null): Promise<void> {
  const prefix = memberId ? privateMemberCachePrefix(memberId) : PRIVATE_CACHE_KEY_PREFIX;
  const downloadPrefix = memberId ? downloadUiCachePrefix(memberId) : DOWNLOAD_UI_CACHE_KEY_PREFIX;
  // Start purge before notifying listeners: outstanding network leases become
  // invalid synchronously, even when device storage is stalled.
  const purge = getContentCache().purgePrefix(prefix);
  invalidatePrefix(prefix);
  invalidatePrefix(downloadPrefix);
  await purge;
}
