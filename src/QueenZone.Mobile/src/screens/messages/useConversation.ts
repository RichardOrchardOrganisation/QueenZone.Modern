import { useCallback, useEffect, useReducer, useRef, useState } from 'react';
import type { CacheSource } from '../../api';
import {
  archiveConversation,
  blockConversationParticipant,
  fetchConversationResult,
  replyToConversation,
  reportConversationMessage,
  unblockConversationParticipant,
  type ConversationDetail,
} from '../../api/messages';
import {
  enqueueMessageReply,
  flushOfflineQueue,
  removeOfflineItem,
  type OfflineQueueItem,
} from '../../offlineQueue';
import { conversationPageSize } from './inboxMeta';
import { isStaleReadFailure, messageFromUnknownError } from './conversationMeta';

type ArchiveBlockState = {
  archiving: boolean;
  archiveError: string | null;
  blocking: boolean;
  blockError: string | null;
  unblocking: boolean;
  unblockError: string | null;
};

const initialArchiveBlockState: ArchiveBlockState = {
  archiving: false,
  archiveError: null,
  blocking: false,
  blockError: null,
  unblocking: false,
  unblockError: null,
};

type ArchiveBlockAction =
  | { type: 'archive/start' }
  | { type: 'archive/success' }
  | { type: 'archive/failure'; message: string }
  | { type: 'block/start' }
  | { type: 'block/success' }
  | { type: 'block/failure'; message: string }
  | { type: 'unblock/start' }
  | { type: 'unblock/success' }
  | { type: 'unblock/failure'; message: string };

function archiveBlockReducer(state: ArchiveBlockState, action: ArchiveBlockAction): ArchiveBlockState {
  switch (action.type) {
    case 'archive/start':
      return { ...state, archiving: true, archiveError: null };
    case 'archive/success':
      return { ...state, archiving: false };
    case 'archive/failure':
      return { ...state, archiving: false, archiveError: action.message };
    case 'block/start':
      return { ...state, blocking: true, blockError: null };
    case 'block/success':
      return { ...state, blocking: false };
    case 'block/failure':
      return { ...state, blocking: false, blockError: action.message };
    case 'unblock/start':
      return { ...state, unblocking: true, unblockError: null };
    case 'unblock/success':
      return { ...state, unblocking: false };
    case 'unblock/failure':
      return { ...state, unblocking: false, unblockError: action.message };
    default:
      return state;
  }
}

export function useConversation(
  conversationId: string | null,
  accessToken: string | null,
  memberId: string | null,
  options: { scrollToEnd: () => void; onArchived: () => void },
) {
  const { scrollToEnd, onArchived } = options;
  const [detail, setDetail] = useState<ConversationDetail | null>(null);
  const [source, setSource] = useState<CacheSource>('network');
  const [cachedAt, setCachedAt] = useState<string | null>(null);
  const detailRef = useRef<ConversationDetail | null>(null);
  detailRef.current = detail;
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [reloadToken, setReloadToken] = useState(0);
  const [archiveBlock, dispatch] = useReducer(archiveBlockReducer, initialArchiveBlockState);

  const load = useCallback(
    async (signal: AbortSignal, mode: 'initial' | 'refresh') => {
      if (!accessToken || !conversationId) {
        setDetail(null);
        setError(conversationId ? 'Sign in to continue.' : 'This conversation is not available.');
        setLoading(false);
        setRefreshing(false);
        return;
      }

      if (mode === 'initial') {
        setLoading(true);
      } else {
        setRefreshing(true);
      }
      setError(null);

      try {
        const next = await fetchConversationResult(accessToken, conversationId, {
          pageSize: conversationPageSize,
          signal,
          memberId,
          networkOnly: mode === 'refresh',
        });
        if (signal.aborted) {
          return;
        }
        setDetail(next.data);
        setSource(next.source);
        setCachedAt(next.cachedAt);
        requestAnimationFrame(scrollToEnd);
      } catch (err: unknown) {
        if (signal.aborted || (err instanceof Error && err.name === 'AbortError')) {
          return;
        }
        if (mode === 'refresh' && detailRef.current && isStaleReadFailure(err)) {
          setSource('cache');
          return;
        }
        setDetail(null);
        setError(messageFromUnknownError(err));
      } finally {
        if (!signal.aborted) {
          setLoading(false);
          setRefreshing(false);
        }
      }
    },
    [accessToken, conversationId, memberId, scrollToEnd],
  );

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal, 'initial');
    return () => controller.abort();
  }, [load, reloadToken]);

  const reload = useCallback(() => {
    setReloadToken((n) => n + 1);
  }, []);

  const refresh = useCallback(() => {
    const controller = new AbortController();
    void load(controller.signal, 'refresh');
  }, [load]);

  const sendReply = useCallback(
    async (body: string) => {
      if (!accessToken || !conversationId || !memberId) {
        throw new Error('Sign in to continue.');
      }

      const queued = await enqueueMessageReply({
        memberId,
        conversationId,
        body,
      });
      void flushOfflineQueue();
      requestAnimationFrame(scrollToEnd);

      void (async () => {
        try {
          const next = await replyToConversation(
            accessToken,
            conversationId,
            queued.payload.body,
            undefined,
            queued.operationId,
          );
          await removeOfflineItem(queued.operationId);
          setDetail(next);
          setSource('network');
          setCachedAt(new Date().toISOString());
        } catch {
          // Offline/timeout failures stay queued; the offline queue flusher retries later.
        }
      })();
    },
    [accessToken, conversationId, memberId, scrollToEnd],
  );

  const submitReport = useCallback(
    async (messageId: string, reason?: string) => {
      if (!accessToken || !conversationId) {
        throw new Error('Sign in to continue.');
      }
      await reportConversationMessage(accessToken, conversationId, messageId, reason);
      setDetail((current) =>
        current
          ? {
              ...current,
              messages: current.messages.map((item) =>
                item.id === messageId ? { ...item, reportedByViewer: true } : item,
              ),
            }
          : current,
      );
    },
    [accessToken, conversationId],
  );

  const archive = useCallback(async () => {
    if (!accessToken || !conversationId) {
      return;
    }
    dispatch({ type: 'archive/start' });
    try {
      await archiveConversation(accessToken, conversationId);
      dispatch({ type: 'archive/success' });
      onArchived();
    } catch (err: unknown) {
      dispatch({ type: 'archive/failure', message: messageFromUnknownError(err) });
    }
  }, [accessToken, conversationId, onArchived]);

  const block = useCallback(async () => {
    if (!accessToken || !conversationId) {
      return;
    }
    dispatch({ type: 'block/start' });
    try {
      await blockConversationParticipant(accessToken, conversationId);
      dispatch({ type: 'block/success' });
      reload();
    } catch (err: unknown) {
      dispatch({ type: 'block/failure', message: messageFromUnknownError(err) });
    }
  }, [accessToken, conversationId, reload]);

  const unblock = useCallback(async () => {
    if (!accessToken || !conversationId) {
      return false;
    }
    dispatch({ type: 'unblock/start' });
    try {
      await unblockConversationParticipant(accessToken, conversationId);
      dispatch({ type: 'unblock/success' });
      reload();
      return true;
    } catch (err: unknown) {
      dispatch({ type: 'unblock/failure', message: messageFromUnknownError(err) });
      return false;
    }
  }, [accessToken, conversationId, reload]);

  return {
    detail,
    source,
    cachedAt,
    error,
    loading,
    refreshing,
    reload,
    refresh,
    sendReply,
    submitReport,
    archive,
    block,
    unblock,
    ...archiveBlock,
  };
}

export type UseConversationResult = ReturnType<typeof useConversation>;
export type { OfflineQueueItem };
