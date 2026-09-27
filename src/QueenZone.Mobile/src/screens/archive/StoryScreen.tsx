import { useCallback, useLayoutEffect } from 'react';
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { fetchArticleDetail, formatPublishedDate } from '../../api';
import { useDetailQuery } from '../../hooks/useDetailQuery';
import type { ArchiveStackParamList } from '../../navigation/types';
import { StoryContent } from '../../ui/StoryContent';
import { openExternalUrl } from '../../ui/openExternalUrl';
import { ErrorBlock, LoadingBlock } from '../../ui/ScreenStates';
import { isHttpUrl } from '../../ui/html/resolveContentUrl';
import { space, type, useTheme } from '../../theme';
import { testIds } from '../../test/testIds';

type Props = NativeStackScreenProps<ArchiveStackParamList, 'Story'>;

export function StoryScreen({ navigation, route }: Props) {
  const { c } = useTheme();
  const { id } = route.params;
  const loadArticle = useCallback((signal: AbortSignal) => fetchArticleDetail(id, signal), [id]);
  const { data: article, error, loading, reload } = useDetailQuery(loadArticle);

  useLayoutEffect(() => {
    navigation.setOptions({
      title: article?.title ?? 'Article',
    });
  }, [navigation, article?.title]);

  if (loading) {
    return <LoadingBlock label="Loading article…" />;
  }

  if (error || !article) {
    return <ErrorBlock message={error ?? 'Article not found.'} onRetry={reload} />;
  }

  const published = formatPublishedDate(article.publishedAt);
  const source = article.source?.trim() || null;
  const sourceIsUrl = isHttpUrl(source);

  return (
    <ScrollView
      testID={testIds.articleStoryScreen}
      style={[styles.scroll, { backgroundColor: c.surfacePage }]}
      contentContainerStyle={styles.content}
    >
      <StoryContent
        category={article.categoryName ?? 'Articles'}
        accentColor={c.accentArchive}
        title={article.title}
        published={published}
        excerpt={article.excerpt}
        body={article.body}
      />
      {source && sourceIsUrl ? (
        <Pressable
          accessibilityRole="link"
          accessibilityLabel="Open source"
          onPress={() => void openExternalUrl(source)}
          style={styles.source}
        >
          <Text style={[type.button, { color: c.accentPrimary }]}>Source</Text>
        </Pressable>
      ) : null}
      {source && !sourceIsUrl ? (
        <Text style={[type.meta, { color: c.textMuted, marginTop: space.xxl }]}>{source}</Text>
      ) : null}
      <View style={{ height: space.section }} />
    </ScrollView>
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
});
