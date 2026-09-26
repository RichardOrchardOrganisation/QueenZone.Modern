import { act, screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchArchivedInbox, unarchiveConversation } from '../../api/messages';
import { ApiError } from '../../api/client';
import { inboxConversationFixture, pagedResponse } from '../../test/fixtures';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { ArchivedScreen } from './ArchivedScreen';

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../../api/messages', () => ({
  fetchArchivedInbox: jest.fn(),
  unarchiveConversation: jest.fn(),
}));

let mockLastFocusEffect: (() => void | (() => void)) | null = null;

jest.mock('@react-navigation/native', () => {
  const actual = jest.requireActual('@react-navigation/native');
  const { useEffect } = jest.requireActual('react');
  return {
    ...actual,
    // Runs like a single focus on mount, and keeps the effect so a test can refocus the screen.
    useFocusEffect: (effect: () => void | (() => void)) => {
      mockLastFocusEffect = effect;
      useEffect(effect, [effect]);
    },
  };
});

const fetchArchivedInboxMock = fetchArchivedInbox as jest.MockedFunction<typeof fetchArchivedInbox>;
const unarchiveConversationMock = unarchiveConversation as jest.MockedFunction<typeof unarchiveConversation>;

function renderArchived() {
  return renderWithProviders(
    <ArchivedScreen navigation={fakeNavigation() as never} route={{ key: 'archived', name: 'Archived' } as never} />,
  );
}

describe('ArchivedScreen', () => {
  beforeEach(() => {
    mockSession.isSignedIn = false;
    mockSession.accessToken = null;
    fetchArchivedInboxMock.mockReset();
    unarchiveConversationMock.mockReset();
  });

  it('gates unsigned visitors', () => {
    renderArchived();
    expect(screen.getByText('Archived messages')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeOnTheScreen();
  });

  it('refreshes the list when the screen is focused again', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    fetchArchivedInboxMock.mockResolvedValue(pagedResponse([inboxConversationFixture()], 1, 1));
    renderArchived();
    await waitFor(() => expect(screen.getByText('Brian')).toBeOnTheScreen());
    expect(fetchArchivedInboxMock).toHaveBeenCalledTimes(1);

    act(() => {
      mockLastFocusEffect?.();
    });

    await waitFor(() => expect(fetchArchivedInboxMock).toHaveBeenCalledTimes(2));
  });

  it('shows an empty state', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    fetchArchivedInboxMock.mockResolvedValueOnce(pagedResponse([], 1, 0));
    renderArchived();
    await waitFor(() => expect(screen.getByText('You have no archived conversations.')).toBeOnTheScreen());
  });

  it('opens a conversation from an archived row', async () => {
    const navigation = fakeNavigation();
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    fetchArchivedInboxMock.mockResolvedValueOnce(
      pagedResponse(
        [
          {
            conversationId: 'convo-1',
            otherParticipantId: 'member-2',
            otherParticipantDisplayName: 'Brian',
            lastMessagePreview: 'See you at Wembley',
            lastMessageAt: '2024-01-15T12:00:00.000Z',
            hasUnread: false,
            unreadCount: 0,
            detailPath: '/messages/convo-1',
          },
        ],
        1,
        1,
      ),
    );
    renderWithProviders(
      <ArchivedScreen navigation={navigation as never} route={{ key: 'archived', name: 'Archived' } as never} />,
    );
    await waitFor(() => expect(screen.getByText('Brian')).toBeOnTheScreen());
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: /Brian/ }));
    expect(navigation.navigate).toHaveBeenCalledWith('Conversation', { id: 'convo-1' });
  });

  it('unarchives a conversation and refreshes the list', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    fetchArchivedInboxMock.mockResolvedValue(
      pagedResponse(
        [
          {
            conversationId: 'convo-1',
            otherParticipantId: 'member-2',
            otherParticipantDisplayName: 'Brian',
            lastMessagePreview: 'See you at Wembley',
            lastMessageAt: '2024-01-15T12:00:00.000Z',
            hasUnread: false,
            unreadCount: 0,
            detailPath: '/messages/convo-1',
          },
        ],
        1,
        1,
      ),
    );
    unarchiveConversationMock.mockResolvedValueOnce(undefined);
    renderArchived();
    await waitFor(() => expect(screen.getByText('Brian')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Unarchive' }));

    await waitFor(() => expect(unarchiveConversationMock).toHaveBeenCalledWith('tok', 'convo-1'));
    await waitFor(() => expect(fetchArchivedInboxMock).toHaveBeenCalledTimes(2));
  });

  it('shows a retryable error', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    fetchArchivedInboxMock.mockRejectedValueOnce(new ApiError(500, 'The server had a problem. Try again shortly.'));
    renderArchived();
    await waitFor(() => expect(screen.getByText('Unable to load')).toBeOnTheScreen());
    expect(screen.getByRole('button', { name: 'Try again' })).toBeOnTheScreen();
  });
});
