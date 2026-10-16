import { screen, userEvent, waitFor } from '@testing-library/react-native';
import {
  fetchForumCategories,
  fetchForumRecentThreads,
  fetchForumStats,
  fetchForumTopic,
  fetchForumTopicPoll,
  fetchForumTopicPosts,
  fetchForumTopicPostsResult,
  fetchForumTopicResult,
  fetchForumTopicWatch,
  fetchInbox,
  fetchLiveActivity,
  fetchNewsDetail,
  fetchNewsPage,
  fetchNewsYearRange,
  fetchOnThisDay,
  fetchPhotoCategories,
  fetchQuoteById,
  fetchRandomQuote,
} from '../../api';
import { fetchConversation, fetchConversationResult } from '../../api/messages';
import {
  conversationDetailFixture,
  conversationMessageFixture,
  newsDetailFixture,
  newsItemFixture,
  pagedResponse,
} from '../../test/fixtures';
import {
  ForumIndexHeaderRight,
  NewsIndexHeaderRight,
  SearchIdentityHeaderRight,
} from '../../navigation/headerButtons';
import { nestedTabParams } from '../../navigation/nestedTab';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, flushVirtualizedList, renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { ArchiveHubScreen } from '../archive/ArchiveHubScreen';
import { ForumScreen } from '../forum/ForumScreen';
import { ThreadScreen } from '../forum/ThreadScreen';
import { ConversationScreen } from '../messages/ConversationScreen';
import { NewsIndexScreen } from '../news/NewsIndexScreen';
import { NewsStoryScreen } from '../news/NewsStoryScreen';
import { PhotosScreen } from '../photos/PhotosScreen';
import { HomeScreen } from './HomeScreen';
import { QuoteScreen } from './QuoteScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchNewsPage: jest.fn(),
    fetchNewsYearRange: jest.fn(),
    fetchNewsDetail: jest.fn(),
    fetchForumRecentThreads: jest.fn(),
    fetchForumCategories: jest.fn(),
    fetchForumStats: jest.fn(),
    fetchForumTopic: jest.fn(),
    fetchForumTopicResult: jest.fn(),
    fetchForumTopicPosts: jest.fn(),
    fetchForumTopicPostsResult: jest.fn(),
    fetchForumTopicPoll: jest.fn(),
    fetchForumTopicWatch: jest.fn(),
    fetchPhotoCategories: jest.fn(),
    fetchOnThisDay: jest.fn(),
    fetchRandomQuote: jest.fn(),
    fetchQuoteById: jest.fn(),
    fetchLiveActivity: jest.fn(),
    fetchInbox: jest.fn(),
  };
});

jest.mock('../../api/messages', () => {
  const actual = jest.requireActual('../../api/messages');
  return {
    ...actual,
    fetchConversation: jest.fn(),
    fetchConversationResult: jest.fn(),
    fetchUnreadConversationCount: jest.fn(),
  };
});

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../messages/useUnreadConversationCount', () => ({
  useUnreadConversationCount: () => 0,
}));

jest.mock('../../widgets/widgetSync', () => ({
  syncHomeWidget: jest.fn().mockResolvedValue(undefined),
}));

jest.mock('expo-linear-gradient', () => {
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- Jest CJS mock factory.
  const { View } = require('react-native');
  return { LinearGradient: View };
});

const fetchNews = fetchNewsPage as jest.MockedFunction<typeof fetchNewsPage>;
const fetchYearRange = fetchNewsYearRange as jest.MockedFunction<typeof fetchNewsYearRange>;
const fetchDetail = fetchNewsDetail as jest.MockedFunction<typeof fetchNewsDetail>;
const fetchForum = fetchForumRecentThreads as jest.MockedFunction<typeof fetchForumRecentThreads>;
const fetchForumCats = fetchForumCategories as jest.MockedFunction<typeof fetchForumCategories>;
const fetchForumIndexStats = fetchForumStats as jest.MockedFunction<typeof fetchForumStats>;
const fetchTopic = fetchForumTopic as jest.MockedFunction<typeof fetchForumTopic>;
const fetchTopicResult = fetchForumTopicResult as jest.MockedFunction<typeof fetchForumTopicResult>;
const fetchPosts = fetchForumTopicPosts as jest.MockedFunction<typeof fetchForumTopicPosts>;
const fetchPostsResult = fetchForumTopicPostsResult as jest.MockedFunction<
  typeof fetchForumTopicPostsResult
>;
const fetchPoll = fetchForumTopicPoll as jest.MockedFunction<typeof fetchForumTopicPoll>;
const fetchWatch = fetchForumTopicWatch as jest.MockedFunction<typeof fetchForumTopicWatch>;
const fetchPhotos = fetchPhotoCategories as jest.MockedFunction<typeof fetchPhotoCategories>;
const fetchDay = fetchOnThisDay as jest.MockedFunction<typeof fetchOnThisDay>;
const fetchQuote = fetchRandomQuote as jest.MockedFunction<typeof fetchRandomQuote>;
const fetchQuoteDetail = fetchQuoteById as jest.MockedFunction<typeof fetchQuoteById>;
const fetchLive = fetchLiveActivity as jest.MockedFunction<typeof fetchLiveActivity>;
const fetchInboxMock = fetchInbox as jest.MockedFunction<typeof fetchInbox>;
const fetchConversationMock = fetchConversation as jest.MockedFunction<typeof fetchConversation>;
const fetchConversationResultMock = fetchConversationResult as jest.MockedFunction<
  typeof fetchConversationResult
>;

const conversationId = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';

function tabRootHeaders(navigation: ReturnType<typeof fakeNavigation>) {
  return [
    <NewsIndexHeaderRight key="news" navigation={navigation as never} />,
    <SearchIdentityHeaderRight
      key="photos"
      navigation={navigation}
      onSearch={() => navigation.navigate('Search')}
    />,
    <SearchIdentityHeaderRight
      key="archive"
      navigation={navigation}
      onSearch={() => navigation.navigate('Search')}
    />,
    <ForumIndexHeaderRight key="forum" navigation={navigation as never} />,
  ];
}

describe('TabRootMasthead placement', () => {
  beforeEach(() => {
    mockSession.isSignedIn = false;
    mockSession.accessToken = null;
    fetchNews.mockResolvedValue(pagedResponse([newsItemFixture({ id: 1003 })], 1, 1));
    fetchYearRange.mockResolvedValue({ minYear: 2006, maxYear: 2026 });
    fetchDetail.mockResolvedValue(newsDetailFixture({ id: 1003, title: 'QueenZone modernisation begins' }));
    fetchForum.mockResolvedValue([]);
    fetchForumCats.mockResolvedValue(
      pagedResponse(
        [
          {
            id: 1,
            name: 'General',
            description: 'Talk about Queen.',
            postCount: 12,
            lastActivityAt: '2026-01-01T00:00:00.000Z',
            latestThreadTitle: 'Ranking every studio album',
            detailPath: '/forum/1/general',
          },
        ],
        1,
        1,
      ),
    );
    fetchForumIndexStats.mockResolvedValue({ boardCount: 1, threadCount: 12, postCount: 12 });
    const topic = {
      id: 1002,
      title: 'Ranking every studio album',
      forumId: 1,
      forumName: 'The Music',
      categoryPath: '/forum/1/the-music',
      detailPath: '/forum/topic/1002/ranking-every-studio-album',
      postCount: 1,
      hasPoll: false,
      isLocked: false,
    };
    const posts = pagedResponse(
      [
        {
          id: 1,
          body: '<p>Hello</p>',
          postedAt: '2024-06-01T10:00:00.000Z',
          authorUsername: 'brightonrock',
          signature: null,
          authorMemberSince: null,
          authorMemberId: null,
          editedAt: null,
          editCount: 0,
          attachments: [],
        },
      ],
      1,
      1,
    );
    fetchTopic.mockResolvedValue(topic);
    fetchTopicResult.mockResolvedValue({ data: topic, source: 'network', cachedAt: '2024-06-01T10:00:00.000Z' });
    fetchPosts.mockResolvedValue(posts);
    fetchPostsResult.mockResolvedValue({ data: posts, source: 'network', cachedAt: '2024-06-01T10:00:00.000Z' });
    fetchPoll.mockResolvedValue({} as never);
    fetchWatch.mockResolvedValue({ watching: false });
    fetchPhotos.mockResolvedValue(pagedResponse([], 1, 0));
    fetchDay.mockResolvedValue(null);
    fetchQuote.mockResolvedValue(null);
    fetchQuoteDetail.mockResolvedValue({
      id: 9,
      text: 'A kind of magic',
      whoSaid: 'Freddie Mercury',
      context: 'Live Aid, 1985',
    });
    fetchLive.mockResolvedValue({ newForumRepliesToday: 0 });
    fetchInboxMock.mockResolvedValue(pagedResponse([], 1, 0));
    const conversation = conversationDetailFixture({
      conversationId,
      otherParticipantId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
      otherParticipantDisplayName: 'Bob',
      messages: [
        conversationMessageFixture({
          createdAt: '2026-01-01T00:00:00.000Z',
        }),
      ],
      totalCount: 1,
      detailPath: `/messages/${conversationId}`,
    });
    fetchConversationMock.mockResolvedValue(conversation);
    fetchConversationResultMock.mockResolvedValue({
      data: conversation,
      source: 'network',
      cachedAt: '2026-01-01T00:00:00.000Z',
    });
  });

  afterEach(async () => {
    await flushVirtualizedList();
  });

  it('keeps the full masthead on Home and peels it from the other tab roots', async () => {
    const navigation = fakeNavigation();

    const home = await renderWithProviders(
      <HomeScreen navigation={navigation as never} route={{ key: 'home', name: 'Home' } as never} />,
      { navigation: false },
    );
    expect(screen.getByTestId(testIds.tabMasthead)).toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.tabIdentityHeader)).not.toBeOnTheScreen();
    await home.unmount();

    const news = await renderWithProviders(
      <NewsIndexScreen
        navigation={navigation as never}
        route={{ key: 'news', name: 'NewsIndex' } as never}
      />,
      { navigation: false },
    );
    await waitFor(() => expect(screen.getByTestId(testIds.newsScreen)).toBeOnTheScreen());
    expect(screen.queryByTestId(testIds.tabMasthead)).not.toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.tabIdentityHeader)).not.toBeOnTheScreen();
    await news.unmount();

    const photos = await renderWithProviders(
      <PhotosScreen
        navigation={navigation as never}
        route={{ key: 'photos', name: 'PhotoIndex' } as never}
      />,
      { navigation: false },
    );
    await waitFor(() => expect(screen.getByTestId(testIds.photosScreen)).toBeOnTheScreen());
    expect(screen.queryByTestId(testIds.tabMasthead)).not.toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.tabIdentityHeader)).not.toBeOnTheScreen();
    await photos.unmount();

    const archive = await renderWithProviders(
      <ArchiveHubScreen
        navigation={navigation as never}
        route={{ key: 'archive', name: 'ArchiveHub' } as never}
      />,
      { navigation: false },
    );
    expect(screen.getByTestId(testIds.archiveHubScreen)).toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.tabMasthead)).not.toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.tabIdentityHeader)).not.toBeOnTheScreen();
    await archive.unmount();

    await renderWithProviders(
      <ForumScreen
        navigation={navigation as never}
        route={{ key: 'forum', name: 'ForumIndex' } as never}
      />,
      { navigation: false },
    );
    await waitFor(() => expect(screen.getByTestId(testIds.forumScreen)).toBeOnTheScreen());
    expect(screen.queryByTestId(testIds.tabMasthead)).not.toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.tabIdentityHeader)).not.toBeOnTheScreen();
  });

  it('hangs identity on the four tab-root native headers', async () => {
    const navigation = fakeNavigation();
    const headers = tabRootHeaders(navigation);

    for (const header of headers) {
      const view = await renderWithProviders(header, { navigation: false });
      expect(screen.getByTestId(testIds.tabIdentityHeader)).toBeOnTheScreen();
      expect(screen.queryByTestId(testIds.tabMasthead)).not.toBeOnTheScreen();
      await view.unmount();
    }
  });

  it('opens search and Profile from Home masthead and the four tab-root headers', async () => {
    const user = userEvent.setup();
    const homeNav = fakeNavigation();
    const home = await renderWithProviders(
      <HomeScreen navigation={homeNav as never} route={{ key: 'home', name: 'Home' } as never} />,
      { navigation: false },
    );
    await user.press(screen.getByTestId(testIds.homeSearch));
    await user.press(screen.getByTestId(testIds.homeProfile));
    expect(homeNav.navigate).toHaveBeenCalledWith('Search');
    expect(homeNav.navigate).toHaveBeenCalledWith('Profile');
    expect(screen.queryByTestId(testIds.homeMessages)).not.toBeOnTheScreen();
    await home.unmount();

    const otherNav = fakeNavigation();
    const headers = tabRootHeaders(otherNav);

    for (const header of headers) {
      otherNav.navigate.mockClear();
      const view = await renderWithProviders(header, { navigation: false });
      await user.press(screen.getByLabelText('Search'));
      await user.press(screen.getByTestId(testIds.homeProfile));
      expect(otherNav.navigate).toHaveBeenCalledWith('Search');
      expect(otherNav.navigate).toHaveBeenCalledWith('HomeTab', { screen: 'Profile' });
      expect(screen.queryByTestId(testIds.homeMessages)).not.toBeOnTheScreen();
      await view.unmount();
    }
  });

  it('opens Inbox from the signed-in messages icon on Home and the four tab-root headers', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    const user = userEvent.setup();

    const homeNav = fakeNavigation();
    const home = await renderWithProviders(
      <HomeScreen navigation={homeNav as never} route={{ key: 'home', name: 'Home' } as never} />,
      { navigation: false },
    );
    await user.press(screen.getByTestId(testIds.homeMessages));
    expect(homeNav.navigate).toHaveBeenCalledWith('Inbox');
    await home.unmount();

    const otherNav = fakeNavigation();
    const headers = tabRootHeaders(otherNav);

    for (const header of headers) {
      otherNav.navigate.mockClear();
      const view = await renderWithProviders(header, { navigation: false });
      await user.press(screen.getByTestId(testIds.homeMessages));
      expect(otherNav.navigate).toHaveBeenCalledWith('HomeTab', nestedTabParams('Inbox'));
      await view.unmount();
    }
  });

  it('is absent on Story, Quote, Thread, and Conversation', async () => {
    const navigation = fakeNavigation();

    const story = await renderWithProviders(
      <NewsStoryScreen
        navigation={navigation as never}
        route={{ key: 'story', name: 'Story', params: { id: 1003 } } as never}
      />,
      { navigation: false },
    );
    await waitFor(() => expect(screen.getByTestId(testIds.newsStoryScreen)).toBeOnTheScreen());
    expect(screen.queryByTestId(testIds.tabMasthead)).not.toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.tabIdentityHeader)).not.toBeOnTheScreen();
    await story.unmount();

    const quote = await renderWithProviders(
      <QuoteScreen
        navigation={navigation as never}
        route={{ key: 'quote', name: 'Quote', params: { id: 9 } } as never}
      />,
      { navigation: false },
    );
    await waitFor(() => expect(screen.getByTestId(testIds.quoteScreen)).toBeOnTheScreen());
    expect(screen.queryByTestId(testIds.tabMasthead)).not.toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.tabIdentityHeader)).not.toBeOnTheScreen();
    await quote.unmount();

    const thread = await renderWithProviders(
      <ThreadScreen
        navigation={navigation as never}
        route={
          {
            key: 'thread',
            name: 'Thread',
            params: { id: 1002, title: 'Ranking every studio album' },
          } as never
        }
      />,
      { navigation: false },
    );
    await waitFor(() => expect(screen.getByTestId(testIds.forumThreadScreen)).toBeOnTheScreen());
    expect(screen.queryByTestId(testIds.tabMasthead)).not.toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.tabIdentityHeader)).not.toBeOnTheScreen();
    await thread.unmount();

    mockSession.isSignedIn = true;
    mockSession.accessToken = 'tok';
    await renderWithProviders(
      <ConversationScreen
        navigation={navigation as never}
        route={{ key: 'conversation', name: 'Conversation', params: { id: conversationId } } as never}
      />,
    );
    await waitFor(() => expect(screen.getByText('Hello')).toBeOnTheScreen());
    expect(screen.queryByTestId(testIds.tabMasthead)).not.toBeOnTheScreen();
    expect(screen.queryByTestId(testIds.tabIdentityHeader)).not.toBeOnTheScreen();
  });
});
