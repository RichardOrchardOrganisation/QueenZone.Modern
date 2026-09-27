import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchQuizDetail, submitQuizAttempt } from '../../api';
import type { QuizDetail, QuizResult } from '../../api/types';
import { createMockSession } from '../../test/mockSession';
import { fakeNavigation, renderWithProviders } from '../../test/render';
import { QuizPlayScreen } from './QuizPlayScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchQuizDetail: jest.fn(),
    submitQuizAttempt: jest.fn(),
  };
});

const mockSession = createMockSession();

jest.mock('../../session/SessionContext', () => ({
  useSession: () => mockSession,
}));

const loadQuiz = fetchQuizDetail as jest.MockedFunction<typeof fetchQuizDetail>;
const submitAttempt = submitQuizAttempt as jest.MockedFunction<typeof submitQuizAttempt>;

function quizFixture(): QuizDetail {
  return {
    id: 'aaefa33a-dd8f-4330-b844-83887246da65',
    title: 'Queen Frontman Trivia',
    description: "A short quiz about Queen's legendary frontman.",
    questions: [
      {
        id: 'q1',
        text: 'Who was the lead singer of Queen?',
        points: 2,
        options: [
          { id: 'q1-a', text: 'Freddie Mercury' },
          { id: 'q1-b', text: 'Brian May' },
        ],
      },
      {
        id: 'q2',
        text: 'What year was Bohemian Rhapsody released?',
        points: 1,
        options: [
          { id: 'q2-a', text: '1975' },
          { id: 'q2-b', text: '1980' },
        ],
      },
    ],
  };
}

function resultFixture(): QuizResult {
  return {
    quizId: 'aaefa33a-dd8f-4330-b844-83887246da65',
    quizTitle: 'Queen Frontman Trivia',
    score: 2,
    maxScore: 3,
    correctCount: 1,
    questionCount: 2,
    recorded: true,
    answers: [
      {
        questionId: 'q1',
        questionText: 'Who was the lead singer of Queen?',
        selectedOptionId: 'q1-a',
        selectedOptionText: 'Freddie Mercury',
        correctOptionId: 'q1-a',
        correctOptionText: 'Freddie Mercury',
        isCorrect: true,
        pointsAwarded: 2,
      },
      {
        questionId: 'q2',
        questionText: 'What year was Bohemian Rhapsody released?',
        selectedOptionId: 'q2-b',
        selectedOptionText: '1980',
        correctOptionId: 'q2-a',
        correctOptionText: '1975',
        isCorrect: false,
        pointsAwarded: 0,
      },
    ],
  };
}

function renderPlay() {
  const navigation = fakeNavigation();
  return {
    navigation,
    ...renderWithProviders(
      <QuizPlayScreen
        navigation={navigation as never}
        route={{ key: 'quiz-play', name: 'QuizPlay', params: { id: 'aaefa33a-dd8f-4330-b844-83887246da65' } } as never}
      />,
      { navigation: false },
    ),
  };
}

describe('QuizPlayScreen', () => {
  beforeEach(() => {
    loadQuiz.mockReset();
    submitAttempt.mockReset();
    mockSession.isSignedIn = false;
    mockSession.accessToken = null;
  });

  it('signed-in member selects answers, submits, and sees the scored breakdown', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'token-123';
    loadQuiz.mockResolvedValue(quizFixture());
    submitAttempt.mockResolvedValue(resultFixture());
    const user = userEvent.setup();
    renderPlay();

    await waitFor(() => expect(screen.getByText('Queen Frontman Trivia')).toBeOnTheScreen());
    await user.press(screen.getByLabelText('Freddie Mercury'));
    await user.press(screen.getByLabelText('1980'));

    const submit = screen.getByRole('button', { name: 'Submit answers' });
    await user.press(submit);

    await waitFor(() => expect(screen.getByText('2 / 3 points')).toBeOnTheScreen());
    expect(submitAttempt).toHaveBeenCalledWith(
      'aaefa33a-dd8f-4330-b844-83887246da65',
      [
        { questionId: 'q1', selectedOptionId: 'q1-a' },
        { questionId: 'q2', selectedOptionId: 'q2-b' },
      ],
      'token-123',
    );
    expect(screen.getByText('1 / 2 correct')).toBeOnTheScreen();
    expect(screen.getByText('Correct answer: 1975')).toBeOnTheScreen();
  });

  it('prompts a signed-out visitor to sign in instead of submitting', async () => {
    loadQuiz.mockResolvedValue(quizFixture());
    renderPlay();

    await waitFor(() => expect(screen.getByText('Queen Frontman Trivia')).toBeOnTheScreen());
    expect(screen.getByRole('button', { name: 'Sign in to submit' })).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Submit answers' })).toBeNull();
    expect(submitAttempt).not.toHaveBeenCalled();
  });

  it('disables submit until every question is answered', async () => {
    mockSession.isSignedIn = true;
    mockSession.accessToken = 'token-123';
    loadQuiz.mockResolvedValue(quizFixture());
    renderPlay();

    await waitFor(() => expect(screen.getByText('Queen Frontman Trivia')).toBeOnTheScreen());
    expect(screen.getByRole('button', { name: 'Submit answers' })).toBeDisabled();
  });
});
