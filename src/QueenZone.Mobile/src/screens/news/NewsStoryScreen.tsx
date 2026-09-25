import { useCallback, useLayoutEffect } from 'react';
import { Linking, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import type { BottomTabScreenProps } from '@react-navigation/bottom-tabs';
import type { CompositeScreenProps } from '@react-navigation/native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { fetchNewsDetail, formatPublishedDate, type NewsDiscussionPreview } from '../../api';
import { useDetailQuery } from '../../hooks/useDetailQuery';
import { HeaderBackButton } from '../../navigation/headerButtons';
import { leaveStoryScreen, nestedTabParams } from '../../navigation/nestedTab';
import type { NewsStackParamList, RootTabParamList } from '../../navigation/types';
import { formatPostTimestamp } from '../forum/forumThreadMeta';
import { Button } from '../../ui/Button';
import { StoryContent } from '../../ui/StoryContent';
import { ErrorBlock, LoadingBlock } from '../../ui/ScreenStates';
import { testIds } from '../../test/testIds';
import { radius, space, type, useTheme } from '../../theme';

type Props = CompositeScreenProps<
  NativeStackScreenProps<NewsStackParamList, 'Story'>,
  BottomTabScreenProps<RootTabParamList>
>;

function discussionInviteLabel(replyCount: number): string {
  return replyCount > 0 ? 'Join the discussion' : 'Start the discussion';
}

function discussionReplyCountLabel(replyCount: number): string {
  return replyCount === 1 ? '1 reply' : `${replyCount} replies`;
}

export function NewsStoryScreen({ navigation, route }: Props) {
  const { c } = useTheme();
  const { id } = route.params;
  const loadArticle = useCallback((signal: AbortSignal) => fetchNewsDetail(id, signal), [id]);
  const { data: article, error, loading, reload } = useDetailQuery(loadArticle);

  useLayoutEffect(() => {
    navigation.setOptions({
      title: article?.title ?? 'Story',
      headerLeft: () => (
        <HeaderBackButton
          testID={testIds.newsStoryBack}
          onPress={() => leaveStoryScreen(navigation)}
        />
      ),
    });
  }, [navigation, article?.title]);

  const openDiscussion = useCallback(
    (topicId: number, title: string) => {
      navigation.navigate('ForumTab', nestedTabParams('Thread', { id: topicId, title }));
    },
    [navigation],
  );

  if (loading) {
    return <LoadingBlock label="Loading article…" />;
  }

  if (error || !article) {
    return <ErrorBlock message={error ?? 'Article not found.'} onRetry={reload} />;
  }

  const published = formatPublishedDate(article.publishedAt);
  const topicId = article.topicId;
  const replyCount = article.discussionReplyCount ?? 0;
  const preview = article.discussionPreview ?? [];

  return (
    <ScrollView
      testID={testIds.newsStoryScreen}
      style={[styles.scroll, { backgroundColor: c.surfacePage }]}
      contentContainerStyle={styles.content}
    >
      <StoryContent
        category="News"
        accentColor={c.accentEditorial}
        title={article.title}
        published={published}
        excerpt={article.excerpt}
        body={article.body}
      />
      {article.sourceUrl ? (
        <Pressable
          accessibilityRole="link"
          accessibilityLabel="Open source"
          onPress={() => Linking.openURL(article.sourceUrl!)}
          style={styles.source}
        >
          <Text style={[type.button, { color: c.accentPrimary }]}>Source</Text>
        </Pressable>
      ) : null}
      {topicId != null ? (
        <StoryDiscussion
          replyCount={replyCount}
          preview={preview}
          onOpenThread={() => openDiscussion(topicId, article.title)}
        />
      ) : null}
      <View style={{ height: space.section }} />
    </ScrollView>
  );
}

function StoryDiscussion({
  replyCount,
  preview,
  onOpenThread,
}: {
  replyCount: number;
  preview: NewsDiscussionPreview[];
  onOpenThread: () => void;
}) {
  const { c } = useTheme();
  const invite = discussionInviteLabel(replyCount);

  return (
    <View
      testID={testIds.newsStoryDiscussion}
      accessibilityLabel="Discussion"
      style={[styles.discussion, { borderTopColor: c.hairline }]}
    >
      <Text style={[type.cardTitle, { color: c.textPrimary }]}>Discussion</Text>
      {replyCount > 0 ? (
        <Text style={[type.meta, { color: c.textMuted, marginTop: space.md }]}>
          {discussionReplyCountLabel(replyCount)}
        </Text>
      ) : null}
      {replyCount > 0 && preview.length > 0
        ? preview.map((item, index) => {
            const posted = formatPostTimestamp(item.postedAt);
            return (
              <Pressable
                key={`${item.authorDisplayName}-${item.postedAt}-${index}`}
                accessibilityRole="button"
                accessibilityLabel={`Open discussion from ${item.authorDisplayName}`}
                onPress={onOpenThread}
                style={[
                  styles.preview,
                  { backgroundColor: c.surfaceCard, borderColor: c.border },
                ]}
              >
                <Text style={[type.listTitle, { color: c.textPrimary }]}>
                  {item.authorDisplayName}
                </Text>
                {posted ? (
                  <Text style={[type.meta, { color: c.textMuted, marginTop: space.xs }]}>
                    {posted}
                  </Text>
                ) : null}
                <Text style={[type.body, { color: c.textSecondary, marginTop: space.sm }]}>
                  {item.excerpt}
                </Text>
              </Pressable>
            );
          })
        : null}
      <View style={styles.invite}>
        <Button
          testID={testIds.newsStoryDiscussionCta}
          variant="outline"
          size="sm"
          label={invite}
          onPress={onOpenThread}
        />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  scroll: { flex: 1 },
  content: {
    paddingHorizontal: 26,
    paddingTop: space.xl,
    paddingBottom: space.section,
  },
  source: {
    marginTop: space.xxl,
    minHeight: 48,
    justifyContent: 'center',
  },
  discussion: {
    marginTop: space.xxl,
    paddingTop: space.xl,
    borderTopWidth: StyleSheet.hairlineWidth,
  },
  preview: {
    marginTop: space.md,
    padding: space.base,
    borderWidth: 1,
    borderRadius: radius.sm,
  },
  invite: {
    alignSelf: 'flex-start',
    marginTop: space.lg,
  },
});
