import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { isCrosswordProgressWrite } from './progressValidation.ts';

const write = { playVersion: '815240db-6ac5-4cdc-8caa-ae1f02f5a7bb', letters: 'A' + '.'.repeat(23) + '#',
  elapsedSeconds: 42, revealedCells: [0], autoCheckUsed: false, updatedAt: '2026-10-04T06:00:00Z' };
describe('persisted crossword writes', () => {
  it('retains valid empty and assisted snapshots without changing them', () => {
    assert.equal(isCrosswordProgressWrite(write), true);
    assert.equal(isCrosswordProgressWrite({ ...write, revealedCells: [], autoCheckUsed: true }), true);
  });
  it('rejects missing versions, malformed data and reveals outside playable cells', () => {
    for (const value of [null, 'not an object', {}, { ...write, playVersion: '' },
      { ...write, playVersion: '00000000-0000-0000-0000-000000000000' }, { ...write, letters: 'secret' },
      { ...write, letters: '.'.repeat(226) }, { ...write, elapsedSeconds: -1 },
      { ...write, elapsedSeconds: 0.5 }, { ...write, elapsedSeconds: 2147483648 },
      { ...write, updatedAt: 'yesterday' }, { ...write, autoCheckUsed: 'false' },
      { ...write, revealedCells: null }, { ...write, revealedCells: [24] },
      { ...write, revealedCells: [25] }, { ...write, revealedCells: [-1] },
      { ...write, revealedCells: [0.5] }, { ...write, revealedCells: Array(26).fill(0) }]) {
      assert.equal(isCrosswordProgressWrite(value), false);
    }
  });
});
