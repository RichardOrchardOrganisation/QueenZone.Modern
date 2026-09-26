import { isValidFinding, keysOverlap, levelRank, parseFilerMarker } from './finding.mjs';
import { ruleInfo } from './config.mjs';

const MS_DAY = 24 * 60 * 60 * 1000;

export function asDate(value) {
  if (value instanceof Date) {
    return value;
  }
  return new Date(value);
}

export function isoDate(value) {
  return asDate(value).toISOString().slice(0, 10);
}

function matchIgnore(candidate, entry) {
  const match = entry.match || {};
  const hasCriterion = Boolean(match.source || match.key || match.rule || match.titleRegex);
  if (!hasCriterion) {
    return false;
  }
  if (match.source && match.source !== candidate.source) {
    return false;
  }
  if (match.key && !(candidate.keys || []).includes(match.key)) {
    return false;
  }
  if (match.rule) {
    const keys = candidate.keys || [];
    const hasRule =
      candidate.rule === match.rule || keys.some((key) => key === match.rule || key.endsWith(`:${match.rule}`));
    if (!hasRule) {
      return false;
    }
  }
  if (match.titleRegex) {
    const pattern = String(match.titleRegex);
    if (pattern.length > 200) {
      return false;
    }
    try {
      if (!new RegExp(pattern).test(candidate.title || '')) {
        return false;
      }
    } catch {
      return false;
    }
  }
  return true;
}

export function partitionIgnore(entries, now) {
  const active = [];
  const expired = [];
  for (const entry of entries || []) {
    if (!entry?.reason || !entry?.expires || Number.isNaN(Date.parse(entry.expires))) {
      continue;
    }
    if (asDate(entry.expires) < now) {
      expired.push(entry);
    } else {
      active.push(entry);
    }
  }
  return { active, expired };
}

export function rankCandidates(candidates) {
  return [...candidates].sort((left, right) => {
    if (right.count !== left.count) {
      return right.count - left.count;
    }
    return levelRank(left.level) - levelRank(right.level);
  });
}

export function findMatch(candidate, existing) {
  const hits = (existing || []).filter((issue) => {
    const marker = parseFilerMarker(issue.body);
    return marker && keysOverlap(candidate.keys, marker.keys);
  });
  if (hits.length === 0) {
    return null;
  }
  const open = hits.filter((issue) => issue.state === 'open');
  const pool = open.length > 0 ? open : hits;
  return [...pool].sort((left, right) => asDate(right.updatedAt || right.createdAt) - asDate(left.updatedAt || left.createdAt))[0];
}

function capSpec(config, loop) {
  return loop === 'telemetry' ? config.caps.telemetry : config.caps.gardener;
}

function loopLabel(config, loop) {
  if (loop !== 'telemetry') {
    return config.labels.gardener;
  }
  const labels = config.labels.telemetry || [];
  return labels.find((name) => name === 'from-telemetry') || 'from-telemetry';
}

export function countRecentFilings(existing, { config, loop, now }) {
  const spec = capSpec(config, loop);
  const since = new Date(now.getTime() - spec.periodDays * MS_DAY);
  const label = loopLabel(config, loop);
  const bots = new Set(config.botLogins || ['github-actions[bot]']);
  return (existing || []).filter((issue) => {
    if (!bots.has(issue.user)) {
      return false;
    }
    if (!(issue.labels || []).includes(label)) {
      return false;
    }
    if (asDate(issue.createdAt) < since) {
      return false;
    }
    return Boolean(parseFilerMarker(issue.body));
  }).length;
}

function remainingCap({ existing, config, loop, now, maxIssues }) {
  const spec = capSpec(config, loop);
  const used = countRecentFilings(existing, { config, loop, now });
  const allowed = Math.min(spec.maxIssues, maxIssues ?? spec.maxIssues);
  return Math.max(0, allowed - used);
}

function commentedRecently(issue, now, hours) {
  if (!issue.lastFilerCommentAt) {
    return false;
  }
  return now - asDate(issue.lastFilerCommentAt) < hours * 60 * 60 * 1000;
}

function isCheckGap(candidate, findingRules) {
  const id = candidate.rule || String((candidate.keys || [])[0] || '').split(':').slice(1).join(':');
  const info = ruleInfo(id, findingRules);
  return Boolean(info?.check);
}

function stormCandidate(ranked, loop, now) {
  const date = isoDate(now);
  return {
    source: loop,
    keys: [`storm:${loop}:${date}`],
    title: `${loop === 'telemetry' ? 'Telemetry' : 'Gardener'} storm ${date}`,
    area: 'infra',
    evidence: ranked.flatMap((item) => item.evidence || []).slice(0, 40),
    count: ranked.length,
    firstSeen: ranked.map((item) => item.firstSeen).filter(Boolean).sort()[0] || now.toISOString(),
    lastSeen: ranked.map((item) => item.lastSeen).filter(Boolean).sort().at(-1) || now.toISOString(),
    level: 'L2',
    proposedCheck: 'Triage the listed signals and file or ignore each one.',
    storm: true,
    stormCandidates: ranked,
  };
}

/**
 * Pure planner. No I/O.
 * @returns {{ create: object[], comment: object[], reopen: object[], skipped: object[], expiredIgnores: object[] }}
 */
export function planFilings({
  candidates = [],
  existing = [],
  ignore = { entries: [] },
  config,
  now = new Date(),
  loop = 'gardener',
  maxIssues,
  findingRules = [],
} = {}) {
  if (!config) {
    throw new Error('planFilings requires config');
  }
  const clock = asDate(now);
  const { active, expired: expiredIgnores } = partitionIgnore(ignore.entries || ignore, clock);
  const minOccurrences = config.caps?.minOccurrences ?? 2;
  const skipped = [];
  const eligible = [];

  for (const candidate of candidates) {
    if (!candidate?.keys?.length || !candidate.title) {
      skipped.push({ candidate, reason: 'invalid' });
      continue;
    }
    if ((candidate.count || 0) < minOccurrences) {
      skipped.push({ candidate, reason: 'below-min-occurrences' });
      continue;
    }
    const ignoreHit = active.find((entry) => matchIgnore(candidate, entry));
    if (ignoreHit) {
      skipped.push({ candidate, reason: 'ignored', ignore: ignoreHit });
      continue;
    }
    if (isCheckGap(candidate, findingRules)) {
      skipped.push({ candidate, reason: 'check-gap' });
      continue;
    }
    eligible.push(candidate);
  }

  const ranked = rankCandidates(eligible);
  const create = [];
  const comment = [];
  const reopen = [];
  let remaining = remainingCap({ existing, config, loop, now: clock, maxIssues });
  let commentsUsed = 0;
  const maxComments = config.caps?.commentsPerRun ?? 10;
  const cooldownHours = config.match?.commentCooldownHours ?? 24;
  const reopenDays = config.match?.closedCompletedReopenDays ?? 30;
  const stormThreshold = config.caps?.stormThreshold ?? 5;

  const unmatched = [];
  const matched = [];
  for (const candidate of ranked) {
    const match = findMatch(candidate, existing);
    if (match) {
      matched.push({ candidate, match });
    } else {
      unmatched.push(candidate);
    }
  }

  const createQueue = [];
  if (unmatched.length > stormThreshold) {
    for (const candidate of unmatched) {
      skipped.push({ candidate, reason: 'storm' });
    }
    const storm = stormCandidate(unmatched, loop, clock);
    const stormMatch = findMatch(storm, existing);
    if (stormMatch) {
      matched.push({ candidate: storm, match: stormMatch });
    } else {
      createQueue.push(storm);
    }
  } else {
    createQueue.push(...unmatched);
  }

  for (const { candidate, match } of matched) {
    if (match.state === 'open') {
      if (commentsUsed >= maxComments) {
        skipped.push({ candidate, reason: 'comment-cap', issue: match.number });
        continue;
      }
      if (commentedRecently(match, clock, cooldownHours)) {
        skipped.push({ candidate, reason: 'comment-cooldown', issue: match.number });
        continue;
      }
      comment.push({ issueNumber: match.number, candidate, kind: 'update' });
      commentsUsed += 1;
      continue;
    }

    const reason = match.stateReason || '';
    if (reason === 'not_planned') {
      skipped.push({
        candidate,
        reason: 'closed-not-planned',
        issue: match.number,
        suggestIgnore: true,
      });
      continue;
    }

    const daysClosed = match.closedAt ? (clock - asDate(match.closedAt)) / MS_DAY : Number.POSITIVE_INFINITY;
    if (reason === 'completed' && daysClosed <= reopenDays) {
      if (commentsUsed >= maxComments) {
        skipped.push({ candidate, reason: 'comment-cap', issue: match.number });
        continue;
      }
      reopen.push({
        issueNumber: match.number,
        candidate,
        labels: [config.labels.regression || 'regression'],
      });
      comment.push({ issueNumber: match.number, candidate, kind: 'regression' });
      commentsUsed += 1;
      continue;
    }

    if (remaining <= 0) {
      skipped.push({ candidate, reason: 'cap', issue: match.number });
      continue;
    }
    create.push({ candidate, previousIssue: match.number });
    remaining -= 1;
  }

  for (const candidate of createQueue) {
    if (remaining <= 0) {
      skipped.push({ candidate, reason: 'cap' });
      continue;
    }
    create.push({ candidate, previousIssue: null });
    remaining -= 1;
  }

  return { create, comment, reopen, skipped, expiredIgnores };
}

export function isGuardrail(candidate) {
  return (
    candidate?.level === 'L1' ||
    candidate?.level === 'L2' ||
    candidate?.repeat === 'yes' ||
    (candidate?.count || 0) >= 2
  );
}

export function unregisteredRules(candidates, findingRules = []) {
  const known = new Set(findingRules.map((rule) => rule.id));
  const ids = new Set();
  for (const candidate of candidates) {
    if (candidate.rule && isValidFinding({
      level: candidate.level || 'L5',
      rule: candidate.rule,
      repeat: candidate.repeat || 'no',
      file: candidate.file || 'unknown',
      verdict: candidate.verdict || 'nit',
    }) && !known.has(candidate.rule)) {
      ids.add(candidate.rule);
    } else if (candidate.rule && !known.has(candidate.rule) && candidate.source === 'review') {
      ids.add(candidate.rule);
    }
  }
  return [...ids].sort();
}
