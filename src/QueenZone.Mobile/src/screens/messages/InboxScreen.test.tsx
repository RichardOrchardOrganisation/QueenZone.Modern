import { act, screen, userEvent, waitFor } from '@testing-library/react-native';
import { archiveConversation, fetchInbox } from '../../api/messages';
import { ApiError } from '../../api/client';
import { getContentCache } from '../../cache';
import { inboxConversationFixture, pagedResponse } from '../../test/fixtures';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { InboxScreen } from './InboxScreen';

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../../api/messages', () => ({
  fetchInbox: jest.fn(),
  archiveConversation: jest.fn(),
}));

jest.mock('../../cache', () => ({
  ...jest.requireActual('../../cache'),
  getContentCache: jest.fn(),
}));

jest.mock('../../offlineQueue', () => ({
  useOfflineQueue: jest.fn(() => []),
  flushOfflineQueue: jest.fn(),
  removeOfflineItem: jest.fn(),
  updateOfflineItem: jest.fn(),
}));

let mockLastFocusEffect: (() => void | (() => void)) | null = null;

jest.mock('@react-navigation/native', () => {
  const actual = jest.requireActual('@react-navigation/native');
  const { useEffect } = jest.requireActual('react');
  return {
    ...actual,
    useNavigation: () => ({ navigate: jest.fn() }),
    // Runs like a single focus on mount, and keeps the effect so a test can refocus the screen.
    useFocusEffect: (effect: () => void | (() => void)) => {
      mockLastFocusEffect = effect;
      useEffect(effect, [effect]);
    },
  };
});

const fetchInboxMock = fetchInbox as jest.MockedFunction<typeof fetchInbox>;
const archiveConversationMock = archiveConversation as jest.MockedFunction<typeof archiveConversation>;
const getContentCacheMock = getContentCache as jest.MockedFunction<typeof getContentCache>;
function renderInbox() {
  return renderWithProviders(
    <InboxScreen navigation={fakeNavigation() as never} route={{ key: 'inbox', name: 'Inbox' } as never} />,
  );
}

describe('InboxScreen', () => {
  beforeEach(() => {
    mockSession.isSignedIn = false;
    mockSession.accessToken = null;
    mockSession.profile = null;
    fetchInboxMock.mockReset();
    archiveConversationMock.mockReset();
    getContentCacheMock.mockReset().mockReturnValue({
      get: jest.fn().mockResolvedValue(null),
    } as unknown as ReturnType<typeof getContentCache>);
  });

  it('gates unsigned visitors', () => {
    renderInbox();
    expect(screen.getByText('Messages')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeOnTheScreen();
  });

  it('shows empty, error, and success list states', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    fetchInboxMock.mockResolvedValueOnce(pagedResponse([], 1, 0));
    renderInbox();
    await waitFor(() => expect(screen.getByText('You have no private messages yet.')).toBeOnTheScreen());
  });

  it('opens a conversation from a labelled inbox row', async () => {
    const navigation = fakeNavigation();
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    fetchInboxMock.mockResolvedValueOnce(
      pagedResponse(
        [
          inboxConversationFixture({
            hasUnread: true,
            unreadCount: 2,
          }),
        ],
        1,
        1,
      ),
    );
    renderWithProviders(
      <InboxScreen navigation={navigation as never} route={{ key: 'inbox', name: 'Inbox' } as never} />,
    );
    await waitFor(() => expect(screen.getByText('Brian')).toBeOnTheScreen());
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: /Brian/ }));
    expect(navigation.navigate).toHaveBeenCalledWith('Conversation', { id: 'convo-1' });
  });

  it('navigates to the archived list', async () => {
    const navigation = fakeNavigation();
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    fetchInboxMock.mockResolvedValueOnce(pagedResponse([], 1, 0));
    renderWithProviders(
      <InboxScreen navigation={navigation as never} route={{ key: 'inbox', name: 'Inbox' } as never} />,
    );
    await waitFor(() => expect(screen.getByText('You have no private messages yet.')).toBeOnTheScreen());
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Archived' }));
    expect(navigation.navigate).toHaveBeenCalledWith('Archived');
  });

  it('starts a new message from the inbox action', async () => {
    const navigation = fakeNavigation();
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    fetchInboxMock.mockResolvedValueOnce(pagedResponse([], 1, 0));
    renderWithProviders(
      <InboxScreen navigation={navigation as never} route={{ key: 'inbox', name: 'Inbox' } as never} />,
    );
    await waitFor(() => expect(screen.getByText('You have no private messages yet.')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByTestId('inbox-compose'));

    expect(navigation.navigate).toHaveBeenCalledWith('ComposeMessage');
  });

  it('archives a conversation from the inbox row and refreshes the list', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    fetchInboxMock.mockResolvedValue(
      pagedResponse([inboxConversationFixture()], 1, 1),
    );
    archiveConversationMock.mockResolvedValueOnce(undefined);
    renderInbox();
    await waitFor(() => expect(screen.getByText('Brian')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Archive' }));

    await waitFor(() => expect(archiveConversationMock).toHaveBeenCalledWith('tok', 'convo-1'));
    await waitFor(() => expect(fetchInboxMock).toHaveBeenCalledTimes(2));
  });

  it('refreshes from the network when the screen is focused again', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    fetchInboxMock.mockResolvedValue(pagedResponse([inboxConversationFixture()], 1, 1));
    renderInbox();
    await waitFor(() => expect(screen.getByText('Brian')).toBeOnTheScreen());
    expect(fetchInboxMock).toHaveBeenCalledTimes(1);

    act(() => {
      mockLastFocusEffect?.();
    });

    await waitFor(() => expect(fetchInboxMock).toHaveBeenCalledTimes(2));
    expect(fetchInboxMock).toHaveBeenLastCalledWith('tok', expect.objectContaining({ networkOnly: true }));
  });

  it('shows markup and URLs in the preview as plain text', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    fetchInboxMock.mockResolvedValueOnce(
      pagedResponse(
        [
          inboxConversationFixture({
            conversationId: 'convo-2',
            otherParticipantId: 'member-3',
            otherParticipantDisplayName: 'John',
            lastMessagePreview: '<script>alert(1)</script> https://example.com',
            detailPath: '/messages/convo-2',
          }),
        ],
        1,
        1,
      ),
    );
    renderInbox();
    await waitFor(() =>
      expect(screen.getByText('<script>alert(1)</script> https://example.com')).toBeOnTheScreen(),
    );
    expect(screen.queryByRole('link')).toBeNull();
  });

  it('shows a retryable error', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    fetchInboxMock.mockRejectedValueOnce(new ApiError(500, 'The server had a problem. Try again shortly.'));
    renderInbox();
    await waitFor(() => expect(screen.getByText('Unable to load')).toBeOnTheScreen());
    expect(screen.getByRole('button', { name: 'Try again' })).toBeOnTheScreen();
  });

  it('renders and retains the cached inbox while a fresh request is pending or fails', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    mockSession.profile = { memberId: 'member-1' } as typeof mockSession.profile;
    const cached = pagedResponse([
      inboxConversationFixture({
        conversationId: 'convo-cached',
        otherParticipantDisplayName: 'Cached Carol',
      }),
    ], 1, 1);
    const cacheGet = jest.fn().mockResolvedValue(cached);
    getContentCacheMock.mockReturnValue({ get: cacheGet } as unknown as ReturnType<typeof getContentCache>);
    let rejectFetch: (error: Error) => void = () => {};
    fetchInboxMock.mockReturnValueOnce(new Promise((_resolve, reject) => { rejectFetch = reject; }));

    renderInbox();
    await waitFor(() => expect(screen.getByText('Cached Carol')).toBeOnTheScreen());
    expect(cacheGet).toHaveBeenCalledWith('messages:member:member-1:inbox:v2');

    rejectFetch(new ApiError(0, 'Unable to reach QueenZone.'));
    await waitFor(() => expect(screen.getByText('Cached Carol')).toBeOnTheScreen());
    expect(screen.queryByText('Unable to load')).toBeNull();
  });

});
