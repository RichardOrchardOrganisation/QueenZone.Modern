import { useCallback, useEffect, useRef, useState } from 'react';
import { Pressable, FlatList, StyleSheet, Text, View } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { Pause, Play } from 'lucide-react-native';
import {
  ApiError,
  fetchAllFanPerformances,
  fetchFanPerformancesPage,
  formatPublishedDate,
  type FanPerformance,
} from '../../api';
import { useFanPerformancePlayer } from '../../audio/FanPerformancePlayer';
import { formatTrackDuration } from '../../audio/formatDuration';
import { DownloadAction } from '../../downloads/DownloadAction';
import { usePagedContent } from '../../hooks/usePagedContent';
import type { ArchiveStackParamList } from '../../navigation/types';
import { useSession } from '../../session/SessionContext';
import { openSignIn } from '../../session/signInNavigation';
import { testIds } from '../../test/testIds';
import { ArticleRow } from '../../ui/ArticleRow';
import { Button } from '../../ui/Button';
import { EmptyBlock, ErrorBlock, ListFooterLoading, LoadingBlock } from '../../ui/ScreenStates';
import { ThemedRefreshControl } from '../../ui/ThemedRefreshControl';
import { space, type, useTheme } from '../../theme';

type Props = NativeStackScreenProps<ArchiveStackParamList, 'FanPerformances'>;

type CatalogPlayMode = 'order' | 'shuffle';

function metaLine(item: FanPerformance): string {
  const parts = [formatPublishedDate(item.dateAdded), formatTrackDuration(item.durationSeconds)].filter(
    Boolean,
  );
  if (item.contributorDisplayName) {
    parts.push(`Submitted by ${item.contributorDisplayName}`);
  }
  return parts.join(' · ');
}

/** Fisher–Yates copy. Call once per Shuffle Play All tap; pass the result as the player queue. */
export function shuffleFanPerformances<T>(items: readonly T[], random: () => number = Math.random): T[] {
  const next = items.slice();
  for (let i = next.length - 1; i > 0; i -= 1) {
    const j = Math.floor(random() * (i + 1));
    const current = next[i];
    const swap = next[j];
    if (current === undefined || swap === undefined) {
      continue;
    }
    next[i] = swap;
    next[j] = current;
  }
  return next;
}

export function FanPerformancesScreen({ navigation }: Props) {
  const { c } = useTheme();
  const { accessToken, isRestoring, profile } = useSession();
  const player = useFanPerformancePlayer();
  const paged = usePagedContent<FanPerformance>(
    useCallback((page, signal) => fetchFanPerformancesPage({ page, pageSize: 20, signal }), []),
  );
  const canPlay = Boolean(accessToken || profile?.memberId);
  const [catalogLoading, setCatalogLoading] = useState(false);
  const [catalogError, setCatalogError] = useState<string | null>(null);
  const [pendingCatalogMode, setPendingCatalogMode] = useState<CatalogPlayMode | null>(null);
  const catalogAbortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    return () => {
      catalogAbortRef.current?.abort();
    };
  }, []);

  const onPlay = useCallback(
    (item: FanPerformance) => {
      if (isRestoring) {
        return;
      }
      if (!canPlay) {
        openSignIn(navigation, { tab: 'ArchiveTab', screen: 'FanPerformances' });
        return;
      }
      if (player.current?.id === item.id) {
        player.toggle();
        return;
      }
      player.play(item, paged.items);
    },
    [canPlay, isRestoring, navigation, paged.items, player],
  );

  const playCatalog = useCallback(
    async (mode: CatalogPlayMode) => {
      if (isRestoring || catalogLoading) {
        return;
      }
      if (!canPlay) {
        openSignIn(navigation, { tab: 'ArchiveTab', screen: 'FanPerformances' });
        return;
      }

      catalogAbortRef.current?.abort();
      const controller = new AbortController();
      catalogAbortRef.current = controller;
      setCatalogLoading(true);
      setPendingCatalogMode(mode);
      setCatalogError(null);

      try {
        const catalog = await fetchAllFanPerformances(controller.signal);
        if (controller.signal.aborted) {
          return;
        }
        if (catalog.length === 0) {
          setCatalogError('No fan performances are available yet.');
          return;
        }
        const queue = mode === 'shuffle' ? shuffleFanPerformances(catalog) : catalog;
        const first = queue[0];
        if (!first) {
          setCatalogError('No fan performances are available yet.');
          return;
        }
        player.play(first, queue);
      } catch (err: unknown) {
        if (err instanceof Error && err.name === 'AbortError') {
          return;
        }
        setCatalogError(err instanceof ApiError ? err.message : 'Unable to load the play queue.');
      } finally {
        if (!controller.signal.aborted) {
          setCatalogLoading(false);
          setPendingCatalogMode(null);
        }
      }
    },
    [canPlay, catalogLoading, isRestoring, navigation, player],
  );

  if (paged.loading && paged.items.length === 0) {
    return <LoadingBlock label="Loading fan performances…" />;
  }

  if (paged.error && paged.items.length === 0) {
    return <ErrorBlock message={paged.error} onRetry={paged.reload} />;
  }

  const header = (
    <View style={styles.header}>
      <View style={styles.playAllRow}>
        <View style={styles.playAllButton}>
          <Button
            label="Play All"
            size="sm"
            loading={catalogLoading && pendingCatalogMode === 'order'}
            disabled={catalogLoading}
            onPress={() => {
              void playCatalog('order');
            }}
            testID={testIds.fanPerformancesPlayAll}
            accessibilityLabel="Play all fan performances"
          />
        </View>
        <View style={styles.playAllButton}>
          <Button
            label="Shuffle Play All"
            size="sm"
            variant="outline"
            loading={catalogLoading && pendingCatalogMode === 'shuffle'}
            disabled={catalogLoading}
            onPress={() => {
              void playCatalog('shuffle');
            }}
            testID={testIds.fanPerformancesShufflePlayAll}
            accessibilityLabel="Shuffle play all fan performances"
          />
        </View>
      </View>
      {catalogError ? (
        <Text style={[type.caption, { color: c.textSecondary }]}>{catalogError}</Text>
      ) : null}
      <View style={styles.submitRow}>
        <Button
          label="Downloads"
          size="sm"
          variant="outline"
          onPress={() => {
            if (!canPlay) {
              openSignIn(navigation, { tab: 'ArchiveTab', screen: 'FanPerformanceDownloads' });
              return;
            }
            navigation.navigate('FanPerformanceDownloads');
          }}
          testID={testIds.fanPerformanceDownloads}
          accessibilityLabel="Downloaded fan performances"
        />
        <Button
          label="Submit a performance"
          size="sm"
          variant="outline"
          onPress={() => navigation.navigate('FanPerformanceSubmit')}
          testID={testIds.fanPerformanceSubmit}
          accessibilityLabel="Submit a fan performance"
        />
      </View>
    </View>
  );

  return (
    <FlatList
      testID={testIds.fanPerformancesScreen}
      style={[styles.list, { backgroundColor: c.surfacePage }]}
      data={paged.items}
      keyExtractor={(item) => String(item.id)}
      ListHeaderComponent={header}
      ListEmptyComponent={<EmptyBlock message="No fan performances are available yet." />}
      ListFooterComponent={<ListFooterLoading visible={paged.loadingMore} />}
      alwaysBounceVertical
      refreshControl={
        <ThemedRefreshControl refreshing={paged.refreshing} onRefresh={paged.refresh} />
      }
      onEndReached={paged.loadMore}
      onEndReachedThreshold={0.4}
      renderItem={({ item }) => {
        const playingThis = player.current?.id === item.id && player.playing;
        return (
          <ArticleRow
            title={item.title}
            subtitle={`Performed by ${item.performedBy}`}
            meta={metaLine(item)}
            hint={!canPlay && !isRestoring ? 'Sign in to play' : undefined}
            leading={
              <View style={styles.leading} pointerEvents="box-none">
                <Pressable
                  testID={`${testIds.fanPerformancePlayPrefix}${item.id}`}
                  accessibilityRole="button"
                  accessibilityState={{ selected: playingThis }}
                  accessibilityLabel={
                    canPlay
                      ? playingThis
                        ? `Pause ${item.title}`
                        : `Play ${item.title}`
                      : `Sign in to play ${item.title}`
                  }
                  hitSlop={{ top: 10, bottom: 10, left: 10, right: 6 }}
                  unstable_pressDelay={0}
                  onPress={() => onPlay(item)}
                  style={[
                    styles.play,
                    {
                      borderColor: playingThis ? c.textPrimary : c.borderStrong,
                      backgroundColor: playingThis ? c.textPrimary : c.surfaceRaised,
                    },
                    playingThis ? null : styles.playOffset,
                  ]}
                >
                  {playingThis ? (
                    <Pause size={20} color={c.surfacePage} fill={c.surfacePage} />
                  ) : (
                    <Play size={20} color={c.textPrimary} fill={c.textPrimary} />
                  )}
                </Pressable>
                {canPlay || isRestoring ? (
                  <DownloadAction
                    key={String(item.id)}
                    track={item}
                    compact
                    onNeedSignIn={() =>
                      openSignIn(navigation, { tab: 'ArchiveTab', screen: 'FanPerformances' })
                    }
                  />
                ) : null}
              </View>
            }
            onPress={() => navigation.navigate('FanPerformanceDetail', { id: item.id })}
            accessibilityLabel={`Open ${item.title}`}
            testID={`${testIds.fanPerformanceRowPrefix}${item.id}`}
          />
        );
      }}
    />
  );
}

const styles = StyleSheet.create({
  list: { flex: 1 },
  header: {
    paddingHorizontal: space.xl,
    paddingTop: space.base,
    paddingBottom: space.sm,
    gap: space.sm,
  },
  playAllRow: {
    flexDirection: 'row',
    gap: space.sm,
  },
  playAllButton: {
    flex: 1,
  },
  submitRow: {
    marginTop: space.xs,
    flexDirection: 'row',
    gap: space.sm,
    flexWrap: 'wrap',
  },
  leading: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space.sm,
  },
  play: {
    width: 48,
    height: 48,
    borderRadius: 24,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  playOffset: {
    paddingLeft: 2,
  },
});
