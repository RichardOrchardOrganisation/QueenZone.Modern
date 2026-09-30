import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import {
  nextStreak,
  optionLetter,
  pointsForAnswer,
  secondsLeft,
  streakLabel,
  verdictFor,
} from './quizSprintMeta.ts';

describe('quizSprintMeta', () => {
  it('scores one point, doubling once the streak reaches three', () => {
    assert.equal(pointsForAnswer(true, 0), 1);
    assert.equal(pointsForAnswer(true, 2), 1);
    assert.equal(pointsForAnswer(true, 3), 2);
    assert.equal(pointsForAnswer(false, 5), 0);
  });

  it('resets the streak on a wrong answer', () => {
    assert.equal(nextStreak(true, 2), 3);
    assert.equal(nextStreak(false, 4), 0);
  });

  it('labels the streak', () => {
    assert.equal(streakLabel(0), 'NO STREAK');
    assert.equal(streakLabel(1), 'NO STREAK');
    assert.equal(streakLabel(2), 'STREAK ×2');
    assert.equal(streakLabel(3), 'STREAK ×3 · DOUBLE POINTS');
  });

  it('picks the verdict by score band', () => {
    assert.match(verdictFor(20), /collector/);
    assert.match(verdictFor(12), /Strong/);
    assert.match(verdictFor(6), /Respectable/);
    assert.match(verdictFor(5), /clock wins/);
  });

  it('derives whole seconds left from the end timestamp and never goes negative', () => {
    assert.equal(secondsLeft(60_000, 0), 60);
    assert.equal(secondsLeft(60_000, 59_100), 1);
    assert.equal(secondsLeft(60_000, 70_000), 0);
  });

  it('letters options from A', () => {
    assert.deepEqual([0, 1, 2, 3].map(optionLetter), ['A', 'B', 'C', 'D']);
  });
});
