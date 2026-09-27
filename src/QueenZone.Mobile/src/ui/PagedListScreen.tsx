import type { ComponentType, ReactNode } from 'react';
import { FlatList, StyleSheet, View, type FlatListProps, type ListRenderItem } from 'react-native';
import type { PagedState } from '../hooks/usePagedContent';
import { useTheme } from '../theme';
import { EmptyBlock, ErrorBlock, ListFooterLoading, LoadingBlock } from './ScreenStates';
import { ThemedRefreshControl } from './ThemedRefreshControl';

type OwnedListProp =
  | 'data'
  | 'renderItem'
  | 'keyExtractor'
  | 'onEndReached'
  | 'onEndReachedThreshold'
  | 'refreshControl'
  | 'ListFooterComponent'
  | 'ListEmptyComponent'
  | 'getItemLayout';

/** Shared infinite-scroll trigger. Owned here so list screens cannot drift. */
const END_REACHED_THRESHOLD = 0.4;

/** Text-list defaults. Image grids override through extra FlatList props. */
const DEFAULT_WINDOW_SIZE = 10;
const DEFAULT_MAX_TO_RENDER_PER_BATCH = 10;
const DEFAULT_INITIAL_NUM_TO_RENDER = 10;

export type PagedListScreenProps<T> = {
  paged: PagedState<T>;
  renderItem: ListRenderItem<T>;
  keyExtractor: (item: T, index: number) => string;
  loadingLabel: string;
  emptyMessage: string;
  /** Extra footer content after the owned loading spinner (e.g. Search `ArchiveFooter`). */
  footerAfter?: ReactNode;
  /**
   * Fixed row height. When set, owns `getItemLayout`.
   * Omit for variable-height rows — a wrong layout overlaps worse than stock.
   */
  itemHeight?: number;
} & Omit<FlatListProps<T>, OwnedListProp>;

function renderListHeader(header: FlatListProps<unknown>['ListHeaderComponent']): ReactNode {
  if (header == null) {
    return null;
  }
  if (typeof header === 'function') {
    const Header = header as ComponentType;
    return <Header />;
  }
  return header;
}

export function PagedListScreen<T>({
  paged,
  renderItem,
  keyExtractor,
  loadingLabel,
  emptyMessage,
  footerAfter,
  style,
  ListHeaderComponent,
  itemHeight,
  ...listProps
}: PagedListScreenProps<T>) {
  const { c } = useTheme();
  const listStyle = [styles.list, { backgroundColor: c.surfacePage }, style];

  if (paged.loading && paged.items.length === 0) {
    return (
      <View style={listStyle}>
        {renderListHeader(ListHeaderComponent)}
        <LoadingBlock label={loadingLabel} />
      </View>
    );
  }

  if (paged.error && paged.items.length === 0) {
    return (
      <View style={listStyle}>
        {renderListHeader(ListHeaderComponent)}
        <ErrorBlock message={paged.error} onRetry={paged.reload} />
      </View>
    );
  }

  return (
    <FlatList
      windowSize={DEFAULT_WINDOW_SIZE}
      maxToRenderPerBatch={DEFAULT_MAX_TO_RENDER_PER_BATCH}
      initialNumToRender={DEFAULT_INITIAL_NUM_TO_RENDER}
      {...listProps}
      style={listStyle}
      data={paged.items}
      keyExtractor={keyExtractor}
      renderItem={renderItem}
      ListHeaderComponent={ListHeaderComponent}
      ListEmptyComponent={<EmptyBlock message={emptyMessage} />}
      ListFooterComponent={
        <>
          <ListFooterLoading visible={paged.loadingMore} />
          {footerAfter}
        </>
      }
      alwaysBounceVertical
      refreshControl={
        <ThemedRefreshControl refreshing={paged.refreshing} onRefresh={paged.refresh} />
      }
      onEndReached={paged.loadMore}
      onEndReachedThreshold={END_REACHED_THRESHOLD}
      getItemLayout={
        itemHeight != null
          ? (_data, index) => ({
              length: itemHeight,
              offset: itemHeight * index,
              index,
            })
          : undefined
      }
    />
  );
}

const styles = StyleSheet.create({
  list: { flex: 1 },
});
