import type { ReactNode } from 'react';
import { memo } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import type { NewsListItem } from '../../api';
import { newsArticleListImageSource, type NewsArticleImageFields } from '../../content/newsArticleImage';
import type { SectionView } from '../../hooks/useHomeSection';
import { resolveContentUrl } from '../../ui/html/resolveContentUrl';
import { ArchiveImage } from '../../ui/ArchiveImage';
import { Eyebrow } from '../../ui/Eyebrow';
import { SectionErrorBlock } from '../../ui/ScreenStates';
import { SectionHeader } from '../../ui/SectionHeader';
import { radius, space, type, useTheme } from '../../theme';

function homeArticleImage(item: NewsArticleImageFields, apiBaseUrl: string): number | { uri: string } {
  const source = newsArticleListImageSource({
    thumbnailUrl: resolveContentUrl(item.thumbnailUrl, apiBaseUrl),
    imageUrl: resolveContentUrl(item.imageUrl, apiBaseUrl),
  });
  if (typeof source === 'number') return source;
  if (Array.isArray(source)) return { uri: source[0]?.uri ?? '' };
  return { uri: source.uri ?? '' };
}

export const HomeNewsSection = memo(function HomeNewsSection({
  newsView,
  latestNews,
  totalNewsCount,
  apiBaseUrl,
  onOpenStory,
  onReloadNews,
  onSeeAll,
}: {
  newsView: SectionView<{ items: NewsListItem[]; totalCount: number }>;
  latestNews: NewsListItem[];
  totalNewsCount: number;
  apiBaseUrl: string;
  onOpenStory: (id: number) => void;
  onReloadNews: () => void;
  onSeeAll: () => void;
}) {
  const { c } = useTheme();
  let newsContent: ReactNode;
  if (newsView.kind === 'skeleton') {
    newsContent = <View style={styles.skeletonList}>
      {[0, 1, 2].map((key) => (
        <View key={key} style={[styles.skeletonRow, { backgroundColor: c.surfaceCard }]} />
      ))}
    </View>;
  } else if (newsView.kind === 'error') {
    newsContent = <SectionErrorBlock message={newsView.message} onRetry={onReloadNews} />;
  } else {
    newsContent = latestNews.map((item) => (
      <Pressable
        key={item.id}
        accessible
        accessibilityRole="button"
        accessibilityLabel={item.title}
        onPress={() => onOpenStory(item.id)}
        style={[styles.row, { borderTopColor: c.hairline }]}
      >
        <View style={styles.rowText}>
          <Eyebrow tone="accent" size={10}>
            {new Date(item.publishedAt).toLocaleDateString(undefined, {
              day: 'numeric',
              month: 'long',
              year: 'numeric',
            })}
          </Eyebrow>
          <Text numberOfLines={2} style={[type.listTitle, { color: c.textPrimary }]}>
            {item.title}
          </Text>
        </View>
        <ArchiveImage
          source={homeArticleImage(item, apiBaseUrl)}
          label={item.title}
          priority="low"
          style={styles.thumb}
        />
      </Pressable>
    ));
  }

  return (
    <>
      <SectionHeader
        title="Latest news"
        actionLabel={totalNewsCount > 0 ? `All ${totalNewsCount.toLocaleString()}+` : 'All'}
        onAction={onSeeAll}
      />
      {newsContent}
    </>
  );
});

const styles = StyleSheet.create({
  skeletonList: { paddingHorizontal: space.xl, gap: 14 },
  skeletonRow: { height: 76, borderRadius: radius.xs },
  row: {
    marginHorizontal: space.xl,
    paddingVertical: 14,
    borderTopWidth: 1,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 14,
  },
  rowText: { flex: 1, gap: 6 },
  thumb: { width: 76, height: 76, borderRadius: radius.xs },
});
