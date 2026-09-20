/** Pure Quiz Sprint rules shared by the play screen (the server stays the source of truth for recorded scores). */

/** A streak this long doubles every further correct answer. */
export const STREAK_BONUS_AT = 3;

/** How long the correct/wrong colouring shows before the next question. */
export const FEEDBACK_MS = 620;

/** Seconds at or under which the countdown turns burgundy. */
export const URGENT_SECONDS = 10;

export function pointsForAnswer(isCorrect: boolean, streakBefore: number): number {
  if (!isCorrect) {
    return 0;
  }
  return streakBefore >= STREAK_BONUS_AT ? 2 : 1;
}

export function nextStreak(isCorrect: boolean, streakBefore: number): number {
  return isCorrect ? streakBefore + 1 : 0;
}

export function streakLabel(streak: number): string {
  if (streak >= STREAK_BONUS_AT) {
    return `STREAK ×${streak} · DOUBLE POINTS`;
  }
  return streak >= 2 ? `STREAK ×${streak}` : 'NO STREAK';
}

export function verdictFor(points: number): string {
  if (points >= 20) {
    return "A collector's run. That belongs at the top of the board.";
  }
  if (points >= 12) {
    return 'Strong. A steadier streak and the top ten is yours.';
  }
  if (points >= 6) {
    return 'Respectable. The archive rewards a second run.';
  }
  return 'The clock wins this one. Try again — the questions reshuffle.';
}

/** Whole seconds left, derived from a stored end timestamp so re-renders cannot drift the clock. */
export function secondsLeft(endsAt: number, now: number): number {
  return Math.max(0, Math.ceil((endsAt - now) / 1000));
}

export function optionLetter(index: number): string {
  return String.fromCharCode(65 + index);
}
