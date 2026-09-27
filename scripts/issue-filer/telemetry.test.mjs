import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadFilerFiles, repoRootFrom } from './config.mjs';
import {
  availabilityKey,
  captureProofCommand,
  candidateFromEvidence,
  correlateSignals,
  dependencyKey,
  exceptionKey,
  extractSentryPath,
  extractTraceId,
  ingestionKey,
  inAppFrames,
  keysForEvidence,
  matchFeatureMap,
  normalizeRoute,
  parseArgAlerts,
  parseEvidenceRows,
  parseSentryIssue,
  requestKey,
  routeMatchesTemplate,
  sentryKey,
  shouldDryRunScheduled,
  telemetrySourceLabels,
} from './telemetry.mjs';

const { config } = loadFilerFiles(repoRootFrom());

const featureMap = [
  {
    id: 'web.news.detail',
    area: 'news',
    url: '/news/{id}/{slug}',
    _surface: 'web',
  },
  {
    id: 'mobile.news.story',
    area: 'news',
    _surface: 'mobile',
  },
];

test('normalizeRoute replaces numeric and GUID segments only', () => {
  assert.equal(normalizeRoute('GET /news/1003/modernisation-begins'), '/news/{id}/modernisation-begins');
  assert.equal(
    normalizeRoute('/forum/topic/2b2d4c6e-1f3a-4b5c-8d9e-0a1b2c3d4e5f/slug'),
    '/forum/topic/{id}/slug',
  );
  assert.equal(normalizeRoute('https://www.queenzone.org/api/v1/news/12?x=1'), '/api/v1/news/{id}');
  assert.equal(normalizeRoute('GET News/Detail'), 'news/detail');
  assert.equal(normalizeRoute(''), '');
});

test('dedupe keys follow the architecture lock', () => {
  assert.equal(sentryKey(12345), 'sentry:12345');
  assert.equal(exceptionKey('System.NullReferenceException at Home'), 'ai:exc:System.NullReferenceException_at_Home');
  assert.equal(requestKey('GET /news/1003/foo'), 'ai:req:/news/{id}/foo:5xx');
  assert.equal(dependencyKey('SQL', 'queenzone-db'), 'ai:dep:SQL:queenzone-db');
  assert.equal(availabilityKey('qz-prod-health'), 'ai:avail:qz-prod-health');
  assert.equal(ingestionKey(), 'ai:ingest:cap');
});

test('feature-map matching uses the #1800 URL templates', () => {
  assert.equal(routeMatchesTemplate('/news/1003/modernisation-begins', '/news/{id}/{slug}'), true);
  const hit = matchFeatureMap('/news/12/launch', featureMap, 'web');
  assert.equal(hit.id, 'web.news.detail');
  assert.equal(hit.area, 'news');
  assert.equal(matchFeatureMap('/not-a-page/1', featureMap, 'web'), null);
  assert.match(captureProofCommand('web.news.detail'), /verify-queenzone\/scripts\/control-queenzone.ps1 capture-proof -Feature web.news.detail/);
  assert.match(captureProofCommand('mobile.photos.viewer'), /verify-queenzone-mobile/);
  assert.match(captureProofCommand(''), /Not mapped/);
});

test('scheduled runs stay dry unless TELEMETRY_TRIAGE_FILE_ISSUES is true', () => {
  assert.equal(shouldDryRunScheduled({ eventName: 'schedule', fileIssues: '' }), true);
  assert.equal(shouldDryRunScheduled({ eventName: 'schedule', fileIssues: 'true' }), false);
  assert.equal(shouldDryRunScheduled({ eventName: 'workflow_dispatch', dispatchDryRun: true }), true);
  assert.equal(shouldDryRunScheduled({ eventName: 'workflow_dispatch', dispatchDryRun: false }), false);
  assert.equal(shouldDryRunScheduled({ eventName: 'workflow_dispatch', dispatchDryRun: 'false' }), false);
});

test('parseArgAlerts keeps only fired qz-prod-* rules and ignores other alerts', () => {
  const alerts = parseArgAlerts({
    data: [
      {
        id: '/alerts/1',
        alertRule: '/subscriptions/x/resourceGroups/Queenzone-RG/providers/microsoft.insights/scheduledqueryrules/qz-prod-server-5xx',
        startDateTime: '2026-09-27T10:00:00Z',
        essentials: { monitorCondition: 'Fired' },
      },
      {
        id: '/alerts/2',
        alertRule: 'some-other-rule',
        essentials: { monitorCondition: 'Fired' },
      },
      {
        id: '/alerts/3',
        alertRule: 'qz-prod-exception-spike',
        essentials: { monitorCondition: 'Resolved' },
      },
    ],
  });
  assert.equal(alerts.length, 1);
  assert.equal(alerts[0].rule, 'qz-prod-server-5xx');
  assert.deepEqual(parseArgAlerts({ data: [] }), []);
  assert.deepEqual(parseArgAlerts([]), []);
});

test('parseEvidenceRows builds request and exception keys', () => {
  const requests = parseEvidenceRows('qz-prod-server-5xx', [{
    Name: 'GET /news/44/story',
    ResultCode: 500,
    operation_Ids: ['abc', 'def'],
    ItemCount: 6,
    lastSeen: '2026-09-27T10:05:00Z',
    AppVersion: '1.2.3',
  }]);
  assert.equal(requests[0].keys[0], 'ai:req:/news/{id}/story:5xx');
  assert.equal(requests[0].resultCode, 500);
  assert.deepEqual(requests[0].operationIds, ['abc', 'def']);
  assert.equal(keysForEvidence('qz-prod-exception-new-problem', { ProblemId: 'NullRef' })[0], 'ai:exc:NullRef');
  assert.deepEqual(parseEvidenceRows('qz-prod-server-5xx', [{ Name: '', ResultCode: 500 }]), []);
});

test('Sentry parsing uses extra.path or the last api breadcrumb with status >= 500', () => {
  const extraEvent = {
    extra: { path: '/api/v1/news/9' },
    release: 'mobile@1.0.0',
    contexts: { trace: { trace_id: 'trace-1' } },
    entries: [{
      data: {
        values: [{
          stacktrace: {
            frames: [
              { filename: 'node_modules/react-native/index.js', in_app: false },
              { filename: 'src/QueenZone.Mobile/src/screens/news/NewsStoryScreen.tsx', lineno: 40, function: 'load', in_app: true },
            ],
          },
        }],
      },
    }],
  };
  assert.equal(extractSentryPath(extraEvent), '/api/v1/news/9');
  assert.equal(extractTraceId(extraEvent), 'trace-1');
  assert.equal(inAppFrames(extraEvent)[0].filename.includes('NewsStoryScreen'), true);

  const crumbEvent = {
    breadcrumbs: {
      values: [
        { category: 'api', data: { path: '/api/v1/old/1', status: 500 } },
        { category: 'api', data: { path: '/api/v1/news/3', status: 503 } },
        { category: 'navigation', data: { path: '/home' } },
      ],
    },
  };
  assert.equal(extractSentryPath(crumbEvent), '/api/v1/news/3');

  const candidate = parseSentryIssue({
    id: '555',
    title: 'TypeError: failed',
    count: 1,
    lastSeen: '2026-09-27T10:00:00Z',
    firstSeen: '2026-09-27T09:50:00Z',
    permalink: 'https://sentry.io/issues/555',
  }, extraEvent, {
    since: new Date('2026-09-27T08:00:00Z'),
    areas: config.areas,
    featureMap,
    areaForFile: (file, areas) => {
      if (String(file).includes('/news/')) {
        return 'news';
      }
      return 'unknown';
    },
    deployedTip: 'abc123',
  });
  assert.equal(candidate.keys[0], 'sentry:555');
  assert.equal(candidate.route, '/api/v1/news/{id}');
  assert.equal(candidate.area, 'news');
  assert.equal(candidate.count, 1);
  assert.equal(candidate.release, 'mobile@1.0.0');

  assert.equal(parseSentryIssue({
    id: '9',
    title: 'old',
    lastSeen: '2026-09-26T00:00:00Z',
  }, {}, { since: new Date('2026-09-27T08:00:00Z') }), null);
});

test('correlation prefers a shared operation_Id over an endpoint match', () => {
  const sentryRoute = {
    source: 'sentry',
    keys: ['sentry:1'],
    title: '[sentry] boom',
    area: 'news',
    route: '/news/{id}/story',
    lastSeen: '2026-09-27T10:00:00Z',
    count: 2,
    resultCode: 0,
    operationId: 'shared-trace',
    operationIds: ['shared-trace'],
    evidence: [{ text: 'sentry' }],
  };
  const otherSentry = {
    ...sentryRoute,
    keys: ['sentry:2'],
    title: '[sentry] other',
    operationId: '',
    operationIds: [],
    lastSeen: '2026-09-27T10:04:00Z',
  };
  const matchingRequest = candidateFromEvidence({
    rule: 'qz-prod-server-5xx',
    kind: 'request',
    keys: ['ai:req:/news/{id}/story:5xx'],
    operationName: 'GET /news/9/story',
    route: '/news/{id}/story',
    resultCode: 500,
    operationIds: ['shared-trace'],
    count: 5,
    lastSeen: '2026-09-27T10:01:00Z',
    release: 'web',
    roleInstance: 'slot',
    problemId: '',
    dependencyType: '',
    target: '',
    test: '',
  }, { featureMap, deployedTip: 'tip' });
  const otherRequest = {
    ...matchingRequest,
    keys: ['ai:req:/photos/{id}:5xx'],
    route: '/photos/{id}',
    operationId: 'other',
    operationIds: ['other'],
    lastSeen: '2026-09-27T10:03:00Z',
  };

  const byTrace = correlateSignals([sentryRoute], [otherRequest, matchingRequest]);
  assert.equal(byTrace.length, 2);
  const correlated = byTrace.find((item) => item.kind === 'correlated');
  assert.equal(correlated.correlatedBy, 'operation_Id');
  assert.deepEqual(correlated.keys.sort(), ['ai:req:/news/{id}/story:5xx', 'sentry:1']);
  assert.ok(correlated.evidence.some((item) => item.text === 'sentry'));

  const byRoute = correlateSignals([otherSentry], [matchingRequest]);
  assert.equal(byRoute[0].correlatedBy, 'route');
  assert.ok(byRoute[0].keys.includes('sentry:2'));

  const tooOld = correlateSignals([{
    ...otherSentry,
    lastSeen: '2026-09-27T09:00:00Z',
  }], [matchingRequest]);
  assert.equal(tooOld.length, 2);
  assert.ok(tooOld.every((item) => item.kind !== 'correlated'));
});

test('telemetry source labels cover both sides of a correlated issue', () => {
  assert.deepEqual(telemetrySourceLabels({ source: 'sentry', keys: ['sentry:1'] }), ['from-sentry']);
  assert.deepEqual(telemetrySourceLabels({ source: 'appinsights', keys: ['ai:req:/x:5xx'] }), ['from-appinsights']);
  assert.deepEqual(telemetrySourceLabels({
    source: 'telemetry',
    keys: ['sentry:1', 'ai:exc:Null'],
  }), ['from-sentry', 'from-appinsights']);
});
