import { areaForFile } from '../config.mjs';

export function groupSonarIssues(issues, { config } = {}) {
  const byRule = new Map();
  for (const issue of issues || []) {
    const rule = issue.rule || issue.ruleKey;
    if (!rule) {
      continue;
    }
    const bucket = byRule.get(rule) || [];
    bucket.push(issue);
    byRule.set(rule, bucket);
  }
  const candidates = [];
  for (const [rule, group] of byRule) {
    const seen = group.map((item) => item.creationDate || item.creation_date || '').filter(Boolean).sort();
    const file = (group[0].component || '').split(':').pop() || '';
    candidates.push({
      source: 'sonar',
      keys: [`sonar:${rule}`],
      rule,
      title: `[sonar] ${rule} (${group.length} issues)`,
      area: areaForFile(file, config?.areas),
      evidence: group.slice(0, 20).map((item) => ({
        url: item.url || sonarIssueUrl(config, item),
        text: item.message || item.key,
      })),
      count: group.length,
      firstSeen: seen[0] || '',
      lastSeen: seen[seen.length - 1] || '',
      level: 'L2',
      proposedCheck: `Enforce Sonar rule ${rule} (quality profile or repo check).`,
    });
  }
  return candidates.sort((left, right) => left.rule.localeCompare(right.rule));
}

function sonarIssueUrl(config, issue) {
  const projectKey = config?.sonar?.projectKey || process.env[config?.sonar?.projectKeyEnv || 'SONAR_PROJECT_KEY'] || '';
  if (!projectKey || !issue.key) {
    return '';
  }
  const host = config?.sonar?.host || 'https://sonarcloud.io';
  return `${host}/project/issues?id=${encodeURIComponent(projectKey)}&issues=${encodeURIComponent(issue.key)}&open=${encodeURIComponent(issue.key)}`;
}

export async function defaultSonarSearch({ host, organization, projectKey, token, createdAfter, fetchImpl = fetch }) {
  const issues = [];
  let page = 1;
  while (page <= 10) {
    const url = new URL('/api/issues/search', host);
    url.searchParams.set('componentKeys', projectKey);
    if (organization) {
      url.searchParams.set('organization', organization);
    }
    url.searchParams.set('createdAfter', createdAfter);
    url.searchParams.set('ps', '100');
    url.searchParams.set('p', String(page));
    url.searchParams.set('statuses', 'OPEN,CONFIRMED,REOPENED');
    const headers = { Accept: 'application/json', 'User-Agent': 'queenzone-issue-filer' };
    if (token) {
      headers.Authorization = `Bearer ${token}`;
    }
    const response = await fetchImpl(url, { headers, signal: AbortSignal.timeout(20_000) });
    if (!response.ok) {
      const error = new Error(`Sonar search failed: ${response.status}`);
      error.status = response.status;
      throw error;
    }
    const data = await response.json();
    issues.push(...(data.issues || []));
    const paging = data.paging || {};
    const total = paging.total || issues.length;
    if (issues.length >= total || (data.issues || []).length === 0) {
      break;
    }
    page += 1;
  }
  return issues;
}

export async function collect(ctx) {
  if (ctx.sonarIssues) {
    return groupSonarIssues(ctx.sonarIssues, { config: ctx.config });
  }
  const search = ctx.sonarSearch;
  if (!search) {
    ctx.warnings?.push('sonar: no search function configured');
    return [];
  }
  try {
    const createdAfter = ctx.since.toISOString().slice(0, 10);
    const issues = await search({ createdAfter });
    return groupSonarIssues(issues, { config: ctx.config });
  } catch (error) {
    ctx.warnings?.push(`sonar: ${error.message}`);
    return [];
  }
}
