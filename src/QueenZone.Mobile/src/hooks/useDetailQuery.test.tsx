import { act, renderHook, waitFor } from '@testing-library/react-native';
import { ApiError } from '../api/client';
import { OFFLINE_MESSAGE, TIMEOUT_MESSAGE } from '../api/errors';
import { deferred } from '../test/fixtures';
import { useDetailQuery } from './useDetailQuery';

async function flush(): Promise<void> {
  await act(async () => {
    await Promise.resolve();
  });
}

describe('useDetailQuery', () => {
  it('shows loading then the loaded resource', async () => {
    const pending = deferred<string>();
    const fetcher = jest.fn().mockReturnValueOnce(pending.promise);
    const { result } = await renderHook(() => useDetailQuery(fetcher));

    expect(result.current).toMatchObject({ data: null, error: null, loading: true });
    pending.resolve('article');
    await waitFor(() =>
      expect(result.current).toMatchObject({ data: 'article', error: null, loading: false }),
    );
  });

  it('maps an ApiError and retries from the failed state', async () => {
    const retry = deferred<string>();
    const fetcher = jest.fn();
    fetcher.mockRejectedValueOnce(
      new ApiError(0, 'Unable to reach QueenZone. Check your connection and try again.'),
    );
    fetcher.mockReturnValueOnce(retry.promise);
    const { result } = await renderHook(() => useDetailQuery(fetcher));

    await waitFor(() =>
      expect(result.current).toMatchObject({
        data: null,
        error: 'Unable to reach QueenZone. Check your connection and try again.',
        loading: false,
      }),
    );

    await act(async () => {
      result.current.reload();
    });
    expect(result.current.loading).toBe(true);
    retry.resolve('recovered');
    await waitFor(() =>
      expect(result.current).toMatchObject({ data: 'recovered', error: null, loading: false }),
    );
  });

  it('ignores a delayed offline reject from the aborted first load', async () => {
    const first = deferred<string>();
    const second = deferred<string>();
    const fetcher = jest.fn().mockReturnValueOnce(first.promise);
    const { result, rerender } = await renderHook(
      ({ fn }: { fn: (signal: AbortSignal) => Promise<string> }) => useDetailQuery(fn),
      {
        initialProps: { fn: fetcher },
      },
    );

    const nextFetcher = jest.fn().mockReturnValueOnce(second.promise);
    await rerender({ fn: nextFetcher });
    const firstSignal = fetcher.mock.calls[0]?.[0] as AbortSignal;
    expect(firstSignal.aborted).toBe(true);

    second.resolve('fresh');
    await waitFor(() =>
      expect(result.current).toMatchObject({ data: 'fresh', error: null, loading: false }),
    );

    first.reject(new ApiError(0, 'Unable to reach QueenZone. Check your connection and try again.'));
    await flush();
    expect(result.current).toMatchObject({ data: 'fresh', error: null, loading: false });
  });

  it('ignores a stale resolve from the aborted first load', async () => {
    const first = deferred<string>();
    const second = deferred<string>();
    const fetcher = jest.fn().mockReturnValueOnce(first.promise);
    const { result, rerender } = await renderHook(
      ({ fn }: { fn: (signal: AbortSignal) => Promise<string> }) => useDetailQuery(fn),
      {
        initialProps: { fn: fetcher },
      },
    );

    const nextFetcher = jest.fn().mockReturnValueOnce(second.promise);
    await rerender({ fn: nextFetcher });
    second.resolve('fresh');
    await waitFor(() =>
      expect(result.current).toMatchObject({ data: 'fresh', error: null, loading: false }),
    );

    first.resolve('stale');
    await flush();
    expect(result.current).toMatchObject({ data: 'fresh', error: null, loading: false });
  });

  it('maps a classified timeout to a failed, not pending, query', async () => {
    const fetcher = jest.fn().mockRejectedValueOnce(ApiError.timeout());
    const { result } = await renderHook(() => useDetailQuery(fetcher));

    await waitFor(() =>
      expect(result.current).toMatchObject({
        data: null,
        error: TIMEOUT_MESSAGE,
        loading: false,
      }),
    );
  });

  it('maps a classified offline failure to a failed, not pending, query', async () => {
    const fetcher = jest.fn().mockRejectedValueOnce(ApiError.offline());
    const { result } = await renderHook(() => useDetailQuery(fetcher));

    await waitFor(() =>
      expect(result.current).toMatchObject({
        data: null,
        error: OFFLINE_MESSAGE,
        loading: false,
      }),
    );
  });

  it('treats a stray AbortError on the initial generation as a timeout failure', async () => {
    const fetcher = jest.fn().mockRejectedValueOnce(Object.assign(new Error('Aborted'), { name: 'AbortError' }));
    const { result } = await renderHook(() => useDetailQuery(fetcher));

    await waitFor(() =>
      expect(result.current).toMatchObject({
        data: null,
        error: TIMEOUT_MESSAGE,
        loading: false,
      }),
    );
  });

  it('does not commit an AbortError from a superseded load', async () => {
    const first = deferred<string>();
    const fetcher = jest.fn().mockReturnValueOnce(first.promise);
    const { result, rerender } = await renderHook(
      ({ fn }: { fn: (signal: AbortSignal) => Promise<string> }) => useDetailQuery(fn),
      {
        initialProps: { fn: fetcher },
      },
    );

    const next = jest.fn().mockResolvedValueOnce('fresh');
    await rerender({ fn: next });
    first.reject(Object.assign(new Error('Aborted'), { name: 'AbortError' }));
    await waitFor(() =>
      expect(result.current).toMatchObject({ data: 'fresh', error: null, loading: false }),
    );
  });
});
