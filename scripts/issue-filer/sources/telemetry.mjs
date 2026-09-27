import { readFileSync } from 'node:fs';
import { areaForFile } from '../config.mjs';
import { loadFeatureMap } from '../../check-feature-map.mjs';
import {
  DEFAULT_SENTRY_HOST,
  DEFAULT_SENTRY_ORG,
  DEFAULT_SENTRY_PROJECT,
  SENTRY_QUERY,
  candidateFromEvidence,
  correlateSignals,
  parseEvidenceRows,
  parseSentryIssue,
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

export async function defaultSentrySearch({
  host = DEFAULT_SENTRY_HOST,
  org = DEFAULT_SENTRY_ORG,
  project = DEFAULT_SENTRY_PROJECT,
  token,
  query = SENTRY_QUERY,
  fetchImpl = fetch,
} = {}) {
  if (!token) {
    const error = new Error('SENTRY_TRIAGE_TOKEN is not set');
    throw error;
  }
  const issues = [];
  let next = new URL(`/api/0/projects/${org}/${project}/issues/`, host);
  next.searchParams.set('query', query);
  next.searchParams.set('limit', '25');
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
      const error = new Error(`Sentry issues failed: ${response.status}`);
      error.status = response.status;
      throw error;
    }
    const data = await response.json();
    issues.push(...(Array.isArray(data) ? data : []));
    const link = response.headers.get?.('link') || '';
    const match = /<([^>]+)>;\s*rel="next"/i.exec(link);
    next = match ? new URL(match[1]) : '';
    pages += 1;
  }
  return issues;
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
    if (!token && !search) {
      warnings.push('sentry: SENTRY_TRIAGE_TOKEN is not set');
    } else {
      try {
        const issues = search
          ? await search({ query: SENTRY_QUERY })
          : await defaultSentrySearch({
            host: ctx.sentryHost || process.env.SENTRY_HOST || DEFAULT_SENTRY_HOST,
            org: ctx.sentryOrg || process.env.SENTRY_ORG || DEFAULT_SENTRY_ORG,
            project: ctx.sentryProject || process.env.SENTRY_PROJECT || DEFAULT_SENTRY_PROJECT,
            token,
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

  return correlateSignals(sentryCandidates, appInsightsCandidates);
}
