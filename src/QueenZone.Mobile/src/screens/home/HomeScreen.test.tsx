import { act, fireEvent, screen, userEvent, waitFor, within } from '@testing-library/react-native';
import { RefreshControl } from 'react-native';
import {
  fetchForumRecentThreads,
  fetchHomePoll,
  fetchInbox,
  fetchQuizSprintDaily,
  fetchLiveActivity,
  fetchNewsPage,
  fetchOnThisDay,
  fetchPhotoCategories,
  fetchRandomQuote,
  voteHomePoll,
} from '../../api';
import { ApiError } from '../../api/client';
import { OFFLINE_MESSAGE, TIMEOUT_MESSAGE } from '../../api/errors';
import { invalidate } from '../../cache/externalStore';
import { NEWS_LIST_CACHE_KEY } from '../../cache/keys';
import { deferred, forumRecentThreadFixture, newsItemFixture, pagedResponse } from '../../test/fixtures';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, flushVirtualizedList, renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { HomeScreen } from './HomeScreen';

const mockAppConfig = {
  appEnv: 'development' as 'development' | 'staging' | 'production',
  apiBaseUrl: 'http://qz.test',
  version: '0.1.0',
  buildTimestampUtc: undefined as string | undefined,
};

jest.mock('../../config/appConfig', () => ({
  getAppConfig: () => mockAppConfig,
}));

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchNewsPage: jest.fn(),
    fetchForumRecentThreads: jest.fn(),
    fetchPhotoCategories: jest.fn(),
    fetchOnThisDay: jest.fn(),
    fetchRandomQuote: jest.fn(),
    fetchHomePoll: jest.fn(),
    voteHomePoll: jest.fn(),
    fetchLiveActivity: jest.fn(),
    fetchInbox: jest.fn(),
    fetchQuizSprintDaily: jest.fn(),
  };
});

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../messages/useUnreadConversationCount', () => ({
  useUnreadConversationCount: () => 0,
}));

jest.mock('expo-linear-gradient', () => {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- Jest CJS mock factory.
  const { View } = require('react-native');
  return { LinearGradient: View };
});

const mockSyncHomeWidget = jest.fn().mockResolvedValue(undefined);
jest.mock('../../widgets/widgetSync', () => ({
  syncHomeWidget: (...args: unknown[]) => mockSyncHomeWidget(...args),
}));

const fetchNews = fetchNewsPage as jest.MockedFunction<typeof fetchNewsPage>;
const fetchForum = fetchForumRecentThreads as jest.MockedFunction<typeof fetchForumRecentThreads>;
const fetchPhotos = fetchPhotoCategories as jest.MockedFunction<typeof fetchPhotoCategories>;
const fetchDay = fetchOnThisDay as jest.MockedFunction<typeof fetchOnThisDay>;
const fetchQuote = fetchRandomQuote as jest.MockedFunction<typeof fetchRandomQuote>;
const fetchPoll = fetchHomePoll as jest.MockedFunction<typeof fetchHomePoll>;
const votePoll = voteHomePoll as jest.MockedFunction<typeof voteHomePoll>;
const fetchLive = fetchLiveActivity as jest.MockedFunction<typeof fetchLiveActivity>;
const fetchInboxMock = fetchInbox as jest.MockedFunction<typeof fetchInbox>;

function onThisDayFixture() {
  return {
    id: 1,
    title: 'The Game',
    summary: 'Queen released The Game.',
    eventDate: '1980-06-30',
    formattedDate: '30 June 1980',
    category: 'music',
    categoryLabel: 'Release',
    sourceUrl: null,
  };
}

function renderHome(navigation = fakeNavigation()) {
  return {
    navigation,
    ...renderWithProviders(
      <HomeScreen navigation={navigation as never} route={{ key: 'home', name: 'Home' } as never} />,
      { navigation: false },
    ),
  };
}

describe('HomeScreen', () => {
  beforeEach(() => {
    mockSession.isSignedIn = false;
    mockSession.accessToken = null;
    mockAppConfig.version = '0.1.0';
    mockAppConfig.buildTimestampUtc = undefined;
    mockAppConfig.appEnv = 'development';
    mockAppConfig.apiBaseUrl = 'http://qz.test';
    fetchNews.mockResolvedValue(
      pagedResponse(
        [
          newsItemFixture({ id: 1003, title: 'QueenZone modernisation begins' }),
          newsItemFixture({ id: 7, title: 'Live Aid remembered' }),
        ],
        1,
        1,
      ),
    );
    fetchForum.mockResolvedValue([forumRecentThreadFixture()]);
    fetchPhotos.mockResolvedValue(pagedResponse([], 1, 0));
    fetchDay.mockResolvedValue(null);
    fetchQuote.mockResolvedValue(null);
    fetchPoll.mockResolvedValue(null);
    votePoll.mockReset();
    fetchLive.mockResolvedValue({ newForumRepliesToday: 0 });
    (fetchQuizSprintDaily as jest.Mock).mockResolvedValue({ top: [], viewer: null, playersToday: 0 });
    mockSyncHomeWidget.mockClear();
    fetchInboxMock.mockReset();
  });

  afterEach(async () => {
    await flushVirtualizedList();
  });

  it('exposes home-screen and home-hero testIDs for Debug device smoke', async () => {
    renderHome();
    expect(screen.getByTestId(testIds.homeScreen)).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.homeScreen).props.alwaysBounceVertical).toBe(true);
    await waitFor(() => expect(screen.getByTestId(testIds.homeHero)).toBeOnTheScreen());
  });

  it('renders recent forum threads from fetchForumRecentThreads instead of a skeleton', async () => {
    fetchForum.mockResolvedValue([
      forumRecentThreadFixture(),
      forumRecentThreadFixture({ topicId: 1003, title: 'True or not true?' }),
    ]);
    renderHome();

    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid remembered' })).toBeOnTheScreen());
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Ranking every studio album' })).toBeOnTheScreen(),
    );
    expect(screen.getByRole('button', { name: 'True or not true?' })).toBeOnTheScreen();
    expect(screen.getByText('In the forum')).toBeOnTheScreen();
    expect(screen.queryByText(TIMEOUT_MESSAGE)).toBeNull();
    expect(screen.queryByText(OFFLINE_MESSAGE)).toBeNull();
    await flushVirtualizedList();
  });

  it('shows a retryable forum error when recent-threads times out', async () => {
    fetchForum.mockRejectedValueOnce(ApiError.timeout());
    fetchForum.mockResolvedValueOnce([forumRecentThreadFixture()]);
    renderHome();

    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid remembered' })).toBeOnTheScreen());
    await waitFor(() => expect(screen.getByText(TIMEOUT_MESSAGE)).toBeOnTheScreen());
    expect(screen.getByRole('button', { name: 'Try again' })).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Ranking every studio album' })).toBeNull();

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Ranking every studio album' })).toBeOnTheScreen(),
    );
    expect(screen.queryByText(TIMEOUT_MESSAGE)).toBeNull();
    await flushVirtualizedList();
  });

  it('shows a retryable forum error when recent-threads is offline', async () => {
    fetchForum.mockRejectedValueOnce(ApiError.offline());
    fetchForum.mockResolvedValueOnce([forumRecentThreadFixture()]);
    renderHome();

    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid remembered' })).toBeOnTheScreen());
    await waitFor(() => expect(screen.getByText(OFFLINE_MESSAGE)).toBeOnTheScreen());
    expect(screen.getByRole('button', { name: 'Try again' })).toBeOnTheScreen();

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Ranking every studio album' })).toBeOnTheScreen(),
    );
    expect(screen.queryByText(OFFLINE_MESSAGE)).toBeNull();
    await flushVirtualizedList();
  });

  it('recovers a failed forum section on pull-to-refresh', async () => {
    fetchForum.mockRejectedValueOnce(ApiError.timeout());
    renderHome();
    await waitFor(() => expect(screen.getByText(TIMEOUT_MESSAGE)).toBeOnTheScreen());

    fetchForum.mockResolvedValueOnce([forumRecentThreadFixture()]);
    await act(async () => {
      fireEvent(screen.UNSAFE_getByType(RefreshControl), 'refresh');
    });
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Ranking every studio album' })).toBeOnTheScreen(),
    );
    expect(screen.queryByText(TIMEOUT_MESSAGE)).toBeNull();
    await flushVirtualizedList();
  });

  it('recovers via pull-to-refresh after an initial-generation forum abort', async () => {
    fetchForum.mockRejectedValueOnce(Object.assign(new Error('Aborted'), { name: 'AbortError' }));
    renderHome();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid remembered' })).toBeOnTheScreen());
    await waitFor(() => expect(screen.getByText(TIMEOUT_MESSAGE)).toBeOnTheScreen());
    expect(screen.queryByRole('button', { name: 'Ranking every studio album' })).toBeNull();

    fetchForum.mockResolvedValueOnce([forumRecentThreadFixture()]);
    await act(async () => {
      fireEvent(screen.UNSAFE_getByType(RefreshControl), 'refresh');
    });
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Ranking every studio album' })).toBeOnTheScreen(),
    );
    expect(screen.queryByText(TIMEOUT_MESSAGE)).toBeNull();
    await flushVirtualizedList();
  });

  // Field path (Richard/Pat): first Home forum load stays on the skeleton;
  // pull-to-refresh starts a new generation and recovers. The aborted first
  // generation must not flash a failed section.
  it('recovers a first-load forum skeleton on pull-to-refresh', async () => {
    const pendingForum = deferred<ReturnType<typeof forumRecentThreadFixture>[]>();
    fetchForum.mockReturnValueOnce(pendingForum.promise);
    renderHome();

    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid remembered' })).toBeOnTheScreen());
    expect(screen.getByText('In the forum')).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Ranking every studio album' })).toBeNull();
    expect(screen.queryByText(TIMEOUT_MESSAGE)).toBeNull();

    await act(async () => {
      fireEvent(screen.UNSAFE_getByType(RefreshControl), 'refresh');
    });
    pendingForum.reject(Object.assign(new Error('Aborted'), { name: 'AbortError' }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Ranking every studio album' })).toBeOnTheScreen(),
    );
    expect(screen.queryByText(TIMEOUT_MESSAGE)).toBeNull();
    await flushVirtualizedList();
  });

  it('opens live news and forum rows with numeric ids, not placeholders', async () => {
    const { navigation } = renderHome();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid remembered' })).toBeOnTheScreen());
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Ranking every studio album' })).toBeOnTheScreen(),
    );

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Live Aid remembered' }));
    expect(navigation.navigate).toHaveBeenCalledWith('Story', { id: 7 });
    expect(navigation.navigate).not.toHaveBeenCalledWith(
      'ArchiveTab',
      expect.objectContaining({ params: { id: 0 } }),
    );

    await user.press(screen.getByRole('button', { name: 'Ranking every studio album' }));
    expect(navigation.navigate).toHaveBeenCalledWith('ForumTab', {
      screen: 'Thread',
      params: { id: 1002, title: 'Ranking every studio album' },
      initial: false,
    });
    expect(JSON.stringify(navigation.navigate.mock.calls)).not.toContain('magic-tour');
    expect(screen.queryByText('Start the discussion')).toBeNull();
    expect(screen.queryByText('Join the discussion')).toBeNull();
  });

  it('refreshes only the news section when a news push invalidates the list', async () => {
    renderHome();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid remembered' })).toBeOnTheScreen());
    const newsCalls = fetchNews.mock.calls.length;
    const forumCalls = fetchForum.mock.calls.length;
    const photoCalls = fetchPhotos.mock.calls.length;

    fetchNews.mockResolvedValueOnce(
      pagedResponse(
        [
          newsItemFixture({ id: 2001, title: 'Brand new headline' }),
          newsItemFixture({ id: 1003, title: 'QueenZone modernisation begins' }),
        ],
        1,
        1,
      ),
    );

    await act(async () => {
      invalidate(NEWS_LIST_CACHE_KEY);
    });

    await waitFor(() => expect(screen.getByText('Brand new headline')).toBeOnTheScreen());
    expect(fetchNews.mock.calls.length).toBe(newsCalls + 1);
    expect(fetchForum.mock.calls.length).toBe(forumCalls);
    expect(fetchPhotos.mock.calls.length).toBe(photoCalls);
    await flushVirtualizedList();
  });

  it('does not refetch news after unmount when a news push arrives', async () => {
    const { unmount } = renderHome();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid remembered' })).toBeOnTheScreen());
    const newsCalls = fetchNews.mock.calls.length;
    unmount();

    await act(async () => {
      invalidate(NEWS_LIST_CACHE_KEY);
    });

    expect(fetchNews.mock.calls.length).toBe(newsCalls);
  });

  it('pull-to-refresh reloads live home sections', async () => {
    renderHome();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid remembered' })).toBeOnTheScreen());
    const newsCalls = fetchNews.mock.calls.length;
    const forumCalls = fetchForum.mock.calls.length;
    await act(async () => {
      fireEvent(screen.UNSAFE_getByType(RefreshControl), 'refresh');
    });
    await waitFor(() => expect(fetchNews.mock.calls.length).toBeGreaterThan(newsCalls));
    expect(fetchForum.mock.calls.length).toBeGreaterThan(forumCalls);
    await flushVirtualizedList();
  });

  it('holds RefreshControl refreshing until the deferred news fetch settles and refetches every section', async () => {
    renderHome();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid remembered' })).toBeOnTheScreen());

    const newsCalls = fetchNews.mock.calls.length;
    const forumCalls = fetchForum.mock.calls.length;
    const photoCalls = fetchPhotos.mock.calls.length;
    const dayCalls = fetchDay.mock.calls.length;
    const quoteCalls = fetchQuote.mock.calls.length;
    const pollCalls = fetchPoll.mock.calls.length;
    const liveCalls = fetchLive.mock.calls.length;
    const inboxCalls = fetchInboxMock.mock.calls.length;

    const pendingNews = deferred<ReturnType<typeof pagedResponse<ReturnType<typeof newsItemFixture>>>>();
    fetchNews.mockReturnValueOnce(pendingNews.promise);

    await act(async () => {
      fireEvent(screen.UNSAFE_getByType(RefreshControl), 'refresh');
    });

    expect(screen.UNSAFE_getByType(RefreshControl).props.refreshing).toBe(true);
    await waitFor(() => expect(fetchNews.mock.calls.length).toBe(newsCalls + 1));
    expect(fetchForum.mock.calls.length).toBe(forumCalls + 1);
    expect(fetchPhotos.mock.calls.length).toBe(photoCalls + 1);
    expect(fetchDay.mock.calls.length).toBe(dayCalls + 1);
    expect(fetchQuote.mock.calls.length).toBe(quoteCalls + 1);
    expect(fetchPoll.mock.calls.length).toBe(pollCalls + 1);
    expect(fetchLive.mock.calls.length).toBe(liveCalls + 1);
    expect(fetchInboxMock.mock.calls.length).toBe(inboxCalls);

    pendingNews.resolve(
      pagedResponse(
        [
          newsItemFixture({ id: 1003, title: 'QueenZone modernisation begins' }),
          newsItemFixture({ id: 7, title: 'Live Aid remembered' }),
        ],
        1,
        1,
      ),
    );
    await waitFor(() => expect(screen.UNSAFE_getByType(RefreshControl).props.refreshing).toBe(false));
    await flushVirtualizedList();
  });

  it('keeps the live strip visible while a pull refreshes', async () => {
    fetchLive.mockResolvedValue({ newForumRepliesToday: 4 });
    renderHome();
    await waitFor(() => expect(screen.getByText('4 new forum replies today')).toBeOnTheScreen());

    const pendingNews = deferred<ReturnType<typeof pagedResponse<ReturnType<typeof newsItemFixture>>>>();
    fetchNews.mockReturnValueOnce(pendingNews.promise);
    await act(async () => {
      fireEvent(screen.UNSAFE_getByType(RefreshControl), 'refresh');
    });

    expect(screen.UNSAFE_getByType(RefreshControl).props.refreshing).toBe(true);
    expect(screen.getByText('4 new forum replies today')).toBeOnTheScreen();

    pendingNews.resolve(
      pagedResponse(
        [
          newsItemFixture({ id: 1003, title: 'QueenZone modernisation begins' }),
          newsItemFixture({ id: 7, title: 'Live Aid remembered' }),
        ],
        1,
        1,
      ),
    );
    await waitFor(() => expect(screen.UNSAFE_getByType(RefreshControl).props.refreshing).toBe(false));
    expect(screen.getByText('4 new forum replies today')).toBeOnTheScreen();
    await flushVirtualizedList();
  });

  it('refetches filter-hidden sections on pull', async () => {
    renderHome();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid remembered' })).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'News' }));
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: 'Ranking every studio album' })).not.toBeOnTheScreen(),
    );

    const forumCalls = fetchForum.mock.calls.length;
    const dayCalls = fetchDay.mock.calls.length;
    await act(async () => {
      fireEvent(screen.UNSAFE_getByType(RefreshControl), 'refresh');
    });
    expect(fetchForum.mock.calls.length).toBe(forumCalls + 1);
    expect(fetchDay.mock.calls.length).toBe(dayCalls + 1);
    await flushVirtualizedList();
  });

  it('keeps On This Day visible while a pull refreshes', async () => {
    fetchDay.mockResolvedValue(onThisDayFixture());
    renderHome();
    await waitFor(() => expect(screen.getByText('Queen released The Game.')).toBeOnTheScreen());

    const pendingNews = deferred<ReturnType<typeof pagedResponse<ReturnType<typeof newsItemFixture>>>>();
    fetchNews.mockReturnValueOnce(pendingNews.promise);
    await act(async () => {
      fireEvent(screen.UNSAFE_getByType(RefreshControl), 'refresh');
    });

    expect(screen.UNSAFE_getByType(RefreshControl).props.refreshing).toBe(true);
    expect(screen.getByText('Queen released The Game.')).toBeOnTheScreen();
    expect(screen.getByText('30 JUNE 1980')).toBeOnTheScreen();

    pendingNews.resolve(
      pagedResponse(
        [
          newsItemFixture({ id: 1003, title: 'QueenZone modernisation begins' }),
          newsItemFixture({ id: 7, title: 'Live Aid remembered' }),
        ],
        1,
        1,
      ),
    );
    await waitFor(() => expect(screen.UNSAFE_getByType(RefreshControl).props.refreshing).toBe(false));
    expect(screen.getByText('Queen released The Game.')).toBeOnTheScreen();
    await flushVirtualizedList();
  });

  it('shows On this day and Queen Quotes as distinct cards', async () => {
    fetchDay.mockResolvedValue(onThisDayFixture());
    fetchQuote.mockResolvedValue({ id: 9, text: 'A kind of magic', whoSaid: 'Freddie Mercury' });
    renderHome();

    await waitFor(() => expect(screen.getByText('Queen released The Game.')).toBeOnTheScreen());
    expect(screen.getByText('On this day')).toBeOnTheScreen();
    expect(screen.getByText('Queen Quotes')).toBeOnTheScreen();
    expect(screen.getByText('“A kind of magic”')).toBeOnTheScreen();
    expect(screen.getByText('— Freddie Mercury')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'View timeline' })).toBeOnTheScreen();
    await flushVirtualizedList();
  });

  it('opens View timeline with ArchiveHub seeded under Timeline', async () => {
    fetchDay.mockResolvedValue(onThisDayFixture());
    const { navigation } = renderHome();

    await waitFor(() => expect(screen.getByRole('button', { name: 'View timeline' })).toBeOnTheScreen());
    fireEvent.press(screen.getByRole('button', { name: 'View timeline' }));
    expect(navigation.navigate).toHaveBeenCalledWith('ArchiveTab', {
      screen: 'Timeline',
      initial: false,
    });
    await flushVirtualizedList();
  });

  it('keeps the On this day card when no quote is published', async () => {
    fetchDay.mockResolvedValue(onThisDayFixture());
    fetchQuote.mockResolvedValue(null);
    renderHome();

    await waitFor(() => expect(screen.getByText('Queen released The Game.')).toBeOnTheScreen());
    expect(screen.getByText('On this day')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'View timeline' })).toBeOnTheScreen();
    expect(screen.queryByText('Queen Quotes')).not.toBeOnTheScreen();
    expect(screen.queryByText(/^“/)).not.toBeOnTheScreen();
    await flushVirtualizedList();
  });

  it('syncs the home screen widget once on-this-day content resolves', async () => {
    fetchDay.mockResolvedValue(onThisDayFixture());
    fetchQuote.mockResolvedValue({ id: 9, text: 'A kind of magic', whoSaid: 'Freddie Mercury' });
    renderHome();

    await waitFor(() =>
      expect(mockSyncHomeWidget).toHaveBeenCalledWith({
        onThisDay: onThisDayFixture(),
        quote: { id: 9, text: 'A kind of magic', whoSaid: 'Freddie Mercury' },
      }),
    );
    await flushVirtualizedList();
  });

  it('syncs a quote-only widget when on-this-day fails', async () => {
    fetchDay.mockRejectedValue(new Error('offline'));
    fetchQuote.mockResolvedValue({ id: 9, text: 'A kind of magic', whoSaid: 'Freddie Mercury' });
    renderHome();

    await waitFor(() =>
      expect(mockSyncHomeWidget).toHaveBeenCalledWith({
        onThisDay: null,
        quote: { id: 9, text: 'A kind of magic', whoSaid: 'Freddie Mercury' },
      }),
    );
    expect(screen.getByText('“A kind of magic”')).toBeOnTheScreen();
    expect(screen.getByText('Queen Quotes')).toBeOnTheScreen();
    expect(screen.queryByText('On this day')).not.toBeOnTheScreen();
    await flushVirtualizedList();
  });

  it('opens the Quote screen from the Queen Quotes card', async () => {
    fetchQuote.mockResolvedValue({ id: 9, text: 'A kind of magic', whoSaid: 'Freddie Mercury' });
    const { navigation } = renderHome();

    await waitFor(() => expect(screen.getByTestId(testIds.homeQuote)).toBeOnTheScreen());
    fireEvent.press(screen.getByTestId(testIds.homeQuote));
    expect(navigation.navigate).toHaveBeenCalledWith('Quote', { id: 9 });
    await flushVirtualizedList();
  });

  it('shows the Queen Quotes card when there is no on-this-day event', async () => {
    fetchDay.mockResolvedValue(null);
    fetchQuote.mockResolvedValue({ id: 9, text: 'A kind of magic', whoSaid: 'Freddie Mercury' });
    renderHome();

    await waitFor(() => expect(screen.getByText('“A kind of magic”')).toBeOnTheScreen());
    expect(screen.getByText('— Freddie Mercury')).toBeOnTheScreen();
    expect(screen.getByText('Queen Quotes')).toBeOnTheScreen();
    expect(screen.queryByText('On this day')).not.toBeOnTheScreen();
    expect(screen.queryByText('Queen released The Game.')).not.toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'View timeline' })).not.toBeOnTheScreen();
    await flushVirtualizedList();
  });

  it('shows the baked version after the last section, not in the masthead', async () => {
    renderHome();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid remembered' })).toBeOnTheScreen());

    const footer = screen.getByTestId(testIds.homeVersion);
    expect(footer).toBeOnTheScreen();
    expect(footer).toHaveTextContent('0.1.0');
    expect(within(screen.getByTestId(testIds.tabMasthead)).queryByTestId(testIds.homeVersion)).toBeNull();
    await flushVirtualizedList();
  });

  it('shows the selected API target outside production', async () => {
    mockAppConfig.appEnv = 'staging';
    mockAppConfig.apiBaseUrl = 'https://dev.queenzone.org';
    renderHome();

    await waitFor(() => expect(screen.getByTestId(testIds.homeEnvironment)).toBeOnTheScreen());
    expect(screen.getByTestId(testIds.homeEnvironment)).toHaveTextContent(
      'STAGING · https://dev.queenzone.org',
    );
    await flushVirtualizedList();
  });

  it('does not show an environment banner for production', async () => {
    mockAppConfig.appEnv = 'production';
    mockAppConfig.apiBaseUrl = 'https://www.queenzone.org';
    renderHome();

    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid remembered' })).toBeOnTheScreen());
    expect(screen.queryByTestId(testIds.homeEnvironment)).toBeNull();
    await flushVirtualizedList();
  });

  it('adds the publish date on Home for signed-in and signed-out visitors', async () => {
    mockAppConfig.version = '0.1.214';
    mockAppConfig.buildTimestampUtc = '2026-08-29T13:40:12Z';

    const signedOut = renderHome();
    await waitFor(() => expect(screen.getByTestId(testIds.homeVersion)).toHaveTextContent('0.1.214 · 29 Aug 2026'));
    expect(within(screen.getByTestId(testIds.tabMasthead)).queryByText('0.1.214 · 29 Aug 2026')).toBeNull();
    await flushVirtualizedList();
    signedOut.unmount();

    mockSession.isSignedIn = true;
    mockSession.accessToken = 'token';
    fetchInboxMock.mockResolvedValue(pagedResponse([], 1, 0));
    renderHome();
    await waitFor(() => expect(screen.getByText('Your messages')).toBeOnTheScreen());
    expect(screen.getByTestId(testIds.homeVersion)).toHaveTextContent('0.1.214 · 29 Aug 2026');
    await flushVirtualizedList();
  });

  it('hides the home poll card when the API returns null', async () => {
    renderHome();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Live Aid remembered' })).toBeOnTheScreen());
    expect(screen.queryByTestId(testIds.homePoll)).toBeNull();
    await flushVirtualizedList();
  });

  it('shows the current home poll with public results and a sign-in link for guests', async () => {
    fetchPoll.mockResolvedValue(openHomePoll());
    renderHome();

    await waitFor(() => expect(screen.getByTestId(testIds.homePoll)).toBeOnTheScreen());
    expect(screen.getByText('Best Queen album?')).toBeOnTheScreen();
    expect(screen.getByText('A Night at the Opera')).toBeOnTheScreen();
    expect(screen.getByText('4 · 80%')).toBeOnTheScreen();
    expect(screen.getByTestId(testIds.homePollSignIn)).toBeOnTheScreen();
    expect(screen.queryByTestId(`${testIds.homePollVote}-opt-1`)).toBeNull();
    await flushVirtualizedList();
  });

  it('lets a signed-in member vote once and then refetches results', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'member-token';
    fetchInboxMock.mockResolvedValue(pagedResponse([], 1, 0));
    fetchPoll.mockResolvedValue(openHomePoll());
    votePoll.mockResolvedValue(votedHomePoll());

    renderHome();
    await waitFor(() => expect(screen.getByTestId(`${testIds.homePollVote}-opt-1`)).toBeOnTheScreen());

    fetchPoll.mockResolvedValue(votedHomePoll());
    fireEvent.press(screen.getByTestId(`${testIds.homePollVote}-opt-1`));

    await waitFor(() => expect(votePoll).toHaveBeenCalledWith('opt-1', 'member-token'));
    await waitFor(() => {
      expect(screen.getByText('Your vote')).toBeOnTheScreen();
      expect(screen.getByText('5 · 83.3%')).toBeOnTheScreen();
    });
    expect(fetchPoll.mock.calls.length).toBeGreaterThan(1);
    await flushVirtualizedList();
  });
});

function openHomePoll() {
  return {
    id: 'poll-1',
    question: 'Best Queen album?',
    options: [
      { id: 'opt-1', text: 'A Night at the Opera', count: 4, percentage: 80 },
      { id: 'opt-2', text: 'News of the World', count: 1, percentage: 20 },
    ],
    totalVotes: 5,
    isClosed: false,
    viewerHasVoted: false,
    selectedOptionId: null,
  };
}

function votedHomePoll() {
  return {
    id: 'poll-1',
    question: 'Best Queen album?',
    options: [
      { id: 'opt-1', text: 'A Night at the Opera', count: 5, percentage: 83.3 },
      { id: 'opt-2', text: 'News of the World', count: 1, percentage: 16.7 },
    ],
    totalVotes: 6,
    isClosed: false,
    viewerHasVoted: true,
    selectedOptionId: 'opt-1',
  };
}
