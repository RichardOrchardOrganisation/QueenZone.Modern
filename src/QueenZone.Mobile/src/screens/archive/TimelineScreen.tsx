import type { NativeStackScreenProps } from '@react-navigation/native-stack';
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { FlatList, Pressable, StyleSheet, Text, View } from 'react-native';
import { fetchOnThisDay, fetchTimelinePage, type TimelineEvent } from '../../api';
import { usePagedContent } from '../../hooks/usePagedContent';
import { HeaderBackButton } from '../../navigation/headerButtons';
import { goBackOrFallback } from '../../navigation/nestedTab';
import type { ArchiveStackParamList } from '../../navigation/types';
import { testIds } from '../../test/testIds';
import { openExternalUrl } from '../../ui/openExternalUrl';
import { EmptyBlock, ErrorBlock, ListFooterLoading, LoadingBlock } from '../../ui/ScreenStates';
import { ThemedRefreshControl } from '../../ui/ThemedRefreshControl';
import { space, type, useTheme } from '../../theme';

type Props = NativeStackScreenProps<ArchiveStackParamList, 'Timeline'>;

type DecadeSection = {
  decade: string;
  events: TimelineEvent[];
};

type TimelineRow = { kind: 'decade'; decade: string } | { kind: 'event'; event: TimelineEvent };

function decadeLabel(iso: string): string {
  const year = new Date(iso).getFullYear();
  if (Number.isNaN(year)) {
    return 'Undated';
  }
  const start = Math.floor(year / 10) * 10;
  return `${start}s`;
}

function usableFocusId(value: number | undefined): number | null {
  return value != null && value > 0 ? value : null;
}

export function TimelineScreen({ navigation, route }: Props) {
  const { c } = useTheme();
  const focusId = usableFocusId(route.params?.focusId);
  const [expandedId, setExpandedId] = useState<number | null>(focusId);
  const [includedFocus, setIncludedFocus] = useState<TimelineEvent | null>(null);
  const listRef = useRef<FlatList<TimelineRow>>(null);
  const scrolledFocusId = useRef<number | null>(null);

  useLayoutEffect(() => {
    navigation.setOptions({
      headerLeft: () => (
        <HeaderBackButton
          testID={testIds.timelineBack}
          onPress={() => goBackOrFallback(navigation, 'ArchiveHub')}
        />
      ),
    });
  }, [navigation]);

  const paged = usePagedContent<TimelineEvent>(
    useCallback((page, signal) => fetchTimelinePage({ page, pageSize: 100, signal }), []),
    100,
  );

  const items = useMemo(() => {
    if (includedFocus && !paged.items.some((event) => event.id === includedFocus.id)) {
      return [...paged.items, includedFocus];
    }
    return paged.items;
  }, [includedFocus, paged.items]);

  useEffect(() => {
    setIncludedFocus((current) => (current?.id === focusId ? current : null));
  }, [focusId]);

  useEffect(() => {
    if (focusId == null || paged.loading) {
      return;
    }
    if (items.some((event) => event.id === focusId)) {
      return;
    }
    let cancelled = false;
    fetchOnThisDay()
      .then((event) => {
        if (!cancelled && event != null && event.id === focusId) {
          setIncludedFocus(event);
        }
      })
      .catch(() => {
        /* paging may still surface the row */
      });
    return () => {
      cancelled = true;
    };
  }, [focusId, items, paged.loading]);

  useEffect(() => {
    if (focusId == null) {
      return;
    }
    if (items.some((event) => event.id === focusId)) {
      setExpandedId(focusId);
      return;
    }
    if (paged.loading || paged.loadingMore) {
      return;
    }
    if (paged.hasMore) {
      paged.loadMore();
      return;
    }
    setExpandedId(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- omit the whole paged object; named fields are already listed.
  }, [focusId, items, paged.hasMore, paged.loadMore, paged.loading, paged.loadingMore]);

  const sections = useMemo(() => {
    const map = new Map<string, TimelineEvent[]>();
    for (const event of items) {
      const key = decadeLabel(event.eventDate);
      const list = map.get(key) ?? [];
      list.push(event);
      map.set(key, list);
    }
    return Array.from(map.entries()).map(([decade, events]) => ({ decade, events }) satisfies DecadeSection);
  }, [items]);

  const rows = useMemo(() => {
    const next: TimelineRow[] = [];
    for (const section of sections) {
      next.push({ kind: 'decade', decade: section.decade });
      for (const event of section.events) {
        next.push({ kind: 'event', event });
      }
    }
    return next;
  }, [sections]);

  const focusIndex = useMemo(() => {
    if (focusId == null) {
      return -1;
    }
    return rows.findIndex((row) => row.kind === 'event' && row.event.id === focusId);
  }, [focusId, rows]);

  useEffect(() => {
    if (focusId == null || focusIndex < 0 || scrolledFocusId.current === focusId) {
      return;
    }
    scrolledFocusId.current = focusId;
    requestAnimationFrame(() => {
      listRef.current?.scrollToIndex({ index: focusIndex, animated: true, viewPosition: 0.25 });
    });
  }, [focusId, focusIndex]);

  if (paged.loading && paged.items.length === 0) {
    return <LoadingBlock label="Loading timeline…" />;
  }

  if (paged.error && paged.items.length === 0) {
    return <ErrorBlock message={paged.error} onRetry={paged.reload} />;
  }

  return (
    <FlatList
      ref={listRef}
      style={[styles.list, { backgroundColor: c.surfacePage }]}
      data={rows}
      keyExtractor={(row) => (row.kind === 'decade' ? `decade:${row.decade}` : `event:${row.event.id}`)}
      ListEmptyComponent={<EmptyBlock message="No timeline events yet." />}
      ListFooterComponent={<ListFooterLoading visible={paged.loadingMore} />}
      refreshControl={
        <ThemedRefreshControl refreshing={paged.refreshing} onRefresh={paged.refresh} />
      }
      onEndReached={paged.loadMore}
      onEndReachedThreshold={0.4}
      onScrollToIndexFailed={({ index }) => {
        setTimeout(() => {
          listRef.current?.scrollToIndex({ index, animated: true, viewPosition: 0.25 });
        }, 80);
      }}
      renderItem={({ item: row }) => {
        if (row.kind === 'decade') {
          return (
            <Text
              style={[
                type.eyebrow,
                {
                  color: c.accentArchive,
                  paddingHorizontal: space.xl,
                  paddingTop: space.xxl,
                  paddingBottom: space.md,
                },
              ]}
            >
              {row.decade}
            </Text>
          );
        }

        const event = row.event;
        const expanded = expandedId === event.id;
        return (
          <Pressable
            accessibilityRole="button"
            accessibilityState={{ expanded }}
            accessibilityLabel={event.title}
            onPress={() => setExpandedId(expanded ? null : event.id)}
            style={({ pressed }) => [
              styles.row,
              { borderTopColor: c.hairline, opacity: pressed ? 0.72 : 1 },
            ]}
          >
            <Text style={[type.meta, { color: c.textMuted }]}>{event.formattedDate}</Text>
            <Text style={[type.listTitle, { color: c.textPrimary, marginTop: space.xs }]}>
              {event.title}
            </Text>
            <Text style={[type.meta, { color: c.accentArchive, marginTop: space.xs }]}>
              {event.categoryLabel}
            </Text>
            {expanded ? (
              <View style={styles.detail}>
                <Text style={[type.body, { color: c.textSecondary }]}>{event.summary}</Text>
                {event.sourceUrl ? (
                  <Pressable
                    accessibilityRole="link"
                    onPress={() => void openExternalUrl(event.sourceUrl!)}
                    style={styles.source}
                  >
                    <Text style={[type.button, { color: c.accentPrimary }]}>Source</Text>
                  </Pressable>
                ) : null}
              </View>
            ) : null}
          </Pressable>
        );
      }}
    />
  );
}

const styles = StyleSheet.create({
  list: { flex: 1 },
  row: {
    paddingVertical: space.base,
    paddingHorizontal: space.xl,
    borderTopWidth: StyleSheet.hairlineWidth,
  },
  detail: {
    marginTop: space.md,
    gap: space.md,
  },
  source: {
    minHeight: 44,
    justifyContent: 'center',
  },
});
