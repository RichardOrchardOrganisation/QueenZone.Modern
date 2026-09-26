import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { areaForFile, filerDir, ruleInfo } from '../config.mjs';
import { lowestLevel, parseFindings } from '../finding.mjs';

const BACKFILL_REL = ['backfill', 'review-findings-60d.json'];

function evidenceUrl(item) {
  return item.html_url || item.htmlUrl || item.url || '';
}

function groupReviewFindings(findings, { config, findingRules }) {
  const byRule = new Map();
  for (const finding of findings) {
    const bucket = byRule.get(finding.rule) || [];
    bucket.push(finding);
    byRule.set(finding.rule, bucket);
  }
  const candidates = [];
  for (const [rule, group] of byRule) {
    const prs = new Set(group.map((item) => item.pr).filter(Boolean));
    const files = group.map((item) => item.file).filter(Boolean);
    const info = ruleInfo(rule, findingRules);
    const level = lowestLevel(group.map((item) => item.level));
    const seen = group.map((item) => item.at).filter(Boolean).sort();
    candidates.push({
      source: 'review',
      keys: [`review:${rule}`],
      rule,
      title: `[review] ${info?.title || rule} (${prs.size || group.length} PRs)`,
      area: areaForFile(files[0], config.areas),
      evidence: group.map((item) => ({
        url: item.url,
        text: `#${item.pr || '?'} ${item.file}`,
      })),
      count: prs.size || group.length,
      firstSeen: seen[0] || '',
      lastSeen: seen[seen.length - 1] || '',
      level,
      repeat: group.some((item) => item.repeat === 'yes') ? 'yes' : 'no',
      file: files[0],
      verdict: group.some((item) => item.verdict === 'blocking') ? 'blocking' : 'nit',
      proposedCheck: info?.check
        ? `Existing check ${info.check} did not prevent this finding.`
        : `Add an L1/L2 check for ${rule}.`,
    });
  }
  return candidates.sort((left, right) => left.rule.localeCompare(right.rule));
}

function readBackfill(root) {
  const filePath = path.join(filerDir(root), ...BACKFILL_REL);
  if (!existsSync(filePath)) {
    return [];
  }
  const payload = JSON.parse(readFileSync(filePath, 'utf8'));
  const rows = Array.isArray(payload) ? payload : payload.findings || [];
  return rows.map((row) => ({
    ...row,
    pr: row.pr || row.pull || row.number,
    url: row.url || row.html_url || '',
    at: row.at || row.created_at || row.createdAt || '',
  }));
}

export async function collect(ctx) {
  const findings = [];
  const malformed = ctx.malformed || [];
  const since = ctx.since;
  const pulls = ctx.pulls || (await ctx.github.listPullRequests({ state: 'all', sort: 'updated', direction: 'desc' }));

  for (const pull of pulls) {
    const updated = new Date(pull.updated_at || pull.updatedAt || 0);
    if (since && updated < since) {
      if (!ctx.pulls) {
        break;
      }
      continue;
    }
    const extra = {
      pr: pull.number,
      url: evidenceUrl(pull),
      at: pull.updated_at || pull.updatedAt || '',
    };
    const fromBody = parseFindings(pull.body || '', extra);
    findings.push(...fromBody.findings);
    malformed.push(...fromBody.malformed);

    const issueComments = ctx.issueComments?.[pull.number] || (ctx.github ? await ctx.github.listIssueComments(pull.number) : []);
    for (const comment of issueComments) {
      const parsed = parseFindings(comment.body || '', {
        pr: pull.number,
        url: evidenceUrl(comment) || extra.url,
        at: comment.created_at || comment.createdAt || extra.at,
      });
      findings.push(...parsed.findings);
      malformed.push(...parsed.malformed);
    }

    const reviewComments = ctx.reviewComments?.[pull.number] || (ctx.github ? await ctx.github.listReviewComments(pull.number) : []);
    for (const comment of reviewComments) {
      const parsed = parseFindings(comment.body || '', {
        pr: pull.number,
        url: evidenceUrl(comment) || extra.url,
        at: comment.created_at || comment.createdAt || extra.at,
        file: comment.path || undefined,
      });
      findings.push(...parsed.findings);
      malformed.push(...parsed.malformed);
    }

    const reviews = ctx.reviews?.[pull.number] || (ctx.github ? await ctx.github.listReviews(pull.number) : []);
    for (const review of reviews) {
      const parsed = parseFindings(review.body || '', {
        pr: pull.number,
        url: evidenceUrl(review) || extra.url,
        at: review.submitted_at || review.submittedAt || extra.at,
      });
      findings.push(...parsed.findings);
      malformed.push(...parsed.malformed);
    }
  }

  if ((ctx.lookbackDays || 0) >= 60 && ctx.root) {
    findings.push(...readBackfill(ctx.root));
  }

  return groupReviewFindings(findings, { config: ctx.config, findingRules: ctx.findingRules || [] });
}

export { groupReviewFindings, readBackfill };
