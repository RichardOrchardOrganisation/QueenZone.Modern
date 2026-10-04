export type CrosswordDirection = 'across' | 'down';
export type CrosswordClue = {
    number: number; direction: CrosswordDirection; row: number; column: number; length: number;
    clue: string; enumeration: string;
};
export type CrosswordModel = {
    width: number; height: number; blocks: boolean[];
    entries: (CrosswordClue & { key: string; cells: number[] })[];
    memberships: number[][];
};
export type CrosswordPlayState = {
    letters: string; cell: number; entry: number; incorrectCells: number[]; correctCells: number[];
    revealedCells: number[]; autoCheck: boolean; autoCheckUsed: boolean;
};
export type CrosswordTimer = {
    elapsedMs: number; runningSince: number | null; started: boolean; paused: boolean; visible: boolean;
};
export type CrosswordSavedProgress = {
    playVersion: string; letters: string; elapsedSeconds: number; revealedCells: number[];
    autoCheckUsed: boolean; updatedAt: string;
};
export function progressSnapshot(state: CrosswordPlayState, timer: CrosswordTimer, playVersion: string, now: number): CrosswordSavedProgress;
export function parseProgress(model: CrosswordModel, playVersion: string, value: unknown): CrosswordSavedProgress | null;
export function chooseProgress(model: CrosswordModel, playVersion: string, local: unknown, server: unknown): { progress: CrosswordSavedProgress | null; restoredFromServer: boolean };
export type CrosswordStorage = { getItem(key: string): string | null | Promise<string | null>; setItem(key: string, value: string): void | Promise<void> };
export function progressStorageKey(puzzleId: string, memberId?: string | null): string;
export function loadLocalProgress(storage: CrosswordStorage, key: string, model: CrosswordModel, playVersion: string): Promise<CrosswordSavedProgress | null>;
export function saveLocalProgress(storage: CrosswordStorage, key: string, progress: CrosswordSavedProgress): Promise<boolean>;
export function createModel(puzzle: { width: number; height: number; blocks: readonly boolean[]; clues: readonly CrosswordClue[] }): CrosswordModel;
export function emptyLetters(model: CrosswordModel): string;
export function restoreLetters(model: CrosswordModel, letters?: string): string;
export function createPlayState(model: CrosswordModel, progress?: { letters?: string; revealedCells?: number[]; autoCheckUsed?: boolean }): CrosswordPlayState;
export function selectCell(model: CrosswordModel, state: CrosswordPlayState, cell: number): CrosswordPlayState;
export function toggleDirection(model: CrosswordModel, state: CrosswordPlayState): CrosswordPlayState;
export function jumpToEntry(model: CrosswordModel, state: CrosswordPlayState, entry: number): CrosswordPlayState;
export function nextEntry(model: CrosswordModel, state: CrosswordPlayState, offset?: number): CrosswordPlayState;
export function typeLetter(model: CrosswordModel, state: CrosswordPlayState, letter: string): CrosswordPlayState;
export function deleteLetter(model: CrosswordModel, state: CrosswordPlayState): CrosswordPlayState;
export function arrow(model: CrosswordModel, state: CrosswordPlayState, key: string): CrosswordPlayState;
export function applyCheck(state: CrosswordPlayState, checks: { index: number; status: 'correct' | 'incorrect' | 'empty' }[]): CrosswordPlayState;
export function applyReveal(model: CrosswordModel, state: CrosswordPlayState, cells: { index: number; letter: string }[]): CrosswordPlayState;
export function setAutoCheck(state: CrosswordPlayState, enabled: boolean): CrosswordPlayState;
export function isClean(state: CrosswordPlayState): boolean;
export function isFilled(model: CrosswordModel, state: CrosswordPlayState): boolean;
export function entryFilled(model: CrosswordModel, state: CrosswordPlayState, entry: number): boolean;
export function cellLabel(model: CrosswordModel, state: CrosswordPlayState, cell: number): string;
export function createTimer(elapsedSeconds?: number): CrosswordTimer;
export function elapsedSeconds(timer: CrosswordTimer, now: number): number;
export function startTimer(timer: CrosswordTimer, now: number): CrosswordTimer;
export function setPaused(timer: CrosswordTimer, paused: boolean, now: number): CrosswordTimer;
export function setVisible(timer: CrosswordTimer, visible: boolean, now: number): CrosswordTimer;
