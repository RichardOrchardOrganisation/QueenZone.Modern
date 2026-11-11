import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import {
  checkFlowText,
  collectViolations,
  compareToBaseline,
  firstCommand,
  ownsStartState,
  suiteFlows,
} from './check-maestro-flows.mjs';

const flow = body => `appId: org.queenzone.mobile\n---\n${body}`;
const rules = findings => findings.map(f => f.rule);

test('suite flow must own its start state', () => {
  assert.equal(ownsStartState(flow('- launchApp:\n    clearState: true\n')), true);
  assert.equal(ownsStartState(flow('- launchApp\n')), true);
  assert.equal(ownsStartState(flow('- openLink: ${URL}\n')), true);
  assert.equal(ownsStartState(flow('- runFlow: common/home-top.yaml\n')), true);
  assert.equal(ownsStartState(flow('- runFlow: open-smoke-auth.yaml\n')), true);
  // Comments before the first command do not count as the first command.
  assert.equal(ownsStartState(flow('# relies on nothing\n- launchApp\n')), true);
  assert.equal(ownsStartState(flow('- tapOn:\n    id: tab-home\n')), false);
  assert.equal(ownsStartState(flow('- runFlow: flows/other.yaml\n')), false);
  assert.deepEqual(firstCommand(flow('- tapOn:\n    id: x\n')), { name: 'tapOn', value: '' });
});

test('start-state and no-optional apply only to suite flows', () => {
  const body = flow('- tapOn:\n    id: tab-home\n- extendedWaitUntil:\n    visible:\n      id: x\n    timeout: 5000\n    optional: true\n');
  assert.deepEqual(rules(checkFlowText(body)), []);
  assert.deepEqual(rules(checkFlowText(body, { suiteFlow: true })).sort(), ['no-optional', 'start-state']);
});

test('every extendedWaitUntil needs a timeout of at most 60s', () => {
  const missing = flow('- extendedWaitUntil:\n    visible:\n      id: x\n');
  const nestedOnly = flow('- extendedWaitUntil:\n    visible:\n      id: x\n      timeout: 5000\n');
  const tooLong = flow('- extendedWaitUntil:\n    visible:\n      id: x\n    timeout: 120000\n');
  const ok = flow('- runFlow:\n    commands:\n      - extendedWaitUntil:\n          visible:\n            id: x\n          timeout: 60000\n');
  assert.deepEqual(rules(checkFlowText(missing)), ['bounded-wait']);
  assert.deepEqual(rules(checkFlowText(nestedOnly)), ['bounded-wait']);
  assert.deepEqual(rules(checkFlowText(tooLong)), ['bounded-wait']);
  assert.deepEqual(rules(checkFlowText(ok)), []);
});

test('retry needs maxRetries <= 1 and a justifying comment', () => {
  const justified = flow('# Loopback starvation: retry the read-only Try again only.\n- retry:\n    maxRetries: 1\n    commands:\n      - tapOn: x\n');
  const silent = flow('- tapOn: y\n- retry:\n    maxRetries: 1\n    commands:\n      - tapOn: x\n');
  const greedy = flow('# reason\n- retry:\n    maxRetries: 3\n    commands:\n      - tapOn: x\n');
  const defaulted = flow('# reason\n- retry:\n    commands:\n      - tapOn: x\n');
  assert.deepEqual(rules(checkFlowText(justified)), []);
  assert.deepEqual(rules(checkFlowText(silent)), ['retry-justified']);
  assert.deepEqual(rules(checkFlowText(greedy)), ['retry-justified']);
  assert.deepEqual(rules(checkFlowText(defaulted)), ['retry-justified']);
});

test('suite parsing reads runFlow entries', () => {
  assert.deepEqual(
    suiteFlows('appId: a\n---\n# note\n- runFlow: flows/01-launch.yaml\n- runFlow: flows/02-tabs.yaml\n'),
    ['flows/01-launch.yaml', 'flows/02-tabs.yaml'],
  );
});

test('baseline lets known violations through and rejects stale entries', () => {
  const violations = [{ key: 'start-state:flows/02.yaml' }, { key: 'bounded-wait:flows/09.yaml' }];
  const result = compareToBaseline(violations, ['start-state:flows/02.yaml', 'start-state:flows/fixed.yaml']);
  assert.deepEqual(result.newViolations.map(v => v.key), ['bounded-wait:flows/09.yaml']);
  assert.deepEqual(result.stale, ['start-state:flows/fixed.yaml']);
});

test('collectViolations treats only gating-suite flows as suite flows', () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'maestro-lint-'));
  try {
    mkdirSync(path.join(dir, 'flows'));
    writeFileSync(path.join(dir, 'smoke.yaml'), 'appId: a\n---\n- runFlow: flows/a.yaml\n');
    writeFileSync(path.join(dir, 'store-screenshots-public.yaml'), 'appId: a\n---\n- runFlow: flows/b.yaml\n');
    writeFileSync(path.join(dir, 'flows', 'a.yaml'), flow('- tapOn: x\n'));
    writeFileSync(path.join(dir, 'flows', 'b.yaml'), flow('- tapOn: x\n'));
    assert.deepEqual(collectViolations(dir).map(v => v.key), ['start-state:flows/a.yaml']);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
