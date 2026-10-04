import { screen, userEvent } from '@testing-library/react-native';
import * as Haptics from 'expo-haptics';
import { renderWithProviders } from '../test/render';
import { CrosswordKeyboard } from './CrosswordKeyboard';
jest.mock('expo-haptics', () => ({ selectionAsync: jest.fn().mockResolvedValue(undefined) }));
it('dispatches letters and backspace without opening a system text input', async () => {
  const letter = jest.fn(); const backspace = jest.fn();
  renderWithProviders(<CrosswordKeyboard onLetter={letter} onBackspace={backspace} disabled={false} />);
  await userEvent.setup().press(screen.getByRole('button', { name: 'B' }));
  await userEvent.setup().press(screen.getByRole('button', { name: 'Backspace' }));
  expect(letter).toHaveBeenCalledWith('B'); expect(backspace).toHaveBeenCalledTimes(1);
  expect(Haptics.selectionAsync).toHaveBeenCalledTimes(2);
  expect(screen.queryByRole('textbox')).toBeNull();
});
it('blocks typing while paused or complete and tolerates unavailable haptics', async () => {
  const letter = jest.fn(); const backspace = jest.fn();
  const view = renderWithProviders(<CrosswordKeyboard onLetter={letter} onBackspace={backspace} disabled />);
  await userEvent.setup().press(screen.getByRole('button', { name: 'A' }));
  expect(letter).not.toHaveBeenCalled();
  view.rerender(<CrosswordKeyboard onLetter={letter} onBackspace={backspace} disabled={false} />);
  (Haptics.selectionAsync as jest.Mock).mockRejectedValueOnce(new Error('Unavailable'));
  await userEvent.setup().press(screen.getByRole('button', { name: 'A' }));
  expect(letter).toHaveBeenCalledWith('A');
});
