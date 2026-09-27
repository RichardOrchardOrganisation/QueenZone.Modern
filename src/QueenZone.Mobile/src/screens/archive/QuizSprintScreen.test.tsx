import { screen, userEvent, waitFor } from '@testing-library/react-native';
import {
  ApiError,
  checkQuizSprintAnswer,
  claimQuizSprintRun,
  fetchQuizSprintDaily,
  finishQuizSprint,
  startQuizSprint,
} from '../../api';
import type { QuizSprintDailyBoard, QuizSprintResult, QuizSprintRound } from '../../api/types';
import { openSignIn } from '../../session/signInNavigation';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { QuizSprintScreen } from './QuizSprintScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    startQuizSprint: jest.fn(),
    claimQuizSprintRun: jest.fn(),
    checkQuizSprintAnswer: jest.fn(),
    finishQuizSprint: jest.fn(),
    fetchQuizSprintDaily: jest.fn(),
  };
});

jest.mock('../../session/signInNavigation', () => ({ openSignIn: jest.fn() }));

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

const start = startQuizSprint as jest.MockedFunction<typeof startQuizSprint>;
const check = checkQuizSprintAnswer as jest.MockedFunction<typeof checkQuizSprintAnswer>;
const finish = finishQuizSprint as jest.MockedFunction<typeof finishQuizSprint>;
const daily = fetchQuizSprintDaily as jest.MockedFunction<typeof fetchQuizSprintDaily>;
const claimRun = claimQuizSprintRun as jest.MockedFunction<typeof claimQuizSprintRun>;

function round(): QuizSprintRound {
  const now = Date.now();
  return {
    ticket: 'ticket-1',
    serverNowUnixMilliseconds: now,
    expiresAtUnixMilliseconds: now + 60_000,
    durationSeconds: 60,
    questions: [
      {
        id: 'q1',
        text: 'Who was the lead singer of Queen?',
        options: [
          { id: 'q1-a', text: 'Freddie Mercury' },
          { id: 'q1-b', text: 'Brian May' },
        ],
      },
    ],
  };
}

function board(): QuizSprintDailyBoard {
  return { top: [{ rank: 1, displayName: 'brightonrock', score: 27, bestStreak: 12 }], viewer: null, playersToday: 1 };
}

function result(overrides: Partial<QuizSprintResult> = {}): QuizSprintResult {
  return { attempted: 1, correct: 1, points: 1, bestStreak: 1, recorded: false, rank: null, answers: [], ...overrides };
}

function renderScreen(params?: { claim?: string }) {
  const navigation = fakeNavigation();
  renderWithProviders(
    <QuizSprintScreen
      navigation={navigation as never}
      route={{ key: 'sprint', name: 'QuizSprint', params } as never}
    />,
    { navigation: false },
  );
  return navigation;
}

describe('QuizSprintScreen', () => {
  beforeEach(() => {
    jest.resetAllMocks();
    mockSession.isSignedIn = false;
    mockSession.accessToken = null;
    daily.mockResolvedValue(board());
  });

  it('shows the intro with best score today and a sign-in prompt for guests', async () => {
    const navigation = renderScreen();
    const user = userEvent.setup();

    await waitFor(() => expect(screen.getByText(/BEST TODAY · BRIGHTONROCK · 27/)).toBeOnTheScreen());
    expect(screen.getByText('Sixty seconds on the clock.')).toBeOnTheScreen();

    await user.press(screen.getByRole('button', { name: 'Sign in to be ranked' }));
    expect(openSignIn).toHaveBeenCalledWith(navigation, { tab: 'ArchiveTab', screen: 'QuizSprint' });

    await user.press(screen.getByRole('button', { name: 'View the leaderboard' }));
    expect(navigation.navigate).toHaveBeenCalledWith('QuizSprintLeaderboard');
  });

  it('plays a round: answers with feedback, then shows the scored results', async () => {
    start.mockResolvedValue(round());
    check.mockResolvedValue({ isCorrect: true, correctOptionId: 'q1-a' });
    finish.mockResolvedValue(result({ points: 1, recorded: false }));
    const user = userEvent.setup();
    renderScreen();

    await user.press(screen.getByRole('button', { name: 'Begin the sprint' }));
    await waitFor(() => expect(screen.getByText('Who was the lead singer of Queen?')).toBeOnTheScreen());
    expect(screen.getByText('NO STREAK')).toBeOnTheScreen();

    await user.press(screen.getByRole('button', { name: 'A. Freddie Mercury' }));
    await waitFor(() => expect(check).toHaveBeenCalledWith('ticket-1', 'q1', 'q1-a'));

    await waitFor(() => expect(finish).toHaveBeenCalled(), { timeout: 3000 });
    expect(finish).toHaveBeenCalledWith('ticket-1', [{ questionId: 'q1', selectedOptionId: 'q1-a' }], null);
    await waitFor(() => expect(screen.getByText('The clock wins this one. Try again — the questions reshuffle.')).toBeOnTheScreen());
    expect(screen.getByText('Your score is only added to the leaderboard if you are signed in.')).toBeOnTheScreen();
    expect(screen.getByText('DAILY LEADERBOARD')).toBeOnTheScreen();
  });

  it('lets a guest sign in from their results and carries the claim token through', async () => {
    start.mockResolvedValue(round());
    check.mockResolvedValue({ isCorrect: true, correctOptionId: 'q1-a' });
    finish.mockResolvedValue(result({ points: 1, recorded: false, claimToken: 'claim-token' }));
    const user = userEvent.setup();
    const navigation = renderScreen();

    await user.press(screen.getByRole('button', { name: 'Begin the sprint' }));
    await waitFor(() => expect(screen.getByText('Who was the lead singer of Queen?')).toBeOnTheScreen());
    await user.press(screen.getByRole('button', { name: 'A. Freddie Mercury' }));

    await waitFor(() => expect(screen.getByRole('button', { name: 'Sign in to save this score' })).toBeOnTheScreen(), {
      timeout: 3000,
    });
    await user.press(screen.getByRole('button', { name: 'Sign in to save this score' }));
    expect(openSignIn).toHaveBeenCalledWith(navigation, {
      tab: 'ArchiveTab',
      screen: 'QuizSprint',
      params: { claim: 'claim-token' },
    });
  });

  it('adds the guest run once signed in and clears the claim param', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'token';
    claimRun.mockResolvedValue({ status: 'claimed', points: 12, rank: 4 });
    const navigation = renderScreen({ claim: 'claim-token' });

    await waitFor(() =>
      expect(screen.getByText('Your score of 12 was added to the leaderboard (rank #4 today).')).toBeOnTheScreen(),
    );
    expect(claimRun).toHaveBeenCalledTimes(1);
    expect(claimRun).toHaveBeenCalledWith('claim-token', 'token');
    await waitFor(() => expect(navigation.setParams).toHaveBeenCalledWith({ claim: undefined }));
  });

  it('reports an already-saved or expired claim without crashing', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'token';
    claimRun.mockResolvedValueOnce({ status: 'already_claimed', points: 12, rank: null });
    renderScreen({ claim: 'claim-token' });
    await waitFor(() => expect(screen.getByText('That score is already on the leaderboard.')).toBeOnTheScreen());
  });

  it('explains when the claim window has passed', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'token';
    claimRun.mockRejectedValueOnce(ApiError.http(410, 'Claim expired'));
    renderScreen({ claim: 'old-token' });
    await waitFor(() => expect(screen.getByText(/finished too long ago/)).toBeOnTheScreen());
  });

  it('does not claim while still signed out', async () => {
    renderScreen({ claim: 'claim-token' });
    await waitFor(() => expect(daily).toHaveBeenCalled());
    expect(claimRun).not.toHaveBeenCalled();
  });

  it('shows the rank for a signed-in run and lets the player run again', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'token';
    start.mockResolvedValue(round());
    check.mockResolvedValue({ isCorrect: false, correctOptionId: 'q1-a' });
    finish.mockResolvedValue(result({ points: 0, correct: 0, recorded: true, rank: 3 }));
    const user = userEvent.setup();
    renderScreen();

    await user.press(screen.getByRole('button', { name: 'Begin the sprint' }));
    await waitFor(() => expect(screen.getByText('Who was the lead singer of Queen?')).toBeOnTheScreen());
    await user.press(screen.getByRole('button', { name: 'B. Brian May' }));

    await waitFor(() => expect(screen.getByText(/rank #3/)).toBeOnTheScreen(), { timeout: 3000 });
    expect(finish).toHaveBeenCalledWith('ticket-1', expect.any(Array), 'token');

    await user.press(screen.getByRole('button', { name: 'Run it again' }));
    expect(screen.getByRole('button', { name: 'Begin the sprint' })).toBeOnTheScreen();
  });

  it('returns to the intro with an error when the round cannot start', async () => {
    start.mockRejectedValue(new Error('No published quiz questions are available yet.'));
    const user = userEvent.setup();
    renderScreen();

    await user.press(screen.getByRole('button', { name: 'Begin the sprint' }));

    await waitFor(() => expect(screen.getByText('No published quiz questions are available yet.')).toBeOnTheScreen());
  });
});
