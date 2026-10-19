import { memo } from 'react';
import { StyleSheet, View } from 'react-native';
import type { NewsListItem } from '../../api';
import { newsArticleListImageSource, type NewsArticleImageFields } from '../../content/newsArticleImage';
import type { SectionView } from '../../hooks/useHomeSection';
import { resolveContentUrl } from '../../ui/html/resolveContentUrl';
import { HeroFeature } from '../../ui/HeroFeature';
import { SectionErrorBlock } from '../../ui/ScreenStates';
import { testIds } from '../../test/testIds';
import { useTheme } from '../../theme';

function homeArticleImage(item: NewsArticleImageFields, apiBaseUrl: string): number | { uri: string } {
  const source = newsArticleListImageSource({
    thumbnailUrl: resolveContentUrl(item.thumbnailUrl, apiBaseUrl),
    imageUrl: resolveContentUrl(item.imageUrl, apiBaseUrl),
  });
  if (typeof source === 'number') return source;
  if (Array.isArray(source)) return { uri: source[0]?.uri ?? '' };
  return { uri: ('uri' in source ? source.uri : undefined) ?? '' };
}

export const HomeHeroSection = memo(function HomeHeroSection({
  newsView,
  hero,
  apiBaseUrl,
  onOpenStory,
  onReloadNews,
}: {
  newsView: SectionView<{ items: NewsListItem[]; totalCount: number }>;
  hero: NewsListItem | null;
  apiBaseUrl: string;
  onOpenStory: (id: number) => void;
  onReloadNews: () => void;
}) {
  const { c } = useTheme();
  if (newsView.kind === 'skeleton') {
    return <View style={[styles.skeleton, { backgroundColor: c.surfaceCard }]} />;
  }
  if (newsView.kind === 'error') {
    return <SectionErrorBlock message={newsView.message} onRetry={onReloadNews} />;
  }
  if (!hero) {
    return null;
  }
  return (
    <HeroFeature
      testID={testIds.homeHero}
      priority="high"
      height={300}
      item={{
        kicker: 'Lead story',
        title: hero.title,
        standfirst: hero.excerpt,
        meta: [],
        image: homeArticleImage(hero, apiBaseUrl),
      }}
      onPress={() => onOpenStory(hero.id)}
    />
  );
});

const styles = StyleSheet.create({
  skeleton: { height: 300 },
});
