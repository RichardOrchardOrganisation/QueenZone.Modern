import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError } from '../api/client';
import type { ApiPagedResponse } from '../api/types';
import { waitForMinimumRefreshVisibility } from './refreshVisibility';

export type PagedFetchMode = 'load' | 'refresh' | 'more';

type Fetcher<T> = (
  page: number,
  signal: AbortSignal,
  mode: PagedFetchMode,
) => Promise<ApiPagedResponse<T>>;

export type PagedState<T> = {
  items: T[];
  page: number;
  totalPages: number;
  totalCount: number;
  loading: boolean;
  refreshing: boolean;
  loadingMore: boolean;
  error: string | null;
  hasMore: boolean;
  reload: () => void;
  refresh: () => void;
  loadMore: () => void;
};

const IDENTITY_KEYS = ['id', 'albumId', 'picId', 'catId', 'conversationId', 'sourceKey'] as const;

function isAbortError(err: unknown): boolean {
  return err instanceof Error && err.name === 'AbortError';
}

function pagedItemKey(item: unknown): unknown {
  if (item !== null && typeof item === 'object') {
    const record = item as Record<string, unknown>;
    for (const key of IDENTITY_KEYS) {
      const value = record[key];
      if (typeof value === 'string' || typeof value === 'number') {
        return `${key}:${value}`;
      }
    }
  }
  return item;
}

export function appendUniquePagedItems<T>(previous: T[], incoming: T[]): T[] {
  if (incoming.length === 0) {
    return previous;
  }

  const seen = new Set(previous.map(pagedItemKey));
  const extra: T[] = [];
  for (const item of incoming) {
    const key = pagedItemKey(item);
    if (seen.has(key)) {
      continue;
    }
    seen.add(key);
    extra.push(item);
  }

  return extra.length === 0 ? previous : [...previous, ...extra];
}

/**
 * Owns AbortControllers and a generation counter so only the latest request
 * may commit. Reset, refresh, load-more, and unmount all go through here.
 */
export class PagedRequestCoordinator {
  private generation = 0;
  private controller: AbortController | null = null;

  begin(): { generation: number; signal: AbortSignal } {
    this.controller?.abort();
    this.generation += 1;
    this.controller = new AbortController();
    return { generation: this.generation, signal: this.controller.signal };
  }

  /** Drop every in-flight request and invalidate their generation (reset/unmount). */
  invalidate(): void {
    this.generation += 1;
    this.controller?.abort();
    this.controller = null;
  }

  isCurrent(generation: number): boolean {
    return generation === this.generation;
  }
}

/**
 * Loads a paged `/api/v1` collection with pull-to-refresh and infinite scroll.
 */
export function usePagedContent<T>(
  fetcher: Fetcher<T>,
  pageSize = 20,
  resetKey: string | number = 0,
): PagedState<T> {
  const [items, setItems] = useState<T[]>([]);
  const [page, setPage] = useState(0);
  const [totalPages, setTotalPages] = useState(0);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);
  const fetcherRef = useRef(fetcher);
  fetcherRef.current = fetcher;
  const coordinatorRef = useRef<PagedRequestCoordinator | null>(null);
  if (coordinatorRef.current === null) {
    coordinatorRef.current = new PagedRequestCoordinator();
  }
  const coordinator = coordinatorRef.current;
  const pageRef = useRef(0);
  const totalPagesRef = useRef(0);
  const loadingRef = useRef(true);
  const refreshingRef = useRef(false);
  const loadingMoreRef = useRef(false);

  const applyPageMeta = (response: ApiPagedResponse<T>) => {
    pageRef.current = response.page;
    totalPagesRef.current = response.totalPages;
    setPage(response.page);
    setTotalPages(response.totalPages);
    setTotalCount(response.totalCount);
  };

  useEffect(() => {
    const { generation, signal } = coordinator.begin();
    loadingRef.current = true;
    refreshingRef.current = false;
    loadingMoreRef.current = false;
    pageRef.current = 0;
    totalPagesRef.current = 0;
    setLoading(true);
    setRefreshing(false);
    setLoadingMore(false);
    setError(null);
    setItems([]);
    setPage(0);
    setTotalPages(0);
    setTotalCount(0);

    fetcherRef
      .current(1, signal, 'load')
      .then((response) => {
        if (!coordinator.isCurrent(generation) || signal.aborted) {
          return;
        }
        setItems(response.items);
        applyPageMeta(response);
        loadingRef.current = false;
        setLoading(false);
      })
      .catch((err: unknown) => {
        if (!coordinator.isCurrent(generation) || signal.aborted || isAbortError(err)) {
          return;
        }
        const message = err instanceof ApiError ? err.message : 'Something went wrong.';
        setError(message);
        loadingRef.current = false;
        setLoading(false);
      });

    return () => coordinator.invalidate();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- generation-guard omit: applyPageMeta is a local helper; listing it would retrigger the load effect.
  }, [coordinator, reloadToken, pageSize, resetKey]);

  const refresh = useCallback(() => {
    const { generation, signal } = coordinator.begin();
    const startedAt = Date.now();
    refreshingRef.current = true;
    loadingMoreRef.current = false;
    setRefreshing(true);
    setLoadingMore(false);
    setError(null);

    const finishRefresh = (apply: () => void) => {
      apply();
      void waitForMinimumRefreshVisibility(startedAt, signal).then(() => {
        if (!coordinator.isCurrent(generation) || signal.aborted) {
          return;
        }
        refreshingRef.current = false;
        setRefreshing(false);
      });
    };

    fetcherRef
      .current(1, signal, 'refresh')
      .then((response) => {
        if (!coordinator.isCurrent(generation) || signal.aborted) {
          return;
        }
        finishRefresh(() => {
          setItems(response.items);
          applyPageMeta(response);
          loadingRef.current = false;
          setLoading(false);
        });
      })
      .catch((err: unknown) => {
        if (!coordinator.isCurrent(generation) || signal.aborted || isAbortError(err)) {
          return;
        }
        finishRefresh(() => {
          const message = err instanceof ApiError ? err.message : 'Something went wrong.';
          setError(message);
          loadingRef.current = false;
          setLoading(false);
        });
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps -- generation-guard omit: applyPageMeta is a local helper; listing it would recreate refresh each render.
  }, [coordinator]);

  const loadMore = useCallback(() => {
    if (loadingRef.current || refreshingRef.current || loadingMoreRef.current) {
      return;
    }
    if (pageRef.current >= totalPagesRef.current || totalPagesRef.current === 0) {
      return;
    }
    const nextPage = pageRef.current + 1;
    const { generation, signal } = coordinator.begin();
    loadingMoreRef.current = true;
    setLoadingMore(true);
    fetcherRef
      .current(nextPage, signal, 'more')
      .then((response) => {
        if (!coordinator.isCurrent(generation) || signal.aborted) {
          return;
        }
        setItems((prev) => appendUniquePagedItems(prev, response.items));
        applyPageMeta(response);
        loadingMoreRef.current = false;
        setLoadingMore(false);
      })
      .catch((err: unknown) => {
        if (!coordinator.isCurrent(generation) || signal.aborted || isAbortError(err)) {
          return;
        }
        loadingMoreRef.current = false;
        setLoadingMore(false);
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps -- generation-guard omit: applyPageMeta is a local helper; listing it would recreate loadMore each render.
  }, [coordinator]);

  return {
    items,
    page,
    totalPages,
    totalCount,
    loading,
    refreshing,
    loadingMore,
    error,
    hasMore: totalPages > 0 && page < totalPages,
    reload: () => setReloadToken((n) => n + 1),
    refresh,
    loadMore,
  };
}
