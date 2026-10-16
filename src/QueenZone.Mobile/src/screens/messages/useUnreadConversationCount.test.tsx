import { act, renderHook, waitFor } from '@testing-library/react-native';
import { fetchUnreadConversationCount } from '../../api/messages';
import { invalidate } from '../../cache/externalStore';
import { NEWS_LIST_CACHE_KEY, PM_UNREAD_CACHE_KEY } from '../../cache/keys';
import { createMockSession } from '../../test/mockSession';
import { useUnreadConversationCount } from './useUnreadConversationCount';

jest.mock('../../api/messages', () => ({
  fetchUnreadConversationCount: jest.fn(),
}));

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('@react-navigation/native', () => {
  const actual = jest.requireActual('@react-navigation/native');
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- Jest CJS mock factory.
  const { useEffect } = require('react');
  return {
    ...actual,
    useFocusEffect: (effect: () => void | (() => void)) => {
      useEffect(effect, [effect]);
    },
  };
});

const fetchUnread = fetchUnreadConversationCount as jest.MockedFunction<typeof fetchUnreadConversationCount>;

describe('useUnreadConversationCount', () => {
  beforeEach(() => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    fetchUnread.mockReset();
    fetchUnread.mockResolvedValue(2);
  });

  it('fetches on focus and returns the API count', async () => {
    const { result } = await renderHook(() => useUnreadConversationCount());
    await waitFor(() => expect(result.current).toBe(2));
    expect(fetchUnread).toHaveBeenCalledTimes(1);
  });

  it('fails soft to 0 when signed out without requesting', async () => {
    mockSession.isSignedIn = false;
    mockSession.accessToken = null;
    const { result } = await renderHook(() => useUnreadConversationCount());
    await waitFor(() => expect(result.current).toBe(0));
    expect(fetchUnread).not.toHaveBeenCalled();
  });

  it('fails soft to 0 when the unread-count request fails', async () => {
    fetchUnread.mockRejectedValueOnce(new Error('offline'));
    const { result } = await renderHook(() => useUnreadConversationCount());
    await waitFor(() => expect(result.current).toBe(0));
    expect(fetchUnread).toHaveBeenCalledTimes(1);
  });

  it('refetches when the PM unread key is invalidated while mounted', async () => {
    const { result } = await renderHook(() => useUnreadConversationCount());
    await waitFor(() => expect(result.current).toBe(2));
    fetchUnread.mockResolvedValueOnce(5);

    await act(async () => {
      invalidate(PM_UNREAD_CACHE_KEY);
    });

    await waitFor(() => expect(result.current).toBe(5));
    expect(fetchUnread).toHaveBeenCalledTimes(2);
  });

  it('skips the network after unmount when the PM unread key is invalidated', async () => {
    const { result, unmount } = await renderHook(() => useUnreadConversationCount());
    await waitFor(() => expect(result.current).toBe(2));
    const calls = fetchUnread.mock.calls.length;
    await unmount();

    await act(async () => {
      invalidate(PM_UNREAD_CACHE_KEY);
    });

    expect(fetchUnread).toHaveBeenCalledTimes(calls);
  });

  it('keeps the last count when a superseded fetch aborts', async () => {
    const { result } = await renderHook(() => useUnreadConversationCount());
    await waitFor(() => expect(result.current).toBe(2));
    const abortErr = new Error('aborted');
    abortErr.name = 'AbortError';
    fetchUnread.mockRejectedValueOnce(abortErr);

    await act(async () => {
      invalidate(PM_UNREAD_CACHE_KEY);
    });

    expect(result.current).toBe(2);
  });

  it('does not refetch when the news list key is invalidated', async () => {
    const { result } = await renderHook(() => useUnreadConversationCount());
    await waitFor(() => expect(result.current).toBe(2));
    const calls = fetchUnread.mock.calls.length;

    await act(async () => {
      invalidate(NEWS_LIST_CACHE_KEY);
    });

    expect(fetchUnread).toHaveBeenCalledTimes(calls);
    expect(result.current).toBe(2);
  });
});
