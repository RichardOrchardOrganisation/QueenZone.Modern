import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { fetchQuizSprintDaily } from '../../api';
import { renderWithProviders } from '../../test/render';
import { testIds } from '../../test/testIds';
import { light, palette } from '../../theme';
import { HomeSprintCard, sprintCardLine } from './HomeSprintCard';

jest.mock('../../api', () => {
  const actual = jest.requireActual('../../api');
  return { ...actual, fetchQuizSprintDaily: jest.fn() };
});

const daily = fetchQuizSprintDaily as jest.MockedFunction<typeof fetchQuizSprintDaily>;

describe('HomeSprintCard', () => {
  beforeEach(() => daily.mockReset());

  it('challenges the player to beat the best score today and starts the sprint', async () => {
    daily.mockResolvedValue({
      top: [{ rank: 1, displayName: 'brightonrock', score: 27, bestStreak: 9 }],
      viewer: null,
      playersToday: 4,
    });
    const onPlay = jest.fn();
    const user = userEvent.setup();
    renderWithProviders(<HomeSprintCard onPlay={onPlay} />, { navigation: false });

    await waitFor(() => expect(screen.getByText(/Beat 27 to top today's board/)).toBeOnTheScreen());
    expect(screen.getByText('Quiz Sprint')).toBeOnTheScreen();
    await user.press(screen.getByRole('button', { name: 'Start the Quiz Sprint' }));
    expect(onPlay).toHaveBeenCalledTimes(1);
  });

  it('still renders when today has no scores or the board fails to load', async () => {
    daily.mockRejectedValue(new Error('offline'));
    renderWithProviders(<HomeSprintCard onPlay={jest.fn()} />, { navigation: false });

    expect(screen.getByText(/Be first on today's board/)).toBeOnTheScreen();
    await waitFor(() => expect(daily).toHaveBeenCalled());
  });

  it('words the line by best score', () => {
    expect(sprintCardLine(null)).toMatch(/Be first/);
    expect(sprintCardLine(0)).toMatch(/Be first/);
    expect(sprintCardLine(12)).toMatch(/Beat 12/);
  });

  it.each(['light', 'dark'] as const)(
    'keeps the black / gold / white stage card in %s mode',
    (preference) => {
      daily.mockRejectedValue(new Error('offline'));
      renderWithProviders(<HomeSprintCard onPlay={jest.fn()} />, {
        navigation: false,
        themePreference: preference,
      });

      const stage = screen.getByTestId(testIds.homeSprintCard);
      expect(stage).toHaveStyle({
        backgroundColor: palette.black,
        borderColor: palette.gold,
      });
      expect(stage).not.toHaveStyle({ backgroundColor: light.surfaceCard });
      expect(screen.getByText('DAILY CHALLENGE')).toHaveStyle({ color: palette.gold });
      expect(screen.getByText('Quiz Sprint')).toHaveStyle({ color: palette.white });
      expect(screen.getByText('START')).toHaveStyle({ color: palette.black });
    },
  );
});
