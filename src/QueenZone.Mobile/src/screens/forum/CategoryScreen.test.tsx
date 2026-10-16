import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchForumCategory, fetchForumCategoryTopics } from '../../api';
import { ApiError } from '../../api/client';
import type { ForumCategoryListItem, ForumTopicListItem } from '../../api/types';
import { pagedResponse } from '../../test/fixtures';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, flushVirtualizedList, renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { CategoryScreen } from './CategoryScreen';

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchForumCategory: jest.fn(),
    fetchForumCategoryTopics: jest.fn(),
  };
});

const fetchCategory = fetchForumCategory as jest.MockedFunction<typeof fetchForumCategory>;
const fetchTopics = fetchForumCategoryTopics as jest.MockedFunction<typeof fetchForumCategoryTopics>;

function categoryFixture(overrides: Partial<ForumCategoryListItem> = {}): ForumCategoryListItem {
  return {
    id: 1,
    name: 'General',
    description: 'Talk about Queen.',
    postCount: 12,
    lastActivityAt: '2026-01-01T00:00:00.000Z',
    latestThreadTitle: 'Ranking every studio album',
    detailPath: '/forum/1/general',
    ...overrides,
  };
}

function topicFixture(overrides: Partial<ForumTopicListItem> = {}): ForumTopicListItem {
  return {
    id: 1002,
    title: 'Ranking every studio album',
    lastActivityAt: '2026-01-01T00:00:00.000Z',
    authorUsername: 'Brian',
    replyCount: 12,
    lastPostUsername: 'Roger',
    isSticky: false,
    detailPath: '/forum/topic/1002/ranking-every-studio-album',
    ...overrides,
  };
}

async function renderCategory(navigation = fakeNavigation()) {
  return {
    navigation,
    ...(await renderWithProviders(
      <CategoryScreen
        navigation={navigation as never}
        route={{ key: 'category', name: 'Category', params: { id: 1, name: 'General' } } as never}
      />,
    )),
  };
}

describe('CategoryScreen', () => {
  beforeEach(() => {
    mockSession.isSignedIn = false;
    mockSession.accessToken = null;
    fetchCategory.mockReset();
    fetchTopics.mockReset();
  });

  afterEach(async () => {
    await flushVirtualizedList();
  });

  it('loads topics and opens a thread', async () => {
    fetchCategory.mockResolvedValue(categoryFixture());
    fetchTopics.mockResolvedValue(pagedResponse([topicFixture()]));

    const { navigation } = await renderCategory();
    await waitFor(() => expect(screen.getByTestId(testIds.forumCategoryScreen)).toBeOnTheScreen());
    expect(screen.getByRole('button', { name: 'Open thread Ranking every studio album' })).toBeOnTheScreen();
    expect(fetchCategory).toHaveBeenCalledWith(1, expect.any(AbortSignal));
    expect(fetchTopics).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ page: 1, pageSize: 25 }),
    );

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Open thread Ranking every studio album' }));
    expect(navigation.navigate).toHaveBeenCalledWith('Thread', {
      id: 1002,
      title: 'Ranking every studio album',
    });
  });

  it('shows an error and retries', async () => {
    fetchCategory.mockResolvedValue(categoryFixture());
    fetchTopics
      .mockRejectedValueOnce(new ApiError(500, 'The server had a problem.'))
      .mockResolvedValueOnce(pagedResponse([topicFixture()]));

    await renderCategory();
    await waitFor(() => expect(screen.getByText('The server had a problem.')).toBeOnTheScreen());

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Open thread Ranking every studio album' })).toBeOnTheScreen(),
    );
    expect(fetchTopics).toHaveBeenCalledTimes(2);
  });

  it('shows empty copy when the board has no topics', async () => {
    fetchCategory.mockResolvedValue(categoryFixture());
    fetchTopics.mockResolvedValue(pagedResponse([], 1, 0));

    await renderCategory();
    await waitFor(() =>
      expect(screen.getByText('No topics are available in this board yet.')).toBeOnTheScreen(),
    );
  });
});
