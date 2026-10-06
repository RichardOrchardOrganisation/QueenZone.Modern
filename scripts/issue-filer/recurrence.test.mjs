import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadFilerFiles, repoRootFrom, validateIgnore } from './config.mjs';
import { partitionIgnore, planFilings } from './core.mjs';
import { correlateSignals, parseSentryIssue } from './telemetry.mjs';
import { loadExisting, runFiler } from './run.mjs';
import { createGitHubClient } from './github-client.mjs';

const { config, ignore } = loadFilerFiles(repoRootFrom());
const baseline = ignore.entries[0].recurrence;
const nextId = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';
const now = new Date('2026-10-07T07:17:00Z');
const issue = {
  number: 2147, state: 'closed', stateReason: 'completed',
  body: '<!-- qz-filer v=1 keys=sentry:7775729576 source=sentry -->',
  closedAt: '2026-10-06T06:11:45Z', createdAt: '2026-10-06T04:45:46Z',
  updatedAt: '2026-10-06T06:11:45Z', user: 'richardorchard', labels: ['bug', 'mobile'],
};
function candidate(fields = {}, event = {}) {
  return parseSentryIssue({ id: '7775729576', title: 'WatchdogTermination',
    count: '1', userCount: 1, firstSeen: baseline.lastSeen, lastSeen: baseline.lastSeen,
    ...fields }, { eventID: baseline.eventId, dateCreated: baseline.lastSeen,
    release: 'org.queenzone.mobile@1.0.53+53', dist: '53', ...event });
}
function plan(signal = candidate(), options = {}) {
  return planFilings({ config, ignore, now, loop: 'telemetry',
    candidates: [signal], existing: [issue], ...options });
}
function assertQuiet(result) {
  assert.equal(result.create.length + result.reopen.length + result.comment.length, 0);
  assert.equal(result.skipped[0].reason, 'ignored');
}
function assertReopen(result) {
  assert.equal(result.create.length, 0);
  assert.deepEqual(result.reopen.map((item) => item.issueNumber), [2147]);
  assert.deepEqual(result.comment.map((item) => item.issueNumber), [2147]);
}

test('known occurrence stays quiet across repeated polls, string counts and timezone representations', () => {
  for (let n = 0; n < 3; n += 1) assertQuiet(plan(candidate({ lastSeen: '2026-10-06T11:27:48+08:00' })));
});
test('distinct event at the same timestamp escapes suppression even at one event/user', () => {
  assertReopen(plan(candidate({}, { eventID: nextId })));
});
test('later lastSeen escapes suppression even if latest event endpoint returns stale baseline', () => {
  assertReopen(plan(candidate({ lastSeen: '2026-10-07T06:00:00Z' })));
});
test('two observed events escape suppression without assuming lifetime comparability', () => {
  assertReopen(plan(candidate({ count: '2' })));
});
test('newly affected user escapes suppression even if count is one', () => {
  assertReopen(plan(candidate({ userCount: 2 })));
});
test('rolling-window decline never hides a new event or new user', () => {
  for (const count of ['0', '1']) {
    assertReopen(plan(candidate({ count, userCount: 1 }, { eventID: nextId })));
    assertReopen(plan(candidate({ count, userCount: 2 })));
    assertQuiet(plan(candidate({ count, userCount: 0 })));
  }
});
test('count fallbacks, missing IDs and invalid times cannot silently extend suppression', () => {
  for (const bad of [undefined, null, '', ' ', [], {}, 'bad', -1, 1.5, Infinity, true, '9007199254740992']) {
    assertReopen(plan(candidate({ count: bad })));
    assertReopen(plan(candidate({ userCount: bad })));
  }
  for (const bad of [undefined, null, '', 'bad', [baseline.eventId]]) {
    assertReopen(plan(candidate({}, { eventID: bad, id: baseline.eventId })));
    assertReopen(plan(candidate({ lastSeen: bad })));
  }
  const withoutObservation = candidate();
  assertReopen(plan(candidate({ lastSeen: [baseline.lastSeen] })));
  delete withoutObservation.sentryObservations;
  assertReopen(plan(withoutObservation));
});
test('upper-case documented eventID is normalized', () => {
  assertQuiet(plan(candidate({}, { eventID: baseline.eventId.toUpperCase() })));
});
test('correlation retains Sentry observations without suppressing independent Azure evidence', () => {
  const sentry = candidate();
  sentry.operationId = 'same-trace';
  const azure = { source: 'appinsights', sources: ['appinsights'], keys: ['ai:req:home'],
    title: 'Azure', operationId: 'same-trace', count: 50, lastSeen: '2026-10-06T03:28:00Z' };
  const merged = correlateSignals([sentry], [azure]);
  assert.equal(merged.length, 1);
  assert.equal(merged[0].count, 51);
  assert.equal(merged[0].sentryObservations[0].count, 1);
  assert.equal(merged[0].sentryObservations[0].lastSeen, baseline.lastSeen);
  // The original source:sentry ignore never hid source:telemetry candidates.
  assertReopen(plan(merged[0]));
  const next = correlateSignals([{ ...candidate({}, { eventID: nextId }), operationId: 'same-trace' }], [azure]);
  assertReopen(plan(next[0]));
});
test('expiry ends suppression but preserves canonical issue beyond 30 and 90 days', () => {
  assertQuiet(plan(candidate(), { now: new Date('2026-11-05T00:00:00Z') }));
  for (const time of ['2026-11-05T00:00:01Z', '2026-11-06T07:17:00Z', '2027-02-01T00:00:00Z']) {
    const result = plan(candidate(), { now: new Date(time) });
    assertReopen(result);
    assert.equal(result.expiredIgnores.length, 1);
  }
});
test('missing or mismatched canonical issue never creates a duplicate', () => {
  for (const existing of [[], [{ ...issue, number: 999 }], [{ ...issue, body: '' }], [{ ...issue, pullRequest: true }]]) {
    const result = plan(candidate({}, { eventID: nextId }), { existing });
    assert.equal(result.create.length + result.reopen.length + result.comment.length, 0);
    assert.equal(result.skipped[0].reason, 'canonical-issue-missing');
  }
});
test('canonical issue takes precedence over another matching issue', () => {
  assertReopen(plan(candidate({}, { eventID: nextId }), { existing: [{ ...issue, number: 999, state: 'open' }, issue] }));
});
test('not-planned closure, comment cap and normal open-issue cooldown remain effective', () => {
  const next = candidate({}, { eventID: nextId });
  const notPlanned = plan(next, { existing: [{ ...issue, stateReason: 'not_planned' }] });
  assert.equal(notPlanned.create.length + notPlanned.reopen.length, 0);
  assert.equal(notPlanned.skipped[0].reason, 'closed-not-planned');
  const capped = plan(next, { config: { ...config, caps: { ...config.caps, commentsPerRun: 0 } } });
  assert.equal(capped.reopen.length, 0);
  assert.equal(capped.skipped[0].reason, 'comment-cap');
  const cooled = plan(next, { existing: [{ ...issue, state: 'open', lastFilerCommentAt: now.toISOString() }] });
  assert.equal(cooled.comment.length + cooled.create.length + cooled.reopen.length, 0);
  assert.equal(cooled.skipped[0].reason, 'comment-cooldown');
});
test('invalid recurrence config is rejected and cannot suppress at runtime', () => {
  for (const change of [{ eventId: '' }, { lastSeen: 'invalid' }, { issueNumber: 0 }]) {
    const entries = [{ ...ignore.entries[0], recurrence: { ...baseline, ...change } }];
    assert.ok(validateIgnore({ entries }).length > 0);
    assert.equal(partitionIgnore(entries, now).active.length, 0);
  }
  assert.ok(validateIgnore({ entries: [{ ...ignore.entries[0], match: { source: 'review' } }] }).length > 0);
});
test('unrelated Sentry issues use normal creation and the Android key stays separate', () => {
  const unrelated = candidate({ id: '7770002140' }, { eventID: nextId });
  const result = plan(unrelated);
  assert.equal(result.reopen.length, 0);
  assert.equal(result.create.length, 1);
});

function fakeGithub() {
  const writes = [];
  const reads = [];
  return {
    writes, reads,
    async listIssuesByLabel() { return []; },
    async getIssue(number) { reads.push(number); return { ...issue }; },
    async listIssueComments() { return []; },
    async ensureLabel() {},
    async reopen(number) { writes.push(['reopen', number]); },
    async addLabels() {},
    async comment(number) { writes.push(['comment', number]); },
    async createIssue() { throw new Error('duplicate creation forbidden'); },
    async findOpenIssueByTitle() { return { number: 1805 }; },
  };
}
test('canonical lookup survives label loss and 90-day lookback after expiry', async () => {
  const github = fakeGithub();
  const existing = await loadExisting(github, config, new Date('2027-02-01T00:00:00Z'), ignore);
  assert.deepEqual(github.reads, [2147]);
  assert.equal(existing[0].number, 2147);
});
test('canonical lookup failures propagate with zero writes', async () => {
  const github = fakeGithub();
  github.getIssue = async () => { throw new Error('GitHub GET issue failed: 403'); };
  await assert.rejects(runFiler({ config, ignore, github, now, loop: 'telemetry',
    collectors: [async () => [candidate({}, { eventID: nextId })]], stdout: () => {} }), /403/);
  assert.deepEqual(github.writes, []);
});
test('injected clients prove quiet baseline, canonical reopen/log and repeated-poll cooldown', async () => {
  const github = fakeGithub();
  const options = { config, ignore, github, now, loop: 'telemetry', stdout: () => {} };
  const quiet = await runFiler({ ...options, collectors: [async () => [candidate()]] });
  assert.equal(quiet.wrote, false);
  assert.deepEqual(github.writes, []);
  const next = candidate({}, { eventID: nextId });
  const recurring = await runFiler({ ...options, collectors: [async () => [next]] });
  assertReopen(recurring.plan);
  assert.deepEqual(github.writes, [['reopen', 2147], ['comment', 2147], ['comment', 1805]]);
  github.getIssue = async () => ({ ...issue, state: 'open' });
  // Use the real comment marker produced by the templates.
  const { buildComment } = await import('./templates.mjs');
  github.listIssueComments = async () => [{ body: buildComment(recurring.plan.comment[0]), created_at: now.toISOString() }];
  const repeated = await runFiler({ ...options, collectors: [async () => [next]] });
  assert.equal(repeated.wrote, false);
  assert.equal(github.writes.length, 3);
});
test('permanent collector error still prevents all writes with a recurrence in the plan', async () => {
  const github = fakeGithub();
  const result = await runFiler({ config, ignore, github, now, loop: 'telemetry',
    warnings: ['sentry: Sentry query rejected: is:unresolved is:new (HTTP 403)'],
    collectors: [async () => [candidate({}, { eventID: nextId })]], stdout: () => {} });
  assert.equal(result.collectFailed, true);
  assert.equal(result.wrote, false);
  assert.deepEqual(github.writes, []);
});
test('canonical GitHub GET normalizes closure and never writes', async () => {
  const calls = [];
  const github = createGitHubClient({ owner: 'owner', repo: 'repo', fetchImpl: async (url, init) => {
    calls.push([url, init.method]);
    return { ok: true, status: 200, headers: {}, text: async () => JSON.stringify({
      number: 2147, state: 'closed', state_reason: 'completed', body: issue.body, labels: ['bug'] }) };
  } });
  const found = await github.getIssue(2147);
  assert.equal(found.stateReason, 'completed');
  assert.deepEqual(calls, [['https://api.github.com/repos/owner/repo/issues/2147', 'GET']]);
});
