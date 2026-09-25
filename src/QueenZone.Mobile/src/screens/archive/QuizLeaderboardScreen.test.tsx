import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchQuizLeaderboard } from '../../api';
import type { QuizLeaderboard } from '../../api/types';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, flushVirtualizedList, renderWithProviders } from '../../test/render';
import { QuizLeaderboardScreen } from './QuizLeaderboardScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchQuizLeaderboard: jest.fn(),
  };
});

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

const fetchLeaderboard = fetchQuizLeaderboard as jest.MockedFunction<typeof fetchQuizLeaderboard>;

function leaderboardFixture(overrides: Partial<QuizLeaderboard> = {}): QuizLeaderboard {
  return {
    top: [
      { rank: 1, displayName: 'brightonrock', score: 15, attemptCount: 2 },
      { rank: 2, displayName: 'Killer Queen', score: 8, attemptCount: 1 },
    ],
    viewer: null,
    totalMembers: 2,
    ...overrides,
  };
}

function renderLeaderboard() {
  const navigation = fakeNavigation();
  return renderWithProviders(
    <QuizLeaderboardScreen
      navigation={navigation as never}
      route={{ key: 'quiz-leaderboard', name: 'QuizLeaderboard' } as never}
    />,
    { navigation: false },
  );
}

describe('QuizLeaderboardScreen', () => {
  beforeEach(() => {
    fetchLeaderboard.mockReset();
    mockSession.accessToken = null;
  });

  afterEach(async () => {
    await flushVirtualizedList();
  });

  it('shows the weekly leaderboard by default', async () => {
    fetchLeaderboard.mockResolvedValue(leaderboardFixture());
    renderLeaderboard();

    await waitFor(() => expect(screen.getByText('brightonrock')).toBeOnTheScreen());
    expect(fetchLeaderboard).toHaveBeenCalledWith('week', expect.anything(), null);
    expect(screen.getByText('2 members ranked.')).toBeOnTheScreen();
  });

  it('switches to all-time when that tab is pressed', async () => {
    fetchLeaderboard.mockResolvedValue(leaderboardFixture());
    const user = userEvent.setup();
    renderLeaderboard();

    await waitFor(() => expect(screen.getByText('brightonrock')).toBeOnTheScreen());
    await user.press(screen.getByRole('button', { name: 'All time' }));

    await waitFor(() => expect(fetchLeaderboard).toHaveBeenLastCalledWith('all', expect.anything(), null));
  });

  it("shows the viewer's rank separately when outside the top page", async () => {
    fetchLeaderboard.mockResolvedValue(
      leaderboardFixture({ viewer: { rank: 9, displayName: 'brightonrock — you', score: 3, attemptCount: 1 } }),
    );
    renderLeaderboard();

    await waitFor(() =>
      expect(screen.getByText('Your rank: #9 · 3 points')).toBeOnTheScreen(),
    );
  });

  it('shows an empty state when no one has played yet', async () => {
    fetchLeaderboard.mockResolvedValue(leaderboardFixture({ top: [], totalMembers: 0 }));
    renderLeaderboard();

    await waitFor(() =>
      expect(screen.getByText('No quiz results yet this week.')).toBeOnTheScreen(),
    );
  });
});
