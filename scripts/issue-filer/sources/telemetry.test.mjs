import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadFilerFiles, repoRootFrom } from '../config.mjs';
import { collect, defaultSentrySearch, sentryNextPageUrl } from './telemetry.mjs';

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
    sentryIssues: [],
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
});

test('Sentry search errors are swallowed so App Insights can still file', async () => {
  const warnings = [];
  const candidates = await collect({
    config,
    since,
    warnings,
    root: repoRootFrom(),
    sentrySearch: async () => {
      throw new Error('sentry down');
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
  assert.equal(candidates.length, 1);
  assert.equal(candidates[0].keys[0], 'ai:exc:SqlException');
  assert.match(warnings[0], /sentry down/);
});
