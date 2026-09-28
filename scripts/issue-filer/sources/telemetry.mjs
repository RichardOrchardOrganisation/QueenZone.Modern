import { readFileSync } from 'node:fs';
import { areaForFile } from '../config.mjs';
import { loadFeatureMap } from '../../check-feature-map.mjs';
import {
  DEFAULT_SENTRY_HOST,
  DEFAULT_SENTRY_ORG,
  DEFAULT_SENTRY_PROJECT,
  LOOKBACK_HOURS,
  buildSentryIssuesUrl,
  candidateFromEvidence,
  correlateSignals,
  formatSentryIssuesError,
  isTelemetryCollectFailure,
  mergeSentryIssuesById,
  parseEvidenceRows,
  parseSentryIssue,
  sentrySearchQueries,
} from '../telemetry.mjs';

function readJsonFile(filePath, fallback) {
  if (!filePath) {
    return fallback;
  }
  try {
    return JSON.parse(readFileSync(filePath, 'utf8'));
  } catch {
    return fallback;
  }
}

async function readSentryBody(response) {
  if (typeof response.text === 'function') {
    const text = await response.text();
    if (!text) {
      return { data: null, text: '' };
    }
    try {
      return { data: JSON.parse(text), text };
    } catch {
      return { data: null, text };
    }
  }
  if (typeof response.json === 'function') {
    const data = await response.json();
    return { data, text: data == null ? '' : JSON.stringify(data) };
  }
  return { data: null, text: '' };
}

async function fetchSentryIssuePages({
  host,
  org,
  project,
  token,
  query,
  fetchImpl,
}) {
  const issues = [];
  let next = buildSentryIssuesUrl({ host, org, project, query });
  let pages = 0;
  while (next && pages < 10) {
    const response = await fetchImpl(next, {
      headers: {
        Accept: 'application/json',
        Authorization: `Bearer ${token}`,
        'User-Agent': 'queenzone-issue-filer',
      },
      signal: AbortSignal.timeout(20_000),
    });
    if (!response.ok) {
      const { text } = await readSentryBody(response);
      const error = new Error(formatSentryIssuesError(response.status, text));
      error.status = response.status;
      throw error;
    }
    const { data } = await readSentryBody(response);
    issues.push(...(Array.isArray(data) ? data : []));
    const link = response.headers.get?.('link') || response.headers.get?.('Link') || '';
    next = sentryNextPageUrl(link);
    pages += 1;
  }
  return issues;
}

export async function defaultSentrySearch({
  host = DEFAULT_SENTRY_HOST,
  org = DEFAULT_SENTRY_ORG,
  project = DEFAULT_SENTRY_PROJECT,
  token,
  query,
  queries,
  lookbackHours,
  fetchImpl = fetch,
} = {}) {
  if (!token) {
    const error = new Error('SENTRY_TRIAGE_TOKEN is not set');
    throw error;
  }
  const queryList = query
    ? [query]
    : (queries || sentrySearchQueries({ lookbackHours }));
  const pages = [];
  for (const item of queryList) {
    pages.push(await fetchSentryIssuePages({
      host,
      org,
      project,
      token,
      query: item,
      fetchImpl,
    }));
  }
  return mergeSentryIssuesById(pages);
}

export function sentryNextPageUrl(linkHeader, { allowedHost = 'sentry.io' } = {}) {
  const header = String(linkHeader || '');
  for (const part of header.split(',')) {
    const [urlPart, ...parameters] = part.split(';');
    const link = urlPart.trim();
    if (!link.startsWith('<') || !link.endsWith('>') ||
        !parameters.some((parameter) => parameter.trim().toLowerCase() === 'rel="next"')) {
      continue;
    }
    try {
      const url = new URL(link.slice(1, -1));
      if (url.protocol !== 'https:' && url.protocol !== 'http:') {
        return '';
      }
      if (url.hostname.toLowerCase() !== allowedHost) {
        return '';
      }
      return url;
    } catch {
      return '';
    }
  }
  return '';
}

export async function defaultSentryLatestEvent({
  host = DEFAULT_SENTRY_HOST,
  token,
  issueId,
  fetchImpl = fetch,
} = {}) {
  const url = new URL(`/api/0/issues/${issueId}/events/latest/`, host);
  const response = await fetchImpl(url, {
    headers: {
      Accept: 'application/json',
      Authorization: `Bearer ${token}`,
      'User-Agent': 'queenzone-issue-filer',
    },
    signal: AbortSignal.timeout(20_000),
  });
  if (!response.ok) {
    return null;
  }
  return response.json();
}

function featureEntries(ctx) {
  if (ctx.featureMap) {
    return ctx.featureMap;
  }
  try {
    return loadFeatureMap(ctx.root).entries;
  } catch {
    return [];
  }
}

export function candidatesFromAzureFiles({
  alerts = [],
  evidence = {},
  featureMap = [],
  areas = [],
  deployedTip = '',
}) {
  const rows = [];
  for (const [rule, list] of Object.entries(evidence || {})) {
    const parsed = parseEvidenceRows(rule, Array.isArray(list) ? list : []);
    const matchingAlerts = (alerts || []).filter((alert) => alert.rule === rule);
    for (const row of parsed) {
      rows.push(candidateFromEvidence(row, {
        featureMap,
        areas,
        areaForFile,
        deployedTip,
        alert: matchingAlerts[0],
      }));
    }
  }
  return rows;
}

export async function collect(ctx) {
  const warnings = ctx.warnings || [];
  const featureMap = featureEntries(ctx);
  const deployedTip = ctx.deployedTip || process.env.DEPLOYED_TIP_SHA || '';
  const since = ctx.since;

  let sentryCandidates = ctx.sentryCandidates || [];
  if (ctx.sentryIssues) {
    sentryCandidates = [];
    for (const issue of ctx.sentryIssues) {
      const event = ctx.sentryEvents?.[issue.id] || ctx.sentryEvents?.[String(issue.id)] || null;
      const candidate = parseSentryIssue(issue, event, {
        since,
        areas: ctx.config?.areas,
        featureMap,
        areaForFile,
        deployedTip,
      });
      if (candidate) {
        sentryCandidates.push(candidate);
      }
    }
  } else if (!ctx.sentryCandidates) {
    const token = ctx.sentryToken ?? process.env.SENTRY_TRIAGE_TOKEN;
    const search = ctx.sentrySearch;
    const lookbackHours = ctx.lookbackHours || LOOKBACK_HOURS;
    const queries = sentrySearchQueries({ lookbackHours });
    if (!token && !search) {
      warnings.push('sentry: SENTRY_TRIAGE_TOKEN is not set');
    } else {
      try {
        const issues = search
          ? await search({ queries })
          : await defaultSentrySearch({
            host: ctx.sentryHost || process.env.SENTRY_HOST || DEFAULT_SENTRY_HOST,
            org: ctx.sentryOrg || process.env.SENTRY_ORG || DEFAULT_SENTRY_ORG,
            project: ctx.sentryProject || process.env.SENTRY_PROJECT || DEFAULT_SENTRY_PROJECT,
            token,
            queries,
            lookbackHours,
            fetchImpl: ctx.fetchImpl || fetch,
          });
        const latestEvent = ctx.sentryLatestEvent || ((issueId) => defaultSentryLatestEvent({
          host: ctx.sentryHost || process.env.SENTRY_HOST || DEFAULT_SENTRY_HOST,
          token,
          issueId,
          fetchImpl: ctx.fetchImpl || fetch,
        }));
        for (const issue of issues || []) {
          let event = null;
          try {
            event = await latestEvent(issue.id);
          } catch (error) {
            warnings.push(`sentry-event: ${error.message}`);
          }
          const candidate = parseSentryIssue(issue, event, {
            since,
            areas: ctx.config?.areas,
            featureMap,
            areaForFile,
            deployedTip,
          });
          if (candidate) {
            sentryCandidates.push(candidate);
          }
        }
      } catch (error) {
        warnings.push(`sentry: ${error.message}`);
      }
    }
  }

  const azureWarningsPath = ctx.azureWarningsPath || process.env.TELEMETRY_AZURE_WARNINGS_PATH;
  if (azureWarningsPath) {
    try {
      const text = readFileSync(azureWarningsPath, 'utf8');
      for (const line of text.split(/\r?\n/).map((item) => item.trim()).filter(Boolean)) {
        warnings.push(`azure: ${line}`);
      }
    } catch {
      // Missing warning file is a clean collect.
    }
  }

  const alerts = ctx.appInsightsAlerts || readJsonFile(
    ctx.appInsightsAlertsPath || process.env.TELEMETRY_ALERTS_PATH,
    [],
  );
  const evidence = ctx.appInsightsEvidence || readJsonFile(
    ctx.appInsightsEvidencePath || process.env.TELEMETRY_EVIDENCE_PATH,
    {},
  );
  const appInsightsCandidates = candidatesFromAzureFiles({
    alerts,
    evidence,
    featureMap,
    areas: ctx.config?.areas,
    deployedTip,
  });

  if (warnings.some(isTelemetryCollectFailure)) {
    return [];
  }
  return correlateSignals(sentryCandidates, appInsightsCandidates);
}
