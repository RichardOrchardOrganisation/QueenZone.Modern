import { act, renderHook, waitFor } from '@testing-library/react-native';
import { ApiError } from '../api/client';
import { appendUniquePagedItems, PagedRequestCoordinator, usePagedContent } from './usePagedContent';
import { deferred, pagedResponse } from '../test/fixtures';

async function flush(): Promise<void> {
  await act(async () => {
    await Promise.resolve();
  });
}

describe('appendUniquePagedItems', () => {
  it('skips incoming items that share a stable identity with the current page', () => {
    expect(appendUniquePagedItems([{ id: 1 }, { id: 2 }], [{ id: 2 }, { id: 3 }])).toEqual([
      { id: 1 },
      { id: 2 },
      { id: 3 },
    ]);
    expect(appendUniquePagedItems([{ albumId: 10 }], [{ albumId: 10 }, { albumId: 11 }])).toEqual([
      { albumId: 10 },
      { albumId: 11 },
    ]);
    expect(appendUniquePagedItems(['a', 'b'], ['b', 'c'])).toEqual(['a', 'b', 'c']);
    expect(appendUniquePagedItems(['a'], [])).toEqual(['a']);
  });
});

describe('PagedRequestCoordinator', () => {
  it('aborts the previous request and only treats the latest generation as current', () => {
    const coordinator = new PagedRequestCoordinator();
    const first = coordinator.begin();
    const second = coordinator.begin();

    expect(first.signal.aborted).toBe(true);
    expect(second.signal.aborted).toBe(false);
    expect(coordinator.isCurrent(first.generation)).toBe(false);
    expect(coordinator.isCurrent(second.generation)).toBe(true);

    coordinator.invalidate();
    expect(second.signal.aborted).toBe(true);
    expect(coordinator.isCurrent(second.generation)).toBe(false);
  });
});

describe('usePagedContent', () => {
  it('loads the first page, then supports empty, error, retry, refresh, and load-more', async () => {
    const fetcher = jest.fn();
    fetcher.mockResolvedValueOnce(pagedResponse(['a', 'b'], 1, 2));
    const { result } = renderHook(() => usePagedContent(fetcher));

    expect(result.current.loading).toBe(true);
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.items).toEqual(['a', 'b']);
    expect(result.current.hasMore).toBe(true);
    expect(fetcher).toHaveBeenCalledWith(1, expect.any(AbortSignal), 'load');

    fetcher.mockResolvedValueOnce(pagedResponse(['c'], 2, 2));
    await act(async () => {
      result.current.loadMore();
    });
    await waitFor(() => expect(result.current.items).toEqual(['a', 'b', 'c']));
    expect(result.current.hasMore).toBe(false);

    fetcher.mockResolvedValueOnce(pagedResponse(['z'], 1, 1));
    await act(async () => {
      result.current.refresh();
    });
    expect(result.current.refreshing).toBe(true);
    await waitFor(() => expect(result.current.items).toEqual(['z']));
    expect(result.current.refreshing).toBe(true);
    await waitFor(() => expect(result.current.refreshing).toBe(false));
    expect(fetcher).toHaveBeenLastCalledWith(1, expect.any(AbortSignal), 'refresh');
  });

  it('suppresses duplicate load-more while a request is in flight or at the end', async () => {
    const second = deferred<ReturnType<typeof pagedResponse<string>>>();
    const fetcher = jest.fn();
    fetcher.mockResolvedValueOnce(pagedResponse(['a'], 1, 3));
    fetcher.mockReturnValueOnce(second.promise);
    const { result } = renderHook(() => usePagedContent(fetcher));
    await waitFor(() => expect(result.current.loading).toBe(false));

    await act(async () => {
      result.current.loadMore();
    });
    await waitFor(() => expect(result.current.loadingMore).toBe(true));
    await act(async () => {
      result.current.loadMore();
    });
    expect(fetcher).toHaveBeenCalledTimes(2);

    second.resolve(pagedResponse(['b'], 2, 3));
    await waitFor(() => expect(result.current.items).toEqual(['a', 'b']));

    fetcher.mockResolvedValueOnce(pagedResponse(['c'], 3, 3));
    await act(async () => {
      result.current.loadMore();
    });
    await waitFor(() => expect(result.current.items).toEqual(['a', 'b', 'c']));
    expect(result.current.hasMore).toBe(false);

    await act(async () => {
      result.current.loadMore();
    });
    expect(fetcher).toHaveBeenCalledTimes(3);
  });

  it('lets a newer refresh win over a slow initial page', async () => {
    const initial = deferred<ReturnType<typeof pagedResponse<string>>>();
    const refreshed = deferred<ReturnType<typeof pagedResponse<string>>>();
    const fetcher = jest.fn();
    fetcher.mockReturnValueOnce(initial.promise);
    fetcher.mockReturnValueOnce(refreshed.promise);
    const { result } = renderHook(() => usePagedContent(fetcher));

    await act(async () => {
      result.current.refresh();
    });
    const initialSignal = fetcher.mock.calls[0][1] as AbortSignal;
    expect(initialSignal.aborted).toBe(true);
    expect(result.current.refreshing).toBe(true);

    refreshed.resolve(pagedResponse(['fresh'], 1, 1));
    await waitFor(() => expect(result.current.items).toEqual(['fresh']));
    expect(result.current.loading).toBe(false);
    expect(result.current.refreshing).toBe(true);
    await waitFor(() => expect(result.current.refreshing).toBe(false));

    initial.resolve(pagedResponse(['stale'], 1, 1));
    await flush();
    expect(result.current.items).toEqual(['fresh']);
  });

  it('does not append a stale load-more page after refresh resets page 1', async () => {
    const extra = deferred<ReturnType<typeof pagedResponse<string>>>();
    const refreshed = deferred<ReturnType<typeof pagedResponse<string>>>();
    const fetcher = jest.fn();
    fetcher.mockResolvedValueOnce(pagedResponse(['a'], 1, 2));
    fetcher.mockReturnValueOnce(extra.promise);
    fetcher.mockReturnValueOnce(refreshed.promise);
    const { result } = renderHook(() => usePagedContent(fetcher));
    await waitFor(() => expect(result.current.loading).toBe(false));

    await act(async () => {
      result.current.loadMore();
    });
    const loadMoreSignal = fetcher.mock.calls[1][1] as AbortSignal;
    await act(async () => {
      result.current.refresh();
    });
    expect(loadMoreSignal.aborted).toBe(true);
    expect(result.current.loadingMore).toBe(false);
    expect(result.current.refreshing).toBe(true);

    refreshed.resolve(pagedResponse(['fresh'], 1, 1));
    await waitFor(() => expect(result.current.items).toEqual(['fresh']));

    extra.resolve(pagedResponse(['stale-page-2'], 2, 2));
    await flush();
    expect(result.current.items).toEqual(['fresh']);
    expect(result.current.items).not.toContain('stale-page-2');
    expect(result.current.loadingMore).toBe(false);
    await waitFor(() => expect(result.current.refreshing).toBe(false));
  });

  it('cancels an in-flight refresh when another refresh starts', async () => {
    const first = deferred<ReturnType<typeof pagedResponse<string>>>();
    const second = deferred<ReturnType<typeof pagedResponse<string>>>();
    const fetcher = jest.fn();
    fetcher.mockResolvedValueOnce(pagedResponse(['seed'], 1, 1));
    fetcher.mockReturnValueOnce(first.promise);
    fetcher.mockReturnValueOnce(second.promise);
    const { result } = renderHook(() => usePagedContent(fetcher));
    await waitFor(() => expect(result.current.items).toEqual(['seed']));

    await act(async () => {
      result.current.refresh();
    });
    await act(async () => {
      result.current.refresh();
    });
    const firstRefreshSignal = fetcher.mock.calls[1][1] as AbortSignal;
    expect(firstRefreshSignal.aborted).toBe(true);
    expect(fetcher).toHaveBeenCalledTimes(3);
    expect(result.current.refreshing).toBe(true);

    first.resolve(pagedResponse(['older'], 1, 1));
    second.resolve(pagedResponse(['newer'], 1, 1));
    await waitFor(() => expect(result.current.items).toEqual(['newer']));
    await flush();
    expect(result.current.items).toEqual(['newer']);
    await waitFor(() => expect(result.current.refreshing).toBe(false));
  });

  it('resets and refetches when resetKey changes, ignoring aborted in-flight results', async () => {
    const first = deferred<ReturnType<typeof pagedResponse<string>>>();
    const fetcher = jest.fn();
    fetcher.mockReturnValueOnce(first.promise);
    fetcher.mockResolvedValueOnce(pagedResponse(['fresh'], 1, 1));
    const { result, rerender } = renderHook(
      ({ key }: { key: string }) => usePagedContent(fetcher, 20, key),
      {
        initialProps: { key: 'one' },
      },
    );
    expect(result.current.loading).toBe(true);
    const firstSignal = fetcher.mock.calls[0][1] as AbortSignal;
    rerender({ key: 'two' });
    expect(firstSignal.aborted).toBe(true);
    first.resolve(pagedResponse(['stale'], 1, 1));
    await waitFor(() => expect(result.current.items).toEqual(['fresh']));
    expect(result.current.items).not.toContain('stale');
    expect(result.current.loading).toBe(false);
  });

  it('aborts an in-flight refresh when resetKey changes', async () => {
    const refreshPending = deferred<ReturnType<typeof pagedResponse<string>>>();
    const fetcher = jest.fn();
    fetcher.mockResolvedValueOnce(pagedResponse(['seed'], 1, 1));
    fetcher.mockReturnValueOnce(refreshPending.promise);
    fetcher.mockResolvedValueOnce(pagedResponse(['reset'], 1, 1));
    const { result, rerender } = renderHook(
      ({ key }: { key: string }) => usePagedContent(fetcher, 20, key),
      {
        initialProps: { key: 'one' },
      },
    );
    await waitFor(() => expect(result.current.items).toEqual(['seed']));

    await act(async () => {
      result.current.refresh();
    });
    const refreshSignal = fetcher.mock.calls[1][1] as AbortSignal;
    rerender({ key: 'two' });
    expect(refreshSignal.aborted).toBe(true);
    refreshPending.resolve(pagedResponse(['stale-refresh'], 1, 1));
    await waitFor(() => expect(result.current.items).toEqual(['reset']));
    await flush();
    expect(result.current.items).not.toContain('stale-refresh');
    await waitFor(() => expect(result.current.refreshing).toBe(false));
    expect(result.current.loading).toBe(false);
  });

  it('does not apply a response after unmount, including in-flight refresh and load-more', async () => {
    const initial = deferred<ReturnType<typeof pagedResponse<string>>>();
    const fetcher = jest.fn().mockReturnValue(initial.promise);
    const { result, unmount } = renderHook(() => usePagedContent(fetcher));
    const initialSignal = fetcher.mock.calls[0][1] as AbortSignal;
    unmount();
    expect(initialSignal.aborted).toBe(true);
    initial.resolve(pagedResponse(['late'], 1, 1));
    await flush();
    expect(result.current.items).toEqual([]);
    expect(result.current.loading).toBe(true);
  });

  it('aborts refresh and load-more on unmount without committing later results', async () => {
    const extra = deferred<ReturnType<typeof pagedResponse<string>>>();
    const fetcher = jest.fn();
    fetcher.mockResolvedValueOnce(pagedResponse(['a'], 1, 2));
    fetcher.mockReturnValueOnce(extra.promise);
    const { result, unmount } = renderHook(() => usePagedContent(fetcher));
    await waitFor(() => expect(result.current.loading).toBe(false));

    await act(async () => {
      result.current.loadMore();
    });
    const loadMoreSignal = fetcher.mock.calls[1][1] as AbortSignal;
    unmount();
    expect(loadMoreSignal.aborted).toBe(true);
    extra.resolve(pagedResponse(['late-page-2'], 2, 2));
    await flush();
    expect(result.current.items).toEqual(['a']);
    expect(result.current.loadingMore).toBe(true);
  });

  it('settles loading flags on error after a superseded request is aborted', async () => {
    const initial = deferred<ReturnType<typeof pagedResponse<string>>>();
    const fetcher = jest.fn();
    fetcher.mockReturnValueOnce(initial.promise);
    fetcher.mockRejectedValueOnce(new ApiError(500, 'The server had a problem. Try again shortly.'));
    const { result } = renderHook(() => usePagedContent(fetcher));

    await act(async () => {
      result.current.refresh();
    });
    initial.reject(Object.assign(new Error('Aborted'), { name: 'AbortError' }));
    await waitFor(() => expect(result.current.error).toBe('The server had a problem. Try again shortly.'));
    expect(result.current.loading).toBe(false);
    await waitFor(() => expect(result.current.refreshing).toBe(false));
  });

  it('settles refresh on a non-ApiError and load-more on a failed next page', async () => {
    const fetcher = jest.fn();
    fetcher.mockResolvedValueOnce(pagedResponse(['a'], 1, 2));
    const { result } = renderHook(() => usePagedContent(fetcher));
    await waitFor(() => expect(result.current.loading).toBe(false));

    fetcher.mockRejectedValueOnce(new Error('network down'));
    await act(async () => {
      result.current.refresh();
    });
    await waitFor(() => expect(result.current.error).toBe('Something went wrong.'));
    await waitFor(() => expect(result.current.refreshing).toBe(false));
    expect(result.current.loading).toBe(false);

    fetcher.mockResolvedValueOnce(pagedResponse(['a'], 1, 2));
    await act(async () => {
      result.current.reload();
    });
    await waitFor(() => expect(result.current.items).toEqual(['a']));

    fetcher.mockRejectedValueOnce(new Error('page 2 failed'));
    await act(async () => {
      result.current.loadMore();
    });
    await waitFor(() => expect(result.current.loadingMore).toBe(false));
    expect(result.current.items).toEqual(['a']);
  });

  it('does not append overlapping load-more identities', async () => {
    const fetcher = jest.fn();
    fetcher.mockResolvedValueOnce(pagedResponse([{ id: 1 }, { id: 2 }], 1, 2));
    fetcher.mockResolvedValueOnce(pagedResponse([{ id: 2 }, { id: 3 }], 2, 2));
    const { result } = renderHook(() => usePagedContent(fetcher));
    await waitFor(() => expect(result.current.loading).toBe(false));

    await act(async () => {
      result.current.loadMore();
    });
    await waitFor(() => expect(result.current.items).toEqual([{ id: 1 }, { id: 2 }, { id: 3 }]));
  });

  it('surfaces ApiError messages and recovers on reload', async () => {
    const fetcher = jest.fn();
    fetcher.mockRejectedValueOnce(new ApiError(500, 'The server had a problem. Try again shortly.'));
    fetcher.mockResolvedValueOnce(pagedResponse(['ok'], 1, 1));
    const { result } = renderHook(() => usePagedContent(fetcher));
    await waitFor(() => expect(result.current.error).toBe('The server had a problem. Try again shortly.'));

    await act(async () => {
      result.current.reload();
    });
    await waitFor(() => expect(result.current.items).toEqual(['ok']));
    expect(result.current.error).toBeNull();
  });
});
