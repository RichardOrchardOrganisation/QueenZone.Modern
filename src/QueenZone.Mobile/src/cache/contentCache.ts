import type { KeyValueStorage } from './storage';

export type ContentCacheOptions = {
  storage: KeyValueStorage;
  /** Hard cap on stored detail payloads (LRU eviction). Default 80. */
  maxEntries?: number;
  /** Prefix for entry keys. Default `qz:content:`. */
  keyPrefix?: string;
  /** Envelope schema. Mismatched or missing versions self-delete. */
  schemaVersion?: number;
};

export type CacheRecord<T> = {
  payload: T;
  cachedAt: string;
};

type StoredEnvelope = {
  schemaVersion: number;
  accessedAt: string;
  /** Monotonic tie-breaker when `accessedAt` collides within the same ms. */
  accessSeq: number;
  cachedAt: string;
  payloadJson: string;
};

/** First versioned envelope. Unversioned rows from the archive-only cache self-delete. */
export const CONTENT_CACHE_SCHEMA_VERSION = 1;

/**
 * Device LRU cap after #764 sizing: archive details plus forum topic/page
 * snapshots and member-scoped conversations. See hosting-scale-and-cache.md.
 */
export const CONTENT_CACHE_MAX_ENTRIES = 80;

const DEFAULT_PREFIX = 'qz:content:';

function isStoredEnvelope(value: unknown): value is StoredEnvelope {
  if (value === null || typeof value !== 'object') {
    return false;
  }
  const envelope = value as StoredEnvelope;
  return (
    typeof envelope.schemaVersion === 'number' &&
    typeof envelope.accessedAt === 'string' &&
    typeof envelope.accessSeq === 'number' &&
    typeof envelope.cachedAt === 'string' &&
    typeof envelope.payloadJson === 'string'
  );
}

/** A request lease is invalidated synchronously, even while device I/O is pending. */
export type CacheLease = {
  readonly current: boolean;
  release(): void;
};

type ActiveLease = CacheLease & { key: string; current: boolean };
type AccessMetadata = { accessedAt: string; accessSeq: number };

/**
 * Single-owner offline store. All storage operations are serialized; purge invalidates
 * outstanding leases before joining that queue, then deletes any already-started write.
 * A compact, recoverable LRU index avoids reading every payload during eviction.
 * Use the process singleton: two owners of the same storage prefix are unsupported.
 */
export class ContentCache {
  private readonly storage: KeyValueStorage;
  private readonly maxEntries: number;
  private readonly keyPrefix: string;
  private readonly schemaVersion: number;
  private accessSeq = 0;
  private tail: Promise<unknown> = Promise.resolve();
  private index: Map<string, AccessMetadata> | null = null;
  private readonly leases = new Set<ActiveLease>();

  constructor(options: ContentCacheOptions) {
    this.storage = options.storage;
    this.maxEntries = options.maxEntries ?? CONTENT_CACHE_MAX_ENTRIES;
    this.keyPrefix = options.keyPrefix ?? DEFAULT_PREFIX;
    this.schemaVersion = options.schemaVersion ?? CONTENT_CACHE_SCHEMA_VERSION;
  }

  entryKey(cacheKey: string): string {
    return `${this.keyPrefix}${cacheKey}`;
  }

  /** Capture before starting network work, release in finally. No per-member history retained. */
  acquireLease(cacheKey: string): CacheLease {
    const lease: ActiveLease = {
      key: cacheKey,
      current: true,
      release: () => { this.leases.delete(lease); },
    };
    this.leases.add(lease);
    return lease;
  }

  async get<T>(cacheKey: string): Promise<T | null> {
    const record = await this.read<T>(cacheKey);
    return record ? record.payload : null;
  }

  async read<T>(cacheKey: string, requestLease?: CacheLease): Promise<CacheRecord<T> | null> {
    const lease = requestLease ?? this.acquireLease(cacheKey);
    try {
      return await this.serial(async () => {
        if (!lease.current) return null;
        try {
          await this.ensureIndex();
        } catch {
          // Offline reads do not depend on optional LRU index availability.
          this.index = null;
        }
        const key = this.entryKey(cacheKey);
        const raw = await this.storage.getItem(key);
        if (!lease.current) return null;
        const envelope = this.parseEnvelope(raw);
        if (!envelope) {
          await this.deleteKeys([key]);
          return null;
        }
        let payload: T;
        try {
          payload = JSON.parse(envelope.payloadJson) as T;
        } catch {
          await this.deleteKeys([key]);
          return null;
        }
        if (this.index) {
          this.index.set(key, this.nextAccess());
          // Only compact metadata is rewritten on hits, never the payload envelope.
          try {
            await this.persistIndex();
          } catch {
            // An optional LRU metadata write must not destroy offline fallback.
            this.index = null;
          }
        }
        return lease.current ? { payload, cachedAt: envelope.cachedAt } : null;
      });
    } finally {
      if (!requestLease) lease.release();
    }
  }

  async put<T>(cacheKey: string, payload: T, requestLease?: CacheLease): Promise<string> {
    const lease = requestLease ?? this.acquireLease(cacheKey);
    const now = new Date().toISOString();
    try {
      return await this.serial(async () => {
        if (!lease.current) return now;
        await this.ensureIndex();
        if (!lease.current) return now;
        const key = this.entryKey(cacheKey);
        const access = this.nextAccess();
        const envelope: StoredEnvelope = {
          schemaVersion: this.schemaVersion,
          ...access,
          cachedAt: now,
          payloadJson: JSON.stringify(payload),
        };
        await this.storage.setItem(key, JSON.stringify(envelope));
        this.index!.set(key, access);
        // A purge queued during setItem will remove this completed write before
        // any newer session's operation can run.
        await this.evictIfNeeded();
        await this.persistIndex();
        return now;
      });
    } finally {
      if (!requestLease) lease.release();
    }
  }

  async remove(cacheKey: string, requestLease?: CacheLease): Promise<void> {
    // An old session's late 401 must not invalidate a newer session's result.
    if (requestLease && !requestLease.current) return;
    this.invalidate((key) => key === cacheKey);
    await this.serial(async () => {
      // Authoritative HTTP invalidation must not depend on optional index reads.
      const key = this.entryKey(cacheKey);
      await this.storage.removeItem(key);
      try {
        await this.ensureIndex();
        this.index!.delete(key);
        await this.persistIndex();
      } catch {
        // Payload deletion has already succeeded. Discard unusable metadata so
        // private key names do not outlive successful authoritative removal.
        this.index = null;
        await this.storage.removeItem(this.metadataKey);
      }
    });
  }

  async purgePrefix(cacheKeyPrefix: string): Promise<void> {
    this.invalidate((key) => key.startsWith(cacheKeyPrefix));
    await this.serial(async () => {
      const keys = (await this.listEntryKeys()).filter((key) => key.startsWith(this.entryKey(cacheKeyPrefix)));
      // Preserve recency for retained entries. Recovery failure must not stop
      // deletion of private payloads; dropping metadata is the fallback only.
      try {
        await this.ensureIndex();
      } catch {
        this.index = null;
      }
      await this.storage.multiRemove(keys);
      if (this.index) {
        for (const key of keys) this.index.delete(key);
        await this.persistIndex();
      } else {
        await this.storage.removeItem(this.metadataKey);
      }
    });
  }

  async clear(): Promise<void> {
    await this.purgePrefix('');
  }

  async size(): Promise<number> {
    return this.serial(async () => (await this.listEntryKeys()).length);
  }

  async listCacheKeys(): Promise<string[]> {
    return this.serial(async () => (await this.listEntryKeys()).map((key) => key.slice(this.keyPrefix.length)));
  }

  private get metadataKey(): string {
    return `${this.keyPrefix}$lru-v1`;
  }

  private invalidate(matches: (key: string) => boolean): void {
    for (const lease of this.leases) {
      if (matches(lease.key)) lease.current = false;
    }
  }

  private serial<T>(operation: () => Promise<T>): Promise<T> {
    const pending = this.tail.then(operation);
    this.tail = pending.catch(() => { this.index = null; });
    return pending;
  }

  private nextAccess(): AccessMetadata {
    return { accessedAt: new Date().toISOString(), accessSeq: ++this.accessSeq };
  }

  private parseEnvelope(raw: string | null): StoredEnvelope | null {
    try {
      const value: unknown = raw ? JSON.parse(raw) : null;
      return isStoredEnvelope(value) && value.schemaVersion === this.schemaVersion ? value : null;
    } catch {
      return null;
    }
  }

  private async listEntryKeys(): Promise<string[]> {
    const all = await this.storage.getAllKeys();
    return all.filter((key) => key.startsWith(this.keyPrefix) && key !== this.metadataKey);
  }

  private async ensureIndex(): Promise<void> {
    if (this.index) return;
    const keys = await this.listEntryKeys();
    let saved: Record<string, AccessMetadata> = {};
    try {
      const raw = await this.storage.getItem(this.metadataKey);
      const parsed: unknown = raw ? JSON.parse(raw) : null;
      if (parsed && typeof parsed === 'object' && 'version' in parsed && parsed.version === 1 &&
          'entries' in parsed && parsed.entries && typeof parsed.entries === 'object') {
        saved = parsed.entries as Record<string, AccessMetadata>;
      }
    } catch {
      // Recover from missing/corrupt/failed metadata without discarding payloads.
    }
    const index = new Map<string, AccessMetadata>();
    for (const key of keys) {
      let access = Object.hasOwn(saved, key) ? saved[key] : undefined;
      if (!access || typeof access.accessedAt !== 'string' || !Number.isFinite(access.accessSeq)) {
        // An unrelated unreadable entry must not hide a healthy offline payload.
        // Unknown metadata receives the oldest score and can be evicted normally.
        let envelope: StoredEnvelope | null = null;
        try {
          envelope = this.parseEnvelope(await this.storage.getItem(key));
        } catch {}
        access = envelope ?? { accessedAt: '', accessSeq: 0 };
      }
      index.set(key, { accessedAt: access.accessedAt, accessSeq: access.accessSeq });
      this.accessSeq = Math.max(this.accessSeq, access.accessSeq);
    }
    this.index = index;
  }

  private async persistIndex(): Promise<void> {
    await this.storage.setItem(this.metadataKey, JSON.stringify({ version: 1, entries: Object.fromEntries(this.index!) }));
  }

  private async deleteKeys(keys: string[]): Promise<void> {
    await this.storage.multiRemove(keys);
    for (const key of keys) this.index?.delete(key);
    if (this.index) await this.persistIndex();
  }

  private async evictIfNeeded(): Promise<void> {
    const entries = [...this.index!];
    if (entries.length <= this.maxEntries) return;
    entries.sort(([ka, a], [kb, b]) => a.accessedAt.localeCompare(b.accessedAt) || a.accessSeq - b.accessSeq || ka.localeCompare(kb));
    const keys = entries.slice(0, entries.length - this.maxEntries).map(([key]) => key);
    await this.storage.multiRemove(keys);
    for (const key of keys) this.index!.delete(key);
    // put persists the final compact index once after eviction finishes.
  }
}
