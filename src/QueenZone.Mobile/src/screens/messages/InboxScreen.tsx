import { memo, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Alert, Pressable, Text, View, type ListRenderItem } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { archiveConversation, fetchInbox, type InboxConversation } from '../../api/messages';
import type { ApiPagedResponse } from '../../api/types';
import { getContentCache, inboxCacheKey } from '../../cache';
import { usePagedContent } from '../../hooks/usePagedContent';
import type { HomeStackParamList } from '../../navigation/types';
import { resolvePushMemberId } from '../../notifications/pushMemberId';
import {
  flushOfflineQueue,
  removeOfflineItem,
  updateOfflineItem,
  useOfflineQueue,
  type OfflineQueueItem,
} from '../../offlineQueue';
import { MemberGate } from '../../session/MemberGate';
import { useSession } from '../../session/SessionContext';
import { radius, space, type, useTheme } from '../../theme';
import { Button } from '../../ui/Button';
import { PageTitleBlock } from '../../ui/PageTitleBlock';
import { PagedListScreen } from '../../ui/PagedListScreen';
import { testIds } from '../../test/testIds';
import {
  formatMessageTimestamp,
  inboxPageSize,
  inboxRowA11yLabel,
  unreadBadgeLabel,
} from './inboxMeta';

type Props = NativeStackScreenProps<HomeStackParamList, 'Inbox'>;

function overlayQueuedComposes(
  items: InboxConversation[],
  queueItems: OfflineQueueItem[],
): InboxConversation[] {
  const pending = queueItems.filter((item) => item.kind === 'message.compose');
  if (pending.length === 0) {
    return items;
  }
  const extra = pending.map((item) => ({
    conversationId: `pending:${item.operationId}`,
    otherParticipantId: 'recipientMemberId' in item.target ? item.target.recipientMemberId : '',
    otherParticipantDisplayName:
      item.state === 'needs_attention'
        ? 'Needs attention'
        : item.state === 'sending'
          ? 'Sending…'
          : 'Queued message',
    lastMessagePreview: item.payload.body,
    lastMessageAt: item.createdAt,
    hasUnread: false,
    unreadCount: 0,
    detailPath: '',
  }));
  return [...extra, ...items];
}

function inboxKeyExtractor(item: InboxConversation): string {
  return item.conversationId;
}

export function InboxScreen({ navigation }: Props) {
  return (
    <MemberGate title="Messages">
      <InboxList navigation={navigation} />
    </MemberGate>
  );
}

function InboxList({ navigation }: Pick<Props, 'navigation'>) {
  const { c } = useTheme();
  const { accessToken, profile } = useSession();
  const memberId = accessToken ? resolvePushMemberId(accessToken, profile?.memberId) : null;
  const queueItems = useOfflineQueue(memberId);
  const cacheKey = memberId ? inboxCacheKey(memberId) : null;
  const [cachedPage, setCachedPage] = useState<ApiPagedResponse<InboxConversation> | null>(null);
  const skipNextFocusRefresh = useRef(true);
  const [archivingId, setArchivingId] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  useEffect(() => {
    if (!cacheKey) {
      setCachedPage(null);
      return;
    }
    let cancelled = false;
    getContentCache()
      .get<ApiPagedResponse<InboxConversation>>(cacheKey)
      .then((cached) => {
        if (!cancelled) {
          setCachedPage(cached);
        }
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [cacheKey]);

  const paged = usePagedContent<InboxConversation>(
    useCallback(
      (page, signal, mode) => {
        if (!accessToken) {
          return Promise.resolve({
            items: [],
            page: 1,
            pageSize: inboxPageSize,
            totalCount: 0,
            totalPages: 0,
          });
        }
        return fetchInbox(accessToken, {
          page,
          pageSize: inboxPageSize,
          signal,
          networkOnly: mode === 'refresh',
        });
      },
      [accessToken],
    ),
    inboxPageSize,
    accessToken ?? '',
  );

  const { refresh: refreshPaged } = paged;

  useFocusEffect(
    useCallback(() => {
      if (skipNextFocusRefresh.current) {
        skipNextFocusRefresh.current = false;
        return;
      }
      refreshPaged();
    }, [refreshPaged]),
  );

  const usingCachedPage = cachedPage !== null && paged.items.length === 0 && (paged.loading || paged.error !== null);
  const sourceItems = usingCachedPage ? cachedPage.items : paged.items;
  const displayItems = useMemo(
    () => overlayQueuedComposes(sourceItems, queueItems),
    [queueItems, sourceItems],
  );

  const handleArchive = useCallback(
    async (conversationId: string) => {
      if (!accessToken) {
        return;
      }
      setActionError(null);
      setArchivingId(conversationId);
      try {
        await archiveConversation(accessToken, conversationId);
        refreshPaged();
      } catch {
        setActionError('Unable to archive this conversation. Try again.');
      } finally {
        setArchivingId(null);
      }
    },
    [accessToken, refreshPaged],
  );

  const openConversation = useCallback(
    (item: InboxConversation) => {
      if (item.conversationId.startsWith('pending:')) {
        const operationId = item.conversationId.slice('pending:'.length);
        Alert.alert(item.otherParticipantDisplayName, item.lastMessagePreview, [
          { text: 'Dismiss', style: 'cancel' },
          {
            text: 'Discard',
            style: 'destructive',
            onPress: () => {
              void removeOfflineItem(operationId);
            },
          },
          {
            text: 'Retry',
            onPress: () => {
              void updateOfflineItem(operationId, {
                state: 'queued',
                nextRetryAt: new Date().toISOString(),
                lastError: null,
              }).then(() => {
                void flushOfflineQueue();
              });
            },
          },
        ]);
        return;
      }
      navigation.navigate('Conversation', { id: item.conversationId });
    },
    [navigation],
  );

  const archiveItem = useCallback(
    (conversationId: string) => {
      if (conversationId.startsWith('pending:')) {
        return;
      }
      void handleArchive(conversationId);
    },
    [handleArchive],
  );

  const renderItem = useCallback<ListRenderItem<InboxConversation>>(
    ({ item }) => (
      <InboxRow
        item={item}
        archiving={archivingId === item.conversationId}
        onPress={openConversation}
        onArchive={archiveItem}
      />
    ),
    [archivingId, archiveItem, openConversation],
  );

  const header = (
    <View>
      <PageTitleBlock
        eyebrow="Community"
        title="Messages"
        subtitle="Private conversations with other members."
      />
      <View style={{ paddingHorizontal: space.xl, paddingBottom: space.lg, flexDirection: 'row', gap: space.sm }}>
        <Button
          label="New message"
          testID={testIds.inboxCompose}
          onPress={() => navigation.navigate('ComposeMessage')}
        />
        <Button
          label="Archived"
          variant="ghost"
          testID={testIds.inboxArchived}
          onPress={() => navigation.navigate('Archived')}
        />
      </View>
      {actionError ? (
        <View style={{ paddingHorizontal: space.xl, paddingBottom: space.md }}>
          <Text style={[type.caption, { color: c.textSecondary }]}>{actionError}</Text>
        </View>
      ) : null}
    </View>
  );

  return (
    <PagedListScreen
      testID={testIds.inboxScreen}
      paged={{
        ...paged,
        items: displayItems,
        loading: paged.loading && !usingCachedPage,
        refreshing: paged.refreshing || (usingCachedPage && paged.loading),
        error: usingCachedPage ? null : paged.error,
      }}
      keyExtractor={inboxKeyExtractor}
      loadingLabel="Loading messages…"
      emptyMessage="You have no private messages yet."
      ListHeaderComponent={header}
      renderItem={renderItem}
    />
  );
}

const InboxRow = memo(function InboxRow({
  item,
  archiving,
  onPress,
  onArchive,
}: {
  item: InboxConversation;
  archiving: boolean;
  onPress: (item: InboxConversation) => void;
  onArchive: (conversationId: string) => void;
}) {
  const { c } = useTheme();
  const unread = unreadBadgeLabel(item.unreadCount);
  return (
    <View style={{ borderTopWidth: 1, borderTopColor: c.hairline }}>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={inboxRowA11yLabel(item)}
        onPress={() => onPress(item)}
        style={({ pressed }) => [
          {
            paddingVertical: space.base,
            paddingHorizontal: space.xl,
            gap: 6,
            opacity: pressed ? 0.72 : 1,
          },
        ]}
      >
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: 10 }}>
          <Text
            numberOfLines={1}
            style={[
              type.listTitle,
              { color: c.textPrimary, flex: 1 },
            ]}
          >
            {item.otherParticipantDisplayName}
          </Text>
          {unread ? (
            <View
              style={{
                backgroundColor: c.accentPrimary,
                borderRadius: radius.pill,
                paddingHorizontal: 8,
                paddingVertical: 3,
              }}
            >
              <Text style={[type.meta, { color: c.textOnAccent, letterSpacing: 0.4 }]}>{unread}</Text>
            </View>
          ) : null}
        </View>
        {item.lastMessagePreview ? (
          <Text numberOfLines={2} style={[type.caption, { color: c.textSecondary }]}>
            {item.lastMessagePreview}
          </Text>
        ) : null}
        <Text style={[type.meta, { color: c.textMuted }]}>{formatMessageTimestamp(item.lastMessageAt)}</Text>
      </Pressable>
      <View style={{ paddingHorizontal: space.xl, paddingBottom: space.md }}>
        <Button
          label="Archive"
          size="sm"
          variant="ghost"
          onPress={() => onArchive(item.conversationId)}
          loading={archiving}
        />
      </View>
    </View>
  );
});
