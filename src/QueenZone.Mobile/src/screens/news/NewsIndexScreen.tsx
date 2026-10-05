import type { BottomTabScreenProps } from '@react-navigation/bottom-tabs';
import type { CompositeScreenProps } from '@react-navigation/native';
import { Image } from 'expo-image';
import { memo, useCallback, useEffect, useState } from 'react';
import { ScrollView, StyleSheet, View, type ListRenderItem } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { fetchNewsPage, fetchNewsYearRange, formatPublishedDate, type NewsListItem, type NewsYearRange } from '../../api';
import { NEWS_LIST_CACHE_KEY } from '../../cache/keys';
import { useStoreRefresh } from '../../cache/useExternalStore';
import { getAppConfig } from '../../config';
import { newsArticleListImageSource, newsArticlePlaceholder } from '../../content/newsArticleImage';
import { newsDecades } from '../../content/newsDecades';
import { usePagedContent } from '../../hooks/usePagedContent';
import type { NewsStackParamList, RootTabParamList } from '../../navigation/types';
import { radius, space, useTheme } from '../../theme';
import { ArticleRow } from '../../ui/ArticleRow';
import { Chip } from '../../ui/Chip';
import { resolveContentUrl } from '../../ui/html/resolveContentUrl';
import { PagedListScreen } from '../../ui/PagedListScreen';
import { testIds } from '../../test/testIds';
import { PageTitleBlock } from '../../ui/PageTitleBlock';
import { YearRail } from '../../ui/YearRail';

/** Compact 3:2 listing thumb — matches the website card crop, not a full-bleed hero. */
const NEWS_LIST_THUMB_WIDTH = 84;
const NEWS_LIST_THUMB_HEIGHT = 56;

type Props = CompositeScreenProps<
  NativeStackScreenProps<NewsStackParamList, 'NewsIndex'>,
  BottomTabScreenProps<RootTabParamList>
>;

export function newsListResetKey(listKey: string, refreshAt: number | undefined): string {
  return refreshAt === undefined ? listKey : `${listKey}:${refreshAt}`;
}

function newsKeyExtractor(item: NewsListItem): string {
  return String(item.id);
}

const NewsListThumbnail = memo(function NewsListThumbnail({ item }: { item: NewsListItem }) {
  const { c } = useTheme();
  const apiBaseUrl = getAppConfig().apiBaseUrl;
  const source = newsArticleListImageSource({
    thumbnailUrl: resolveContentUrl(item.thumbnailUrl, apiBaseUrl),
    imageUrl: resolveContentUrl(item.imageUrl, apiBaseUrl),
  });
  const isRemote = typeof source === 'object' && source !== null && 'uri' in source;
  const [failed, setFailed] = useState(false);
  const display = failed || !isRemote ? newsArticlePlaceholder : source;

  useEffect(() => {
    setFailed(false);
  }, [item.id, item.thumbnailUrl, item.imageUrl]);

  return (
    <View
      style={[
        styles.thumbFrame,
        { backgroundColor: c.surfaceCard, borderRadius: radius.md },
      ]}
    >
      <Image
        testID={`news-story-${item.id}-thumb`}
        source={display}
        placeholder={isRemote ? newsArticlePlaceholder : undefined}
        onError={isRemote ? () => setFailed(true) : undefined}
        style={styles.thumb}
        contentFit="cover"
        recyclingKey={String(item.id)}
        cachePolicy="memory-disk"
        priority="low"
        accessible={false}
        importantForAccessibility="no"
        accessibilityIgnoresInvertColors
      />
    </View>
  );
});

const NewsIndexRow = memo(function NewsIndexRow({
  item,
  onOpen,
}: {
  item: NewsListItem;
  onOpen: (id: number) => void;
}) {
  return (
    <ArticleRow
      title={item.title}
      subtitle={item.excerpt}
      meta={formatPublishedDate(item.publishedAt)}
      leading={<NewsListThumbnail item={item} />}
      leadingInteractive={false}
      onPress={() => onOpen(item.id)}
      accessibilityLabel={`Open ${item.title}`}
      testID={`news-story-${item.id}`}
    />
  );
});

export function NewsIndexScreen({ navigation, route }: Props) {
  const [decade, setDecade] = useState<(typeof newsDecades)[number]>(newsDecades[0]);
  const [selectedYear, setSelectedYear] = useState<number | null>(null);
  const [yearRange, setYearRange] = useState<NewsYearRange | null>(null);
  const refreshAt = route.params?.refreshAt;

  useEffect(() => {
    const controller = new AbortController();
    fetchNewsYearRange(controller.signal)
      .then(setYearRange)
      .catch(() => undefined);
    return () => controller.abort();
  }, []);

  const paged = usePagedContent<NewsListItem>(
    useCallback(
      (page, signal) =>
        fetchNewsPage({
          page,
          pageSize: 20,
          decade: selectedYear === null ? decade.decadeStart ?? undefined : undefined,
          year: selectedYear ?? undefined,
          signal,
        }),
      [decade, selectedYear],
    ),
    20,
    newsListResetKey(selectedYear === null ? decade.label : `year-${selectedYear}`, refreshAt),
  );
  useStoreRefresh(NEWS_LIST_CACHE_KEY, paged.refresh);

  const selectDecade = useCallback((option: (typeof newsDecades)[number]) => {
    setSelectedYear(null);
    setDecade(option);
  }, []);

  const selectYear = useCallback((year: number) => {
    setSelectedYear(year);
  }, []);

  const openStory = useCallback(
    (id: number) => {
      navigation.navigate('Story', { id });
    },
    [navigation],
  );

  const renderItem = useCallback<ListRenderItem<NewsListItem>>(
    ({ item }) => <NewsIndexRow item={item} onOpen={openStory} />,
    [openStory],
  );

  const countLine =
    paged.totalCount > 0
      ? `${paged.totalCount.toLocaleString('en-GB')} articles · restored from Queenzone.com`
      : 'Restored from Queenzone.com';

  const header = (
    <View>
      <PageTitleBlock eyebrow="The archive" subtitle={countLine} />
      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={{ paddingHorizontal: space.xl, gap: 8, paddingBottom: 18 }}
      >
        {newsDecades.map((option) => (
          <Chip
            key={option.label}
            label={option.label}
            active={selectedYear === null && decade.label === option.label}
            onPress={() => selectDecade(option)}
          />
        ))}
      </ScrollView>
    </View>
  );

  let emptyMessage: string;
  if (selectedYear !== null) {
    emptyMessage = `No articles for ${selectedYear} yet.`;
  } else {
    emptyMessage = decade.decadeStart === null
      ? 'No news articles yet.'
      : 'No articles for this decade yet.';
  }

  return (
    <View style={styles.container}>
      <PagedListScreen
        testID={testIds.newsScreen}
        paged={paged}
        keyExtractor={newsKeyExtractor}
        loadingLabel="Loading news…"
        emptyMessage={emptyMessage}
        ListHeaderComponent={header}
        renderItem={renderItem}
      />
      <YearRail
        minYear={yearRange?.minYear ?? null}
        maxYear={yearRange?.maxYear ?? null}
        activeYear={selectedYear}
        onSelectYear={selectYear}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  thumbFrame: {
    width: NEWS_LIST_THUMB_WIDTH,
    height: NEWS_LIST_THUMB_HEIGHT,
    overflow: 'hidden',
  },
  thumb: {
    width: NEWS_LIST_THUMB_WIDTH,
    height: NEWS_LIST_THUMB_HEIGHT,
  },
});
