import { memo, useState } from 'react';
import { Alert, Pressable, StyleSheet, Text, View } from 'react-native';
import type { ForumPost } from '../../api';
import { flushOfflineQueue, removeOfflineItem, updateOfflineItem, type OfflineQueueItem } from '../../offlineQueue';
import { RichHtmlBody } from '../../ui/RichHtmlBody';
import { testIds } from '../../test/testIds';
import { space, type, useTheme } from '../../theme';
import { formatMemberSince, formatPostTimestamp } from './forumThreadMeta';
import { ForumAttachmentList } from './ForumAttachmentList';

export type DisplayPost = ForumPost & {
  queueState?: OfflineQueueItem['state'];
  operationId?: string;
};

export function postKeyExtractor(item: DisplayPost): string {
  return item.operationId ?? String(item.id);
}

export const ForumPostRow = memo(function ForumPostRow({
  post,
  isSignedIn,
  accessToken,
  interactionsEnabled,
  isCurrentMember = false,
  isReported = false,
  isBlocked = false,
  onReport = () => undefined,
  onBlock = () => undefined,
  onUnblock = () => undefined,
}: {
  post: DisplayPost;
  isSignedIn: boolean;
  accessToken: string | null;
  interactionsEnabled: boolean;
  isCurrentMember?: boolean;
  isReported?: boolean;
  isBlocked?: boolean;
  onReport?: () => void;
  onBlock?: () => void;
  onUnblock?: () => void;
}) {
  const { c } = useTheme();
  const posted = formatPostTimestamp(post.postedAt);
  const memberSince = formatMemberSince(post.authorMemberSince);
  const meta = [posted, memberSince ? `Member since ${memberSince}` : null].filter(Boolean).join(' · ');
  const [revealed, setRevealed] = useState(false);

  const openMenu = () => {
    const actions = [
      { text: 'Cancel', style: 'cancel' as const },
      ...(isReported ? [] : [{ text: 'Report post', onPress: onReport }]),
      ...(post.authorMemberId
        ? isBlocked
          ? [{ text: 'Unblock member', onPress: onUnblock }]
          : [{ text: 'Block member', style: 'destructive' as const, onPress: onBlock }]
        : []),
    ];
    Alert.alert('Post actions', undefined, actions);
  };

  return (
    <View style={[styles.post, { borderTopColor: c.hairline }]}>
      <View style={styles.authorRow}>
        <Text style={[type.listTitle, { color: c.textPrimary, flex: 1 }]} allowFontScaling>{post.authorUsername}</Text>
        {!isCurrentMember && !post.queueState ? (
          <Pressable accessibilityRole="button" accessibilityLabel={`Actions for ${post.authorUsername}'s post`} disabled={!interactionsEnabled} onPress={openMenu} hitSlop={8}>
            <Text style={[type.listTitle, { color: c.accentPrimary }]}>•••</Text>
          </Pressable>
        ) : null}
      </View>
      {meta ? (
        <Text style={[type.meta, { color: c.textMuted, marginTop: space.xs }]}>{meta}</Text>
      ) : null}
      {isBlocked && !revealed ? (
        <Pressable accessibilityRole="button" accessibilityLabel="Show post from blocked member" onPress={() => setRevealed(true)} style={styles.blocked}>
          <Text style={[type.body, { color: c.textSecondary }]}>Post from a blocked member. Show this post</Text>
        </Pressable>
      ) : <View style={styles.body}><RichHtmlBody html={post.body} horizontalInset={space.xl} /></View>}
      {isReported ? <Text accessibilityRole="text" style={[type.caption, { color: c.textMuted, marginTop: space.sm }]}>Report submitted</Text> : null}
      {post.queueState ? (
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={
            post.queueState === 'sending'
              ? 'Sending…'
              : post.queueState === 'needs_attention'
                ? 'Needs attention'
                : 'Queued'
          }
          testID={testIds.pendingForumPost}
          onPress={() => {
            if (post.queueState !== 'needs_attention' || !post.operationId) {
              return;
            }
            Alert.alert('This reply could not be sent.', undefined, [
              { text: 'Dismiss', style: 'cancel' },
              {
                text: 'Discard',
                style: 'destructive',
                onPress: () => {
                  void removeOfflineItem(post.operationId!);
                },
              },
              {
                text: 'Retry',
                onPress: () => {
                  void updateOfflineItem(post.operationId!, {
                    state: 'queued',
                    nextRetryAt: new Date().toISOString(),
                    lastError: null,
                  }).then(() => {
                    void flushOfflineQueue();
                  });
                },
              },
            ]);
          }}
        >
          <Text style={[type.caption, { color: c.accentPrimary, marginTop: space.xs }]}>
            {post.queueState === 'sending'
              ? 'Sending…'
              : post.queueState === 'needs_attention'
                ? 'Needs attention'
                : 'Queued'}
          </Text>
        </Pressable>
      ) : null}
      {post.attachments.length > 0 ? (
        <ForumAttachmentList
          attachments={post.attachments}
          isSignedIn={isSignedIn}
          accessToken={accessToken}
          interactionsEnabled={interactionsEnabled}
        />
      ) : null}
      {post.signature ? (
        <Text style={[type.caption, { color: c.textMuted, marginTop: space.md }]}>{post.signature}</Text>
      ) : null}
    </View>
  );
});

const styles = StyleSheet.create({
  post: {
    paddingHorizontal: space.xl,
    paddingVertical: space.lg,
    borderTopWidth: StyleSheet.hairlineWidth,
  },
  body: {
    marginTop: space.md,
  },
  authorRow: { flexDirection: 'row', alignItems: 'center', gap: space.md },
  blocked: { marginTop: space.md, padding: space.md },
});
