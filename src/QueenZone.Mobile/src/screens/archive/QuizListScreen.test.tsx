import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchQuizzesPage } from '../../api';
import type { QuizListItem } from '../../api/types';
import { pagedResponse } from '../../test/fixtures';
import { fakeNavigation, flushVirtualizedList, renderWithProviders } from '../../test/render';
import { QuizListScreen } from './QuizListScreen';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return {
    ...actual,
    fetchQuizzesPage: jest.fn(),
  };
});

const fetchPage = fetchQuizzesPage as jest.MockedFunction<typeof fetchQuizzesPage>;

function quizFixture(overrides: Partial<QuizListItem> = {}): QuizListItem {
  return {
    id: 'aaefa33a-dd8f-4330-b844-83887246da65',
    title: 'Queen Frontman Trivia',
    description: "A short quiz about Queen's legendary frontman.",
    questionCount: 2,
    ...overrides,
  };
}

function renderQuizList() {
  const navigation = fakeNavigation();
  return {
    navigation,
    ...renderWithProviders(
      <QuizListScreen navigation={navigation as never} route={{ key: 'quiz-list', name: 'QuizList' } as never} />,
      { navigation: false },
    ),
  };
}

describe('QuizListScreen', () => {
  beforeEach(() => {
    fetchPage.mockReset();
  });

  afterEach(async () => {
    await flushVirtualizedList();
  });

  it('lists published quizzes and navigates to a quiz on press', async () => {
    fetchPage.mockResolvedValue(pagedResponse([quizFixture()]));
    const { navigation } = renderQuizList();

    await waitFor(() => expect(screen.getByText('Queen Frontman Trivia')).toBeOnTheScreen());
    expect(fetchPage).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 20 }));
    expect(screen.getByText('2 questions')).toBeOnTheScreen();

    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'Open quiz Queen Frontman Trivia' }));
    expect(navigation.navigate).toHaveBeenCalledWith('QuizPlay', {
      id: 'aaefa33a-dd8f-4330-b844-83887246da65',
    });
  });

  it('navigates to the leaderboard', async () => {
    fetchPage.mockResolvedValue(pagedResponse([quizFixture()]));
    const { navigation } = renderQuizList();

    await waitFor(() => expect(screen.getByText('Queen Frontman Trivia')).toBeOnTheScreen());
    const user = userEvent.setup();
    await user.press(screen.getByRole('button', { name: 'View the leaderboard' }));
    expect(navigation.navigate).toHaveBeenCalledWith('QuizLeaderboard');
  });

  it('shows an empty state when no quizzes are published', async () => {
    fetchPage.mockResolvedValue(pagedResponse([]));
    renderQuizList();

    await waitFor(() =>
      expect(screen.getByText('No quizzes have been published yet.')).toBeOnTheScreen(),
    );
  });
});
