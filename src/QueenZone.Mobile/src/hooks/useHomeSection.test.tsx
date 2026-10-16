import { act, renderHook, waitFor } from '@testing-library/react-native';
import { ApiError } from '../api/client';
import { OFFLINE_MESSAGE, TIMEOUT_MESSAGE } from '../api/errors';
import { deferred } from '../test/fixtures';
import { sectionViewOf, useHomeSection, type SectionSnapshot } from './useHomeSection';

async function flush(): Promise<void> {
  await act(async () => {
    await Promise.resolve();
  });
}

describe('sectionViewOf', () => {
  it('maps each snapshot to the view the screen should render', () => {
    expect(sectionViewOf({ status: 'pending', data: null })).toEqual({ kind: 'skeleton' });
    expect(sectionViewOf({ status: 'pending', data: 'stale' })).toEqual({ kind: 'content', data: 'stale' });
    expect(sectionViewOf({ status: 'ready', data: 'ok' })).toEqual({ kind: 'content', data: 'ok' });
    expect(sectionViewOf({ status: 'failed', data: 'stale', message: 'boom' })).toEqual({
      kind: 'content',
      data: 'stale',
    });
    expect(sectionViewOf({ status: 'failed', data: null, message: 'boom' })).toEqual({
      kind: 'error',
      message: 'boom',
    });
  });

  it('treats a ready null payload as content', () => {
    const snapshot: SectionSnapshot<string | null> = { status: 'ready', data: null };
    expect(sectionViewOf(snapshot)).toEqual({ kind: 'content', data: null });
  });
});

describe('useHomeSection', () => {
  it('shows skeleton on first paint, then content', async () => {
    const pending = deferred<string>();
    const fetcher = jest.fn().mockReturnValueOnce(pending.promise);
    const { result } = await renderHook(() => useHomeSection(fetcher));

    expect(result.current.view).toEqual({ kind: 'skeleton' });
    pending.resolve('hello');
    await waitFor(() => expect(result.current.view).toEqual({ kind: 'content', data: 'hello' }));
  });

  it('keeps the content view while a refresh is pending', async () => {
    const refreshPending = deferred<string>();
    const fetcher = jest.fn();
    fetcher.mockResolvedValueOnce('hello');
    fetcher.mockReturnValueOnce(refreshPending.promise);
    const { result } = await renderHook(() => useHomeSection(fetcher));
    await waitFor(() => expect(result.current.view).toEqual({ kind: 'content', data: 'hello' }));

    await act(async () => {
      void result.current.refresh();
    });
    expect(result.current.view).toEqual({ kind: 'content', data: 'hello' });

    refreshPending.resolve('newer');
    await waitFor(() => expect(result.current.view).toEqual({ kind: 'content', data: 'newer' }));
  });

  it('stays on content when refresh fails after data has loaded', async () => {
    const fetcher = jest.fn();
    fetcher.mockResolvedValueOnce('hello');
    fetcher.mockRejectedValueOnce(new ApiError(500, 'The server had a problem. Try again shortly.'));
    const { result } = await renderHook(() => useHomeSection(fetcher));
    await waitFor(() => expect(result.current.view).toEqual({ kind: 'content', data: 'hello' }));

    await act(async () => {
      await result.current.refresh();
    });
    expect(result.current.view).toEqual({ kind: 'content', data: 'hello' });
  });

  it('shows error when refresh fails with no data', async () => {
    const first = deferred<string>();
    const fetcher = jest.fn();
    fetcher.mockReturnValueOnce(first.promise);
    fetcher.mockRejectedValueOnce(new ApiError(500, 'The server had a problem. Try again shortly.'));
    const { result } = await renderHook(() => useHomeSection(fetcher));
    expect(result.current.view).toEqual({ kind: 'skeleton' });

    await act(async () => {
      void result.current.refresh();
    });
    first.reject(Object.assign(new Error('Aborted'), { name: 'AbortError' }));
    await waitFor(() =>
      expect(result.current.view).toEqual({
        kind: 'error',
        message: 'The server had a problem. Try again shortly.',
      }),
    );
  });

  it('lets a newer refresh win and ignores a stale resolve', async () => {
    const first = deferred<string>();
    const second = deferred<string>();
    const fetcher = jest.fn();
    fetcher.mockResolvedValueOnce('seed');
    fetcher.mockReturnValueOnce(first.promise);
    fetcher.mockReturnValueOnce(second.promise);
    const { result } = await renderHook(() => useHomeSection(fetcher));
    await waitFor(() => expect(result.current.view).toEqual({ kind: 'content', data: 'seed' }));

    await act(async () => {
      void result.current.refresh();
    });
    await act(async () => {
      void result.current.refresh();
    });
    const firstSignal = fetcher.mock.calls[1][0];
    expect(firstSignal.aborted).toBe(true);

    first.resolve('stale');
    second.resolve('fresh');
    await waitFor(() => expect(result.current.view).toEqual({ kind: 'content', data: 'fresh' }));
    await flush();
    expect(result.current.view).toEqual({ kind: 'content', data: 'fresh' });
  });

  it('resolves refresh() on abort and supersede', async () => {
    const first = deferred<string>();
    const second = deferred<string>();
    const fetcher = jest.fn();
    fetcher.mockResolvedValueOnce('seed');
    fetcher.mockReturnValueOnce(first.promise);
    fetcher.mockReturnValueOnce(second.promise);
    const { result } = await renderHook(() => useHomeSection(fetcher));
    await waitFor(() => expect(result.current.view).toEqual({ kind: 'content', data: 'seed' }));

    let firstRefresh: Promise<void> = Promise.resolve();
    await act(async () => {
      firstRefresh = result.current.refresh();
    });
    await act(async () => {
      void result.current.refresh();
    });

    first.reject(Object.assign(new Error('Aborted'), { name: 'AbortError' }));
    await expect(firstRefresh).resolves.toBeUndefined();
    expect(result.current.view).toEqual({ kind: 'content', data: 'seed' });

    second.resolve('fresh');
    await waitFor(() => expect(result.current.view).toEqual({ kind: 'content', data: 'fresh' }));
  });

  it('reloads when the fetcher identity changes', async () => {
    const first = jest.fn().mockResolvedValue('one');
    const second = jest.fn().mockResolvedValue('two');
    const { result, rerender } = await renderHook(
      ({ fn }: { fn: (signal: AbortSignal) => Promise<string> }) => useHomeSection(fn),
      {
        initialProps: { fn: first },
      },
    );
    await waitFor(() => expect(result.current.view).toEqual({ kind: 'content', data: 'one' }));

    await rerender({ fn: second });
    await waitFor(() => expect(result.current.view).toEqual({ kind: 'content', data: 'two' }));
    expect(second).toHaveBeenCalled();
  });

  it('shows a retryable error when the section times out', async () => {
    const fetcher = jest.fn();
    fetcher.mockRejectedValueOnce(ApiError.timeout());
    fetcher.mockResolvedValueOnce('ok');
    const { result } = await renderHook(() => useHomeSection(fetcher));

    await waitFor(() =>
      expect(result.current.view).toEqual({
        kind: 'error',
        message: TIMEOUT_MESSAGE,
      }),
    );

    await act(async () => {
      result.current.reload();
    });
    await waitFor(() => expect(result.current.view).toEqual({ kind: 'content', data: 'ok' }));
  });

  it('shows a retryable error when the section is offline', async () => {
    const fetcher = jest.fn();
    fetcher.mockRejectedValueOnce(ApiError.offline());
    fetcher.mockResolvedValueOnce('ok');
    const { result } = await renderHook(() => useHomeSection(fetcher));

    await waitFor(() =>
      expect(result.current.view).toEqual({
        kind: 'error',
        message: OFFLINE_MESSAGE,
      }),
    );

    await act(async () => {
      result.current.reload();
    });
    await waitFor(() => expect(result.current.view).toEqual({ kind: 'content', data: 'ok' }));
  });

  it('treats a stray AbortError on the initial generation as a timeout failure', async () => {
    const fetcher = jest.fn().mockRejectedValueOnce(Object.assign(new Error('Aborted'), { name: 'AbortError' }));
    const { result } = await renderHook(() => useHomeSection(fetcher));

    await waitFor(() =>
      expect(result.current.view).toEqual({
        kind: 'error',
        message: TIMEOUT_MESSAGE,
      }),
    );
  });

  it('recovers via refresh after an initial-generation stray AbortError', async () => {
    const fetcher = jest.fn();
    fetcher.mockRejectedValueOnce(Object.assign(new Error('Aborted'), { name: 'AbortError' }));
    fetcher.mockResolvedValueOnce('ok');
    const { result } = await renderHook(() => useHomeSection(fetcher));

    await waitFor(() =>
      expect(result.current.view).toEqual({
        kind: 'error',
        message: TIMEOUT_MESSAGE,
      }),
    );

    await act(async () => {
      await result.current.refresh();
    });
    expect(result.current.view).toEqual({ kind: 'content', data: 'ok' });
  });

  // Field path (Richard/Pat): first load can stay pending; PTR starts a new
  // generation and recovers. The superseded initial abort must not flash failed.
  it('lets refresh recover a first generation that stayed pending', async () => {
    const first = deferred<string>();
    const second = deferred<string>();
    const fetcher = jest.fn();
    fetcher.mockReturnValueOnce(first.promise);
    fetcher.mockReturnValueOnce(second.promise);
    const { result } = await renderHook(() => useHomeSection(fetcher));
    expect(result.current.view).toEqual({ kind: 'skeleton' });

    await act(async () => {
      void result.current.refresh();
    });
    const firstSignal = fetcher.mock.calls[0][0] as AbortSignal;
    expect(firstSignal.aborted).toBe(true);
    expect(result.current.view).toEqual({ kind: 'skeleton' });

    first.reject(Object.assign(new Error('Aborted'), { name: 'AbortError' }));
    await flush();
    expect(result.current.view).toEqual({ kind: 'skeleton' });

    second.resolve('ok');
    await waitFor(() => expect(result.current.view).toEqual({ kind: 'content', data: 'ok' }));
  });

  it('reloads from error without data through skeleton then content', async () => {
    const retry = deferred<string>();
    const fetcher = jest.fn();
    fetcher.mockRejectedValueOnce(new ApiError(500, 'The server had a problem. Try again shortly.'));
    fetcher.mockReturnValueOnce(retry.promise);
    const { result } = await renderHook(() => useHomeSection(fetcher));
    await waitFor(() =>
      expect(result.current.view).toEqual({
        kind: 'error',
        message: 'The server had a problem. Try again shortly.',
      }),
    );

    await act(async () => {
      result.current.reload();
    });
    expect(result.current.view).toEqual({ kind: 'skeleton' });

    retry.resolve('ok');
    await waitFor(() => expect(result.current.view).toEqual({ kind: 'content', data: 'ok' }));
  });
});
