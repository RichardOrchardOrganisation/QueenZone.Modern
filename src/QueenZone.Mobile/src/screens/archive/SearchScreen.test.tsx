import { useNavigation } from '@react-navigation/native';
import { act, fireEvent, screen, userEvent, waitFor } from '@testing-library/react-native';
import * as WebBrowser from 'expo-web-browser';
import { RefreshControl } from 'react-native';
import { fetchSearchPage } from '../../api';
import { ApiError } from '../../api/client';
import type { SearchResult } from '../../api/types';
import { deferred, pagedResponse } from '../../test/fixtures';
import { renderWithProviders, flushVirtualizedList } from '../../test/render';
import { testIds } from '../../test/testIds';
import { SearchRouteScreen, SearchScreen } from './SearchScreen';

const mockTabNavigate = jest.fn();

jest.mock('@react-navigation/native', () => {
  const actual = jest.requireActual('@react-navigation/native');
  return {
    ...actual,
    useNavigation: jest.fn(),
  };
});

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchSearchPage: jest.fn(),
  };
});

jest.mock('../../config/appConfig', () => ({
  getAppConfig: () => ({ apiBaseUrl: 'https://www.queenzone.org', appEnv: 'development', version: '0.1.0' }),
}));

const fetchSearch = fetchSearchPage as jest.MockedFunction<typeof fetchSearchPage>;

function resultFixture(overrides: Partial<SearchResult> = {}): SearchResult {
  return {
    contentType: 'news',
    sourceKey: 'news:1003',
    title: 'QueenZone modernisation begins',
    summary: 'The rebuild starts.',
    url: '/news/1003/queenzone-modernisation-begins',
    publishedAt: '2026-06-11T09:00:00Z',
    imageUrl: null,
    category: null,
    authorDisplayName: null,
    id: 1003,
    ...overrides,
  };
}

function renderSearch(onOpen = jest.fn()) {
  return {
    onOpen,
    ...renderWithProviders(<SearchScreen onOpen={onOpen} />, { navigation: false }),
  };
}

describe('SearchScreen', () => {
  beforeEach(() => {
    fetchSearch.mockReset();
    mockTabNavigate.mockReset();
    (useNavigation as jest.Mock).mockReturnValue({
      getParent: () => ({ navigate: mockTabNavigate }),
    });
  });

  afterEach(async () => {
    await flushVirtualizedList();
  });

  it('shows suggested query presets and does not search one character', async () => {
    renderSearch();
    expect(screen.getByRole('button', { name: 'Search for Bohemian Rhapsody' })).toBeOnTheScreen();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Search the archive'), 'a');
    await waitFor(() => expect(fetchSearch).not.toHaveBeenCalled());
  });

  it('loads live results and opens a news story by numeric id', async () => {
    const pending = deferred<ReturnType<typeof pagedResponse<SearchResult>>>();
    fetchSearch.mockReturnValueOnce(pending.promise);
    const { onOpen } = renderSearch();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Search the archive'), 'modernisation');
    await waitFor(() => expect(screen.getByLabelText('Searching the archive…')).toBeOnTheScreen());
    pending.resolve(pagedResponse([resultFixture()], 1, 1));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'QueenZone modernisation begins. News' })).toBeOnTheScreen(),
    );
    expect(screen.getByTestId('search-result-news-1003')).toBeOnTheScreen();
    expect(fetchSearch).toHaveBeenCalledWith(
      expect.objectContaining({ q: 'modernisation', type: null, page: 1, pageSize: 20 }),
    );
    await user.press(screen.getByRole('button', { name: 'QueenZone modernisation begins. News' }));
    expect(onOpen).toHaveBeenCalledWith(
      { kind: 'tab', tab: 'NewsTab', screen: 'Story', params: { id: 1003 } },
      expect.objectContaining({ sourceKey: 'news:1003', id: 1003 }),
    );
  });

  it('opens a forum thread by numeric topic id, not a slug', async () => {
    fetchSearch.mockResolvedValue(
      pagedResponse(
        [
          resultFixture({
            contentType: 'forum',
            sourceKey: 'forum-thread:1002',
            title: 'Ranking every studio album',
            url: '/forum/topic/1002/ranking-every-studio-album',
            id: 1002,
          }),
        ],
        1,
        1,
      ),
    );
    const { onOpen } = renderSearch();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Search the archive'), 'studio album');
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Ranking every studio album. Forum' })).toBeOnTheScreen(),
    );
    await user.press(screen.getByRole('button', { name: 'Ranking every studio album. Forum' }));
    expect(onOpen).toHaveBeenCalledWith(
      { kind: 'tab', tab: 'ForumTab', screen: 'Thread', params: { id: 1002 } },
      expect.objectContaining({ id: 1002 }),
    );
  });

  it('falls back to the website URL when a news hit has no usable id', async () => {
    fetchSearch.mockResolvedValue(
      pagedResponse(
        [
          resultFixture({
            sourceKey: 'news:0',
            title: 'Unparseable news hit',
            url: '/news/1003/queenzone-modernisation-begins',
            id: 0,
          }),
        ],
        1,
        1,
      ),
    );
    const { onOpen } = renderSearch();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Search the archive'), 'modernisation');
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Unparseable news hit. News' })).toBeOnTheScreen(),
    );
    await user.press(screen.getByRole('button', { name: 'Unparseable news hit. News' }));
    expect(onOpen).toHaveBeenCalledWith(
      {
        kind: 'web',
        url: 'https://www.queenzone.org/news/1003/queenzone-modernisation-begins',
      },
      expect.objectContaining({ sourceKey: 'news:0' }),
    );
  });

  it('shows empty copy when the index has no matches', async () => {
    fetchSearch.mockResolvedValue(pagedResponse([], 1, 0));
    renderSearch();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Search the archive'), 'xyzzy unmatched');
    await waitFor(() =>
      expect(screen.getByText('No results found for “xyzzy unmatched”. Try different keywords.')).toBeOnTheScreen(),
    );
  });

  it('shows an error and retries', async () => {
    fetchSearch
      .mockRejectedValueOnce(new ApiError(0, 'Unable to reach QueenZone. Check your connection and try again.'))
      .mockResolvedValueOnce(pagedResponse([resultFixture()], 1, 1));
    renderSearch();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Search the archive'), 'modernisation');
    await waitFor(() => expect(screen.getByText('Unable to load')).toBeOnTheScreen());
    await user.press(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'QueenZone modernisation begins. News' })).toBeOnTheScreen(),
    );
  });

  it('loads the next page when the result list reaches the end', async () => {
    fetchSearch
      .mockResolvedValueOnce(pagedResponse([resultFixture()], 1, 2))
      .mockResolvedValueOnce(
        pagedResponse(
          [resultFixture({ sourceKey: 'news:7', id: 7, title: 'Second page hit' })],
          2,
          2,
        ),
      );
    renderSearch();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Search the archive'), 'modernisation');
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'QueenZone modernisation begins. News' })).toBeOnTheScreen(),
    );
    fireEvent(screen.getByTestId('search-results'), 'onEndReached');
    await waitFor(() =>
      expect(fetchSearch).toHaveBeenCalledWith(expect.objectContaining({ q: 'modernisation', page: 2 })),
    );
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Second page hit. News' })).toBeOnTheScreen(),
    );
  });

  it('narrows the live query when a type chip is pressed', async () => {
    fetchSearch.mockResolvedValue(pagedResponse([resultFixture()], 1, 1));
    renderSearch();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Search the archive'), 'archive');
    await waitFor(() => expect(screen.getByText('News')).toBeOnTheScreen());
    await user.press(screen.getByText('News'));
    await waitFor(() =>
      expect(fetchSearch).toHaveBeenCalledWith(expect.objectContaining({ q: 'archive', type: 'news' })),
    );
    expect(screen.getByTestId(testIds.searchTypeFilters)).toHaveStyle({ flexGrow: 0, flexShrink: 0 });
  });

  it('pull-to-refresh reloads the current search page', async () => {
    fetchSearch.mockResolvedValue(pagedResponse([resultFixture()], 1, 1));
    renderSearch();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Search the archive'), 'modernisation');
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'QueenZone modernisation begins. News' })).toBeOnTheScreen(),
    );
    const callsAfterLoad = fetchSearch.mock.calls.length;
    await act(async () => {
      fireEvent(screen.UNSAFE_getByType(RefreshControl), 'refresh');
    });
    await waitFor(() => expect(fetchSearch.mock.calls.length).toBeGreaterThan(callsAfterLoad));
    expect(fetchSearch).toHaveBeenLastCalledWith(
      expect.objectContaining({ q: 'modernisation', page: 1 }),
    );
    await flushVirtualizedList();
  });
});

describe('SearchRouteScreen', () => {
  beforeEach(() => {
    fetchSearch.mockReset();
    mockTabNavigate.mockReset();
    (useNavigation as jest.Mock).mockReturnValue({
      getParent: () => ({ navigate: mockTabNavigate }),
    });
  });

  it('runs a Siri-provided search term without requiring keyboard input', async () => {
    fetchSearch.mockResolvedValue(pagedResponse([resultFixture()], 1, 1));
    renderWithProviders(
      <SearchRouteScreen route={{ key: 'siri-search', name: 'Search', params: { query: 'Queen II' } }} />,
      { navigation: false },
    );
    await waitFor(() => expect(fetchSearch).toHaveBeenCalledWith(expect.objectContaining({ q: 'Queen II' })));
  });

  it('navigates news hits through the tab parent', async () => {
    fetchSearch.mockResolvedValue(pagedResponse([resultFixture()], 1, 1));
    renderWithProviders(<SearchRouteScreen />, { navigation: false });
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Search the archive'), 'modernisation');
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'QueenZone modernisation begins. News' })).toBeOnTheScreen(),
    );
    await user.press(screen.getByRole('button', { name: 'QueenZone modernisation begins. News' }));
    expect(mockTabNavigate).toHaveBeenCalledWith('NewsTab', {
      screen: 'Story',
      params: { id: 1003 },
      initial: false,
    });
  });

  it('opens article hits in the in-app browser', async () => {
    const openBrowser = WebBrowser.openBrowserAsync as jest.MockedFunction<typeof WebBrowser.openBrowserAsync>;
    openBrowser.mockClear();
    fetchSearch.mockResolvedValue(
      pagedResponse(
        [
          resultFixture({
            contentType: 'article',
            sourceKey: 'article:some-slug',
            title: 'A community article',
            url: '/articles/some-slug',
            id: null,
          }),
        ],
        1,
        1,
      ),
    );
    renderWithProviders(<SearchRouteScreen />, { navigation: false });
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Search the archive'), 'community');
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'A community article. Articles' })).toBeOnTheScreen(),
    );
    await user.press(screen.getByRole('button', { name: 'A community article. Articles' }));
    expect(openBrowser).toHaveBeenCalledWith('https://www.queenzone.org/articles/some-slug');
    expect(mockTabNavigate).not.toHaveBeenCalled();
  });

  it('opens the website URL when the tab parent is missing', async () => {
    (useNavigation as jest.Mock).mockReturnValue({ getParent: () => undefined });
    const openBrowser = WebBrowser.openBrowserAsync as jest.MockedFunction<typeof WebBrowser.openBrowserAsync>;
    openBrowser.mockClear();
    fetchSearch.mockResolvedValue(pagedResponse([resultFixture()], 1, 1));
    renderWithProviders(<SearchRouteScreen />, { navigation: false });
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Search the archive'), 'modernisation');
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'QueenZone modernisation begins. News' })).toBeOnTheScreen(),
    );
    await user.press(screen.getByRole('button', { name: 'QueenZone modernisation begins. News' }));
    expect(mockTabNavigate).not.toHaveBeenCalled();
    expect(openBrowser).toHaveBeenCalledWith(
      'https://www.queenzone.org/news/1003/queenzone-modernisation-begins',
    );
  });
});
