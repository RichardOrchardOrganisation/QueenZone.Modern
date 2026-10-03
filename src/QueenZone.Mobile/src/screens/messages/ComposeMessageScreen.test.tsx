import { Button } from '../../ui/Button';
import { act, fireEvent, screen, userEvent, waitFor } from '@testing-library/react-native';
import { ApiError } from '../../api/client';
import { composeMessage, searchRecipients } from '../../api/messages';
import { conversationDetailFixture, memberProfileFixture } from '../../test/fixtures';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, flushVirtualizedList, renderWithProviders } from '../../test/render';
import { ComposeMessageScreen } from './ComposeMessageScreen';

const mockSession = createMockSession();
const recipient = { memberId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', displayName: 'Bob' };
const conversationId = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../../api/messages', () => ({
  searchRecipients: jest.fn(),
  composeMessage: jest.fn(),
}));

jest.mock('../../offlineQueue', () => ({
  enqueueMessageCompose: jest.fn(async (input: { body: string; memberId: string; recipientMemberId: string }) => ({
    operationId: 'op-compose',
    payload: { body: input.body },
    memberId: input.memberId,
    kind: 'message.compose',
    target: { recipientMemberId: input.recipientMemberId },
  })),
  removeOfflineItem: jest.fn(),
  flushOfflineQueue: jest.fn(),
}));

const searchRecipientsMock = searchRecipients as jest.MockedFunction<typeof searchRecipients>;
const composeMessageMock = composeMessage as jest.MockedFunction<typeof composeMessage>;

function renderCompose(navigation = fakeNavigation()) {
  return {
    navigation,
    ...renderWithProviders(
      <ComposeMessageScreen
        navigation={navigation as never}
        route={{ key: 'compose', name: 'ComposeMessage' } as never}
      />,
    ),
  };
}

async function pickRecipient(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Recipient search'), 'Bob');
  await waitFor(() => expect(screen.getByRole('button', { name: 'Message Bob' })).toBeOnTheScreen());
  await user.press(screen.getByRole('button', { name: 'Message Bob' }));
  await waitFor(() => expect(screen.getByText('Bob')).toBeOnTheScreen());
}

describe('ComposeMessageScreen', () => {
  beforeEach(() => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    mockSession.profile = memberProfileFixture({ memberId: 'member-1' });
    searchRecipientsMock.mockReset();
    composeMessageMock.mockReset();
    searchRecipientsMock.mockResolvedValue([recipient]);
    composeMessageMock.mockResolvedValue(conversationDetailFixture({ conversationId }));
  });

  afterEach(async () => {
    await flushVirtualizedList();
  });

  it('debounces recipient search by 250ms', async () => {
    jest.useFakeTimers();
    try {
      const user = userEvent.setup({ advanceTimers: jest.advanceTimersByTime });
      renderCompose();
      await waitFor(() => expect(screen.getByLabelText('Recipient search')).toBeOnTheScreen());
      await user.type(screen.getByLabelText('Recipient search'), 'Bob');
      expect(searchRecipientsMock).not.toHaveBeenCalled();

      await act(async () => {
        jest.advanceTimersByTime(249);
      });
      expect(searchRecipientsMock).not.toHaveBeenCalled();

      await act(async () => {
        jest.advanceTimersByTime(1);
      });
      await waitFor(() =>
        expect(searchRecipientsMock).toHaveBeenCalledWith('tok', 'Bob', expect.any(AbortSignal)),
      );
    } finally {
      jest.useRealTimers();
    }
  });

  it('picks a recipient, sends, and replaces with Conversation', async () => {
    const { navigation } = renderCompose();
    await waitFor(() => expect(screen.getByLabelText('Recipient search')).toBeOnTheScreen());

    const user = userEvent.setup();
    await pickRecipient(user);
    await user.type(screen.getByLabelText('Message body'), 'Hello Bob');
    await user.press(screen.getByRole('button', { name: 'Send message' }));

    await waitFor(() =>
      expect(composeMessageMock).toHaveBeenCalledWith(
        'tok',
        recipient.memberId,
        'Hello Bob',
        undefined,
        'op-compose',
      ),
    );
    expect(navigation.replace).toHaveBeenCalledWith('Conversation', { id: conversationId });
  });

  it('does not send when the body is empty', async () => {
    renderCompose();
    await waitFor(() => expect(screen.getByLabelText('Recipient search')).toBeOnTheScreen());

    const user = userEvent.setup();
    await pickRecipient(user);
    await user.press(screen.getByRole('button', { name: 'Send message' }));

    await waitFor(() => expect(screen.getByText('Message body is required.')).toBeOnTheScreen());
    expect(composeMessageMock).not.toHaveBeenCalled();
  });

  it('does not send when no recipient is chosen', async () => {
    renderCompose();
    await waitFor(() => expect(screen.getByLabelText('Message body')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Message body'), 'Hello');
    expect(screen.getByRole('button', { name: 'Send message' })).toBeDisabled();
    await user.press(screen.getByRole('button', { name: 'Send message' }));

    expect(composeMessageMock).not.toHaveBeenCalled();
  });

  it.each(['token', 'member'])('rejects sending after the %s identity disappears', async (identity) => {
    renderCompose();
    const user = userEvent.setup();
    await pickRecipient(user);
    await user.type(screen.getByLabelText('Message body'), 'Hello');
    if (identity === 'token') mockSession.accessToken = null;
    else mockSession.profile = memberProfileFixture({ memberId: '' });
    fireEvent.changeText(screen.getByLabelText('Message body'), 'Hello again');
    const send = screen.UNSAFE_getAllByType(Button).find((button) => button.props.label === 'Send message')!;
    await act(async () => send.props.onPress());
    expect(screen.getByText('Sign in to continue.')).toBeOnTheScreen();
    expect(composeMessageMock).not.toHaveBeenCalled();
  });

  it('returns to the inbox when a composed message is queued offline', async () => {
    composeMessageMock.mockRejectedValueOnce(new ApiError(0, 'Offline'));
    const { navigation } = renderCompose();
    const user = userEvent.setup();
    await pickRecipient(user);
    await user.type(screen.getByLabelText('Message body'), 'Hello offline');
    await user.press(screen.getByRole('button', { name: 'Send message' }));
    await waitFor(() => expect(navigation.goBack).toHaveBeenCalledTimes(1));
    expect(navigation.replace).not.toHaveBeenCalled();
  });

  it('keeps the composer on screen when send fails', async () => {
    composeMessageMock.mockRejectedValueOnce(new ApiError(500, 'The server had a problem.'));
    const { navigation } = renderCompose();
    await waitFor(() => expect(screen.getByLabelText('Recipient search')).toBeOnTheScreen());

    const user = userEvent.setup();
    await pickRecipient(user);
    await user.type(screen.getByLabelText('Message body'), 'Hello Bob');
    await user.press(screen.getByRole('button', { name: 'Send message' }));

    await waitFor(() => expect(screen.getByText('The server had a problem.')).toBeOnTheScreen());
    expect(navigation.replace).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Send message' })).toBeOnTheScreen();
  });
});

describe('recipient search rejection and race guards', () => {
  beforeEach(() => {
    mockSession.isSignedIn = true; mockSession.accessToken = 'tok';
    mockSession.profile = memberProfileFixture({ memberId: 'member-1' });
    searchRecipientsMock.mockReset(); composeMessageMock.mockReset();
    jest.useFakeTimers();
  });
  afterEach(() => jest.useRealTimers());

  it('ignores an older successful response and an older error after a new search', async () => {
    const pending: { resolve: (value: typeof recipient[]) => void; reject: (error: Error) => void }[] = [];
    searchRecipientsMock.mockImplementation(() => new Promise((resolve, reject) => pending.push({ resolve, reject })));
    renderCompose();
    const input = screen.getByLabelText('Recipient search');
    fireEvent.changeText(input, 'old');
    await act(async () => jest.advanceTimersByTimeAsync(250));
    fireEvent.changeText(input, 'new');
    await act(async () => jest.advanceTimersByTimeAsync(250));
    const newer = { ...recipient, displayName: 'Newest Fan' };
    await act(async () => pending[1].resolve([newer]));
    expect(screen.getByRole('button', { name: 'Message Newest Fan' })).toBeOnTheScreen();
    await act(async () => pending[0].resolve([recipient]));
    expect(screen.queryByRole('button', { name: 'Message Bob' })).toBeNull();
    fireEvent.changeText(input, 'third');
    await act(async () => jest.advanceTimersByTimeAsync(250));
    fireEvent.changeText(input, 'fourth');
    await act(async () => jest.advanceTimersByTimeAsync(250));
    await act(async () => pending[3].resolve([newer]));
    await act(async () => pending[2].reject(new Error('Stale error')));
    expect(screen.queryByText('Something went wrong.')).toBeNull();
    expect(screen.getByRole('button', { name: 'Message Newest Fan' })).toBeOnTheScreen();
  });

  it.each(['network', 'abort'])('handles a current %s rejection', async (kind) => {
    const error = new Error('Search unavailable');
    if (kind === 'abort') error.name = 'AbortError';
    searchRecipientsMock.mockRejectedValue(error);
    renderCompose();
    fireEvent.changeText(screen.getByLabelText('Recipient search'), 'Bob');
    await act(async () => jest.advanceTimersByTimeAsync(250));
    expect(screen.queryByRole('button', { name: 'Message Bob' })).toBeNull();
    if (kind === 'network') expect(screen.getByText('Something went wrong.')).toBeOnTheScreen();
    else expect(screen.queryByText('Something went wrong.')).toBeNull();
  });

  it('keeps sending disabled without a recipient and validates a directly invoked send handler', async () => {
    renderCompose();
    expect(screen.getByRole('button', { name: 'Send message' })).toBeDisabled();
    const send = screen.UNSAFE_getAllByType(Button).find((button) => button.props.label === 'Send message')!;
    await act(async () => send.props.onPress());
    expect(screen.getByText('Choose a recipient.')).toBeOnTheScreen();
    expect(composeMessageMock).not.toHaveBeenCalled();
  });
});
