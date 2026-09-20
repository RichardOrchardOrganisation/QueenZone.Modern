import { screen, waitFor } from '@testing-library/react-native';
import { fetchQuizSprintDaily } from '../../api';
import type { QuizSprintDailyBoard } from '../../api/types';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { QuizSprintLeaderboardScreen } from './QuizSprintLeaderboardScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return { ...actual, fetchQuizSprintDaily: jest.fn() };
});

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

const daily = fetchQuizSprintDaily as jest.MockedFunction<typeof fetchQuizSprintDaily>;

function renderBoard() {
  renderWithProviders(
    <QuizSprintLeaderboardScreen
      navigation={fakeNavigation() as never}
      route={{ key: 'board', name: 'QuizSprintLeaderboard' } as never}
    />,
    { navigation: false },
  );
}

describe('QuizSprintLeaderboardScreen', () => {
  beforeEach(() => {
    daily.mockReset();
    mockSession.accessToken = null;
  });

  it("lists today's best runs and how many members played", async () => {
    const data: QuizSprintDailyBoard = {
      top: [
        { rank: 1, displayName: 'brightonrock', score: 27, bestStreak: 12 },
        { rank: 2, displayName: 'Killer Queen', score: 20, bestStreak: 6 },
      ],
      viewer: null,
      playersToday: 2,
    };
    daily.mockResolvedValue(data);
    renderBoard();

    await waitFor(() => expect(screen.getByText('brightonrock')).toBeOnTheScreen());
    expect(screen.getByText('2 members ranked today.')).toBeOnTheScreen();
    expect(daily).toHaveBeenCalledWith(expect.anything(), null);
  });

  it('shows the viewer as You, even outside the top page', async () => {
    daily.mockResolvedValue({
      top: [{ rank: 1, displayName: 'brightonrock', score: 27, bestStreak: 12 }],
      viewer: { rank: 9, displayName: 'me', score: 3, bestStreak: 2 },
      playersToday: 9,
    });
    renderBoard();

    await waitFor(() => expect(screen.getByText('You')).toBeOnTheScreen());
  });

  it('shows an empty state when nobody has played yet', async () => {
    daily.mockResolvedValue({ top: [], viewer: null, playersToday: 0 });
    renderBoard();

    await waitFor(() => expect(screen.getByText('No scores yet today. Be the first on the board.')).toBeOnTheScreen());
    expect(screen.getByText('0 members ranked today.')).toBeOnTheScreen();
  });

  it('offers a retry when the board fails to load', async () => {
    daily.mockRejectedValue(new Error('offline'));
    renderBoard();

    await waitFor(() => expect(screen.getByText(/offline|Something went wrong/)).toBeOnTheScreen());
  });
});
