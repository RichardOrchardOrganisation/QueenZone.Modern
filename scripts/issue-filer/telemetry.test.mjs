import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadFilerFiles, repoRootFrom } from './config.mjs';
import {
  availabilityKey,
  buildSentryIssuesUrl,
  captureProofCommand,
  candidateFromEvidence,
  correlateSignals,
  dependencyKey,
  dimensionWhere,
  evidenceKqlForAlert,
  exceptionKey,
  extractSentryPath,
  extractTraceId,
  formatSentryIssuesError,
  ingestionKey,
  inAppFrames,
  isTelemetryCollectFailure,
  keysForEvidence,
  matchFeatureMap,
  mergeSentryIssuesById,
  normalizeRoute,
  parseArgAlerts,
  parseEvidenceRows,
  parseLookbackHours,
  parseSentryIssue,
  redact,
  argAlertsQuery,
  evidenceTimespan,
  sentryStatsPeriod,
  SCHEDULE_CADENCE_HOURS,
  TELEMETRY_WINDOW_HOURS,
  WINDOW_OVERLAP_HOURS,
  requestKey,
  routeMatchesTemplate,
  sentryErrorDetail,
  sentryKey,
  sentrySearchQueries,
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

test('redact masks emails, addresses, tokens, connection-string pairs, and long secrets', () => {
  assert.equal(redact('mail user@example.com please'), 'mail [email] please');
  assert.equal(redact('release mobile@1.0.0'), 'release mobile@1.0.0');
  assert.equal(redact('mail user@example.com.'), 'mail [email]');
  assert.equal(redact('from 203.0.113.10'), 'from [ip]');
  assert.equal(redact('v6 2001:0db8:85a3:0000:0000:8a2e:0370:7334'), 'v6 [ip]');
  assert.equal(redact('v6 invalid 2001:xyz'), 'v6 invalid 2001:xyz');
  assert.equal(redact('jwt eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxIn0.sig'), 'jwt [token]');
  assert.equal(redact('auth Bearer abcdefghijklmnop'), 'auth [token]');
  assert.equal(redact('key sk-abcdefghijklmnopqrstuvwxyz1234'), 'key [token]');
  assert.equal(redact('Password=hunter2;AccountKey=abcdEF1234;SharedAccessSignature=sv=2020'), 'Password=[secret];AccountKey=[secret];SharedAccessSignature=[secret]');
  assert.equal(redact('hex 0123456789abcdef0123456789abcdef'), 'hex [secret]');
  assert.equal(redact('b64 YWJjZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXoxMjM0NTY='), 'b64 [secret]');
  const long = redact(`prefix ${'x'.repeat(200)}`);
  assert.ok(long.length <= 120);
  assert.match(long, /…$/);
});

test('redact masks every connection credential and location alias without exposing nested values', () => {
  const keys = ['Password', 'Pwd', 'Pass', 'User ID', 'UserId', 'Username', 'UID', 'AccountKey',
    'SharedAccessSignature', 'SharedAccessKey', 'Server', 'Data Source', 'Initial Catalog', 'Database'];
  for (const key of keys) {
    for (const spelling of [key, key.toLowerCase(), key.toUpperCase()]) {
      assert.equal(redact(`${spelling}=privateValue;`, { maxLength: 0 }), `${spelling}=[secret];`);
    }
  }
  assert.equal(redact('Password=Database=privateValue;Server=host', { maxLength: 0 }), 'Password=[secret];Server=[secret]');
  assert.equal(redact('bEaReR MixedCASE_123456789'), '[token]');
});

test('dedupe keys follow the architecture lock', () => {
  assert.equal(sentryKey(12345), 'sentry:12345');
  assert.equal(exceptionKey('System.NullReferenceException at Home'), 'ai:exc:System.NullReferenceException_at_Home');
  assert.equal(requestKey('GET /news/1003/foo'), 'ai:req:/news/{id}/foo:5xx');
  assert.equal(requestKey('GET /news/1003/foo', 'p95'), 'ai:req:/news/{id}/foo:p95');
  assert.equal(keysForEvidence('qz-prod-request-p95', { Name: 'GET /news/1003/foo' })[0], 'ai:req:/news/{id}/foo:p95');
  assert.equal(keysForEvidence('qz-prod-exception-spike', { ProblemId: 'spike' })[0], 'ai:exc:spike');
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

test('Sentry issue search splits new, regressed, and escalating and omits OR', () => {
  const queries = sentrySearchQueries({ lookbackHours: 26 });
  assert.deepEqual(queries, [
    'is:unresolved is:new lastSeen:-26h',
    'is:unresolved is:regressed lastSeen:-26h',
    'is:unresolved is:escalating lastSeen:-26h',
  ]);
  for (const query of queries) {
    assert.doesNotMatch(query, /\bOR\b|\bAND\b|[()]/);
    const url = buildSentryIssuesUrl({ query, statsPeriod: sentryStatsPeriod(26) });
    assert.equal(url.pathname, '/api/0/projects/self-0tb/queenzone-mobile/issues/');
    assert.equal(url.searchParams.get('query'), query);
    assert.equal(url.searchParams.get('limit'), '25');
    assert.equal(url.searchParams.get('statsPeriod'), '26h');
  }
});

test('telemetry window is cadence plus overlap and drives ARG and evidence spans', () => {
  assert.equal(SCHEDULE_CADENCE_HOURS, 24);
  assert.equal(WINDOW_OVERLAP_HOURS, 4);
  assert.equal(TELEMETRY_WINDOW_HOURS, 28);
  assert.equal(sentryStatsPeriod(TELEMETRY_WINDOW_HOURS), '28h');
  assert.match(argAlertsQuery(TELEMETRY_WINDOW_HOURS), /ago\(28h\)/);
  assert.doesNotMatch(argAlertsQuery(TELEMETRY_WINDOW_HOURS), /ago\(2h\)/);
  assert.equal(evidenceTimespan(TELEMETRY_WINDOW_HOURS), 'PT28H');
  assert.deepEqual(sentrySearchQueries(), [
    'is:unresolved is:new lastSeen:-28h',
    'is:unresolved is:regressed lastSeen:-28h',
    'is:unresolved is:escalating lastSeen:-28h',
  ]);
  assert.equal(parseLookbackHours(undefined, { fallback: TELEMETRY_WINDOW_HOURS }), 28);
  assert.equal(parseLookbackHours('12'), 12);
  assert.throws(() => parseLookbackHours('0'), /lookback-hours/);
  assert.throws(() => parseLookbackHours('169'), /lookback-hours/);
  assert.throws(() => parseLookbackHours('1.5'), /lookback-hours/);
});

test('sentryErrorDetail uses the detail field, truncated and redacted', () => {
  assert.equal(
    sentryErrorDetail('{"detail":"Boolean statements containing \\"OR\\" or \\"AND\\" are not supported in this search"}'),
    'Boolean statements containing "OR" or "AND" are not supported in this search',
  );
  assert.equal(
    sentryErrorDetail(JSON.stringify({ detail: { message: 'nope Bearer abcdefghijklmnop' } })),
    'nope [token]',
  );
  assert.equal(sentryErrorDetail('{"message":"ignore me"}'), '');
  const long = sentryErrorDetail(JSON.stringify({ detail: `prefix ${'x'.repeat(200)}` }));
  assert.ok(long.length <= 120);
  assert.match(long, /…$/);
  assert.equal(
    formatSentryIssuesError(400, '{"detail":"Boolean statements containing \\"OR\\" or \\"AND\\" are not supported in this search"}'),
    'Sentry issues failed: 400: Boolean statements containing "OR" or "AND" are not supported in this search',
  );
  assert.equal(formatSentryIssuesError(400, ''), 'Sentry issues failed: 400');
});

test('isTelemetryCollectFailure treats Sentry and Azure source errors as fatal', () => {
  assert.equal(isTelemetryCollectFailure('sentry: Sentry issues failed: 400: Boolean statements'), true);
  assert.equal(isTelemetryCollectFailure('sentry: SENTRY_TRIAGE_TOKEN is not set'), false);
  assert.equal(isTelemetryCollectFailure('sentry-event: timeout'), false);
  assert.equal(isTelemetryCollectFailure('azure: azure-graph-failed'), true);
  assert.equal(isTelemetryCollectFailure('azure: azure-workspace-failed'), true);
  assert.equal(isTelemetryCollectFailure('azure: azure-kql-failed:qz-prod-server-5xx'), true);
  assert.equal(isTelemetryCollectFailure('azure: azure-login-failed'), true);
  assert.equal(isTelemetryCollectFailure('azure: azure-arm-vars-missing'), true);
  assert.equal(isTelemetryCollectFailure('azure: azure-kql-unscoped:qz-prod-server-5xx'), false);
});

test('mergeSentryIssuesById keeps the first row per id', () => {
  assert.deepEqual(
    mergeSentryIssuesById([[{ id: '1', title: 'a' }], [{ id: '1', title: 'b' }, { id: '2' }]]),
    [{ id: '1', title: 'a' }, { id: '2' }],
  );
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

test('evidence KQL is scoped to the fired alert dimension', () => {
  const scoped = evidenceKqlForAlert({
    rule: 'qz-prod-exception-new-problem',
    dimensions: { ProblemId: 'System.NullReferenceException' },
  });
  assert.match(scoped, /ProblemId == 'System\.NullReferenceException'/);
  assert.equal(evidenceKqlForAlert({ rule: 'qz-prod-exception-new-problem', dimensions: {} }), '');
  assert.equal(dimensionWhere({
    rule: 'qz-prod-server-5xx',
    dimensions: { Name: 'GET /news/1/story' },
  }), "Name == 'GET /news/1/story'");
  assert.match(evidenceKqlForAlert({
    rule: 'qz-prod-dependency-failures',
    dimensions: { DependencyType: 'SQL', Target: 'queenzone-db' },
  }), /DependencyType == 'SQL' and Target == 'queenzone-db'/);
  assert.match(evidenceKqlForAlert({ rule: 'qz-prod-exception-spike' }), /ProblemId='spike'/);
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
    shortId: 'QUEENZONE-MOBILE-E',
    userCount: 4,
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
  assert.equal(candidate.shortId, 'QUEENZONE-MOBILE-E');
  assert.equal(candidate.userCount, 4);
  assert.equal(candidate.route, '/api/v1/news/{id}');
  assert.equal(candidate.area, 'news');
  assert.equal(candidate.count, 1);
  assert.equal(candidate.release, 'mobile@1.0.0');
  assert.equal(parseSentryIssue({
    id: 'leak',
    title: 'crash user@example.com',
    lastSeen: '2026-09-27T10:00:00Z',
  }, {}, { since: new Date('2026-09-27T08:00:00Z') }).title, '[sentry] crash [email]');

  assert.equal(parseSentryIssue({
    id: '9',
    title: 'old',
    lastSeen: '2026-09-26T00:00:00Z',
  }, {}, { since: new Date('2026-09-27T08:00:00Z') }), null);

  const escalating = parseSentryIssue({
    id: '777',
    shortId: 'QUEENZONE-MOBILE-E',
    userCount: 9,
    title: 'escalating TypeError',
    count: 12,
    firstSeen: '2026-01-01T00:00:00Z',
    lastSeen: '2026-09-27T10:00:00Z',
    permalink: 'https://sentry.io/issues/777',
  }, {}, { since: new Date('2026-09-27T08:00:00Z') });
  assert.equal(escalating.keys[0], 'sentry:777');
  assert.equal(escalating.shortId, 'QUEENZONE-MOBILE-E');
  assert.equal(escalating.userCount, 9);
  assert.equal(escalating.firstSeen, '2026-01-01T00:00:00Z');
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
