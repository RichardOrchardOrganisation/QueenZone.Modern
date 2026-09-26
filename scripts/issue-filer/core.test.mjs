import { test } from 'node:test';
import assert from 'node:assert/strict';
import { countRecentFilings, findMatch, planFilings, rankCandidates } from './core.mjs';
import { loadFilerFiles, repoRootFrom } from './config.mjs';

const now = new Date('2026-09-26T08:00:00Z');
const { config } = loadFilerFiles(repoRootFrom());

function candidate(overrides = {}) {
  return {
    source: 'review',
    keys: ['review:csharp.regex-timeout'],
    rule: 'csharp.regex-timeout',
    title: '[review] csharp.regex-timeout (2 PRs)',
    area: 'web',
    evidence: [{ url: 'https://example.test/1', text: '#1' }],
    count: 2,
    firstSeen: '2026-09-20T00:00:00Z',
    lastSeen: '2026-09-22T00:00:00Z',
    level: 'L2',
    ...overrides,
  };
}

function existing(overrides = {}) {
  return {
    number: 50,
    title: 'old',
    body: '<!-- qz-filer v=1 keys=review:csharp.regex-timeout source=review -->',
    state: 'open',
    stateReason: '',
    closedAt: null,
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-01T00:00:00Z',
    user: 'alice',
    labels: ['gardener'],
    ...overrides,
  };
}

test('empty candidates stay silent', () => {
  const plan = planFilings({ candidates: [], existing: [], ignore: { entries: [] }, config, now });
  assert.deepEqual(plan.create, []);
  assert.deepEqual(plan.comment, []);
  assert.deepEqual(plan.reopen, []);
});

test('single occurrence stays silent', () => {
  const plan = planFilings({
    candidates: [candidate({ count: 1 })],
    existing: [],
    ignore: { entries: [] },
    config,
    now,
  });
  assert.equal(plan.create.length, 0);
  assert.equal(plan.skipped[0].reason, 'below-min-occurrences');
});

test('ignore match skips and expired ignore does not', () => {
  const ignore = {
    entries: [
      {
        match: { source: 'review', rule: 'csharp.regex-timeout' },
        reason: 'tracked elsewhere',
        owner: 'richardorchard',
        expires: '2026-09-01',
      },
    ],
  };
  const expired = planFilings({ candidates: [candidate()], existing: [], ignore, config, now });
  assert.equal(expired.create.length, 1);
  assert.equal(expired.expiredIgnores.length, 1);

  ignore.entries[0].expires = '2027-01-01';
  const active = planFilings({ candidates: [candidate()], existing: [], ignore, config, now });
  assert.equal(active.create.length, 0);
  assert.equal(active.skipped[0].reason, 'ignored');
});

test('open match comments unless cooldown or comment cap', () => {
  const plan = planFilings({
    candidates: [candidate()],
    existing: [existing()],
    ignore: { entries: [] },
    config,
    now,
  });
  assert.equal(plan.comment.length, 1);
  assert.equal(plan.comment[0].kind, 'update');

  const cooled = planFilings({
    candidates: [candidate()],
    existing: [existing({ lastFilerCommentAt: '2026-09-26T07:00:00Z' })],
    ignore: { entries: [] },
    config,
    now,
  });
  assert.equal(cooled.comment.length, 0);
  assert.equal(cooled.skipped[0].reason, 'comment-cooldown');
});

test('closed completed within 30 days reopens with regression', () => {
  const plan = planFilings({
    candidates: [candidate()],
    existing: [existing({
      state: 'closed',
      stateReason: 'completed',
      closedAt: '2026-09-20T00:00:00Z',
    })],
    ignore: { entries: [] },
    config,
    now,
  });
  assert.equal(plan.reopen.length, 1);
  assert.deepEqual(plan.reopen[0].labels, ['regression']);
  assert.equal(plan.comment[0].kind, 'regression');
  assert.equal(plan.create.length, 0);
});

test('closed as not planned never reopens', () => {
  const plan = planFilings({
    candidates: [candidate()],
    existing: [existing({ state: 'closed', stateReason: 'not_planned', closedAt: '2026-09-20T00:00:00Z' })],
    ignore: { entries: [] },
    config,
    now,
  });
  assert.equal(plan.reopen.length, 0);
  assert.equal(plan.create.length, 0);
  assert.equal(plan.skipped[0].reason, 'closed-not-planned');
  assert.equal(plan.skipped[0].suggestIgnore, true);
});

test('older closed issue files a new issue that links the old one', () => {
  const plan = planFilings({
    candidates: [candidate()],
    existing: [existing({
      number: 77,
      state: 'closed',
      stateReason: 'completed',
      closedAt: '2026-07-01T00:00:00Z',
    })],
    ignore: { entries: [] },
    config,
    now,
  });
  assert.equal(plan.create.length, 1);
  assert.equal(plan.create[0].previousIssue, 77);
});

test('gardener cap counts bot filings from this week', () => {
  const botIssues = [1, 2].map((number) => existing({
    number,
    keys: undefined,
    body: `<!-- qz-filer v=1 keys=review:other-${number} source=review -->`,
    user: 'github-actions[bot]',
    createdAt: '2026-09-25T00:00:00Z',
    labels: ['gardener'],
  }));
  assert.equal(countRecentFilings(botIssues, { config, loop: 'gardener', now }), 2);
  const plan = planFilings({
    candidates: [candidate()],
    existing: botIssues,
    ignore: { entries: [] },
    config,
    now,
    loop: 'gardener',
  });
  assert.equal(plan.create.length, 0);
  assert.equal(plan.skipped.at(-1).reason, 'cap');
});

test('telemetry caps at 3 per day and storms above 5 candidates', () => {
  const many = Array.from({ length: 6 }, (_, index) => candidate({
    keys: [`sentry:${index}`],
    title: `sentry ${index}`,
    source: 'sentry',
    count: 2,
  }));
  const storm = planFilings({
    candidates: many,
    existing: [],
    ignore: { entries: [] },
    config,
    now,
    loop: 'telemetry',
  });
  assert.equal(storm.create.length, 1);
  assert.equal(storm.create[0].candidate.storm, true);
  assert.match(storm.create[0].candidate.title, /Telemetry storm 2026-09-26/);

  const three = many.slice(0, 3);
  const under = planFilings({
    candidates: three,
    existing: [],
    ignore: { entries: [] },
    config,
    now,
    loop: 'telemetry',
  });
  assert.equal(under.create.length, 3);

  const botDay = [1, 2, 3].map((number) => existing({
    number,
    body: `<!-- qz-filer v=1 keys=sentry:${number} source=sentry -->`,
    user: 'github-actions[bot]',
    createdAt: '2026-09-26T01:00:00Z',
    labels: ['bug'],
  }));
  const capped = planFilings({
    candidates: three,
    existing: botDay,
    ignore: { entries: [] },
    config,
    now,
    loop: 'telemetry',
  });
  assert.equal(capped.create.length, 0);
});

test('ranking prefers higher count then lower level', () => {
  const ranked = rankCandidates([
    candidate({ keys: ['a'], title: 'a', count: 2, level: 'L1' }),
    candidate({ keys: ['b'], title: 'b', count: 4, level: 'L5' }),
    candidate({ keys: ['c'], title: 'c', count: 4, level: 'L2' }),
  ]);
  assert.deepEqual(ranked.map((item) => item.title), ['c', 'b', 'a']);
});

test('rule with an existing check is a check-gap, not a new filing', () => {
  const plan = planFilings({
    candidates: [candidate()],
    existing: [],
    ignore: { entries: [] },
    config,
    now,
    findingRules: [{ id: 'csharp.regex-timeout', title: 'Regex timeout', level: 'L2', check: 'scripts/check.mjs' }],
  });
  assert.equal(plan.create.length, 0);
  assert.equal(plan.skipped[0].reason, 'check-gap');
});

test('findMatch uses any overlapping key', () => {
  const issue = existing({ body: '<!-- qz-filer v=1 keys=review:other,review:csharp.regex-timeout source=review -->' });
  assert.equal(findMatch(candidate(), [issue]).number, 50);
});

test('comment cap is 10 per run', () => {
  const candidates = Array.from({ length: 12 }, (_, index) => candidate({
    keys: [`review:rule-${index}`],
    title: `rule ${index}`,
    rule: `docs.rule-${index}`,
  }));
  const issues = candidates.map((item, index) => existing({
    number: 100 + index,
    body: `<!-- qz-filer v=1 keys=${item.keys[0]} source=review -->`,
  }));
  const plan = planFilings({
    candidates,
    existing: issues,
    ignore: { entries: [] },
    config,
    now,
  });
  assert.equal(plan.comment.length, 10);
  assert.equal(plan.skipped.filter((item) => item.reason === 'comment-cap').length, 2);
});
