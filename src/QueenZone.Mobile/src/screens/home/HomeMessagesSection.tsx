import type { ReactNode } from 'react';
import { memo } from 'react';
import { ChevronRight } from 'lucide-react-native';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import type { InboxConversation } from '../../api';
import type { SectionView } from '../../hooks/useHomeSection';
import { SectionErrorBlock } from '../../ui/ScreenStates';
import { SectionHeader } from '../../ui/SectionHeader';
import { initials } from '../../ui/initials';
import { fonts, radius, space, type, useTheme } from '../../theme';

export const HomeMessagesSection = memo(function HomeMessagesSection({
  isSignedIn,
  messagesView,
  onOpenConversation,
  onOpenInbox,
  onReloadMessages,
  onSignIn,
}: {
  isSignedIn: boolean;
  messagesView: SectionView<{ items: InboxConversation[] } | null>;
  onOpenConversation: (conversationId: string) => void;
  onOpenInbox: () => void;
  onReloadMessages: () => void;
  onSignIn: () => void;
}) {
  const { c } = useTheme();

  if (!isSignedIn) {
    return (
      <Pressable
        accessibilityRole="button"
        accessibilityLabel="Member sign in"
        onPress={onSignIn}
        style={[styles.signInCard, { backgroundColor: c.surfaceRaised, borderColor: c.hairline }]}
      >
        <Text style={[type.body, { color: c.textSecondary }]}>Member sign in</Text>
        <ChevronRight size={17} color={c.textMuted} strokeWidth={1.5} />
      </Pressable>
    );
  }

  let messagesContent: ReactNode;
  if (messagesView.kind === 'skeleton') {
    messagesContent = <View style={styles.skeletonList}>
      {[0, 1].map((key) => (
        <View key={key} style={[styles.skeletonRow, { backgroundColor: c.surfaceCard }]} />
      ))}
    </View>;
  } else if (messagesView.kind === 'error') {
    messagesContent = <SectionErrorBlock message={messagesView.message} onRetry={onReloadMessages} />;
  } else if (messagesView.kind === 'content' && messagesView.data !== null && messagesView.data.items.length > 0) {
    messagesContent = messagesView.data.items.map((conversation) => (
      <Pressable
        key={conversation.conversationId}
        accessible
        accessibilityRole="button"
        accessibilityLabel={conversation.otherParticipantDisplayName}
        onPress={() => onOpenConversation(conversation.conversationId)}
        style={[styles.row, { borderTopColor: c.hairline }]}
      >
        <View style={[styles.avatar, { backgroundColor: c.surfaceSheet, borderColor: c.border }]}>
          <Text style={[styles.avatarLabel, { color: c.textPrimary }]}>
            {initials(conversation.otherParticipantDisplayName)}
          </Text>
        </View>
        <View style={styles.rowText}>
          <Text style={[styles.rowTitle, { color: c.textPrimary }]}>
            {conversation.otherParticipantDisplayName}
          </Text>
          <Text numberOfLines={1} style={[type.body, styles.rowPreview, { color: c.textSecondary }]}>
            {conversation.lastMessagePreview}
          </Text>
        </View>
        {conversation.hasUnread ? (
          <View style={[styles.unreadDot, { backgroundColor: c.accentPrimary }]} />
        ) : null}
      </Pressable>
    ));
  } else {
    messagesContent = null;
  }

  return (
    <View style={[styles.card, { backgroundColor: c.surfaceRaised, borderColor: c.hairline }]}>
      <SectionHeader title="Your messages" actionLabel="Inbox" onAction={onOpenInbox} />
      {messagesContent}
    </View>
  );
});

const styles = StyleSheet.create({
  card: {
    marginTop: space.xxl,
    paddingVertical: 26,
    paddingHorizontal: space.xl,
    borderTopWidth: 1,
    borderBottomWidth: 1,
  },
  skeletonList: { gap: 12 },
  skeletonRow: { height: 44, borderRadius: radius.xs },
  row: {
    paddingVertical: 13,
    borderTopWidth: 1,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
  },
  avatar: {
    width: space.avatar,
    height: space.avatar,
    borderRadius: radius.avatar,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  avatarLabel: { fontFamily: fonts.display, fontSize: 12 },
  rowText: { flex: 1, gap: 4 },
  rowTitle: { fontFamily: fonts.bodySemi, fontSize: 14.5 },
  rowPreview: { fontSize: 13 },
  unreadDot: { width: 7, height: 7, borderRadius: 3.5 },
  signInCard: {
    marginTop: space.xxl,
    paddingVertical: 18,
    paddingHorizontal: space.xl,
    borderTopWidth: 1,
    borderBottomWidth: 1,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
});
