import { act, screen } from '@testing-library/react-native';
import { renderWithProviders } from '../test/render';
import { createTimer, startTimer } from './core';
import { CrosswordTimerText } from './CrosswordTimerText';

it('advances the visible clock from the shared timer without exposing seconds on the hook', () => {
  jest.useFakeTimers();
  const now = Date.now();
  jest.setSystemTime(now);
  const timer = startTimer(createTimer(), now);
  renderWithProviders(<CrosswordTimerText timer={timer} />);
  expect(screen.getByTestId('crossword-timer')).toHaveTextContent('0:00');
  act(() => { jest.advanceTimersByTime(1000); });
  expect(screen.getByTestId('crossword-timer')).toHaveTextContent('0:01');
  act(() => { jest.advanceTimersByTime(59000); });
  expect(screen.getByTestId('crossword-timer')).toHaveTextContent('1:00');
});
afterEach(() => { jest.useRealTimers(); });
