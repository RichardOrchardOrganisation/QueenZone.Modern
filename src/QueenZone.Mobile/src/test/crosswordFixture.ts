import type { CrosswordDetail } from '../api/types';
/** Small complete public shape for solver component tests, without solutions. */
export function crosswordFixture(overrides: Partial<CrosswordDetail> = {}): CrosswordDetail {
  return { id: '11111111-2222-4333-8444-555555555555', playVersion: '22222222-3333-4444-8555-666666666666',
    slug: 'meet-the-band', title: 'Meet the band', description: 'Queen clues', difficulty: 'easy', style: 'american',
    width: 5, height: 5, archived: false, blocks: Array(25).fill(false), numbering: [1, 2, 3, 4, 5, 6, 0, 0, 0, 0, 7, 0, 0, 0, 0, 8, 0, 0, 0, 0, 9, 0, 0, 0, 0],
    clues: [
      ...Array.from({ length: 5 }, (_, row) => ({ number: row === 0 ? 1 : row + 5, direction: 'across' as const, row, column: 0, length: 5, clue: row === 0 ? 'Queen guitarist' : `Across ${row}`, enumeration: '(5)' })),
      ...Array.from({ length: 5 }, (_, column) => ({ number: column + 1, direction: 'down' as const, row: 0, column, length: 5, clue: `Down ${column}`, enumeration: '(5)' })),
    ], ...overrides };
}
