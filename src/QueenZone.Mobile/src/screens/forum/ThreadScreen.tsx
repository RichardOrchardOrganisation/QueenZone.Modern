import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { Alert, FlatList, StyleSheet, Text, View, type ListRenderItem } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import {
  fetchForumTopicPostsResult,
  fetchForumPostModerationState,
  blockForumPostAuthor,
  unblockForumPostAuthor,
  isOfflineFailure,
  isTimeoutFailure,
  type CacheSource,
  type ForumPost,
} from '../../api';
import { usePagedContent } from '../../hooks/usePagedContent';
import { HeaderBackButton } from '../../navigation/headerButtons';
import { goBackOrFallback } from '../../navigation/nestedTab';
import type { ForumStackParamList } from '../../navigation/types';
import { resolvePushMemberId } from '../../notifications/pushMemberId';
import { type OfflineQueueItem, useOfflineQueue } from '../../offlineQueue';
import { useSession } from '../../session/SessionContext';
import { openForumComposer, openSignIn } from '../../session/signInNavigation';
import { EmptyBlock, ErrorBlock, LoadingBlock, OfflineBanner } from '../../ui/ScreenStates';
import { ThemedRefreshControl } from '../../ui/ThemedRefreshControl';
import { testIds } from '../../test/testIds';
import { space, type, useTheme } from '../../theme';
import { ForumPollCard } from './ForumPollCard';
import { ForumPostRow, postKeyExtractor, type DisplayPost } from './ForumPostRow';
import { ForumWatchControl } from './ForumWatchControl';
import { ThreadReplyFooter } from './ThreadReplyFooter';
import { forumPostsPageSize, parseTopicId, topicReplyAllowed } from './forumThreadMeta';
import { useForumThread } from './useForumThread';

type Props = NativeStackScreenProps<ForumStackParamList, 'Thread'>;

function isStaleReadFailure(err: unknown): boolean {
  return isOfflineFailure(err) || isTimeoutFailure(err);
}

export function ThreadScreen({ navigation, route }: Props) {
  const { c } = useTheme();
  const { isSignedIn, accessToken, profile } = useSession();
  const memberId = accessToken ? resolvePushMemberId(accessToken, profile?.memberId) : null;
  const queueItems = useOfflineQueue(memberId);
  const { id: rawId, title } = route.params;
  const id = parseTopicId(rawId);
  const [postsSource, setPostsSource] = useState<CacheSource>('network');
  const [postsCachedAt, setPostsCachedAt] = useState<string | null>(null);
  const [reportedPostIds, setReportedPostIds] = useState<Set<number>>(() => new Set());
  const [blockedMemberIds, setBlockedMemberIds] = useState<Set<string>>(() => new Set());

  useEffect(() => {
    if (route.params.reportedPostId) {
      setReportedPostIds((current) => new Set(current).add(route.params.reportedPostId!));
    }
  }, [route.params.reportedPostId]);

  const forumThread = useForumThread(id, accessToken, navigation);
  const { topic, topicError, topicSource, topicCachedAt } = forumThread;

  const paged = usePagedContent<ForumPost>(
    useCallback(
      async (page, signal, mode) => {
        if (id === null) {
          return { items: [], page: 1, pageSize: forumPostsPageSize, totalCount: 0, totalPages: 0 };
        }
        try {
          const result = await fetchForumTopicPostsResult(id, {
            page,
            pageSize: forumPostsPageSize,
            signal,
            networkOnly: mode === 'refresh',
          });
          setPostsSource(result.source);
          setPostsCachedAt(result.cachedAt);
          return result.data;
        } catch (err: unknown) {
          if (mode === 'refresh' && isStaleReadFailure(err)) {
            setPostsSource('cache');
          }
          throw err;
        }
      },
      [id],
    ),
    forumPostsPageSize,
  );

  useEffect(() => {
    if (!accessToken || paged.items.length === 0) return;
    const postIds = paged.items.map((post) => post.id);
    const authorMemberIds = [...new Set(paged.items.map((post) => post.authorMemberId).filter((authorId): authorId is string => Boolean(authorId)))];
    void fetchForumPostModerationState(accessToken, postIds, authorMemberIds).then((state) => {
      setReportedPostIds(new Set(state.reportedPostIds));
      setBlockedMemberIds(new Set(state.blockedMemberIds));
    }).catch(() => undefined);
  }, [accessToken, paged.items]);

  useLayoutEffect(() => {
    navigation.setOptions({
      title: topic?.title ?? title ?? 'Thread',
      headerLeft: () => (
        <HeaderBackButton
          testID={testIds.forumThreadBack}
          onPress={() => goBackOrFallback(navigation, 'ForumIndex')}
        />
      ),
    });
  }, [navigation, topic?.title, title]);

  const retry = useCallback(() => {
    forumThread.retryTopic();
    paged.reload();
  }, [forumThread, paged]);

  const refresh = useCallback(() => {
    forumThread.refreshTopic();
    paged.refresh();
  }, [forumThread, paged]);

  const skipFocusRefresh = useRef(true);
  useEffect(() => {
    return navigation.addListener('focus', () => {
      if (skipFocusRefresh.current) {
        skipFocusRefresh.current = false;
        return;
      }
      refresh();
    });
  }, [navigation, refresh]);

  const openReply = useCallback(() => {
    if (id === null) {
      return;
    }
    openForumComposer(navigation, isSignedIn, {
      threadId: id,
      threadTitle: topic?.title ?? title,
      isLocked: topic?.isLocked,
    });
  }, [id, isSignedIn, navigation, title, topic?.isLocked, topic?.title]);

  const listData = useMemo(
    () => overlayQueuedPosts(paged.items, queueItems, id),
    [id, paged.items, queueItems],
  );

  const offlineSnapshot = topicSource === 'cache' || postsSource === 'cache';

  const renderItem = useCallback<ListRenderItem<DisplayPost>>(
    ({ item }) => {
      const isCurrentMember = Boolean(memberId && item.authorMemberId === memberId);
      const report = () => {
        if (!isSignedIn) {
          openSignIn(navigation, { tab: 'ForumTab', screen: 'ForumReport', params: { postId: item.id, authorUsername: item.authorUsername, threadId: rawId, threadTitle: topic?.title ?? title } });
          return;
        }
        navigation.navigate('ForumReport', { postId: item.id, authorUsername: item.authorUsername, threadId: rawId, threadTitle: topic?.title ?? title });
      };
      const block = () => {
        if (!isSignedIn || !accessToken) {
          openSignIn(navigation, { tab: 'ForumTab', screen: 'Thread', params: route.params });
          return;
        }
        Alert.alert('Block member?', `${item.authorUsername} will no longer be able to contact you privately. Their forum posts will be collapsed.`, [
          { text: 'Cancel', style: 'cancel' },
          { text: 'Block', style: 'destructive', onPress: () => void blockForumPostAuthor(accessToken, item.id).then(() => {
            if (item.authorMemberId) setBlockedMemberIds((current) => new Set(current).add(item.authorMemberId!));
          }).catch(() => Alert.alert('Could not block member', 'Try again when you have a connection.')) },
        ]);
      };
      const unblock = () => {
        if (!isSignedIn || !accessToken) {
          openSignIn(navigation, { tab: 'ForumTab', screen: 'Thread', params: route.params });
          return;
        }
        Alert.alert('Unblock member?', `${item.authorUsername} will be able to contact you privately again. Their forum posts will be shown.`, [
          { text: 'Cancel', style: 'cancel' },
          { text: 'Unblock', onPress: () => void unblockForumPostAuthor(accessToken, item.id).then(() => {
            if (item.authorMemberId) {
              setBlockedMemberIds((current) => {
                const next = new Set(current);
                next.delete(item.authorMemberId!);
                return next;
              });
            }
          }).catch(() => Alert.alert('Could not unblock member', 'Try again when you have a connection.')) },
        ]);
      };
      return (
      <ForumPostRow
        post={item}
        isSignedIn={isSignedIn}
        accessToken={accessToken}
        interactionsEnabled={!offlineSnapshot}
        isCurrentMember={isCurrentMember}
        isReported={reportedPostIds.has(item.id)}
        isBlocked={Boolean(item.authorMemberId && blockedMemberIds.has(item.authorMemberId))}
        onReport={report}
        onBlock={block}
        onUnblock={unblock}
      />
      );
    },
    [accessToken, blockedMemberIds, isSignedIn, memberId, navigation, offlineSnapshot, rawId, reportedPostIds, route.params, title, topic?.title],
  );

  if (id === null) {
    return (
      <ErrorBlock message={topicError ?? 'This discussion is not available in the archive yet.'} />
    );
  }

  const topicPending = !topic && !topicError;
  if ((paged.loading && paged.items.length === 0) || topicPending) {
    return <LoadingBlock label="Loading thread…" />;
  }

  const snapshotCachedAt = postsCachedAt ?? topicCachedAt;

  if (topicError && !topic) {
    return <ErrorBlock message={topicError} onRetry={retry} />;
  }

  if (paged.error && paged.items.length === 0) {
    return <ErrorBlock message={paged.error} onRetry={retry} />;
  }

  const stats = topic ? `${topic.postCount.toLocaleString()} posts · ${topic.forumName}` : null;

  const header = (
    <View style={styles.header}>
      {offlineSnapshot ? <OfflineBanner cachedAt={snapshotCachedAt} testID={testIds.offlineBanner} /> : null}
      <Text style={[type.eyebrow, { color: c.accentPrimary }]}>{topic?.forumName ?? 'Forum'}</Text>
      <Text
        style={[type.articleTitle, { color: c.textPrimary, marginTop: space.sm }]}
        allowFontScaling
        maxFontSizeMultiplier={1.4}
      >
        {topic?.title ?? title ?? 'Thread'}
      </Text>
      {stats ? (
        <Text style={[type.meta, { color: c.textMuted, marginTop: space.md }]}>{stats}</Text>
      ) : null}
      <ForumWatchControl
        isSignedIn={isSignedIn}
        watching={forumThread.watching}
        watchBusy={forumThread.watchBusy}
        watchError={forumThread.watchError}
        disabled={offlineSnapshot}
        onToggle={forumThread.toggleWatch}
      />
      {forumThread.poll && !offlineSnapshot ? (
        <View style={styles.poll}>
          <ForumPollCard
            poll={forumThread.poll}
            isSignedIn={isSignedIn}
            hasAccessToken={Boolean(accessToken)}
            busy={forumThread.pollBusy}
            error={forumThread.pollError}
            onVote={forumThread.votePoll}
            onClose={forumThread.closePoll}
            onSignIn={() => openSignIn(navigation)}
          />
        </View>
      ) : null}
      {forumThread.pollError && !forumThread.poll ? (
        <ErrorBlock message={forumThread.pollError} onRetry={retry} />
      ) : null}
    </View>
  );

  const canReply = topicReplyAllowed(topic);

  return (
    <FlatList
      testID={testIds.forumThreadScreen}
      style={[styles.list, { backgroundColor: c.surfacePage }]}
      data={listData}
      keyExtractor={postKeyExtractor}
      ListHeaderComponent={header}
      ListEmptyComponent={<EmptyBlock message="No posts are available in this thread yet." />}
      ListFooterComponent={
        <ThreadReplyFooter
          canReply={canReply}
          isSignedIn={isSignedIn}
          loadingMore={paged.loadingMore}
          onReply={openReply}
        />
      }
      refreshControl={
        <ThemedRefreshControl refreshing={paged.refreshing} onRefresh={refresh} />
      }
      onEndReached={paged.loadMore}
      onEndReachedThreshold={0.4}
      renderItem={renderItem}
    />
  );
}

function overlayQueuedPosts(
  posts: ForumPost[],
  queueItems: OfflineQueueItem[],
  topicId: number | null,
): DisplayPost[] {
  const rows: DisplayPost[] = posts.map((post) => ({ ...post }));
  if (topicId == null) {
    return rows;
  }
  for (const item of queueItems) {
    if (item.kind !== 'forum.reply' || !('topicId' in item.target) || item.target.topicId !== topicId) {
      continue;
    }
    rows.push({
      id: 0,
      body: item.payload.body,
      postedAt: item.createdAt,
      authorUsername: 'You',
      signature: null,
      authorMemberSince: null,
      authorMemberId: item.memberId,
      editedAt: null,
      editCount: 0,
      attachments: [],
      queueState: item.state,
      operationId: item.operationId,
    });
  }
  return rows;
}

const styles = StyleSheet.create({
  list: { flex: 1 },
  header: {
    paddingHorizontal: space.xl,
    paddingTop: space.xl,
    paddingBottom: space.base,
  },
  poll: {
    marginHorizontal: -space.xl,
    marginTop: space.lg,
  },
});
