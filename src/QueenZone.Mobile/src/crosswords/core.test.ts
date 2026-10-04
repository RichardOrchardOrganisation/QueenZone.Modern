import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { readFileSync, readdirSync } from 'node:fs';
import {
  createModel, createPlayState, emptyLetters, restoreLetters, selectCell, toggleDirection, jumpToEntry,
  nextEntry, typeLetter, deleteLetter, arrow, applyCheck, applyReveal, setAutoCheck, isClean, isFilled,
  entryFilled, cellLabel, createTimer, elapsedSeconds, startTimer, setPaused, setVisible,
  progressSnapshot, parseProgress, chooseProgress,
  progressStorageKey, loadLocalProgress, saveLocalProgress,
} from './core.ts';
import type { CrosswordClue } from './core.ts';

const clues: CrosswordClue[] = Array.from({ length: 5 }, (_, index) => ({
  number: index === 0 ? 1 : index + 5, direction: 'across', row: index, column: 0, length: 5, clue: 'A clue', enumeration: '(5)',
}));
clues.push(...Array.from({ length: 5 }, (_, index): CrosswordClue => ({
  number: index + 1, direction: 'down', row: 0, column: index, length: 5, clue: 'A clue', enumeration: '(5)',
})));
const puzzle = { width: 5, height: 5, blocks: Array<boolean>(25).fill(false), clues };
const model = createModel(puzzle);

describe('shared progress version and conflict rules', () => {
  const version = '11111111-2222-4333-8444-555555555555';
  it('isolates guest and member saves and tolerates unavailable or corrupt storage', async () => {
    const key = progressStorageKey('puzzle');
    assert.notEqual(key, progressStorageKey('puzzle', 'member-one'));
    assert.notEqual(progressStorageKey('puzzle', 'member-one'), progressStorageKey('puzzle', 'member-two'));
    const data = new Map<string, string>();
    const storage = { getItem: async (item: string) => data.get(item) ?? null,
      setItem: async (item: string, value: string) => { data.set(item, value); } };
    const snapshot = progressSnapshot(createPlayState(model), createTimer(), version, 6000);
    assert.equal(await loadLocalProgress(storage, key, model, version), null);
    assert.equal(await saveLocalProgress(storage, key, snapshot), true);
    assert.deepEqual(await loadLocalProgress(storage, key, model, version), snapshot);
    data.set(key, 'invalid JSON');
    assert.equal(await loadLocalProgress(storage, key, model, version), null);
    const unavailable = { getItem: () => { throw new Error('Unavailable'); }, setItem: () => { throw new Error('Quota'); } };
    assert.equal(await loadLocalProgress(unavailable, key, model, version), null);
    assert.equal(await saveLocalProgress(unavailable, key, snapshot), false);
  });
  it('preserves letters, assists and elapsed time in a versioned snapshot', () => {
    const state = setAutoCheck(typeLetter(model, createPlayState(model), 'A'), true);
    const snapshot = progressSnapshot(state, startTimer(createTimer(), 1000), version, 6000);
    assert.equal(snapshot.elapsedSeconds, 5);
    assert.equal(snapshot.autoCheckUsed, true);
    assert.deepEqual(parseProgress(model, version, snapshot), snapshot);
    assert.throws(() => progressSnapshot(state, createTimer(), '', 6000));
    assert.throws(() => progressSnapshot(state, createTimer(), '00000000-0000-0000-0000-000000000000', 6000));
  });
  it('rejects obsolete and corrupt saves without restoring invalid attempts', () => {
    const snapshot = progressSnapshot(createPlayState(model), createTimer(), version, 6000);
    for (const value of [null, {}, { ...snapshot, playVersion: 'old' }, { ...snapshot, letters: '?' + snapshot.letters.slice(1) },
      { ...snapshot, elapsedSeconds: -1 }, { ...snapshot, elapsedSeconds: 1.5 }, { ...snapshot, autoCheckUsed: null },
      { ...snapshot, revealedCells: [999] }, { ...snapshot, updatedAt: 'bad' }]) {
      assert.equal(parseProgress(model, version, value), null);
    }
    assert.equal(parseProgress(model, '', snapshot), null);
  });
  it('chooses the newer whole grid and reports when it restored another device', () => {
    const local = progressSnapshot(createPlayState(model), createTimer(), version, 6000);
    const server = { ...local, letters: 'A' + local.letters.slice(1), updatedAt: new Date(7000).toISOString() };
    assert.deepEqual(chooseProgress(model, version, local, server), { progress: server, restoredFromServer: true });
    assert.deepEqual(chooseProgress(model, version, server, local), { progress: server, restoredFromServer: false });
    assert.deepEqual(chooseProgress(model, version, local, local), { progress: local, restoredFromServer: false });
    assert.deepEqual(chooseProgress(model, version, null, server), { progress: server, restoredFromServer: true });
    assert.deepEqual(chooseProgress(model, version, null, null), { progress: null, restoredFromServer: false });
  });
});

describe('shared crossword navigation', () => {
  it('fills letters, skips already-filled cells and advances to the next entry', () => {
    let state = createPlayState(model);
    assert.equal(state.cell, 0);
    state = typeLetter(model, state, 'a');
    assert.equal(state.letters[0], 'A');
    assert.equal(state.cell, 1);
    for (const letter of 'BCDE') state = typeLetter(model, state, letter);
    assert.equal(state.cell, 5);
    assert.equal(state.entry, 1);
    assert.ok(entryFilled(model, state, 0));
    state = jumpToEntry(model, state, 5);
    assert.equal(state.cell, 5, 'The crossing at 0 is already filled');
    state = selectCell(model, state, 0);
    assert.equal(typeLetter(model, state, 'Z').cell, 5);
    assert.equal(typeLetter(model, state, '11'), state);
    assert.equal(typeLetter(model, state, 'é'), state);
  });

  it('selects and toggles crossings, jumps to clues and wraps next/previous entries', () => {
    const initial = createPlayState(model);
    const toggled = selectCell(model, initial, initial.cell);
    assert.equal(model.entries[toggled.entry].direction, 'down');
    assert.equal(toggleDirection(model, toggled).entry, 0);
    assert.equal(jumpToEntry(model, initial, 999), initial);
    assert.equal(nextEntry(model, initial, -1).entry, 9);
    assert.equal(nextEntry(model, nextEntry(model, initial, -1)).entry, 0);
    assert.equal(selectCell(model, initial, -1), initial);
    assert.equal(selectCell(model, initial, 25), initial);
    assert.equal(selectCell(model, initial, 10).entry, 2);
  });

  it('backspaces a letter, backs up from empty cells and wraps at the first cell', () => {
    const initial = createPlayState(model);
    const filled = { ...initial, letters: 'AB' + '.'.repeat(23), cell: 1 };
    assert.equal(deleteLetter(model, filled).letters, 'A' + '.'.repeat(24));
    const empty = { ...filled, cell: 2 };
    const backward = deleteLetter(model, empty);
    assert.equal(backward.cell, 1);
    assert.equal(backward.letters, 'A' + '.'.repeat(24));
    const wrap = deleteLetter(model, initial);
    assert.equal(wrap.cell, 24);
    assert.equal(wrap.entry, 9);
  });

  it('switches perpendicular direction before moving and stops at grid boundaries', () => {
    const initial = createPlayState(model);
    const down = arrow(model, initial, 'ArrowDown');
    assert.equal(down.cell, 0);
    assert.equal(model.entries[down.entry].direction, 'down');
    assert.equal(arrow(model, down, 'ArrowDown').cell, 5);
    assert.equal(arrow(model, down, 'ArrowUp').cell, 0);
    assert.equal(arrow(model, initial, 'ArrowRight').cell, 1);
    assert.equal(arrow(model, initial, 'ArrowLeft').cell, 0);
    assert.equal(arrow(model, initial, 'Escape'), initial);
  });

  it('keeps error markers until the letter changes and preserves reveal/auto-check history', () => {
    let state = { ...createPlayState(model), letters: 'A' + '.'.repeat(24) };
    state = applyCheck(state, [{ index: 0, status: 'incorrect' }, { index: 1, status: 'correct' }]);
    assert.deepEqual(state.incorrectCells, [0]);
    assert.ok(cellLabel(model, state, 0).includes('Incorrect'));
    assert.deepEqual(typeLetter(model, state, 'A').incorrectCells, [0]);
    assert.deepEqual(typeLetter(model, state, 'B').incorrectCells, []);
    state = applyReveal(model, state, [{ index: 0, letter: 'Z' }, { index: 999, letter: 'X' }, { index: 1, letter: 'ab' }]);
    assert.equal(state.letters[0], 'Z');
    assert.deepEqual(state.revealedCells, [0]);
    assert.deepEqual(state.incorrectCells, []);
    assert.equal(isClean(state), false);
    assert.ok(cellLabel(model, state, 0).includes('Revealed'));
    assert.deepEqual(deleteLetter(model, state).revealedCells, [0]);
    const auto = setAutoCheck(createPlayState(model), true);
    assert.equal(setAutoCheck(auto, false).autoCheckUsed, true);
    assert.equal(isClean(setAutoCheck(auto, false)), false);
    assert.equal(isClean(createPlayState(model)), true);
    const rechecked = applyCheck(state, [{ index: 1, status: 'empty' }]);
    assert.ok(!rechecked.correctCells.includes(1));
  });

  it('safely restores storage and exposes complete shape-based labels without solutions', () => {
    assert.equal(restoreLetters(model, 'bad'), emptyLetters(model));
    assert.equal(restoreLetters(model), emptyLetters(model));
    assert.equal(restoreLetters(model, 'a? ' + '.'.repeat(22)), 'A' + '.'.repeat(24));
    const state = createPlayState(model, { letters: 'Z'.repeat(25), revealedCells: [0, 0, -1, 25], autoCheckUsed: true });
    assert.deepEqual(state.revealedCells, [0]);
    assert.ok(isFilled(model, state));
    assert.equal(cellLabel(model, createPlayState(model), 0), '1 across, 5 letters, letter 1, blank. Also 1 down.');
    assert.equal(typeLetter(model, state, 'A').cell, 0, 'A full grid stays on the selected cell');
  });

  it('rejects incompatible grids and orphan cells instead of creating unsafe navigation', () => {
    assert.throws(() => createModel({ ...puzzle, width: 4 }));
    assert.throws(() => createModel({ ...puzzle, width: 5.5 }));
    assert.throws(() => createModel({ ...puzzle, blocks: [] }));
    assert.throws(() => createModel({ ...puzzle, clues: [] }));
    assert.throws(() => createModel({ ...puzzle, clues: clues.slice(0, 1) }));
    assert.throws(() => createModel({ ...puzzle, clues: [{ ...clues[0]!, length: 2 }] }));
    assert.throws(() => createModel({ ...puzzle, clues: [{ ...clues[0]!, column: 1 }] }));
    assert.throws(() => createModel({ ...puzzle, blocks: [true, ...puzzle.blocks.slice(1)] }));
  });

  it('navigates all ten seed shapes, including unchecked cells and blocks', () => {
    const directory = new URL('../../../../data/crosswords/', import.meta.url);
    for (const file of readdirSync(directory).filter(name => name.endsWith('.json'))) {
      const seed = JSON.parse(readFileSync(new URL(file, directory), 'utf8')) as { width: number; height: number; grid: string[] };
      const seedClues: CrosswordClue[] = [];
      let number = 0;
      const white = (row: number, column: number) => row >= 0 && row < seed.height && column >= 0 && column < seed.width && seed.grid[row]![column] !== '#';
      for (let row = 0; row < seed.height; row++) for (let column = 0; column < seed.width; column++) {
        if (!white(row, column)) continue;
        const across = !white(row, column - 1) && white(row, column + 1);
        const down = !white(row - 1, column) && white(row + 1, column);
        if (!across && !down) continue;
        number++;
        for (const direction of ['across', 'down'] as const) {
          if (!(direction === 'across' ? across : down)) continue;
          let length = 0;
          while (white(row + (direction === 'down' ? length : 0), column + (direction === 'across' ? length : 0))) length++;
          seedClues.push({ number, direction, row, column, length, clue: 'A clue', enumeration: `(${length})` });
        }
      }
      const seedModel = createModel({ ...seed, blocks: seed.grid.join('').split('').map(cell => cell === '#'), clues: seedClues });
      let state = createPlayState(seedModel);
      for (let cell = 0; cell < seedModel.blocks.length; cell++) {
        if (seedModel.blocks[cell]) continue;
        const selected = selectCell(seedModel, state, cell);
        for (const key of ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown']) {
          const moved = arrow(seedModel, arrow(seedModel, selected, key), key);
          assert.equal(seedModel.blocks[moved.cell], false, 'Arrow navigation never selects a block');
          if (key === 'ArrowLeft' || key === 'ArrowRight') assert.equal(Math.floor(moved.cell / seedModel.width), Math.floor(cell / seedModel.width));
          else assert.equal(moved.cell % seedModel.width, cell % seedModel.width);
        }
        assert.equal(seedModel.blocks[deleteLetter(seedModel, selected).cell], false, 'Backspace stays within entry cells');
      }
      let count = 0;
      while (!isFilled(seedModel, state) && count < 226) { state = typeLetter(seedModel, state, 'Z'); count++; }
      assert.ok(isFilled(seedModel, state), `${file} can fill every white cell`);
      assert.equal(count, seedModel.blocks.filter(block => !block).length);
      const block = seedModel.blocks.indexOf(true);
      assert.equal(selectCell(seedModel, state, block), state);
      assert.equal(cellLabel(seedModel, state, block), 'Block');
      assert.equal(restoreLetters(seedModel, 'z'.repeat(seedModel.blocks.length))[block], '#');
    }
  });
});

describe('shared crossword timer', () => {
  it('starts on first input and ignores hidden, manually paused or backwards-clock time', () => {
    let timer = createTimer();
    assert.equal(elapsedSeconds(timer, 10000), 0);
    timer = startTimer(timer, 1000);
    assert.equal(startTimer(timer, 1500), timer);
    assert.equal(elapsedSeconds(timer, 0), 0);
    assert.equal(elapsedSeconds(timer, 2500), 1);
    timer = setVisible(timer, false, 2500);
    assert.equal(elapsedSeconds(timer, 100000), 1);
    assert.equal(startTimer(timer, 100000), timer);
    timer = setVisible(timer, true, 100000);
    assert.equal(elapsedSeconds(timer, 101500), 3);
    timer = setPaused(timer, true, 101500);
    timer = setVisible(timer, false, 102000);
    timer = setVisible(timer, true, 200000);
    assert.equal(timer.runningSince, null);
    assert.equal(startTimer(timer, 200000), timer);
    timer = setPaused(timer, false, 200000);
    assert.equal(elapsedSeconds(timer, 201000), 4);
  });

  it('restores an elapsed total without running until play resumes', () => {
    const restored = createTimer(123);
    assert.equal(elapsedSeconds(restored, 500000), 123);
    assert.equal(elapsedSeconds(startTimer(restored, 500000), 501000), 124);
    assert.equal(elapsedSeconds(createTimer(-1), 0), 0);
    assert.equal(elapsedSeconds(createTimer(Number.NaN), 0), 0);
    assert.equal(setPaused(createTimer(), false, 0).runningSince, null);
  });
});

it('keeps assists from both same-version snapshots while choosing whole-grid letters by timestamp', () => {
  const version = '11111111-2222-4333-8444-555555555555';
  const local = { playVersion: version, letters: emptyLetters(model), elapsedSeconds: 12, revealedCells: [0], autoCheckUsed: true, updatedAt: '2026-10-04T00:00:00Z' };
  const remote = { ...local, revealedCells: [], autoCheckUsed: false, updatedAt: '2026-10-04T01:00:00Z' };
  const chosen = chooseProgress(model, version, local, remote);
  assert.equal(chosen.restoredFromServer, true);
  assert.equal(chosen.progress?.letters, remote.letters);
  assert.deepEqual(chosen.progress?.revealedCells, [0]);
  assert.equal(chosen.progress?.autoCheckUsed, true);
  assert.equal(chooseProgress(model, version, { ...local, playVersion: '00000000-0000-0000-0000-000000000000' }, remote).progress?.autoCheckUsed, false);
});
