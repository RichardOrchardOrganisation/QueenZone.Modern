import { screen, userEvent } from '@testing-library/react-native';
import { renderWithProviders } from '../test/render';
import { dark, light } from '../theme/tokens';
import { CrosswordCell, type CrosswordCellProps } from './CrosswordCell';

const scenarios = (['dark', 'light'] as const).flatMap(mode => [
  { mode, selected: false, revealed: false },
  { mode, selected: true, revealed: false },
  { mode, selected: true, revealed: true },
]);

it.each(scenarios)('keeps the narrow I visible in the tree, labelled and selectable through fit/zoom/fit ($mode, selected=$selected, revealed=$revealed)', async ({ mode, selected, revealed }) => {
  const onPress = jest.fn();
  const label = `6 across, 7 letters, letter 3, I.${revealed ? ' Revealed.' : ''}`;
  const props: CrosswordCellProps = { cell: 50, letter: 'I', number: '', size: 24.4,
    selected, inWord: selected, incorrect: false, revealed, disabled: false, label, onPress };
  const cell = (size: number) => <CrosswordCell {...props} size={size} />;
  const { rerender } = renderWithProviders(cell(24.4), { themePreference: mode, navigation: false });
  for (const size of [24.4, 36.6, 24.4]) {
    rerender(cell(size));
    const letter = screen.getByText('I');
    expect(letter.props.numberOfLines).toBe(1);
    expect(letter.props.maxFontSizeMultiplier).toBe(1.2);
    expect(letter).toHaveStyle({ alignSelf: 'stretch', textAlign: 'center', fontSize: size * 0.5,
      fontFamily: 'Inter-Medium', color: (mode === 'dark' ? dark : light).textPrimary });
    expect(screen.getByRole('button', { name: label }).props.accessibilityState).toEqual({ selected, disabled: false });
    if (revealed) expect(screen.getByText('▲', { includeHiddenElements: true })).toBeTruthy();
  }
  await userEvent.setup().press(screen.getByRole('button', { name: label }));
  expect(onPress).toHaveBeenCalledWith(50);
});

it('keeps incorrect/revealed marks and blanks independent of the letter layout', () => {
  const props: CrosswordCellProps = { cell: 50, letter: 'I', number: '', size: 24.4,
    selected: false, inWord: false, incorrect: true, revealed: false, disabled: true,
    label: '6 across, letter 3, I. Incorrect.', onPress: jest.fn() };
  const { rerender } = renderWithProviders(<CrosswordCell {...props} />, { navigation: false });
  expect(screen.getByText('I')).toHaveStyle({ textDecorationLine: 'line-through' });
  expect(screen.getByText('×', { includeHiddenElements: true })).toBeTruthy();
  expect(screen.getByRole('button', { name: props.label })).toBeDisabled();
  rerender(<CrosswordCell {...props} letter="." incorrect={false} revealed label="6 across, letter 3, blank. Revealed." />);
  expect(screen.queryByText('I')).toBeNull();
  expect(screen.getByText('▲', { includeHiddenElements: true })).toBeTruthy();
});
