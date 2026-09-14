import { act, renderHook, waitFor } from '@testing-library/react-native';
import { ApiError } from '../../api/client';
import {
  archiveConversation,
  blockConversationParticipant,
  fetchConversationResult,
  replyToConversation,
  reportConversationMessage,
  unblockConversationParticipant,
} from '../../api/messages';
import { conversationDetailFixture } from '../../test/fixtures';
import { useConversation } from './useConversation';

jest.mock('../../api/messages', () => ({
  fetchConversationResult: jest.fn(),
  replyToConversation: jest.fn(),
  reportConversationMessage: jest.fn(),
  archiveConversation: jest.fn(),
  blockConversationParticipant: jest.fn(),
  unblockConversationParticipant: jest.fn(),
}));

jest.mock('../../offlineQueue', () => ({
  enqueueMessageReply: jest.fn(),
  flushOfflineQueue: jest.fn(),
  removeOfflineItem: jest.fn(),
}));

const fetchConversationResultMock = fetchConversationResult as jest.MockedFunction<
  typeof fetchConversationResult
>;
const unblockConversationParticipantMock = unblockConversationParticipant as jest.MockedFunction<
  typeof unblockConversationParticipant
>;
const blockConversationParticipantMock = blockConversationParticipant as jest.MockedFunction<
  typeof blockConversationParticipant
>;

const conversationId = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';
const cachedAt = '2026-09-14T12:00:00.000Z';

function blockedDetail() {
  return conversationDetailFixture({
    conversationId,
    canSendReply: false,
    hasBlockedOtherParticipant: true,
  });
}

function openDetail() {
  return conversationDetailFixture({
    conversationId,
    canSendReply: true,
    hasBlockedOtherParticipant: false,
  });
}

describe('useConversation unblock', () => {
  beforeEach(() => {
    fetchConversationResultMock.mockReset();
    unblockConversationParticipantMock.mockReset();
    blockConversationParticipantMock.mockReset();
    (archiveConversation as jest.Mock).mockReset();
    (replyToConversation as jest.Mock).mockReset();
    (reportConversationMessage as jest.Mock).mockReset();
    fetchConversationResultMock.mockResolvedValue({
      data: blockedDetail(),
      source: 'network',
      cachedAt,
    });
  });

  it('posts unblock, clears busy/error, and reloads so canSendReply refreshes', async () => {
    fetchConversationResultMock
      .mockResolvedValueOnce({ data: blockedDetail(), source: 'network', cachedAt })
      .mockResolvedValueOnce({ data: openDetail(), source: 'network', cachedAt });
    unblockConversationParticipantMock.mockResolvedValue(undefined);

    const { result } = renderHook(() =>
      useConversation(conversationId, 'tok', 'member-1', {
        scrollToEnd: jest.fn(),
        onArchived: jest.fn(),
      }),
    );

    await waitFor(() => expect(result.current.detail?.hasBlockedOtherParticipant).toBe(true));

    let ok = false;
    await act(async () => {
      ok = await result.current.unblock();
    });

    expect(ok).toBe(true);
    expect(unblockConversationParticipantMock).toHaveBeenCalledWith('tok', conversationId);
    await waitFor(() => expect(result.current.detail?.canSendReply).toBe(true));
    expect(result.current.unblocking).toBe(false);
    expect(result.current.unblockError).toBeNull();
    expect(fetchConversationResultMock).toHaveBeenCalledTimes(2);
  });

  it('surfaces an unblock error without reloading', async () => {
    unblockConversationParticipantMock.mockRejectedValue(new ApiError(500, 'Could not unblock.'));

    const { result } = renderHook(() =>
      useConversation(conversationId, 'tok', 'member-1', {
        scrollToEnd: jest.fn(),
        onArchived: jest.fn(),
      }),
    );

    await waitFor(() => expect(result.current.detail?.hasBlockedOtherParticipant).toBe(true));

    let ok = true;
    await act(async () => {
      ok = await result.current.unblock();
    });

    expect(ok).toBe(false);
    expect(result.current.unblocking).toBe(false);
    expect(result.current.unblockError).toBe('Could not unblock.');
    expect(result.current.detail?.hasBlockedOtherParticipant).toBe(true);
    expect(fetchConversationResultMock).toHaveBeenCalledTimes(1);
  });
});
