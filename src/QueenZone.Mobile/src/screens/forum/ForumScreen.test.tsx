import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { RefreshControl } from 'react-native';
import { fetchForumCategories, fetchForumStats } from '../../api';
import type { ForumCategoryListItem } from '../../api/types';
import { pagedResponse } from '../../test/fixtures';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, flushVirtualizedList, renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { formatForumCount } from './forumListMeta';
import { ForumScreen } from './ForumScreen';

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchForumCategories: jest.fn(),
    fetchForumStats: jest.fn(),
  };
});

const fetchCategories = fetchForumCategories as jest.MockedFunction<typeof fetchForumCategories>;
const fetchStats = fetchForumStats as jest.MockedFunction<typeof fetchForumStats>;

function categoryFixture(overrides: Partial<ForumCategoryListItem> = {}): ForumCategoryListItem {
  return {
    id: 1,
    name: 'General',
    description: 'Talk about Queen.',
    postCount: 10,
    lastActivityAt: '2026-01-01T00:00:00.000Z',
    latestThreadTitle: 'Ranking every studio album',
    detailPath: '/forum/1/general',
    ...overrides,
  };
}

function renderForum(navigation = fakeNavigation()) {
  return {
    navigation,
    ...renderWithProviders(
      <ForumScreen
        navigation={navigation as never}
        route={{ key: 'forum', name: 'ForumIndex' } as never}
      />,
    ),
  };
}

describe('ForumScreen', () => {
  beforeEach(() => {
    mockSession.isSignedIn = false;
    mockSession.accessToken = null;
    fetchCategories.mockReset();
    fetchStats.mockReset();
  });

  afterEach(async () => {
    await flushVirtualizedList();
  });

  it('shows Boards, Threads, and Posts without using API board/post totals', async () => {
    fetchCategories.mockResolvedValue({
      ...pagedResponse(
        [categoryFixture({ postCount: 10 }), categoryFixture({ id: 2, name: 'Live', postCount: 5 })],
        1,
        1,
      ),
      totalCount: 7,
    });
    fetchStats.mockResolvedValue({ boardCount: 99, threadCount: 12600, postCount: 99999 });

    renderForum();
    await waitFor(() => expect(screen.getByTestId(testIds.forumScreen)).toBeOnTheScreen());
    await waitFor(() => expect(screen.getByText('Threads')).toBeOnTheScreen());

    expect(screen.getAllByText('Boards').length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText('Posts')).toBeOnTheScreen();
    expect(screen.getByText(formatForumCount(7))).toBeOnTheScreen();
    expect(screen.getByText(formatForumCount(12600))).toBeOnTheScreen();
    expect(screen.getByText(formatForumCount(15))).toBeOnTheScreen();
    expect(screen.queryByText(formatForumCount(99))).toBeNull();
    expect(screen.queryByText(formatForumCount(99999))).toBeNull();
    expect(fetchStats).toHaveBeenCalledWith(expect.any(AbortSignal));
  });

  it('pull-to-refresh reloads both the boards and the forum stats', async () => {
    fetchCategories.mockResolvedValue(pagedResponse([categoryFixture()], 1, 1));
    fetchStats.mockResolvedValue({ boardCount: 1, threadCount: 3, postCount: 10 });

    renderForum();
    await waitFor(() => expect(screen.getByText('General')).toBeOnTheScreen());
    expect(fetchCategories).toHaveBeenCalledTimes(1);
    expect(fetchStats).toHaveBeenCalledTimes(1);

    await act(async () => {
      fireEvent(screen.UNSAFE_getByType(RefreshControl), 'refresh');
    });

    await waitFor(() => expect(fetchCategories).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(fetchStats).toHaveBeenCalledTimes(2));
  });
});
