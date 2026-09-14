import { Alert, FlatList, Keyboard, KeyboardAvoidingView, Platform, RefreshControl } from 'react-native';
import { act, fireEvent, screen, userEvent, waitFor } from '@testing-library/react-native';
import { ApiError } from '../../api/client';
import type { CachedResult } from '../../api';
import {
  archiveConversation,
  blockConversationParticipant,
  fetchConversation,
  fetchConversationResult,
  replyToConversation,
  reportConversationMessage,
  unblockConversationParticipant,
} from '../../api/messages';
import type { ConversationDetail, ConversationMessage } from '../../api/messages';
import { enqueueMessageReply, useOfflineQueue } from '../../offlineQueue';
import { conversationDetailFixture, memberProfileFixture } from '../../test/fixtures';
import { testIds } from '../../test/testIds';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, flushVirtualizedList, renderWithProviders, testHeaderHeight } from '../../test/render';
import { space } from '../../theme';
import { ConversationScreen, messageBubbleRenderProbe } from './ConversationScreen';

const conversationId = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';
const theirMessageId = '11111111-2222-3333-4444-555555555555';
const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../../api/messages', () => ({
  fetchConversation: jest.fn(),
  fetchConversationResult: jest.fn(),
  replyToConversation: jest.fn(),
  reportConversationMessage: jest.fn(),
  archiveConversation: jest.fn(),
  blockConversationParticipant: jest.fn(),
  unblockConversationParticipant: jest.fn(),
}));

jest.mock('../../offlineQueue', () => ({
  enqueueMessageReply: jest.fn(async (input: { body: string; memberId: string; conversationId: string }) => ({
    operationId: 'op-queued',
    payload: { body: input.body },
    memberId: input.memberId,
    kind: 'message.reply',
    target: { conversationId: input.conversationId },
    state: 'queued',
    lastError: null,
  })),
  useOfflineQueue: jest.fn(() => []),
  removeOfflineItem: jest.fn(),
  updateOfflineItem: jest.fn(),
  flushOfflineQueue: jest.fn(),
}));

const fetchConversationMock = fetchConversation as jest.MockedFunction<typeof fetchConversation>;
const fetchConversationResultMock = fetchConversationResult as jest.MockedFunction<
  typeof fetchConversationResult
>;

const NETWORK_CACHED_AT = '2026-08-19T12:00:00.000Z';

function asNetwork<T>(data: T): CachedResult<T> {
  return { data, source: 'network', cachedAt: NETWORK_CACHED_AT };
}

function asCache<T>(data: T): CachedResult<T> {
  return { data, source: 'cache', cachedAt: NETWORK_CACHED_AT };
}
const replyToConversationMock = replyToConversation as jest.MockedFunction<typeof replyToConversation>;
const enqueueMessageReplyMock = enqueueMessageReply as jest.MockedFunction<typeof enqueueMessageReply>;
const useOfflineQueueMock = useOfflineQueue as jest.MockedFunction<typeof useOfflineQueue>;
const reportConversationMessageMock = reportConversationMessage as jest.MockedFunction<
  typeof reportConversationMessage
>;
const archiveConversationMock = archiveConversation as jest.MockedFunction<typeof archiveConversation>;
const blockConversationParticipantMock = blockConversationParticipant as jest.MockedFunction<
  typeof blockConversationParticipant
>;
const unblockConversationParticipantMock = unblockConversationParticipant as jest.MockedFunction<
  typeof unblockConversationParticipant
>;

function renderConversation(navigation = fakeNavigation()) {
  return renderWithProviders(
    <ConversationScreen
      navigation={navigation as never}
      route={{ key: 'conversation', name: 'Conversation', params: { id: conversationId } } as never}
    />,
  );
}

function conversationDetail(
  messages: ConversationMessage[],
  overrides: Partial<Pick<ConversationDetail, 'canSendReply' | 'hasBlockedOtherParticipant'>> = {},
): ConversationDetail {
  return conversationDetailFixture({
    conversationId,
    otherParticipantId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    otherParticipantDisplayName: 'Bob',
    messages,
    totalCount: messages.length,
    detailPath: `/messages/${conversationId}`,
    ...overrides,
  });
}

describe('ConversationScreen', () => {
  beforeEach(() => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    mockSession.profile = memberProfileFixture({ memberId: 'member-1' });
    fetchConversationMock.mockReset();
    fetchConversationResultMock.mockReset();
    fetchConversationResultMock.mockImplementation(async (token, id, query) =>
      asNetwork(await fetchConversationMock(token, id, query)),
    );
    replyToConversationMock.mockReset();
    enqueueMessageReplyMock.mockClear();
    useOfflineQueueMock.mockReturnValue([]);
    reportConversationMessageMock.mockReset();
    archiveConversationMock.mockReset();
    blockConversationParticipantMock.mockReset();
    unblockConversationParticipantMock.mockReset();
  });

  afterEach(async () => {
    messageBubbleRenderProbe.current = null;
    await flushVirtualizedList();
  });

  it('renders markup and URLs as plain text', async () => {
    const body = '<script>alert(1)</script> https://example.com';
    fetchConversationMock.mockResolvedValue(
      conversationDetail([
        {
          id: theirMessageId,
          senderMemberId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
          senderDisplayName: 'Bob',
          body,
          createdAt: '2026-08-19T12:00:00.000Z',
          isMine: false,
          sortKey: 1,
          reportedByViewer: false,
        },
      ]),
    );

    renderConversation();
    await waitFor(() => expect(screen.getByText(body)).toBeOnTheScreen(), { timeout: 8000 });
    expect(screen.queryByRole('link')).toBeNull();
  });

  it('lets a member report someone else\'s message with an optional reason', async () => {
    fetchConversationMock.mockResolvedValue(
      conversationDetail([
        {
          id: theirMessageId,
          senderMemberId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
          senderDisplayName: 'Bob',
          body: 'Abusive text',
          createdAt: '2026-08-19T12:00:00.000Z',
          isMine: false,
          sortKey: 1,
          reportedByViewer: false,
        },
        {
          id: '22222222-3333-4444-5555-666666666666',
          senderMemberId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
          senderDisplayName: 'Alice',
          body: 'My reply',
          createdAt: '2026-08-19T12:01:00.000Z',
          isMine: true,
          sortKey: 2,
          reportedByViewer: false,
        },
      ]),
    );
    reportConversationMessageMock.mockResolvedValue({
      reportId: '99999999-aaaa-bbbb-cccc-dddddddddddd',
      alreadyReported: false,
    });

    renderConversation();
    await waitFor(() => expect(screen.getByText('Abusive text')).toBeOnTheScreen(), { timeout: 8000 });
    expect(screen.queryByRole('button', { name: 'Report message' })).toBeOnTheScreen();
    expect(screen.getByText('My reply')).toBeOnTheScreen();

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Report message' }));
    await user.type(screen.getByLabelText('Optional reason'), 'Harassment');
    await user.press(screen.getByRole('button', { name: 'Submit report' }));

    await waitFor(() => expect(screen.getByText('REPORTED')).toBeOnTheScreen());
    expect(reportConversationMessageMock).toHaveBeenCalledWith(
      'tok',
      conversationId,
      theirMessageId,
      'Harassment',
    );
  });

  it('shows an error block with retry when the fetch fails', async () => {
    fetchConversationMock.mockRejectedValueOnce(new ApiError(500, 'The server had a problem.'));
    fetchConversationMock.mockResolvedValueOnce(conversationDetail([]));

    renderConversation();
    await waitFor(() => expect(screen.getByText('The server had a problem.')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(fetchConversationMock).toHaveBeenCalledTimes(2));
  });

  it('shows the generic sending-blocked notice when the reply composer is hidden', async () => {
    fetchConversationMock.mockResolvedValue(
      conversationDetail(
        [
          {
            id: theirMessageId,
            senderMemberId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
            senderDisplayName: 'Bob',
            body: 'Hello',
            createdAt: '2026-08-19T12:00:00.000Z',
            isMine: false,
            sortKey: 1,
            reportedByViewer: false,
          },
        ],
        { canSendReply: false },
      ),
    );

    renderConversation();
    await waitFor(() => expect(screen.getByText('Unable to send message.')).toBeOnTheScreen());
    expect(screen.queryByLabelText('Reply')).toBeNull();
  });

  it('shows the you-blocked-this-member notice ahead of the generic notice', async () => {
    fetchConversationMock.mockResolvedValue(
      conversationDetail(
        [
          {
            id: theirMessageId,
            senderMemberId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
            senderDisplayName: 'Bob',
            body: 'Hello',
            createdAt: '2026-08-19T12:00:00.000Z',
            isMine: false,
            sortKey: 1,
            reportedByViewer: false,
          },
        ],
        { canSendReply: false, hasBlockedOtherParticipant: true },
      ),
    );

    renderConversation();
    await waitFor(() =>
      expect(
        screen.getByText('You have blocked this member. They can no longer send you private messages.'),
      ).toBeOnTheScreen(),
    );
    expect(screen.queryByText('Unable to send message.')).toBeNull();
  });

  it('does not re-render message rows when typing a reply', async () => {
    fetchConversationMock.mockResolvedValue(
      conversationDetail([
        {
          id: theirMessageId,
          senderMemberId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
          senderDisplayName: 'Bob',
          body: 'Hello',
          createdAt: '2026-08-19T12:00:00.000Z',
          isMine: false,
          sortKey: 1,
          reportedByViewer: false,
        },
      ]),
    );

    let bubbleRenders = 0;
    messageBubbleRenderProbe.current = () => {
      bubbleRenders += 1;
    };

    renderConversation();
    await waitFor(() => expect(screen.getByText('Hello')).toBeOnTheScreen());
    const afterLoad = bubbleRenders;
    expect(afterLoad).toBeGreaterThan(0);

    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Reply'), 'x');
    expect(screen.getByLabelText('Reply').props.value).toBe('x');
    expect(bubbleRenders).toBe(afterLoad);
  });

  it('sends a reply and clears the draft', async () => {
    fetchConversationMock.mockResolvedValue(conversationDetail([]));
    replyToConversationMock.mockResolvedValue(
      conversationDetail([
        {
          id: '33333333-4444-5555-6666-777777777777',
          senderMemberId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
          senderDisplayName: 'Alice',
          body: 'Hello Bob',
          createdAt: '2026-08-19T12:02:00.000Z',
          isMine: true,
          sortKey: 1,
          reportedByViewer: false,
        },
      ]),
    );

    renderConversation();
    await waitFor(() => expect(screen.getByLabelText('Reply')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Reply'), 'Hello Bob');
    await user.press(screen.getByRole('button', { name: 'Send reply' }));

    await waitFor(() => expect(screen.getByText('Hello Bob')).toBeOnTheScreen());
    expect(enqueueMessageReplyMock).toHaveBeenCalledWith({
      memberId: 'member-1',
      conversationId,
      body: 'Hello Bob',
    });
    expect(replyToConversationMock).toHaveBeenCalledWith(
      'tok',
      conversationId,
      'Hello Bob',
      undefined,
      'op-queued',
    );
    expect(screen.getByLabelText('Reply').props.value).toBe('');
  });

  it('disables the send button until a reply is drafted', async () => {
    fetchConversationMock.mockResolvedValue(conversationDetail([]));

    renderConversation();
    await waitFor(() => expect(screen.getByLabelText('Reply')).toBeOnTheScreen());

    expect(screen.getByRole('button', { name: 'Send reply' })).toBeDisabled();

    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Reply'), 'Hi');
    expect(screen.getByRole('button', { name: 'Send reply' })).toBeEnabled();

    expect(replyToConversationMock).not.toHaveBeenCalled();
  });

  it('archives the conversation and returns to the inbox', async () => {
    const alertSpy = jest.spyOn(Alert, 'alert').mockImplementation((_title, _message, buttons) => {
      buttons?.find((button) => button.text === 'Archive')?.onPress?.();
    });
    const navigation = fakeNavigation();
    fetchConversationMock.mockResolvedValue(
      conversationDetail([
        {
          id: theirMessageId,
          senderMemberId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
          senderDisplayName: 'Bob',
          body: 'Hello',
          createdAt: '2026-08-19T12:00:00.000Z',
          isMine: false,
          sortKey: 1,
          reportedByViewer: false,
        },
      ]),
    );
    archiveConversationMock.mockResolvedValueOnce(undefined);

    renderConversation(navigation);
    await waitFor(() => expect(screen.getByText('Hello')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Archive conversation' }));

    await waitFor(() => expect(archiveConversationMock).toHaveBeenCalledWith('tok', conversationId));
    expect(navigation.navigate).toHaveBeenCalledWith('Inbox');
    alertSpy.mockRestore();
  });

  it('blocks the other participant from the overflow menu and shows the blocked notice', async () => {
    const alertSpy = jest.spyOn(Alert, 'alert').mockImplementation((_title, _message, buttons) => {
      const target =
        buttons?.find((button) => button.text === 'Block member' || button.text === 'Block') ??
        buttons?.find((button) => button.style !== 'cancel');
      target?.onPress?.();
    });
    const navigation = fakeNavigation();
    fetchConversationMock.mockResolvedValueOnce(
      conversationDetail([
        {
          id: theirMessageId,
          senderMemberId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
          senderDisplayName: 'Bob',
          body: 'Hello',
          createdAt: '2026-08-19T12:00:00.000Z',
          isMine: false,
          sortKey: 1,
          reportedByViewer: false,
        },
      ]),
    );
    fetchConversationMock.mockResolvedValue(
      conversationDetail(
        [
          {
            id: theirMessageId,
            senderMemberId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
            senderDisplayName: 'Bob',
            body: 'Hello',
            createdAt: '2026-08-19T12:00:00.000Z',
            isMine: false,
            sortKey: 1,
            reportedByViewer: false,
          },
        ],
        { canSendReply: false, hasBlockedOtherParticipant: true },
      ),
    );
    blockConversationParticipantMock.mockResolvedValue(undefined);

    const main = renderConversation(navigation);
    await waitFor(() => expect(main.getByText('Hello')).toBeOnTheScreen());

    // The native-stack header isn't mounted by this harness (navigation is a
    // jest.fn() stub), so render the headerRight element it was configured
    // with directly to reach the overflow button.
    const latestOptions = navigation.setOptions.mock.calls.at(-1)?.[0];
    const header = renderWithProviders(latestOptions.headerRight(), { navigation: false });

    const user = userEvent.setup();
    await user.press(header.getByRole('button', { name: 'More options' }));

    await waitFor(() =>
      expect(blockConversationParticipantMock).toHaveBeenCalledWith('tok', conversationId),
    );
    // The composer (which needs canSendReply) disappears once the reload
    // reflects the block, and the blocked notice takes its place.
    await waitFor(
      () => expect(main.queryByLabelText('Reply')).toBeNull(),
      { timeout: 8000 },
    );
    await waitFor(
      () =>
        expect(
          main.queryByText('You have blocked this member. They can no longer send you private messages.'),
        ).not.toBeNull(),
      { timeout: 8000 },
    );
    alertSpy.mockRestore();
  }, 15000);

  it('unblocks the other participant from the overflow menu and shows a success alert', async () => {
    const titles: string[] = [];
    const alertSpy = jest.spyOn(Alert, 'alert').mockImplementation((title, _message, buttons) => {
      titles.push(String(title));
      const target =
        buttons?.find((button) => button.text === 'Unblock member' || button.text === 'Unblock') ??
        buttons?.find((button) => button.style !== 'cancel');
      target?.onPress?.();
    });
    const navigation = fakeNavigation();
    const blockedThread = conversationDetail(
      [
        {
          id: theirMessageId,
          senderMemberId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
          senderDisplayName: 'Bob',
          body: 'Hello',
          createdAt: '2026-08-19T12:00:00.000Z',
          isMine: false,
          sortKey: 1,
          reportedByViewer: false,
        },
      ],
      { canSendReply: false, hasBlockedOtherParticipant: true },
    );
    const openThread = conversationDetail(
      [
        {
          id: theirMessageId,
          senderMemberId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
          senderDisplayName: 'Bob',
          body: 'Hello',
          createdAt: '2026-08-19T12:00:00.000Z',
          isMine: false,
          sortKey: 1,
          reportedByViewer: false,
        },
      ],
      { canSendReply: true, hasBlockedOtherParticipant: false },
    );
    fetchConversationMock.mockImplementation(async () =>
      fetchConversationMock.mock.calls.length <= 1 ? blockedThread : openThread,
    );
    unblockConversationParticipantMock.mockResolvedValue(undefined);

    const main = renderConversation(navigation);
    await waitFor(() =>
      expect(
        main.getByText('You have blocked this member. They can no longer send you private messages.'),
      ).toBeOnTheScreen(),
    );

    const latestOptions = navigation.setOptions.mock.calls.at(-1)?.[0];
    const header = renderWithProviders(latestOptions.headerRight(), { navigation: false });

    const user = userEvent.setup();
    await user.press(header.getByRole('button', { name: 'More options' }));

    await waitFor(() =>
      expect(unblockConversationParticipantMock).toHaveBeenCalledWith('tok', conversationId),
    );
    await waitFor(
      () => {
        expect(
          main.queryByText('You have blocked this member. They can no longer send you private messages.'),
        ).toBeNull();
        expect(main.queryByTestId(testIds.conversationComposer)).not.toBeNull();
      },
      { timeout: 8000 },
    );
    expect(titles).toContain('Unblock member');
    expect(titles).toContain('Member unblocked');
    expect(blockConversationParticipantMock).not.toHaveBeenCalled();
    alertSpy.mockRestore();
  }, 15000);

  it('shows a cached conversation with an offline banner and hides report, archive, and block', async () => {
    const snapshot = conversationDetail([
      {
        id: theirMessageId,
        senderMemberId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
        senderDisplayName: 'Bob',
        body: 'Hello from cache',
        createdAt: '2026-08-19T12:00:00.000Z',
        isMine: false,
        sortKey: 1,
        reportedByViewer: false,
      },
    ]);
    fetchConversationResultMock.mockResolvedValue(asCache(snapshot));

    renderConversation();
    await waitFor(() => expect(screen.getByText('Hello from cache')).toBeOnTheScreen());
    expect(screen.getByTestId(testIds.offlineBanner)).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Send reply' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Archive conversation' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Report message' })).toBeNull();
    expect(fetchConversationMock).not.toHaveBeenCalled();
    expect(fetchConversationResultMock).toHaveBeenCalledTimes(1);
  });

  it('shows a queued reply on a cached snapshot when the send is offline', async () => {
    const snapshot = conversationDetail([
      {
        id: theirMessageId,
        senderMemberId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
        senderDisplayName: 'Bob',
        body: 'Hello from cache',
        createdAt: '2026-08-19T12:00:00.000Z',
        isMine: false,
        sortKey: 1,
        reportedByViewer: false,
      },
    ]);
    fetchConversationResultMock.mockResolvedValue(asCache(snapshot));
    replyToConversationMock.mockRejectedValueOnce(ApiError.offline());
    useOfflineQueueMock.mockReturnValue([
      {
        schemaVersion: 1,
        operationId: 'op-queued',
        memberId: 'member-1',
        kind: 'message.reply',
        target: { conversationId },
        payload: { body: 'Queued hello' },
        createdAt: '2026-08-19T12:05:00.000Z',
        updatedAt: '2026-08-19T12:05:00.000Z',
        attemptCount: 0,
        nextRetryAt: '2026-08-19T12:05:00.000Z',
        state: 'queued',
        lastError: null,
      },
    ]);

    renderConversation();
    await waitFor(() => expect(screen.getByText('Hello from cache')).toBeOnTheScreen());
    expect(screen.getByText('Queued hello')).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.pendingMessage)).toBeOnTheScreen();
    expect(screen.getByLabelText('Queued')).toBeOnTheScreen();
  });

  it('does not GET the conversation again when pull-to-refresh fails offline', async () => {
    const snapshot = conversationDetail([
      {
        id: theirMessageId,
        senderMemberId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
        senderDisplayName: 'Bob',
        body: 'Hello from cache',
        createdAt: '2026-08-19T12:00:00.000Z',
        isMine: false,
        sortKey: 1,
        reportedByViewer: false,
      },
    ]);
    fetchConversationResultMock.mockResolvedValueOnce(asCache(snapshot));

    renderConversation();
    await waitFor(() => expect(screen.getByText('Hello from cache')).toBeOnTheScreen());
    expect(fetchConversationResultMock).toHaveBeenCalledTimes(1);

    fetchConversationResultMock.mockRejectedValueOnce(ApiError.offline());
    fireEvent(screen.UNSAFE_getByType(RefreshControl), 'refresh');

    await waitFor(() => expect(fetchConversationResultMock).toHaveBeenCalledTimes(2));
    expect(screen.getByText('Hello from cache')).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.offlineBanner)).toBeOnTheScreen();
    expect(fetchConversationMock).not.toHaveBeenCalled();
  });

  it('hydrates from cache using the JWT sub when /me profile is unavailable', async () => {
    const encode = (value: object) => Buffer.from(JSON.stringify(value)).toString('base64url');
    const accessToken = `${encode({ alg: 'none', typ: 'JWT' })}.${encode({ sub: 'member-from-jwt' })}.sig`;
    mockSession.profile = null;
    mockSession.accessToken = accessToken;
    const snapshot = conversationDetail([
      {
        id: theirMessageId,
        senderMemberId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
        senderDisplayName: 'Bob',
        body: 'Hello from cache',
        createdAt: '2026-08-19T12:00:00.000Z',
        isMine: false,
        sortKey: 1,
        reportedByViewer: false,
      },
    ]);
    fetchConversationResultMock.mockResolvedValue(asCache(snapshot));

    renderConversation();
    await waitFor(() => expect(screen.getByText('Hello from cache')).toBeOnTheScreen());
    expect(fetchConversationResultMock).toHaveBeenCalledWith(
      accessToken,
      conversationId,
      expect.objectContaining({ memberId: 'member-from-jwt' }),
    );
  });

  it('offsets KeyboardAvoidingView by the native-stack header height on iOS', async () => {
    fetchConversationMock.mockResolvedValue(conversationDetail([]));

    renderConversation();
    await waitFor(() => expect(screen.getByLabelText('Reply')).toBeOnTheScreen());

    const avoiding = screen.UNSAFE_getByType(KeyboardAvoidingView);
    expect(avoiding.props.behavior).toBe('padding');
    expect(avoiding.props.keyboardVerticalOffset).toBe(testHeaderHeight);
  });

  it('keeps the composer as a sibling of the thread list, not a FlatList footer', async () => {
    fetchConversationMock.mockResolvedValue(conversationDetail([]));

    renderConversation();
    await waitFor(() => expect(screen.getByLabelText('Reply')).toBeOnTheScreen());

    const list = screen.UNSAFE_getByType(FlatList);
    expect(list.props.ListFooterComponent).toBeFalsy();
    expect(screen.getByLabelText('Reply')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Send reply' })).toBeOnTheScreen();
  });

  it('keeps the home-indicator inset on the composer while the keyboard is closed', async () => {
    fetchConversationMock.mockResolvedValue(conversationDetail([]));

    renderConversation();
    await waitFor(() => expect(screen.getByLabelText('Reply')).toBeOnTheScreen());

    expect(screen.getByTestId(testIds.conversationComposer)).toHaveStyle({ paddingBottom: 34 });
  });

  it('drops the home-indicator inset from the composer while the iOS keyboard is open', async () => {
    const listeners: Record<string, (() => void)[]> = {};
    const addListener = jest.spyOn(Keyboard, 'addListener').mockImplementation((event, callback) => {
      listeners[event] ??= [];
      listeners[event].push(callback as () => void);
      return { remove: jest.fn() } as unknown as ReturnType<typeof Keyboard.addListener>;
    });

    fetchConversationMock.mockResolvedValue(conversationDetail([]));
    renderConversation();
    await waitFor(() => expect(screen.getByLabelText('Reply')).toBeOnTheScreen());

    act(() => {
      listeners.keyboardWillShow?.forEach((listener) => listener());
    });
    expect(screen.getByTestId(testIds.conversationComposer)).toHaveStyle({ paddingBottom: space.md });

    act(() => {
      listeners.keyboardWillHide?.forEach((listener) => listener());
    });
    expect(screen.getByTestId(testIds.conversationComposer)).toHaveStyle({ paddingBottom: 34 });

    addListener.mockRestore();
  });

  it('leaves KeyboardAvoidingView behavior undefined on Android', async () => {
    const originalOs = Platform.OS;
    Object.defineProperty(Platform, 'OS', { configurable: true, value: 'android' });

    try {
      fetchConversationMock.mockResolvedValue(conversationDetail([]));
      renderConversation();
      await waitFor(() => expect(screen.getByLabelText('Reply')).toBeOnTheScreen());

      const avoiding = screen.UNSAFE_getByType(KeyboardAvoidingView);
      expect(avoiding.props.behavior).toBeUndefined();
      expect(avoiding.props.keyboardVerticalOffset).toBe(0);
      expect(screen.getByTestId(testIds.conversationComposer)).toHaveStyle({ paddingBottom: 34 });
    } finally {
      Object.defineProperty(Platform, 'OS', { configurable: true, value: originalOs });
    }
  });
});

