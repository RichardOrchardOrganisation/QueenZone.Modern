import {
  createModel, createPlayState, typeLetter, deleteLetter, toggleDirection, jumpToEntry, nextEntry,
} from './core';

const model = createModel({
  width: 5, height: 5, blocks: Array<boolean>(25).fill(false),
  clues: [
    ...Array.from({ length: 5 }, (_, row) => ({ number: row === 0 ? 1 : row + 5, direction: 'across' as const,
      row, column: 0, length: 5, clue: 'Across clue', enumeration: '(5)' })),
    ...Array.from({ length: 5 }, (_, column) => ({ number: column + 1, direction: 'down' as const,
      row: 0, column, length: 5, clue: 'Down clue', enumeration: '(5)' })),
  ],
});

it('imports the same web module through the mobile transform and types letters', () => {
  const next = typeLetter(model, createPlayState(model), 'b');
  expect(next.letters[0]).toBe('B');
  expect(next.cell).toBe(1);
  expect(deleteLetter(model, next).letters[0]).toBe('.');
});

it('toggles crossings and jumps to a selected clue in the mobile runtime', () => {
  const first = createPlayState(model);
  expect(model.entries[toggleDirection(model, first).entry].direction).toBe('down');
  expect(jumpToEntry(model, first, 2).cell).toBe(10);
  expect(nextEntry(model, first, -1).entry).toBe(9);
});
