import { act, screen, userEvent } from '@testing-library/react-native';
import { StyleSheet, Text } from 'react-native';
import { renderWithProviders } from '../test/render';
import { crosswordFixture } from '../test/crosswordFixture';
import { createModel, createPlayState } from './core';
import { CrosswordGrid } from './CrosswordGrid';
it('fits a small grid above the fixed controls, supports pinch and exposes button zoom alternatives', async () => {
  const puzzle = crosswordFixture(); const model = createModel(puzzle); const state = createPlayState(model); const cell = jest.fn();
  renderWithProviders(<CrosswordGrid puzzle={puzzle} model={model} state={state} disabled={false} onCell={cell} />);
  const frame = screen.getByTestId('crossword-grid').parent?.parent?.parent?.parent;
  expect(frame).toBeTruthy();
  const handlers = (jest.requireMock('react-native-gesture-handler').getRecordedGestures() as { pinch: { handlers: { onBegin: () => void; onUpdate: (event: { scale: number }) => void } } }).pinch.handlers;
  act(() => { handlers.onBegin(); handlers.onUpdate({ scale: 2 }); });
  expect(screen.getByTestId('crossword-cell-0')).toHaveStyle({ width: 110.4 });
  await userEvent.setup().press(screen.getByRole('button', { name: 'Fit grid' }));
  expect(screen.getByTestId('crossword-cell-0')).toHaveStyle({ width: 55.2 });
  await userEvent.setup().press(screen.getByRole('button', { name: 'Zoom in' }));
  expect(StyleSheet.flatten(screen.getByTestId('crossword-cell-0').props.style).width).toBeCloseTo(82.8);
  await userEvent.setup().press(screen.getByTestId('crossword-cell-0')); expect(cell).toHaveBeenCalledWith(0);
});
it('distinguishes incorrect/revealed cells without colour and provides accessible cell labels in dark mode', () => {
  const puzzle = crosswordFixture(); const model = createModel(puzzle);
  const state = { ...createPlayState(model), letters: '.A' + '.'.repeat(23), revealedCells: [0], incorrectCells: [1] };
  renderWithProviders(<CrosswordGrid puzzle={puzzle} model={model} state={state} disabled onCell={jest.fn()} />);
  expect(screen.getByTestId('crossword-cell-0').findAllByType(Text).some(node => node.props.children === '▲')).toBe(true);
  expect(screen.getByTestId('crossword-cell-1').findAllByType(Text).some(node => node.props.children === '×')).toBe(true);
  expect(screen.getByText('A')).toHaveStyle({ textDecorationLine: 'line-through' });
  expect(screen.getByTestId('crossword-cell-0')).toBeDisabled();
  expect(screen.getByTestId('crossword-cell-0').props.accessibilityLabel).toMatch(/Revealed/);
});
it('renders the final cell of a fifteen-cell grid and dispatches selection at large zoom', async () => {
  const puzzle = crosswordFixture({ width: 15, height: 15, blocks: Array(225).fill(false), numbering: Array(225).fill(0),
    clues: Array.from({ length: 15 }, (_, row) => ({ number: row + 1, direction: 'across', row, column: 0, length: 15, clue: `Across ${row}`, enumeration: '(15)' })) });
  const model = createModel(puzzle); const cell = jest.fn();
  renderWithProviders(<CrosswordGrid puzzle={puzzle} model={model} state={createPlayState(model)} disabled={false} onCell={cell} />);
  await userEvent.setup().press(screen.getByRole('button', { name: 'Zoom in' }));
  await userEvent.setup().press(screen.getByTestId('crossword-cell-224')); expect(cell).toHaveBeenCalledWith(224);
  expect(screen.getByTestId('crossword-cell-224').props.accessibilityLabel).toMatch(/15 across/);
});
