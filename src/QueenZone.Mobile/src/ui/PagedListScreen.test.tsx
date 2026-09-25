import { FlatList, RefreshControl, Text } from 'react-native';
import { fireEvent, screen, userEvent } from '@testing-library/react-native';
import type { PagedState } from '../hooks/usePagedContent';
import { flushVirtualizedList, renderWithProviders } from '../test/render';
import { dark, palette } from '../theme';
import { PagedListScreen } from './PagedListScreen';

type Item = { id: number; title: string };

function pagedState(overrides: Partial<PagedState<Item>> = {}): PagedState<Item> {
  return {
    items: [],
    page: 0,
    totalPages: 0,
    totalCount: 0,
    loading: false,
    refreshing: false,
    loadingMore: false,
    error: null,
    hasMore: false,
    reload: jest.fn(),
    refresh: jest.fn(),
    loadMore: jest.fn(),
    ...overrides,
  };
}

function renderList(paged: PagedState<Item>) {
  return renderWithProviders(
    <PagedListScreen
      paged={paged}
      loadingLabel="Loading items…"
      emptyMessage="No items yet."
      keyExtractor={(item) => String(item.id)}
      renderItem={({ item }) => <Text>{item.title}</Text>}
    />,
    { navigation: false },
  );
}

describe('PagedListScreen', () => {
  afterEach(async () => {
    await flushVirtualizedList();
  });

  it('shows the loading gate before the first page arrives', () => {
    renderList(pagedState({ loading: true }));
    expect(screen.getByLabelText('Loading items…')).toBeOnTheScreen();
    expect(screen.queryByText('No items yet.')).toBeNull();
  });

  it('shows the error gate and retries through paged.reload', async () => {
    const reload = jest.fn();
    renderList(pagedState({ error: 'The server had a problem.', reload }));
    expect(screen.getByText('Unable to load')).toBeOnTheScreen();
    expect(screen.getByText('The server had a problem.')).toBeOnTheScreen();

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Try again' }));
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('shows empty copy when the first page has no items', () => {
    renderList(pagedState());
    expect(screen.getByText('No items yet.')).toBeOnTheScreen();
  });

  it('renders rows and owns end-reached threshold plus themed refresh colours', () => {
    const loadMore = jest.fn();
    const refresh = jest.fn();
    renderList(
      pagedState({
        items: [{ id: 1, title: 'Live Aid' }],
        page: 1,
        totalPages: 2,
        totalCount: 2,
        hasMore: true,
        loadMore,
        refresh,
      }),
    );

    expect(screen.getByText('Live Aid')).toBeOnTheScreen();
    expect(screen.queryByLabelText('Loading items…')).toBeNull();
    expect(screen.queryByText('No items yet.')).toBeNull();

    const list = screen.UNSAFE_getByType(FlatList);
    expect(list.props.onEndReachedThreshold).toBe(0.4);
    expect(list.props.windowSize).toBe(10);
    expect(list.props.maxToRenderPerBatch).toBe(10);
    expect(list.props.initialNumToRender).toBe(10);
    expect(list.props.removeClippedSubviews).toBeUndefined();
    expect(list.props.getItemLayout).toBeUndefined();
    expect(list.props.alwaysBounceVertical).toBe(true);
    expect(list.props.data).toEqual([{ id: 1, title: 'Live Aid' }]);

    const refreshControl = screen.UNSAFE_getByType(RefreshControl);
    expect(refreshControl.props.tintColor).toBe(dark.accentPrimary);
    expect(refreshControl.props.colors).toEqual([dark.accentPrimary]);
    expect(refreshControl.props.progressBackgroundColor).toBe(palette.grey800);

    fireEvent(list, 'onEndReached');
    expect(loadMore).toHaveBeenCalledTimes(1);
    fireEvent(refreshControl, 'refresh');
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it('renders a function ListHeaderComponent on the loading gate', () => {
    function Header() {
      return <Text>Decade chips</Text>;
    }

    renderWithProviders(
      <PagedListScreen
        paged={pagedState({ loading: true })}
        loadingLabel="Loading items…"
        emptyMessage="No items yet."
        keyExtractor={(item) => String(item.id)}
        renderItem={({ item }) => <Text>{item.title}</Text>}
        ListHeaderComponent={Header}
      />,
      { navigation: false },
    );

    expect(screen.getByText('Decade chips')).toBeOnTheScreen();
    expect(screen.getByLabelText('Loading items…')).toBeOnTheScreen();
  });

  it('lets callers override the text-list virtualization numbers', () => {
    renderWithProviders(
      <PagedListScreen
        paged={pagedState({ items: [{ id: 1, title: 'Live Aid' }] })}
        loadingLabel="Loading items…"
        emptyMessage="No items yet."
        keyExtractor={(item) => String(item.id)}
        renderItem={({ item }) => <Text>{item.title}</Text>}
        windowSize={7}
        maxToRenderPerBatch={12}
        initialNumToRender={12}
        removeClippedSubviews
      />,
      { navigation: false },
    );

    const list = screen.UNSAFE_getByType(FlatList);
    expect(list.props.windowSize).toBe(7);
    expect(list.props.maxToRenderPerBatch).toBe(12);
    expect(list.props.initialNumToRender).toBe(12);
    expect(list.props.removeClippedSubviews).toBe(true);
  });

  it('owns getItemLayout from itemHeight', () => {
    renderWithProviders(
      <PagedListScreen
        paged={pagedState({ items: [{ id: 1, title: 'Live Aid' }] })}
        loadingLabel="Loading items…"
        emptyMessage="No items yet."
        keyExtractor={(item) => String(item.id)}
        renderItem={({ item }) => <Text>{item.title}</Text>}
        itemHeight={48}
      />,
      { navigation: false },
    );

    const list = screen.UNSAFE_getByType(FlatList);
    expect(list.props.getItemLayout(null, 0)).toEqual({ length: 48, offset: 0, index: 0 });
    expect(list.props.getItemLayout(null, 3)).toEqual({ length: 48, offset: 144, index: 3 });
  });

  it('keeps the list visible when a later refresh or error happens with items', () => {
    renderList(
      pagedState({
        items: [{ id: 2, title: 'Wembley' }],
        loading: true,
        error: 'Stale refresh failed.',
        refreshing: true,
      }),
    );
    expect(screen.getByText('Wembley')).toBeOnTheScreen();
    expect(screen.queryByLabelText('Loading items…')).toBeNull();
    expect(screen.queryByText('Unable to load')).toBeNull();
  });
});
