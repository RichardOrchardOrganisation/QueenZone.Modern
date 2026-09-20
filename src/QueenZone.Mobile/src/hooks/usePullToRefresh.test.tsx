import { act, renderHook, waitFor } from '@testing-library/react-native';
import { deferred } from '../test/fixtures';
import { usePullToRefresh } from './usePullToRefresh';

describe('usePullToRefresh', () => {
  it('sets refreshing true while a deferred task is pending and false after settle', async () => {
    const pending = deferred<void>();
    const { result } = renderHook(() => usePullToRefresh([() => pending.promise]));

    expect(result.current.refreshing).toBe(false);
    await act(async () => {
      result.current.onRefresh();
    });
    expect(result.current.refreshing).toBe(true);

    pending.resolve();
    await waitFor(() => expect(result.current.refreshing).toBe(false));
  });

  it('keeps the spinner until the newest pull settles', async () => {
    const first = deferred<void>();
    const second = deferred<void>();
    const task = jest.fn();
    task.mockReturnValueOnce(first.promise);
    task.mockReturnValueOnce(second.promise);
    const { result } = renderHook(() => usePullToRefresh([task]));

    await act(async () => {
      result.current.onRefresh();
    });
    await act(async () => {
      result.current.onRefresh();
    });
    expect(result.current.refreshing).toBe(true);

    first.resolve();
    await act(async () => {
      await Promise.resolve();
    });
    expect(result.current.refreshing).toBe(true);

    second.resolve();
    await waitFor(() => expect(result.current.refreshing).toBe(false));
  });

  it('clears the spinner when a task rejects', async () => {
    const { result } = renderHook(() =>
      usePullToRefresh([() => Promise.reject(new Error('section failed'))]),
    );

    await act(async () => {
      result.current.onRefresh();
    });
    expect(result.current.refreshing).toBe(true);
    await waitFor(() => expect(result.current.refreshing).toBe(false));
  });

  it('keeps refreshing true for a painted frame and min duration when the task resolves immediately', async () => {
    const { result } = renderHook(() => usePullToRefresh([() => Promise.resolve()]));

    await act(async () => {
      result.current.onRefresh();
    });
    expect(result.current.refreshing).toBe(true);

    await act(async () => {
      await Promise.resolve();
      await Promise.resolve();
    });
    expect(result.current.refreshing).toBe(true);

    await waitFor(() => expect(result.current.refreshing).toBe(false));
  });

  it('does not throw when unmounted during a refresh', async () => {
    const pending = deferred<void>();
    const { result, unmount } = renderHook(() => usePullToRefresh([() => pending.promise]));

    await act(async () => {
      result.current.onRefresh();
    });
    unmount();
    pending.resolve();
    await act(async () => {
      await Promise.resolve();
    });
  });
});
