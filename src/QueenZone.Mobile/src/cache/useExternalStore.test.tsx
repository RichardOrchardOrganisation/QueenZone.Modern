import { act, renderHook } from '@testing-library/react-native';
import { invalidate, invalidatePrefix, resetExternalStoreForTests } from './externalStore';
import { NEWS_CACHE_KEY_PREFIX, NEWS_LIST_CACHE_KEY, PM_UNREAD_CACHE_KEY } from './keys';
import { usePrefixVersion, useStoreRefresh, useStoreVersion } from './useExternalStore';

describe('useExternalStore', () => {
  beforeEach(() => {
    resetExternalStoreForTests();
  });

  it('re-renders useStoreVersion when the key is invalidated', async () => {
    const { result } = await renderHook(() => useStoreVersion(NEWS_LIST_CACHE_KEY));
    expect(result.current).toBe(0);

    await act(() => {
      invalidate(NEWS_LIST_CACHE_KEY);
    });

    expect(result.current).toBe(1);
  });

  it('follows the current key instead of staying on the first subscribed key', async () => {
    const { result, rerender } = await renderHook(({ key }: { key: string }) => useStoreVersion(key), {
      initialProps: { key: 'downloads:member:1:performance:191' },
    });
    expect(result.current).toBe(0);

    await act(() => {
      invalidate('downloads:member:1:performance:193');
    });
    expect(result.current).toBe(0);

    await rerender({ key: 'downloads:member:1:performance:193' });
    expect(result.current).toBe(1);

    await act(() => {
      invalidate('downloads:member:1:performance:191');
    });
    expect(result.current).toBe(1);

    await act(() => {
      invalidate('downloads:member:1:performance:193');
    });
    expect(result.current).toBe(2);
  });

  it('re-renders usePrefixVersion for matching key and prefix invalidation only', async () => {
    const { result } = await renderHook(() => usePrefixVersion(NEWS_CACHE_KEY_PREFIX));
    expect(result.current).toBe(0);

    await act(() => {
      invalidate(PM_UNREAD_CACHE_KEY);
    });
    expect(result.current).toBe(0);

    await act(() => {
      invalidate(NEWS_LIST_CACHE_KEY);
    });
    expect(result.current).toBe(1);

    await act(() => {
      invalidatePrefix(NEWS_CACHE_KEY_PREFIX);
    });
    expect(result.current).toBe(2);
  });

  it('calls refresh when the key is invalidated while mounted', async () => {
    const refresh = jest.fn();
    await renderHook(() => useStoreRefresh(NEWS_LIST_CACHE_KEY, refresh));

    await act(() => {
      invalidate(NEWS_LIST_CACHE_KEY);
    });

    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it('does not call refresh after unmount', async () => {
    const refresh = jest.fn();
    const { unmount } = await renderHook(() => useStoreRefresh(NEWS_LIST_CACHE_KEY, refresh));
    await unmount();

    await act(() => {
      invalidate(NEWS_LIST_CACHE_KEY);
    });

    expect(refresh).not.toHaveBeenCalled();
  });

  it('does not call refresh on the generation that was current at mount', async () => {
    const refresh = jest.fn();
    await renderHook(() => useStoreRefresh(NEWS_LIST_CACHE_KEY, refresh));
    expect(refresh).not.toHaveBeenCalled();
  });
});
