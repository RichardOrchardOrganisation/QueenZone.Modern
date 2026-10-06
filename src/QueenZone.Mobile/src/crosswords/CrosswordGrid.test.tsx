import { act, screen, userEvent } from '@testing-library/react-native';
import { StyleSheet, Text } from 'react-native';
import { renderWithProviders } from '../test/render';
import { crosswordFixture } from '../test/crosswordFixture';
import { createModel, createPlayState, selectCell } from './core';
import { CrosswordGrid } from './CrosswordGrid';

const { cellRenders } = jest.requireMock('./CrosswordCell') as { cellRenders: number[] };

jest.mock('./CrosswordCell', () => {
  const React = jest.requireActual('react') as typeof import('react');
  const actual = jest.requireActual('./CrosswordCell') as typeof import('./CrosswordCell');
  const renders: number[] = [];
  function sameCellProps(previous: Record<string, unknown>, next: Record<string, unknown>) {
    return Object.keys(next).every(key => previous[key] === next[key]);
  }
  function CrosswordCellMock(props: import('./CrosswordCell').CrosswordCellProps) {
    renders.push(props.cell);
    return React.createElement(actual.CrosswordCellView, props);
  }
  CrosswordCellMock.displayName = 'CrosswordCellMock';
  return {
    ...actual,
    cellRenders: renders,
    CrosswordCell: React.memo(CrosswordCellMock, sameCellProps),
  };
});

function pinchHandlers() {
  return (jest.requireMock('react-native-gesture-handler').getRecordedGestures() as {
    pinch: { handlers: { onBegin: () => void; onUpdate: (event: { scale: number }) => void; onEnd: () => void } };
  }).pinch.handlers;
}

function cellWidth(id = 'crossword-cell-0') {
  return StyleSheet.flatten(screen.getByTestId(id).props.style).width as number;
}

beforeEach(() => { cellRenders.length = 0; });

it('fits a small grid above the fixed controls, supports pinch and exposes button zoom alternatives', async () => {
  const puzzle = crosswordFixture(); const model = createModel(puzzle); const state = createPlayState(model); const cell = jest.fn();
  renderWithProviders(<CrosswordGrid puzzle={puzzle} model={model} state={state} disabled={false} onCell={cell} />);
  const frame = screen.getByTestId('crossword-grid').parent?.parent?.parent?.parent;
  expect(frame).toBeTruthy();
  const handlers = pinchHandlers();
  cellRenders.length = 0;
  act(() => { handlers.onBegin(); handlers.onUpdate({ scale: 1.4 }); handlers.onUpdate({ scale: 2 }); });
  expect(cellRenders).toEqual([]);
  expect(screen.getByTestId('crossword-cell-0')).toHaveStyle({ width: 55.2 });
  act(() => { handlers.onEnd(); });
  expect(screen.getByTestId('crossword-cell-0')).toHaveStyle({ width: 110.4 });
  expect(cellRenders).toHaveLength(25);
  expect(new Set(cellRenders).size).toBe(25);
  await userEvent.setup().press(screen.getByRole('button', { name: 'Fit grid' }));
  expect(screen.getByTestId('crossword-cell-0')).toHaveStyle({ width: 55.2 });
  await userEvent.setup().press(screen.getByRole('button', { name: 'Zoom in' }));
  expect(StyleSheet.flatten(screen.getByTestId('crossword-cell-0').props.style).width).toBeCloseTo(82.8);
  await userEvent.setup().press(screen.getByTestId('crossword-cell-0')); expect(cell).toHaveBeenCalledWith(0);
});
it('clamps pinch commits to 1 and 4', () => {
  const puzzle = crosswordFixture(); const model = createModel(puzzle);
  renderWithProviders(<CrosswordGrid puzzle={puzzle} model={model} state={createPlayState(model)} disabled={false} onCell={jest.fn()} />);
  const handlers = pinchHandlers();
  cellRenders.length = 0;
  act(() => { handlers.onBegin(); handlers.onUpdate({ scale: 0.2 }); });
  expect(cellRenders).toHaveLength(0);
  expect(cellWidth()).toBe(55.2);
  cellRenders.length = 0;
  act(() => { handlers.onEnd(); });
  expect(cellWidth()).toBe(55.2);
  act(() => { handlers.onBegin(); handlers.onUpdate({ scale: 10 }); handlers.onEnd(); });
  expect(cellWidth()).toBe(220.8);
  expect(new Set(cellRenders).size).toBe(25);
});
it('re-renders only the cells whose selection or word highlight changed', () => {
  const puzzle = crosswordFixture(); const model = createModel(puzzle); const onCell = jest.fn();
  const initial = createPlayState(model);
  const { rerender } = renderWithProviders(<CrosswordGrid puzzle={puzzle} model={model} state={initial} disabled={false} onCell={onCell} />, { navigation: false });
  cellRenders.length = 0;
  const within = selectCell(model, initial, 2);
  rerender(<CrosswordGrid puzzle={puzzle} model={model} state={within} disabled={false} onCell={onCell} />);
  expect([...cellRenders].sort((left, right) => left - right)).toEqual([0, 2]);
  cellRenders.length = 0;
  const nextWord = selectCell(model, initial, 6);
  rerender(<CrosswordGrid puzzle={puzzle} model={model} state={nextWord} disabled={false} onCell={onCell} />);
  expect([...cellRenders].sort((left, right) => left - right)).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
});
it('keeps onCell identity so unchanged cells skip renders', () => {
  const puzzle = crosswordFixture(); const model = createModel(puzzle); const onCell = jest.fn();
  const initial = createPlayState(model);
  const { rerender } = renderWithProviders(<CrosswordGrid puzzle={puzzle} model={model} state={initial} disabled={false} onCell={onCell} />, { navigation: false });
  const first = onCell;
  cellRenders.length = 0;
  rerender(<CrosswordGrid puzzle={puzzle} model={model} state={selectCell(model, initial, 1)} disabled={false} onCell={onCell} />);
  expect(onCell).toBe(first);
  expect(cellRenders).not.toContain(24);
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
