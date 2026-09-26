import { isGuardrail } from './core.mjs';

export function buildMarker({ keys, source }) {
  const safeKeys = (keys || []).filter((key) => key && !/\s/.test(key));
  return `<!-- qz-filer v=1 keys=${safeKeys.join(',')} source=${source || 'unknown'} -->`;
}

export function labelsFor(candidate, config, loop = 'gardener') {
  const labels = [];
  if (loop === 'telemetry') {
    labels.push(...(config.labels.telemetry || ['bug', 'from-telemetry']));
    if (candidate.area === 'unknown') {
      labels.push(config.labels.needsTriage || 'needs-triage');
    }
  } else {
    labels.push(config.labels.gardener);
    if (isGuardrail(candidate)) {
      labels.push(config.labels.guardrail);
    }
  }
  return [...new Set(labels.filter(Boolean))];
}

export function escapeMarkdown(text) {
  return String(text || '')
    .replace(/\\/g, '\\\\')
    .replace(/`/g, '\\`')
    .replace(/\[/g, '\\[')
    .replace(/\]/g, '\\]')
    .replace(/\(/g, '\\(')
    .replace(/\)/g, '\\)')
    .replace(/@/g, '\\@')
    .replace(/\b(close[sd]?|fix(?:e[sd])?|resolve[sd]?)\s+#(\d+)/gi, (_, verb, number) => `${verb} ticket ${number}`)
    .replace(/#(\d+)/g, '#\u200b$1');
}

function safeUrl(url) {
  try {
    const parsed = new URL(String(url || ''));
    if (parsed.protocol === 'https:' || parsed.protocol === 'http:') {
      return parsed.href;
    }
  } catch {
    return '';
  }
  return '';
}

function evidenceLines(evidence = []) {
  if (evidence.length === 0) {
    return '- No linked evidence was supplied.';
  }
  return evidence
    .slice(0, 20)
    .map((item) => {
      const label = escapeMarkdown(item.text || item.url || 'evidence');
      const url = safeUrl(item.url);
      return url ? `- [${label}](${url})` : `- ${label}`;
    })
    .join('\n');
}

function proposedCheck(candidate) {
  if (candidate.proposedCheck) {
    return candidate.proposedCheck;
  }
  if (candidate.level === 'L1') {
    return `Make \`${candidate.rule || candidate.keys?.[0]}\` impossible in code.`;
  }
  return `Add an L1/L2 check (analyzer, lint, CI script, or Sonar rule) for \`${candidate.rule || candidate.keys?.[0]}\`.`;
}

function stormList(candidates = []) {
  return candidates
    .map((item) => `- **${item.title}** (${item.count}) keys: \`${(item.keys || []).join(', ')}\``)
    .join('\n');
}

export function buildIssue({ candidate, config, previousIssue, loop = 'gardener' }) {
  const marker = buildMarker({ keys: candidate.keys, source: candidate.source });
  const labels = labelsFor(candidate, config, loop);
  const previous = previousIssue ? `\nPreviously closed as #${previousIssue}.\n` : '';
  const storm = candidate.storm
    ? `\n## Ranked signals\n\n${stormList(candidate.stormCandidates)}\n`
    : '';
  const body = `## User story

As a QueenZone maintainer, I want repeated ${candidate.source} signal \`${candidate.keys?.[0] || candidate.title}\` cleaned up so the paved path stays narrow for agents.

## Acceptance criteria

1. The repeated signal is fixed, or an ignore.json entry is added with a reason and expiry.
2. The proposed check below is added at L1 or L2, or the issue explains why a higher level is the lowest practical fix.
3. Linked evidence is addressed or explicitly deferred.

## Evidence

- Area: ${candidate.area || 'unknown'}
- Count: ${candidate.count}
- First seen: ${candidate.firstSeen || 'unknown'}
- Last seen: ${candidate.lastSeen || 'unknown'}
${evidenceLines(candidate.evidence)}
${storm}
## Proposed check

${proposedCheck(candidate)}
${previous}
${marker}
`;
  return {
    title: candidate.title,
    body: body.replace(/\n{3,}/g, '\n\n').trim() + '\n',
    labels,
  };
}

export function buildComment({ candidate, kind }) {
  const heading = kind === 'regression' ? 'Reopened as a regression' : 'Seen again';
  const lines = [
    `## ${heading}`,
    '',
    `Count is now **${candidate.count}** for \`${candidate.keys?.[0] || candidate.title}\`.`,
    '',
    evidenceLines(candidate.evidence),
    '',
    '<!-- qz-filer v=1 -->',
  ];
  return lines.join('\n') + '\n';
}

export function buildLogComment({ create, reopen, mention }) {
  const filed = (create || []).map((item) => `- create: ${item.candidate.title}`).join('\n');
  const reopened = (reopen || []).map((item) => `- reopen: #${item.issueNumber} ${item.candidate.title}`).join('\n');
  return [
    `${mention} Issue filer wrote ${create.length} issue(s) and reopened ${reopen.length}.`,
    filed,
    reopened,
    '',
    '<!-- qz-filer v=1 -->',
  ]
    .filter(Boolean)
    .join('\n') + '\n';
}

export function buildRankedReport(candidates) {
  if (!candidates.length) {
    return 'No ranked gardener candidates in this lookback.\n';
  }
  const lines = ['## Ranked gardener candidates', ''];
  candidates.forEach((item, index) => {
    lines.push(`${index + 1}. **${item.title}** — ${item.count} hits, level ${item.level || 'n/a'}, keys \`${(item.keys || []).join(', ')}\``);
    if (item.proposedCheck) {
      lines.push(`   Proposed check: ${item.proposedCheck}`);
    }
  });
  lines.push('', '<!-- qz-filer v=1 -->', '');
  return lines.join('\n');
}

export function formatPlanSummary(plan, extras = {}) {
  const lines = [
    '## Issue filer plan',
    '',
    `- create: ${plan.create.length}`,
    `- comment: ${plan.comment.length}`,
    `- reopen: ${plan.reopen.length}`,
    `- skipped: ${plan.skipped.length}`,
    `- expired ignore entries: ${plan.expiredIgnores.length}`,
  ];
  if (extras.malformed?.length) {
    lines.push(`- malformed qz-finding tags: ${extras.malformed.length}`);
  }
  if (extras.unregistered?.length) {
    lines.push(`- unregistered rules: ${extras.unregistered.join(', ')}`);
  }
  if (extras.warnings?.length) {
    lines.push(`- warnings: ${extras.warnings.join('; ')}`);
  }
  if (plan.create.length === 0 && plan.comment.length === 0 && plan.reopen.length === 0) {
    lines.push('', 'Silent run: nothing to file.');
  }
  for (const item of plan.create) {
    lines.push(`- will create: ${item.candidate.title}`);
  }
  for (const item of plan.skipped.filter((row) => row.suggestIgnore)) {
    lines.push(`- suggest ignore for closed-as-not-planned #${item.issue} (${item.candidate.title})`);
  }
  for (const entry of plan.expiredIgnores) {
    lines.push(`- expired ignore: ${entry.reason}`);
  }
  return lines.join('\n') + '\n';
}
