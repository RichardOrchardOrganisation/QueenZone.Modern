import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchQuizSprintLeaderboard } from '../../api';
import type { QuizSprintBoard } from '../../api/types';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { QuizSprintLeaderboardScreen } from './QuizSprintLeaderboardScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return { ...actual, fetchQuizSprintLeaderboard: jest.fn() };
});

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

const load = fetchQuizSprintLeaderboard as jest.MockedFunction<typeof fetchQuizSprintLeaderboard>;

function board(overrides: Partial<QuizSprintBoard> = {}): QuizSprintBoard {
  return {
    scope: 'daily',
    top: [
      { rank: 1, displayName: 'brightonrock', score: 27, bestStreak: 12 },
      { rank: 2, displayName: 'Killer Queen', score: 20, bestStreak: 6 },
    ],
    viewer: null,
    players: 2,
    ...overrides,
  };
}

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
    load.mockReset();
    mockSession.accessToken = null;
  });

  it("shows today's board by default with the member count", async () => {
    load.mockResolvedValue(board());
    renderBoard();

    await waitFor(() => expect(screen.getByText('brightonrock')).toBeOnTheScreen());
    expect(screen.getByText("Today's leaderboard")).toBeOnTheScreen();
    expect(screen.getByText('2 members ranked today.')).toBeOnTheScreen();
    expect(load).toHaveBeenCalledWith('daily', expect.anything(), null);
  });

  it('switches to the all-time board', async () => {
    load.mockResolvedValueOnce(board());
    const user = userEvent.setup();
    renderBoard();
    await waitFor(() => expect(screen.getByText('brightonrock')).toBeOnTheScreen());

    load.mockResolvedValueOnce(
      board({ scope: 'all', players: 40, top: [{ rank: 1, displayName: 'legend', score: 41, bestStreak: 20 }] }),
    );
    await user.press(screen.getByRole('button', { name: 'All time' }));

    await waitFor(() => expect(screen.getByText('legend')).toBeOnTheScreen());
    expect(screen.getByText('All-time leaderboard')).toBeOnTheScreen();
    expect(screen.getByText('40 members ranked.')).toBeOnTheScreen();
    expect(load).toHaveBeenLastCalledWith('all', expect.anything(), null);
  });

  it('shows the viewer as You, even outside the top page', async () => {
    load.mockResolvedValue(
      board({ viewer: { rank: 9, displayName: 'me', score: 3, bestStreak: 2 }, players: 9 }),
    );
    renderBoard();

    await waitFor(() => expect(screen.getByText('You')).toBeOnTheScreen());
  });

  it('shows an empty state when nobody has played yet', async () => {
    load.mockResolvedValue(board({ top: [], players: 0 }));
    renderBoard();

    await waitFor(() => expect(screen.getByText('No scores yet today. Be the first on the board.')).toBeOnTheScreen());
    expect(screen.getByText('0 members ranked today.')).toBeOnTheScreen();
  });

  it('offers a retry when the board fails to load', async () => {
    load.mockRejectedValue(new Error('offline'));
    renderBoard();

    await waitFor(() => expect(screen.getByText(/offline|Something went wrong/)).toBeOnTheScreen());
  });
});
