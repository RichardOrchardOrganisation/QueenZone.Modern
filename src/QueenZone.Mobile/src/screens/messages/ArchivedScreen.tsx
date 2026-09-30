import { memo, useCallback, useRef, useState } from 'react';
import { Pressable, Text, View, type ListRenderItem } from 'react-native';
import { useFocusEffect } from '@react-navigation/native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { fetchArchivedInbox, unarchiveConversation, type InboxConversation } from '../../api/messages';
import { usePagedContent } from '../../hooks/usePagedContent';
import type { HomeStackParamList } from '../../navigation/types';
import { MemberGate } from '../../session/MemberGate';
import { useSession } from '../../session/SessionContext';
import { space, type, useTheme } from '../../theme';
import { Button } from '../../ui/Button';
import { PageTitleBlock } from '../../ui/PageTitleBlock';
import { PagedListScreen } from '../../ui/PagedListScreen';
import { testIds } from '../../test/testIds';
import { formatMessageTimestamp, inboxPageSize, inboxRowA11yLabel, unreadBadgeLabel } from './inboxMeta';

type Props = NativeStackScreenProps<HomeStackParamList, 'Archived'>;

function archivedKeyExtractor(item: InboxConversation): string {
  return item.conversationId;
}

export function ArchivedScreen({ navigation }: Props) {
  return (
    <MemberGate title="Archived messages">
      <ArchivedList navigation={navigation} />
    </MemberGate>
  );
}

function ArchivedList({ navigation }: Pick<Props, 'navigation'>) {
  const { c } = useTheme();
  const { accessToken } = useSession();
  const skipNextFocusRefresh = useRef(true);
  const [unarchivingId, setUnarchivingId] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const paged = usePagedContent<InboxConversation>(
    useCallback(
      (page, signal) => {
        if (!accessToken) {
          return Promise.resolve({
            items: [],
            page: 1,
            pageSize: inboxPageSize,
            totalCount: 0,
            totalPages: 0,
          });
        }
        return fetchArchivedInbox(accessToken, { page, pageSize: inboxPageSize, signal });
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

  const handleUnarchive = useCallback(
    async (conversationId: string) => {
      if (!accessToken) {
        return;
      }
      setActionError(null);
      setUnarchivingId(conversationId);
      try {
        await unarchiveConversation(accessToken, conversationId);
        refreshPaged();
      } catch {
        setActionError('Unable to unarchive this conversation. Try again.');
      } finally {
        setUnarchivingId(null);
      }
    },
    [accessToken, refreshPaged],
  );

  const openConversation = useCallback(
    (conversationId: string) => {
      navigation.navigate('Conversation', { id: conversationId });
    },
    [navigation],
  );

  const renderItem = useCallback<ListRenderItem<InboxConversation>>(
    ({ item }) => (
      <ArchivedRow
        item={item}
        unarchiving={unarchivingId === item.conversationId}
        onPress={openConversation}
        onUnarchive={handleUnarchive}
      />
    ),
    [handleUnarchive, openConversation, unarchivingId],
  );

  const header = (
    <View>
      <PageTitleBlock
        eyebrow="Community"
        title="Archived messages"
        subtitle="Conversations you have archived."
      />
      {actionError ? (
        <View style={{ paddingHorizontal: space.xl, paddingBottom: space.md }}>
          <Text style={[type.caption, { color: c.textSecondary }]}>{actionError}</Text>
        </View>
      ) : null}
    </View>
  );

  return (
    <PagedListScreen
      testID={testIds.archivedScreen}
      paged={paged}
      keyExtractor={archivedKeyExtractor}
      loadingLabel="Loading archived messages…"
      emptyMessage="You have no archived conversations."
      ListHeaderComponent={header}
      renderItem={renderItem}
    />
  );
}

const ArchivedRow = memo(function ArchivedRow({
  item,
  unarchiving,
  onPress,
  onUnarchive,
}: {
  item: InboxConversation;
  unarchiving: boolean;
  onPress: (conversationId: string) => void;
  onUnarchive: (conversationId: string) => Promise<void>;
}) {
  const { c } = useTheme();
  const unread = unreadBadgeLabel(item.unreadCount);
  return (
    <View style={{ borderTopWidth: 1, borderTopColor: c.hairline }}>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={inboxRowA11yLabel(item)}
        onPress={() => onPress(item.conversationId)}
        style={({ pressed }) => [
          { paddingVertical: space.base, paddingHorizontal: space.xl, gap: 6, opacity: pressed ? 0.72 : 1 },
        ]}
      >
        <Text numberOfLines={1} style={[type.listTitle, { color: c.textPrimary }]}>
          {item.otherParticipantDisplayName}
        </Text>
        {item.lastMessagePreview ? (
          <Text numberOfLines={2} style={[type.caption, { color: c.textSecondary }]}>
            {item.lastMessagePreview}
          </Text>
        ) : null}
        <Text style={[type.meta, { color: c.textMuted }]}>
          {formatMessageTimestamp(item.lastMessageAt)}
          {unread ? ` · ${unread}` : ''}
        </Text>
      </Pressable>
      <View style={{ paddingHorizontal: space.xl, paddingBottom: space.md }}>
        <Button
          label="Unarchive"
          size="sm"
          variant="ghost"
          onPress={() => {
            void onUnarchive(item.conversationId);
          }}
          loading={unarchiving}
        />
      </View>
    </View>
  );
});
