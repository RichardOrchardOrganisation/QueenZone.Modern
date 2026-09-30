/**
 * Telemetry triage helpers for #1805. Pure parsing, route normalisation,
 * correlation, and dedupe-key construction. The shared filer still owns
 * markers, caps, ignore, reopen, and comments.
 */
import { isIP } from 'node:net';

const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const NUMERIC_RE = /^\d+$/;
const METHOD_RE = /^(GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)\s+/i;
const CORRELATE_WINDOW_MS = 10 * 60 * 1000;
// Issues search rejects OR/AND/parentheses (docs.sentry.io/concepts/search/
// "Using OR and AND" — those operators are only for Explore, Dashboards, and
// Monitors). Split new vs regressed and merge by id. lastSeen:-Nh is the
// documented 2h lookback (docs.sentry.io/concepts/search/searchable-properties/issues/).
const SENTRY_STATUS_FILTERS = ['is:unresolved is:new', 'is:unresolved is:regressed'];
const DEFAULT_SENTRY_HOST = 'https://sentry.io';
const DEFAULT_SENTRY_ORG = 'self-0tb';
const DEFAULT_SENTRY_PROJECT = 'queenzone-mobile';
const SENTRY_ISSUES_LIMIT = 25;
const SENTRY_STATS_PERIOD = '24h';

export const LOOKBACK_HOURS = 2;
export const REDACT_MAX_LENGTH = 120;

const NON_WHITESPACE_RE = /\S+/g;
const IPV4_RE = /\b(?:\d{1,3}\.){3}\d{1,3}\b/g;
const IPV6_CANDIDATE_RE = /[0-9A-Fa-f:]+/g;
const JWT_RE = /\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b/g;
const BEARER_RE = /\b(?:Bearer|token)\s+[A-Za-z0-9._\-+/=]{8,}/gi;
const API_KEY_RE = /\b(?:sk|pk|ghp|gho|github_pat|AIza)[-_][A-Za-z0-9_-]{16,}\b/g;
const CONN_PAIR_RE = /\b(?:Password|Pwd|Pass|User\s*ID|User\s*Id|Username|UID|AccountKey|SharedAccessSignature|SharedAccessKey|Server|Data\s*Source|Initial\s*Catalog|Database)\s*=\s*[^;\s]+/gi;
const LONG_HEX_RE = /\b[0-9A-Fa-f]{32,}\b/g;
const LONG_B64_RE = /\b[A-Za-z0-9+/]{32,}={1,2}(?![A-Za-z0-9+/=])/g;

export function redact(text, { maxLength = REDACT_MAX_LENGTH } = {}) {
  let value = String(text ?? '');
  value = value.replace(NON_WHITESPACE_RE, (candidate) => {
    const at = candidate.indexOf('@');
    if (at < 1) {
      return candidate;
    }
    const domain = candidate.slice(at + 1);
    return domain.split('.').slice(1).some((part) => /^[A-Za-z]{2,}/.test(part))
      ? '[email]'
      : candidate;
  });
  value = value.replace(IPV4_RE, '[ip]');
  value = value.replace(IPV6_CANDIDATE_RE, (candidate) => isIP(candidate) === 6 ? '[ip]' : candidate);
  value = value.replace(BEARER_RE, '[token]');
  value = value.replace(JWT_RE, '[token]');
  value = value.replace(CONN_PAIR_RE, (match) => `${match.split('=')[0].trim()}=[secret]`);
  value = value.replace(API_KEY_RE, '[token]');
  value = value.replace(LONG_HEX_RE, '[secret]');
  value = value.replace(LONG_B64_RE, '[secret]');
  value = value.replace(/\s+/g, ' ').trim();
  if (maxLength > 0 && value.length > maxLength) {
    return `${value.slice(0, Math.max(1, maxLength - 1))}…`;
  }
  return value;
}

export function sentrySearchQueries({ lookbackHours = LOOKBACK_HOURS } = {}) {
  const hours = Number(lookbackHours);
  const windowHours = Number.isFinite(hours) && hours > 0 ? hours : LOOKBACK_HOURS;
  const lookback = `lastSeen:-${windowHours}h`;
  return SENTRY_STATUS_FILTERS.map((filter) => `${filter} ${lookback}`);
}

export function buildSentryIssuesUrl({
  host = DEFAULT_SENTRY_HOST,
  org = DEFAULT_SENTRY_ORG,
  project = DEFAULT_SENTRY_PROJECT,
  query,
  limit = SENTRY_ISSUES_LIMIT,
  statsPeriod = SENTRY_STATS_PERIOD,
} = {}) {
  const url = new URL(`/api/0/projects/${org}/${project}/issues/`, host);
  url.searchParams.set('query', query);
  url.searchParams.set('limit', String(limit));
  url.searchParams.set('statsPeriod', statsPeriod);
  return url;
}

export function mergeSentryIssuesById(lists) {
  const byId = new Map();
  for (const issue of lists.flat()) {
    if (!issue?.id) {
      continue;
    }
    const key = String(issue.id);
    if (!byId.has(key)) {
      byId.set(key, issue);
    }
  }
  return [...byId.values()];
}

export function sentryErrorDetail(bodyText, { maxLength = REDACT_MAX_LENGTH } = {}) {
  const raw = String(bodyText ?? '').trim();
  if (!raw) {
    return '';
  }
  let detail = '';
  try {
    const parsed = JSON.parse(raw);
    if (typeof parsed?.detail === 'string') {
      detail = parsed.detail;
    } else if (parsed?.detail && typeof parsed.detail === 'object') {
      detail = parsed.detail.message || parsed.detail.code || '';
    }
  } catch {
    detail = '';
  }
  return detail ? redact(detail, { maxLength }) : '';
}

export function formatSentryIssuesError(status, bodyText) {
  const detail = sentryErrorDetail(bodyText);
  return detail ? `Sentry issues failed: ${status}: ${detail}` : `Sentry issues failed: ${status}`;
}

export function isTelemetryCollectFailure(warning) {
  const text = String(warning || '');
  if (text.startsWith('sentry: ')) {
    return !text.includes('SENTRY_TRIAGE_TOKEN is not set');
  }
  if (text.startsWith('azure: ')) {
    const azure = text.slice('azure: '.length);
    return azure === 'azure-graph-failed'
      || azure === 'azure-workspace-failed'
      || azure === 'azure-login-failed'
      || azure === 'azure-arm-vars-missing'
      || azure.startsWith('azure-kql-failed:');
  }
  return false;
}

export const ARG_ALERTS_QUERY = `
alertsmanagementresources
| where type =~ 'microsoft.alertsmanagement/alerts'
| where properties.essentials.monitorCondition =~ 'Fired'
| where properties.essentials.alertRule has 'qz-prod-'
| where todatetime(properties.essentials.startDateTime) >= ago(2h)
| project id, name, alertRule=tostring(properties.essentials.alertRule), startDateTime=tostring(properties.essentials.startDateTime), essentials=properties.essentials, context=properties.context
`.trim();

const EVIDENCE_COLUMNS = `project ProblemId, operation_Name, ResultCode, operation_Ids, ItemCount, AppVersion, AppRoleInstance, lastSeen, DependencyType, target, test`;
export const REQUIRES_DIMENSION = new Set([
  'qz-prod-server-5xx',
  'qz-prod-exception-new-problem',
  'qz-prod-dependency-failures',
  'qz-prod-request-p95',
]);

export function kqlLiteral(value) {
  const raw = String(value ?? '').trim();
  if (!raw || !/^[A-Za-z0-9_./{}:+\s\-()[\]]{1,200}$/.test(raw)) {
    return '';
  }
  return `'${raw.replace(/'/g, "''")}'`;
}

function dimensionValue(dimensions, ...names) {
  const map = dimensions || {};
  for (const name of names) {
    if (map[name] != null && String(map[name]).trim()) {
      return String(map[name]).trim();
    }
  }
  return '';
}

export function dimensionWhere(alert) {
  const rule = alert?.rule || '';
  const dimensions = alert?.dimensions || {};
  if (rule === 'qz-prod-server-5xx' || rule === 'qz-prod-request-p95') {
    const literal = kqlLiteral(dimensionValue(dimensions, 'Name', 'name', 'operation_Name'));
    return literal ? `Name == ${literal}` : '';
  }
  if (rule === 'qz-prod-exception-new-problem') {
    const literal = kqlLiteral(dimensionValue(dimensions, 'ProblemId', 'problemId'));
    return literal ? `ProblemId == ${literal}` : '';
  }
  if (rule === 'qz-prod-dependency-failures') {
    const type = kqlLiteral(dimensionValue(dimensions, 'DependencyType', 'dependencyType'));
    const target = kqlLiteral(dimensionValue(dimensions, 'Target', 'target'));
    const parts = [];
    if (type) {
      parts.push(`DependencyType == ${type}`);
    }
    if (target) {
      parts.push(`Target == ${target}`);
    }
    return parts.join(' and ');
  }
  if (rule === 'qz-prod-availability') {
    const literal = kqlLiteral(dimensionValue(dimensions, 'Name', 'name', 'test'));
    return literal ? `Name == ${literal}` : '';
  }
  return '';
}

function whereLine(filter) {
  return filter ? `| where ${filter}\n` : '';
}

const EVIDENCE_BUILDERS = {
  'qz-prod-server-5xx': (filter) => `
AppRequests
| where toint(ResultCode) >= 500
${whereLine(filter)}| summarize ItemCount=sum(ItemCount), ResultCode=max(toint(ResultCode)), operation_Ids=make_set(OperationId, 5), AppVersion=any(AppVersion), AppRoleInstance=any(AppRoleInstance), lastSeen=max(TimeGenerated) by Name
| extend ProblemId='', operation_Name=Name, DependencyType='', target='', test=''
| ${EVIDENCE_COLUMNS}
`.trim(),
  'qz-prod-exception-new-problem': (filter) => `
AppExceptions
${whereLine(filter)}| summarize ItemCount=sum(ItemCount), operation_Ids=make_set(OperationId, 5), AppVersion=any(AppVersion), AppRoleInstance=any(AppRoleInstance), lastSeen=max(TimeGenerated), operation_Name=any(OperationName) by ProblemId
| extend ResultCode=0, DependencyType='', target='', test=''
| ${EVIDENCE_COLUMNS}
`.trim(),
  'qz-prod-exception-spike': (filter) => `
AppExceptions
${whereLine(filter)}| summarize ItemCount=sum(ItemCount), operation_Ids=make_set(OperationId, 5), AppVersion=any(AppVersion), AppRoleInstance=any(AppRoleInstance), lastSeen=max(TimeGenerated), operation_Name=any(OperationName)
| extend ProblemId='spike', ResultCode=0, DependencyType='', target='', test=''
| ${EVIDENCE_COLUMNS}
`.trim(),
  'qz-prod-dependency-failures': (filter) => `
AppDependencies
| where Success == false
${whereLine(filter)}| summarize ItemCount=sum(ItemCount), operation_Ids=make_set(OperationId, 5), AppVersion=any(AppVersion), AppRoleInstance=any(AppRoleInstance), lastSeen=max(TimeGenerated), operation_Name=any(OperationName) by DependencyType, Target
| extend ProblemId='', ResultCode=0, target=Target, test=''
| ${EVIDENCE_COLUMNS}
`.trim(),
  'qz-prod-availability': (filter) => `
AppAvailabilityResults
| where Success == false
${whereLine(filter)}| summarize ItemCount=sum(ItemCount), operation_Ids=make_set(Id, 5), AppVersion=any(AppVersion), AppRoleInstance=any(Location), lastSeen=max(TimeGenerated) by Name
| extend ProblemId='', operation_Name=Name, ResultCode=0, DependencyType='', target='', test=Name
| ${EVIDENCE_COLUMNS}
`.trim(),
  'qz-prod-ingestion-cap': () => `
_LogOperation
| where * has 'OverQuota' or * has 'over-quota' or * has 'CollectionDisabled' or * has 'collection-stopped'
| summarize ItemCount=count(), lastSeen=max(TimeGenerated)
| extend ProblemId='', operation_Name='', ResultCode=0, operation_Ids=dynamic([]), AppVersion='', AppRoleInstance='', DependencyType='', target='', test='ingestion'
| ${EVIDENCE_COLUMNS}
`.trim(),
  'qz-prod-request-p95': (filter) => `
AppRequests
| where Name !has '/health'
${whereLine(filter)}| summarize ItemCount=sum(ItemCount), operation_Ids=make_set(OperationId, 5), AppVersion=any(AppVersion), AppRoleInstance=any(AppRoleInstance), lastSeen=max(TimeGenerated), ResultCode=max(toint(ResultCode)) by Name
| extend ProblemId='', operation_Name=Name, DependencyType='', target='', test=''
| ${EVIDENCE_COLUMNS}
`.trim(),
};

export const EVIDENCE_KQL = Object.fromEntries(
  Object.entries(EVIDENCE_BUILDERS).map(([rule, build]) => [rule, build('')]),
);

export function evidenceKqlForAlert(alert) {
  const builder = EVIDENCE_BUILDERS[alert?.rule];
  if (!builder) {
    return '';
  }
  const filter = dimensionWhere(alert);
  if (REQUIRES_DIMENSION.has(alert.rule) && !filter) {
    return '';
  }
  return builder(filter);
}

export function keyPart(value, fallback = 'unknown') {
  const raw = String(value ?? '').trim();
  if (!raw) {
    return fallback;
  }
  return raw.replace(/\s+/g, '_').replace(/,/g, '');
}

export function sentryKey(issueId) {
  return `sentry:${keyPart(issueId)}`;
}

export function exceptionKey(problemId) {
  return `ai:exc:${keyPart(problemId)}`;
}

export function requestKey(operationName, suffix = '5xx') {
  return `ai:req:${keyPart(normalizeRoute(operationName) || 'unknown')}:${suffix}`;
}

export function dependencyKey(type, target) {
  return `ai:dep:${keyPart(type)}:${keyPart(target)}`;
}

export function availabilityKey(test) {
  return `ai:avail:${keyPart(test)}`;
}

export function ingestionKey() {
  return 'ai:ingest:cap';
}

export function normalizeRoute(value) {
  let path = String(value || '').trim();
  if (!path) {
    return '';
  }
  path = path.replace(METHOD_RE, '');
  if (/^https?:\/\//i.test(path)) {
    try {
      path = new URL(path).pathname;
    } catch {
      path = path.split('?')[0].split('#')[0];
    }
  } else {
    path = path.split('?')[0].split('#')[0];
  }
  let end = path.length;
  while (end > 0 && path[end - 1] === '/') {
    end -= 1;
  }
  path = path.slice(0, end);
  if (!path) {
    return '/';
  }
  if (!path.startsWith('/')) {
    return path.toLowerCase();
  }
  const parts = path.split('/').map((segment) => {
    if (!segment) {
      return '';
    }
    if (NUMERIC_RE.test(segment) || GUID_RE.test(segment)) {
      return '{id}';
    }
    return segment;
  });
  return parts.join('/').toLowerCase();
}

export function routeMatchesTemplate(route, template) {
  const left = normalizeRoute(route).split('/').filter(Boolean);
  const right = normalizeRoute(template).split('/').filter(Boolean);
  if (left.length !== right.length) {
    return false;
  }
  return left.every((segment, index) => {
    const expected = right[index];
    if (expected.startsWith('{') && expected.endsWith('}')) {
      return true;
    }
    return segment === expected || segment === '{id}';
  });
}

export function matchFeatureMap(route, entries = [], surface) {
  const normalized = normalizeRoute(route);
  if (!normalized) {
    return null;
  }
  let best = null;
  for (const entry of entries) {
    if (surface && entry._surface && entry._surface !== surface) {
      continue;
    }
    const url = entry.url;
    if (!url || !routeMatchesTemplate(normalized, url)) {
      continue;
    }
    if (!best || url.length > best.url.length) {
      best = { id: entry.id, area: entry.area || entry._area || 'unknown', url };
    }
  }
  return best;
}

export function captureProofCommand(featureId) {
  if (!featureId) {
    return 'Not mapped: add a feature-map id before running capture-proof.';
  }
  if (String(featureId).startsWith('mobile.')) {
    return `pwsh -File .cursor/skills/verify-queenzone-mobile/scripts/control-queenzone-mobile.ps1 capture-proof -Feature ${featureId} -Platform android`;
  }
  return `pwsh -File .cursor/skills/verify-queenzone/scripts/control-queenzone.ps1 capture-proof -Feature ${featureId}`;
}

export function shouldDryRunScheduled({ eventName, dispatchDryRun, fileIssues }) {
  if (eventName === 'workflow_dispatch') {
    return dispatchDryRun !== false && dispatchDryRun !== 'false';
  }
  return fileIssues !== 'true';
}

export function ruleNameFromAlertRule(alertRule) {
  const value = String(alertRule || '').trim();
  if (!value) {
    return '';
  }
  const parts = value.split('/').filter(Boolean);
  return parts.at(-1) || value;
}

function pick(row, names) {
  for (const name of names) {
    if (row && row[name] != null && row[name] !== '') {
      return row[name];
    }
  }
  return '';
}

function asStringList(value) {
  if (Array.isArray(value)) {
    return value.map((item) => String(item)).filter(Boolean);
  }
  if (value == null || value === '') {
    return [];
  }
  if (typeof value === 'string') {
    const trimmed = value.trim();
    if (!trimmed) {
      return [];
    }
    if (trimmed.startsWith('[')) {
      try {
        const parsed = JSON.parse(trimmed);
        return asStringList(parsed);
      } catch {
        return [trimmed];
      }
    }
    return [trimmed];
  }
  return [String(value)];
}

function dimensionMap(alert) {
  const buckets = [
    alert?.essentials?.dimensions,
    alert?.properties?.essentials?.dimensions,
    alert?.context?.context?.condition?.allOf,
    alert?.properties?.context?.context?.condition?.allOf,
  ];
  const map = {};
  for (const bucket of buckets) {
    const list = Array.isArray(bucket) ? bucket : [];
    for (const item of list) {
      const dimensions = item?.dimensions || (item?.name && item?.value != null ? [item] : []);
      for (const dimension of dimensions) {
        const name = dimension?.name || dimension?.Name;
        const value = dimension?.value || dimension?.Value;
        if (name && value != null) {
          map[name] = value;
        }
      }
    }
  }
  return map;
}

export function parseArgAlerts(payload) {
  const rows = Array.isArray(payload)
    ? payload
    : payload?.data || payload?.rows || [];
  const alerts = [];
  for (const row of rows) {
    const essentials = row.essentials || row.properties?.essentials || {};
    const alertRule = row.alertRule || essentials.alertRule || '';
    const rule = ruleNameFromAlertRule(alertRule);
    if (!rule.startsWith('qz-prod-')) {
      continue;
    }
    const condition = String(essentials.monitorCondition || row.monitorCondition || '').toLowerCase();
    if (condition && condition !== 'fired') {
      continue;
    }
    alerts.push({
      id: row.id || row.name || '',
      rule,
      alertRule,
      startDateTime: row.startDateTime || essentials.startDateTime || '',
      dimensions: dimensionMap({ ...row, essentials }),
      essentials,
    });
  }
  return alerts;
}

export function firedRuleNames(alerts) {
  return [...new Set((alerts || []).map((alert) => alert.rule).filter((rule) => rule in EVIDENCE_KQL))];
}

function evidenceKind(rule) {
  if (rule === 'qz-prod-server-5xx' || rule === 'qz-prod-request-p95') {
    return 'request';
  }
  if (rule === 'qz-prod-dependency-failures') {
    return 'dependency';
  }
  if (rule === 'qz-prod-availability') {
    return 'availability';
  }
  if (rule === 'qz-prod-ingestion-cap') {
    return 'ingestion';
  }
  return 'exception';
}

export function keysForEvidence(rule, row) {
  const kind = evidenceKind(rule);
  if (rule === 'qz-prod-request-p95') {
    return [requestKey(pick(row, ['operation_Name', 'Name', 'name']), 'p95')];
  }
  if (kind === 'request') {
    return [requestKey(pick(row, ['operation_Name', 'Name', 'name']))];
  }
  if (kind === 'dependency') {
    return [dependencyKey(pick(row, ['DependencyType', 'dependencyType']), pick(row, ['target', 'Target']))];
  }
  if (kind === 'availability') {
    return [availabilityKey(pick(row, ['test', 'Name', 'name', 'operation_Name']))];
  }
  if (kind === 'ingestion') {
    return [ingestionKey()];
  }
  if (rule === 'qz-prod-exception-spike') {
    const problemId = pick(row, ['ProblemId', 'problemId']);
    return [problemId && problemId !== 'spike' ? exceptionKey(problemId) : 'ai:exc:spike'];
  }
  return [exceptionKey(pick(row, ['ProblemId', 'problemId']))];
}

export function parseEvidenceRows(rule, rows = []) {
  const parsed = [];
  for (const row of rows || []) {
    const operationName = pick(row, ['operation_Name', 'Name', 'name']);
    const problemId = pick(row, ['ProblemId', 'problemId']);
    const resultCode = Number(pick(row, ['ResultCode', 'resultCode']) || 0);
    const keys = keysForEvidence(rule, row);
    if (!keys[0] || keys[0].endsWith(':unknown') || keys[0].endsWith(':unknown:5xx')) {
      continue;
    }
    parsed.push({
      rule,
      kind: evidenceKind(rule),
      keys,
      operationName,
      route: normalizeRoute(operationName),
      resultCode,
      operationIds: asStringList(pick(row, ['operation_Ids', 'operation_Id', 'OperationId'])),
      count: Number(pick(row, ['ItemCount', 'itemCount']) || 1) || 1,
      lastSeen: pick(row, ['lastSeen', 'TimeGenerated', 'timeGenerated']) || '',
      release: pick(row, ['AppVersion', 'appVersion']) || '',
      roleInstance: pick(row, ['AppRoleInstance', 'appRoleInstance']) || '',
      problemId,
      dependencyType: pick(row, ['DependencyType', 'dependencyType']),
      target: pick(row, ['target', 'Target']),
      test: pick(row, ['test', 'Name']),
    });
  }
  return parsed;
}

function extraValue(event, name) {
  const extra = event?.extra || event?.context?.extra || {};
  return extra[name];
}

export function extractSentryPath(event) {
  const extraPath = extraValue(event, 'path');
  if (extraPath) {
    return String(extraPath);
  }
  const breadcrumbs = event?.breadcrumbs?.values || event?.breadcrumbs || [];
  const list = Array.isArray(breadcrumbs) ? breadcrumbs : [];
  for (let index = list.length - 1; index >= 0; index -= 1) {
    const crumb = list[index];
    const status = Number(crumb?.data?.status ?? crumb?.data?.status_code ?? crumb?.data?.statusCode);
    const category = String(crumb?.category || '');
    if (category === 'api' && status >= 500) {
      return String(crumb.data?.path || crumb.data?.url || '');
    }
  }
  for (let index = list.length - 1; index >= 0; index -= 1) {
    const crumb = list[index];
    const status = Number(crumb?.data?.status ?? crumb?.data?.status_code ?? crumb?.data?.statusCode);
    if ((crumb?.category === 'api' || crumb?.type === 'http') && status >= 500) {
      return String(crumb.data?.path || crumb.data?.url || '');
    }
  }
  return '';
}

export function extractTraceId(event) {
  return String(
    event?.contexts?.trace?.trace_id
    || event?.tags?.operation_Id
    || extraValue(event, 'operation_Id')
    || event?.contexts?.trace?.span_id
    || '',
  );
}

export function inAppFrames(event) {
  const frames = [];
  const entries = event?.entries || [];
  for (const entry of entries) {
    const values = entry?.data?.values || entry?.data?.exception?.values || [];
    for (const value of values) {
      for (const frame of value?.stacktrace?.frames || []) {
        if (frame?.in_app || String(frame?.filename || '').includes('QueenZone')) {
          frames.push(frame);
        }
      }
    }
  }
  const exceptionValues = event?.exception?.values || [];
  for (const value of exceptionValues) {
    for (const frame of value?.stacktrace?.frames || []) {
      if (frame?.in_app || String(frame?.filename || '').includes('QueenZone')) {
        frames.push(frame);
      }
    }
  }
  return frames.slice(-5).reverse();
}

export function formatFrame(frame) {
  if (typeof frame === 'string') {
    return frame;
  }
  const file = frame?.filename || frame?.abs_path || 'unknown';
  const line = frame?.lineno || frame?.lineNo || '';
  const fn = frame?.function || frame?.absPath || '';
  return `${file}${line ? `:${line}` : ''}${fn ? ` in ${fn}` : ''}`;
}

function withinLookback(timestamp, since) {
  if (!since || !timestamp) {
    return true;
  }
  const date = new Date(timestamp);
  if (Number.isNaN(date.getTime())) {
    return false;
  }
  return date >= since;
}

export function parseSentryIssue(issue, event, {
  since,
  areas = [],
  featureMap = [],
  areaForFile,
  deployedTip = '',
} = {}) {
  if (!issue?.id) {
    return null;
  }
  const lastSeen = issue.lastSeen || issue.last_seen || event?.dateCreated || event?.datetime || '';
  if (!withinLookback(lastSeen, since)) {
    return null;
  }
  const path = extractSentryPath(event) || issue.culprit || '';
  const route = normalizeRoute(path);
  const frames = inAppFrames(event);
  const file = frames[0]?.filename || '';
  const feature = matchFeatureMap(route, featureMap, 'mobile')
    || matchFeatureMap(route, featureMap, 'web');
  const areaFromFile = typeof areaForFile === 'function' ? areaForFile(file, areas) : 'unknown';
  const area = feature?.area || (areaFromFile && areaFromFile !== 'unknown' ? areaFromFile : 'unknown');
  const count = Number(issue.count || event?.count || 1) || 1;
  const permalink = issue.permalink || issue.web_url || '';
  return {
    source: 'sentry',
    sources: ['sentry'],
    keys: [sentryKey(issue.id)],
    title: redact(`[sentry] ${issue.title || issue.metadata?.title || issue.id}`),
    area,
    featureId: feature?.id || '',
    captureProof: captureProofCommand(feature?.id || ''),
    evidence: permalink ? [{ url: permalink, text: 'Sentry issue' }] : [],
    count,
    firstSeen: issue.firstSeen || issue.first_seen || lastSeen,
    lastSeen,
    release: redact(event?.release || issue.project?.slug || '', { maxLength: 80 }),
    deployedTip,
    frames: frames.map((frame) => redact(formatFrame(frame), { maxLength: 200 })),
    route,
    operationId: extractTraceId(event),
    operationIds: [extractTraceId(event)].filter(Boolean),
    resultCode: 0,
    kind: 'sentry',
    level: 'L2',
  };
}

export function candidateFromEvidence(row, {
  featureMap = [],
  areas = [],
  areaForFile,
  deployedTip = '',
  alert,
} = {}) {
  const feature = matchFeatureMap(row.route, featureMap, 'web');
  const area = feature?.area || 'unknown';
  const evidence = [];
  if (alert?.id) {
    evidence.push({ text: redact(`Fired alert ${alert.rule}`) });
  }
  for (const id of row.operationIds.slice(0, 5)) {
    evidence.push({ text: redact(`operation_Id ${id}`) });
  }
  if (row.release) {
    evidence.push({ text: redact(`AppVersion ${row.release}`) });
  }
  if (row.roleInstance) {
    evidence.push({ text: redact(`AppRoleInstance ${row.roleInstance}`) });
  }
  return {
    source: 'appinsights',
    sources: ['appinsights'],
    keys: row.keys,
    title: redact(`[appinsights] ${row.rule} ${row.operationName || row.problemId || row.target || row.test || row.rule}`),
    area,
    featureId: feature?.id || '',
    captureProof: captureProofCommand(feature?.id || ''),
    evidence,
    count: row.count,
    firstSeen: row.lastSeen,
    lastSeen: row.lastSeen,
    release: row.release || deployedTip,
    deployedTip,
    frames: [],
    route: row.route,
    operationId: row.operationIds[0] || '',
    operationIds: row.operationIds,
    resultCode: row.resultCode,
    kind: row.kind,
    level: 'L2',
  };
}

function uniqueKeys(lists) {
  const keys = [];
  const seen = new Set();
  for (const key of lists.flat()) {
    if (!key || seen.has(key)) {
      continue;
    }
    seen.add(key);
    keys.push(key);
  }
  return keys;
}

function mergeCandidates(left, right, reason) {
  const sources = uniqueKeys([left.sources || [left.source], right.sources || [right.source]]);
  const featureId = left.featureId || right.featureId || '';
  return {
    ...left,
    source: sources.length > 1 ? 'telemetry' : (left.source || right.source),
    sources,
    keys: uniqueKeys([left.keys || [], right.keys || []]),
    title: left.source === 'sentry' ? left.title : (right.source === 'sentry' ? right.title : left.title),
    area: left.area !== 'unknown' ? left.area : right.area,
    featureId,
    captureProof: captureProofCommand(featureId),
    evidence: [...(left.evidence || []), ...(right.evidence || [])],
    count: (left.count || 0) + (right.count || 0),
    firstSeen: [left.firstSeen, right.firstSeen].filter(Boolean).sort((a, b) => a.localeCompare(b))[0] || left.firstSeen,
    lastSeen: [left.lastSeen, right.lastSeen].filter(Boolean).sort((a, b) => a.localeCompare(b)).at(-1) || left.lastSeen,
    release: left.release || right.release,
    deployedTip: left.deployedTip || right.deployedTip,
    frames: (left.frames || []).length ? left.frames : right.frames,
    route: left.route || right.route,
    operationId: left.operationId || right.operationId,
    operationIds: uniqueKeys([left.operationIds || [], right.operationIds || []]),
    resultCode: Math.max(left.resultCode || 0, right.resultCode || 0),
    kind: 'correlated',
    correlatedBy: reason,
    level: 'L2',
  };
}

function sameTrace(left, right) {
  const leftIds = new Set((left.operationIds || []).concat(left.operationId || '').filter(Boolean));
  if (leftIds.size === 0) {
    return false;
  }
  return (right.operationIds || []).concat(right.operationId || '').some((id) => id && leftIds.has(id));
}

function withinCorrelateWindow(left, right, windowMs) {
  const leftTime = Date.parse(left.lastSeen || '');
  const rightTime = Date.parse(right.lastSeen || '');
  if (!Number.isFinite(leftTime) || !Number.isFinite(rightTime)) {
    return false;
  }
  return Math.abs(leftTime - rightTime) <= windowMs;
}

export function correlateSignals(sentryCandidates, appInsightsCandidates, { windowMs = CORRELATE_WINDOW_MS } = {}) {
  const sentry = [...(sentryCandidates || [])];
  const insights = [...(appInsightsCandidates || [])];
  const usedSentry = new Set();
  const usedInsights = new Set();
  const merged = [];

  const take = (sentryItem, insightItem, reason) => {
    merged.push(mergeCandidates(sentryItem, insightItem, reason));
    usedSentry.add(sentryItem);
    usedInsights.add(insightItem);
  };

  for (const sentryItem of sentry) {
    const hit = insights.find((item) => !usedInsights.has(item) && sameTrace(sentryItem, item));
    if (hit) {
      take(sentryItem, hit, 'operation_Id');
    }
  }

  for (const sentryItem of sentry) {
    if (usedSentry.has(sentryItem) || !sentryItem.route) {
      continue;
    }
    const hit = insights.find((item) => {
      if (usedInsights.has(item) || item.kind !== 'request') {
        return false;
      }
      if ((item.resultCode || 0) < 500) {
        return false;
      }
      if (item.route !== sentryItem.route) {
        return false;
      }
      return withinCorrelateWindow(sentryItem, item, windowMs);
    });
    if (hit) {
      take(sentryItem, hit, 'route');
    }
  }

  for (const sentryItem of sentry) {
    if (!usedSentry.has(sentryItem)) {
      merged.push(sentryItem);
    }
  }
  for (const insightItem of insights) {
    if (!usedInsights.has(insightItem)) {
      merged.push(insightItem);
    }
  }
  return merged;
}

export function telemetrySourceLabels(candidate) {
  const labels = [];
  const keys = candidate?.keys || [];
  const sources = new Set(candidate?.sources || [candidate?.source]);
  const children = candidate?.stormCandidates || [];
  const keyPool = [...keys, ...children.flatMap((child) => child.keys || [])];
  const sourcePool = [...sources, ...children.flatMap((child) => child.sources || [child.source])];
  if (sourcePool.includes('sentry') || keyPool.some((key) => String(key).startsWith('sentry:'))) {
    labels.push('from-sentry');
  }
  if (
    sourcePool.includes('appinsights')
    || keyPool.some((key) => String(key).startsWith('ai:'))
  ) {
    labels.push('from-appinsights');
  }
  return labels;
}

export {
  CORRELATE_WINDOW_MS,
  DEFAULT_SENTRY_HOST,
  DEFAULT_SENTRY_ORG,
  DEFAULT_SENTRY_PROJECT,
  SENTRY_ISSUES_LIMIT,
  SENTRY_STATS_PERIOD,
  SENTRY_STATUS_FILTERS,
};
