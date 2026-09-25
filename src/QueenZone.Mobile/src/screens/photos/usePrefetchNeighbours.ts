import { Image } from 'expo-image';
import { useCallback, useEffect, useRef } from 'react';

/**
 * Prefetch ±1 neighbour originals after the current picture loads.
 * Stable callback, ref-based, no state — the screen chrome does not re-render.
 */
export function usePrefetchNeighbours(
  currentUri: string | null | undefined,
  neighbourUris: Array<string | null | undefined>,
): (uri: string) => void {
  const currentUriRef = useRef(currentUri);
  const neighbourUrisRef = useRef(neighbourUris);
  const loadedUriRef = useRef<string | null>(null);
  const prefetchedForRef = useRef<string | null>(null);
  currentUriRef.current = currentUri;
  neighbourUrisRef.current = neighbourUris;

  const runPrefetch = useCallback(() => {
    const current = currentUriRef.current;
    if (current == null || loadedUriRef.current !== current) {
      return;
    }
    if (prefetchedForRef.current === current) {
      return;
    }

    const uris = neighbourUrisRef.current.filter(
      (value): value is string => typeof value === 'string' && value.length > 0,
    );
    if (uris.length === 0) {
      return;
    }

    prefetchedForRef.current = current;
    void Image.prefetch(uris, 'memory-disk').catch(() => {});
  }, []);

  const neighbourKey = neighbourUris.filter((value) => typeof value === 'string' && value.length > 0).join('\0');
  useEffect(() => {
    runPrefetch();
  }, [currentUri, neighbourKey, runPrefetch]);

  return useCallback(
    (uri: string) => {
      if (uri !== currentUriRef.current) {
        return;
      }
      loadedUriRef.current = uri;
      runPrefetch();
    },
    [runPrefetch],
  );
}
