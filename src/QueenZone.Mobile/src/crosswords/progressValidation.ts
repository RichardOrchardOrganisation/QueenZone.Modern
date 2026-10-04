import type { CrosswordProgressWrite } from '../api/types';

const versionPattern = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function validElapsed(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= 2147483647;
}
function validReveals(value: unknown, letters: string): value is number[] {
  if (!Array.isArray(value) || value.length > letters.length) return false;
  return value.every(cell => Number.isInteger(cell) && cell >= 0 && cell < letters.length && letters[cell] !== '#');
}
/** Stored writes never acquire an optional-version escape hatch. */
export function isCrosswordProgressWrite(value: unknown): value is CrosswordProgressWrite {
  if (!value || typeof value !== 'object') return false;
  const progress = value as Partial<CrosswordProgressWrite>;
  if (typeof progress.playVersion !== 'string' || !versionPattern.test(progress.playVersion)) return false;
  if (typeof progress.letters !== 'string' || !/^[A-Z.#]{25,225}$/.test(progress.letters)) return false;
  if (!validElapsed(progress.elapsedSeconds)) return false;
  if (typeof progress.updatedAt !== 'string' || !Number.isFinite(Date.parse(progress.updatedAt))) return false;
  return typeof progress.autoCheckUsed === 'boolean' && validReveals(progress.revealedCells, progress.letters);
}
