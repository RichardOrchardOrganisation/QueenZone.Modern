import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadFilerFiles, repoRootFrom } from '../config.mjs';
import {
  collect,
  createSentryFetch,
  defaultSentrySearch,
  SENTRY_MAX_RETRY_WAIT_MS,
  sentryNextPageUrl,
  sentryRetryDelayMs,
} from './telemetry.mjs';

const { config } = loadFilerFiles(repoRootFrom());
const since = new Date('2026-09-27T08:00:00Z');

test('telemetry collect correlates fixture Sentry and App Insights rows', async () => {
  const candidates = await collect({
    config,
    since,
    root: repoRootFrom(),
    sentryIssues: [{
      id: '42',
      title: 'API 500 on news',
      count: 3,
      lastSeen: '2026-09-27T10:00:00Z',
      firstSeen: '2026-09-27T09:50:00Z',
      permalink: 'https://sentry.io/issues/42',
    }],
    sentryEvents: {
      42: {
        extra: { path: '/news/1003/story' },
        release: 'mobile@9',
      },
    },
    appInsightsAlerts: [{ rule: 'qz-prod-server-5xx', id: 'alert-1' }],
    appInsightsEvidence: {
      'qz-prod-server-5xx': [{
        Name: 'GET /news/1003/story',
        ResultCode: 500,
        ItemCount: 5,
        lastSeen: '2026-09-27T10:02:00Z',
        operation_Ids: ['op-1'],
      }],
    },
    featureMap: [{
      id: 'web.news.detail',
      area: 'news',
      url: '/news/{id}/{slug}',
      _surface: 'web',
    }],
    deployedTip: 'deadbeef',
  });
  assert.equal(candidates.length, 1);
  assert.equal(candidates[0].kind, 'correlated');
  assert.ok(candidates[0].keys.includes('sentry:42'));
  assert.ok(candidates[0].keys.includes('ai:req:/news/{id}/story:5xx'));
  assert.equal(candidates[0].area, 'news');
});

test('zero fired alerts is a clean no-op on the App Insights side', async () => {
  const warnings = [];
  const candidates = await collect({
    config,
    since,
    warnings,
    root: repoRootFrom(),
    sentryIssues: [],
    appInsightsAlerts: [],
    appInsightsEvidence: {},
  });
  assert.deepEqual(candidates, []);
});

test('azure warning file is surfaced so a collect failure is not a silent no-op', async () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'telemetry-warn-'));
  const warnPath = path.join(dir, 'warnings.txt');
  writeFileSync(warnPath, 'azure-graph-failed\n');
  const warnings = [];
  const candidates = await collect({
    config,
    since,
    warnings,
    root: repoRootFrom(),
    sentryIssues: [{
      id: '42',
      title: 'API 500 on news',
      count: 3,
      lastSeen: '2026-09-27T10:00:00Z',
      firstSeen: '2026-09-27T09:50:00Z',
      permalink: 'https://sentry.io/issues/42',
    }],
    sentryEvents: {
      42: { extra: { path: '/news/1003/story' } },
    },
    appInsightsAlerts: [],
    appInsightsEvidence: {},
    azureWarningsPath: warnPath,
  });
  assert.deepEqual(candidates, []);
  assert.match(warnings[0], /azure-graph-failed/);
});

test('Sentry Link next URL is pinned to sentry.io', () => {
  assert.equal(
    String(sentryNextPageUrl('<https://sentry.io/api/0/issues/?cursor=1>; rel="next"')),
    'https://sentry.io/api/0/issues/?cursor=1',
  );
  assert.equal(sentryNextPageUrl('<https://evil.example/steal>; rel="next"'), '');
  assert.equal(sentryNextPageUrl('<http://127.0.0.1/x>; rel="next"'), '');
  assert.equal(sentryNextPageUrl('<https://sentry.io.evil.example/x>; rel="next"'), '');
  assert.equal(
    String(sentryNextPageUrl('<https://sentry.io/first>; rel="prev", <https://sentry.io/second>; results="true"; rel="next"')),
    'https://sentry.io/second',
  );
});

test('defaultSentrySearch refuses a Link next URL off sentry.io', async () => {
  const urls = [];
  const issues = await defaultSentrySearch({
    token: 't',
    query: 'is:unresolved is:new lastSeen:-2h',
    fetchImpl: async (url) => {
      urls.push(String(url));
      return {
        ok: true,
        json: async () => [{ id: '1' }],
        headers: {
          get: (name) => (String(name).toLowerCase() === 'link'
            ? '<https://evil.example/next>; rel="next"'
            : ''),
        },
      };
    },
  });
  assert.equal(issues.length, 1);
  assert.equal(urls.length, 1);
  assert.match(urls[0], /sentry\.io/);
  assert.match(urls[0], /query=is%3Aunresolved\+is%3Anew\+lastSeen%3A-2h/);
  assert.match(urls[0], /statsPeriod=14d/);
});

test('defaultSentrySearch runs new, regressed, and escalating queries and merges by id', async () => {
  const urls = [];
  const issues = await defaultSentrySearch({
    token: 't',
    lookbackHours: 2,
    minGapMs: 0,
    fetchImpl: async (url) => {
      urls.push(String(url));
      const query = new URL(url).searchParams.get('query');
      const payload = query.includes('is:new')
        ? [{ id: '1', title: 'new' }, { id: '2', title: 'shared' }]
        : query.includes('is:regressed')
          ? [{ id: '2', title: 'regressed' }, { id: '3', title: 'only-regressed' }]
          : [{ id: '2', title: 'escalating' }, { id: '4', title: 'only-escalating' }];
      return {
        ok: true,
        json: async () => payload,
        headers: { get: () => '' },
      };
    },
  });
  assert.equal(urls.length, 3);
  assert.ok(urls.some((url) => url.includes('is%3Anew')));
  assert.ok(urls.some((url) => url.includes('is%3Aregressed')));
  assert.ok(urls.some((url) => url.includes('is%3Aescalating')));
  assert.ok(urls.every((url) => !url.includes('OR')));
  assert.ok(urls.every((url) => url.includes('statsPeriod=14d')));
  assert.deepEqual(issues.map((issue) => issue.id), ['1', '2', '3', '4']);
  assert.equal(issues.find((issue) => issue.id === '2').title, 'shared');
});

test('defaultSentrySearch fails closed on escalating 4xx and does not keep earlier queries', async () => {
  const urls = [];
  await assert.rejects(
    () => defaultSentrySearch({
      token: 'secret-token-value',
      lookbackHours: 28,
      minGapMs: 0,
      fetchImpl: async (url) => {
        urls.push(String(url));
        const query = new URL(url).searchParams.get('query') || '';
        if (query.includes('is:escalating')) {
          return sentryJson(400, {
            detail: 'unknown filter Bearer secret-token-value',
          });
        }
        return sentryJson(200, [{ id: '1', title: 'kept-if-swallowed' }]);
      },
    }),
    (error) => {
      assert.equal(error.status, 400);
      assert.match(error.query, /is:unresolved is:escalating lastSeen:-28h/);
      assert.match(error.message, /Sentry query rejected: is:unresolved is:escalating lastSeen:-28h \(HTTP 400\)/);
      assert.doesNotMatch(error.message, /secret-token-value/);
      assert.doesNotMatch(error.message, /Authorization/i);
      return true;
    },
  );
  assert.ok(urls.some((url) => url.includes('is%3Anew')));
  assert.ok(urls.some((url) => url.includes('is%3Aescalating')));
});

test('defaultSentrySearch includes redacted Sentry detail on 400', async () => {
  await assert.rejects(
    () => defaultSentrySearch({
      token: 'super-secret-token-value',
      query: 'is:unresolved (is:new OR is:regressed)',
      fetchImpl: async () => ({
        ok: false,
        status: 400,
        json: async () => ({
          detail: 'Boolean statements containing "OR" or "AND" are not supported in this search Bearer super-secret-token-value',
        }),
        headers: { get: () => '' },
      }),
    }),
    (error) => {
      assert.match(error.message, /Sentry query rejected: is:unresolved \(is:new OR is:regressed\) \(HTTP 400\)/);
      assert.match(error.message, /Boolean statements containing "OR"/);
      assert.doesNotMatch(error.message, /super-secret-token-value/);
      assert.match(error.message, /\[token\]/);
      return true;
    },
  );
});

test('Sentry search errors fail closed and do not return partial Azure candidates', async () => {
  const warnings = [];
  const candidates = await collect({
    config,
    since,
    warnings,
    root: repoRootFrom(),
    sentrySearch: async () => {
      throw new Error('Sentry issues failed: 400: Boolean statements containing "OR" or "AND" are not supported in this search');
    },
    sentryToken: 'token',
    appInsightsAlerts: [{ rule: 'qz-prod-exception-spike' }],
    appInsightsEvidence: {
      'qz-prod-exception-spike': [{
        ProblemId: 'SqlException',
        ItemCount: 8,
        lastSeen: '2026-09-27T10:00:00Z',
        OperationName: 'GET /forum',
      }],
    },
  });
  assert.deepEqual(candidates, []);
  assert.match(warnings[0], /Sentry issues failed: 400/);
});

function sentryHeaders(map = {}) {
  const normalised = Object.fromEntries(
    Object.entries(map).map(([key, value]) => [key.toLowerCase(), value]),
  );
  return {
    get: (name) => normalised[String(name).toLowerCase()] || '',
  };
}

function sentryJson(status, data, headers = {}) {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => data,
    text: async () => JSON.stringify(data),
    headers: sentryHeaders(headers),
  };
}

function assertDelayWithinCap(ms) {
  assert.equal(Number.isFinite(ms), true);
  assert.ok(ms >= 0);
  assert.ok(ms <= SENTRY_MAX_RETRY_WAIT_MS);
}

test('sentryRetryDelayMs prefers Retry-After seconds, then X-Sentry-Rate-Limit-Reset', () => {
  assert.equal(sentryRetryDelayMs(sentryHeaders({ 'Retry-After': '2' }), { now: 0 }), 2000);
  assert.equal(
    sentryRetryDelayMs(sentryHeaders({
      'Retry-After': 'Wed, 21 Oct 2015 07:28:01 GMT',
    }), { now: Date.parse('Wed, 21 Oct 2015 07:28:00 GMT') }),
    1000,
  );
  assert.equal(
    sentryRetryDelayMs(sentryHeaders({ 'X-Sentry-Rate-Limit-Reset': '1005' }), { now: 1_000_000 }),
    5000,
  );
  assert.equal(sentryRetryDelayMs(sentryHeaders(), { attempt: 1 }), 1000);
  assert.equal(sentryRetryDelayMs(sentryHeaders(), { attempt: 2 }), 2000);
});

test('sentryRetryDelayMs clamps header delays to [0, cap]', () => {
  const now = 1_000_000;
  const cases = [
    sentryRetryDelayMs(sentryHeaders(), { now, attempt: 1 }),
    sentryRetryDelayMs(sentryHeaders({ 'Retry-After': '-3' }), { now, attempt: 1 }),
    sentryRetryDelayMs(sentryHeaders({ 'Retry-After': 'NaN' }), { now, attempt: 1 }),
    sentryRetryDelayMs(sentryHeaders({
      'Retry-After': 'Wed, 21 Oct 2015 07:28:00 GMT',
    }), { now: Date.parse('Wed, 21 Oct 2015 07:28:01 GMT') }),
    sentryRetryDelayMs(sentryHeaders({ 'Retry-After': '86400' }), { now }),
    sentryRetryDelayMs(sentryHeaders({ 'X-Sentry-Rate-Limit-Reset': '9999999999' }), { now }),
    sentryRetryDelayMs(sentryHeaders({
      'Retry-After': 'Wed, 21 Oct 2099 07:28:01 GMT',
    }), { now }),
  ];
  for (const delay of cases) {
    assertDelayWithinCap(delay);
  }
  assert.equal(sentryRetryDelayMs(sentryHeaders({ 'Retry-After': '86400' }), { now }), SENTRY_MAX_RETRY_WAIT_MS);
  assert.equal(
    sentryRetryDelayMs(sentryHeaders({ 'X-Sentry-Rate-Limit-Reset': '9999999999' }), { now }),
    SENTRY_MAX_RETRY_WAIT_MS,
  );
  assert.equal(
    sentryRetryDelayMs(sentryHeaders({
      'Retry-After': 'Wed, 21 Oct 2099 07:28:01 GMT',
    }), { now }),
    SENTRY_MAX_RETRY_WAIT_MS,
  );
});

test('defaultSentrySearch retries a 429 then succeeds', async () => {
  const sleeps = [];
  let calls = 0;
  const issues = await defaultSentrySearch({
    token: 't',
    query: 'is:unresolved is:new lastSeen:-2h',
    minGapMs: 0,
    sleep: async (ms) => {
      sleeps.push(ms);
    },
    fetchImpl: async () => {
      calls += 1;
      if (calls === 1) {
        return sentryJson(429, { detail: 'Limit is 5 requests in 1 seconds' }, { 'Retry-After': '1' });
      }
      return sentryJson(200, [{ id: '9', title: 'recovered' }]);
    },
  });
  assert.equal(calls, 2);
  assert.deepEqual(sleeps, [1000]);
  assert.equal(issues.length, 1);
  assert.equal(issues[0].id, '9');
});

test('persistent Sentry 429 fails closed and files nothing', async () => {
  const warnings = [];
  let calls = 0;
  const candidates = await collect({
    config,
    since,
    warnings,
    root: repoRootFrom(),
    sentryToken: 'super-secret-token-value',
    minGapMs: 0,
    maxAttempts: 3,
    sleep: async () => {},
    fetchImpl: async () => {
      calls += 1;
      return sentryJson(429, {
        detail: 'You are attempting to use this endpoint too frequently. Limit is 5 requests in 1 seconds Bearer super-secret-token-value',
      });
    },
    appInsightsAlerts: [{ rule: 'qz-prod-exception-spike' }],
    appInsightsEvidence: {
      'qz-prod-exception-spike': [{
        ProblemId: 'SqlException',
        ItemCount: 8,
        lastSeen: '2026-09-27T10:00:00Z',
        OperationName: 'GET /forum',
      }],
    },
  });
  assert.equal(calls, 3);
  assert.deepEqual(candidates, []);
  assert.match(warnings[0], /Sentry query rejected: is:unresolved is:new lastSeen:-28h \(HTTP 429\)/);
  assert.match(warnings[0], /Limit is 5 requests in 1 seconds/);
  assert.doesNotMatch(warnings[0], /super-secret-token-value/);
});

test('collect with ctx.now as a Date still issues Sentry requests', async () => {
  const warnings = [];
  const urls = [];
  const candidates = await collect({
    config,
    since,
    warnings,
    root: repoRootFrom(),
    now: new Date('2026-09-27T10:00:00Z'),
    sentryToken: 'token',
    minGapMs: 0,
    sleep: async () => {},
    fetchImpl: async (url) => {
      urls.push(String(url));
      if (String(url).includes('/events/latest/')) {
        return sentryJson(200, { extra: { path: '/news/1003/story' } });
      }
      return sentryJson(200, [{
        id: '77',
        title: 'API 500 on news',
        count: 2,
        lastSeen: '2026-09-27T10:00:00Z',
        firstSeen: '2026-09-27T09:50:00Z',
        permalink: 'https://sentry.io/issues/77',
      }]);
    },
    appInsightsAlerts: [],
    appInsightsEvidence: {},
  });
  assert.ok(urls.some((url) => url.includes('/issues/')));
  assert.ok(urls.some((url) => url.includes('/events/latest/')));
  assert.equal(candidates.length, 1);
  assert.ok(candidates[0].keys.includes('sentry:77'));
  assert.equal(warnings.some((warning) => /now is not a function/.test(warning)), false);
  assert.deepEqual(warnings, []);
});

test('Sentry requests are not issued concurrently', async () => {
  let inFlight = 0;
  let maxInFlight = 0;
  const started = [];
  const sentryFetch = createSentryFetch({
    minGapMs: 0,
    sleep: async () => {},
    fetchImpl: async (url) => {
      inFlight += 1;
      maxInFlight = Math.max(maxInFlight, inFlight);
      started.push(String(url));
      await new Promise((resolve) => {
        setTimeout(resolve, 20);
      });
      inFlight -= 1;
      return sentryJson(200, [{ id: String(started.length) }]);
    },
  });
  await Promise.all([
    defaultSentrySearch({
      token: 't',
      query: 'is:unresolved is:new lastSeen:-2h',
      sentryFetch,
    }),
    defaultSentrySearch({
      token: 't',
      query: 'is:unresolved is:regressed lastSeen:-2h',
      sentryFetch,
    }),
    sentryFetch('https://sentry.io/api/0/issues/1/events/latest/'),
  ]);
  assert.equal(maxInFlight, 1);
  assert.equal(started.length, 3);
});
