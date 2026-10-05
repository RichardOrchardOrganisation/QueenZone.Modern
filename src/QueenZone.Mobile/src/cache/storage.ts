/**
 * Minimal async key-value store used by the offline content cache.
 * Production uses AsyncStorage; tests use an in-memory map.
 */
export type KeyValueStorage = {
  getItem(key: string): Promise<string | null>;
  setItem(key: string, value: string): Promise<void>;
  removeItem(key: string): Promise<void>;
  getAllKeys(): Promise<readonly string[]>;
  multiRemove(keys: readonly string[]): Promise<void>;
};

export function createMemoryStorage(
  initial?: Record<string, string>,
): KeyValueStorage {
  const map = new Map<string, string>(Object.entries(initial ?? {}));

  return {
    getItem(key) {
      return Promise.resolve(map.get(key) ?? null);
    },
    setItem(key, value) {
      map.set(key, value);
      return Promise.resolve();
    },
    removeItem(key) {
      map.delete(key);
      return Promise.resolve();
    },
    getAllKeys() {
      return Promise.resolve([...map.keys()]);
    },
    multiRemove(keys) {
      for (const key of keys) {
        map.delete(key);
      }
      return Promise.resolve();
    },
  };
}
