import { isValidFinding, keysOverlap, levelRank, parseFilerMarker } from './finding.mjs';
import { isPositiveInteger, ruleInfo, validateRecurrence } from './config.mjs';

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

function matchesIgnoreCriteria(candidate, entry) {
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

function matchIgnore(candidate, entry) {
  if (!matchesIgnoreCriteria(candidate, entry)) return false;
  if (!entry.recurrence) return true;
  const observations = (candidate.sentryObservations || []).filter((item) => item.key === entry.match.key);
  // Only the pinned single occurrence can be quiet. Counts above one prove
  // more than one occurrence/user even in a window; counts at or below one
  // do not prove absence of recurrence. Identity and time must also agree.
  return observations.length > 0 && observations.every((item) =>
    item.eventId === entry.recurrence.eventId
    && typeof item.lastSeen === 'string'
    && Date.parse(item.lastSeen) === Date.parse(entry.recurrence.lastSeen)
    && Number.isSafeInteger(item.count) && item.count >= 0 && item.count <= 1
    && Number.isSafeInteger(item.userCount) && item.userCount >= 0 && item.userCount <= 1);
}

/**
 * When an ignore entry sets maxUsers / maxEvents and the candidate has that
 * count, exceeding either ceiling means the ignore does not apply.
 * A ceiling whose count is absent on the candidate (typical App Insights
 * userCount) is skipped, not treated as zero.
 */
export function ignoreCeilingReason(candidate, entry) {
  const parts = [];
  if (isPositiveInteger(entry?.maxUsers) && Number.isFinite(candidate?.userCount) && candidate.userCount > entry.maxUsers) {
    parts.push(`users ${candidate.userCount} > ${entry.maxUsers}`);
  }
  if (isPositiveInteger(entry?.maxEvents) && Number.isFinite(candidate?.count) && candidate.count > entry.maxEvents) {
    parts.push(`events ${candidate.count} > ${entry.maxEvents}`);
  }
  if (parts.length === 0) {
    return null;
  }
  return `ignore ceiling exceeded: ${parts.join(', ')}`;
}

export function partitionIgnore(entries, now) {
  const active = [];
  const expired = [];
  for (const entry of entries || []) {
    if (!entry?.reason || !entry?.expires || Number.isNaN(Date.parse(entry.expires))) {
      continue;
    }
    if (validateRecurrence(entry).length > 0) continue;
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

function queueUnmatchedCreates({ unmatched, existing, loop, now, stormThreshold, skipped, matched }) {
  const ingested = unmatched.filter((candidate) => candidate.ingested);
  const live = unmatched.filter((candidate) => !candidate.ingested);
  if (unmatched.length > stormThreshold) {
    if (ingested.length > 0) {
      for (const candidate of live) {
        skipped.push({ candidate, reason: 'storm' });
      }
      return ingested;
    }
    for (const candidate of unmatched) {
      skipped.push({ candidate, reason: 'storm' });
    }
    const storm = stormCandidate(unmatched, loop, now);
    const stormMatch = findMatch(storm, existing);
    if (stormMatch) {
      matched.push({ candidate: storm, match: stormMatch });
      return [];
    }
    return [storm];
  }
  if (ingested.length > 0) {
    return [...ingested, ...live];
  }
  return unmatched;
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

function classifyCandidate(candidate, { loop, minOccurrences, active, findingRules }) {
  if (!candidate?.keys?.length || !candidate.title) {
    return { skip: { candidate, reason: 'invalid' } };
  }
  if (loop !== 'telemetry' && (candidate.count || 0) < minOccurrences) {
    return { skip: { candidate, reason: 'below-min-occurrences' } };
  }
  const ignoreHit = active.find((entry) => matchesIgnoreCriteria(candidate, entry));
  if (ignoreHit) {
    const ceilingReason = ignoreCeilingReason(candidate, ignoreHit);
    if (!ceilingReason && matchIgnore(candidate, ignoreHit)) {
      return { skip: { candidate, reason: 'ignored', ignore: ignoreHit } };
    }
    if (ceilingReason) candidate = { ...candidate, ignoreCeilingReason: ceilingReason };
  }
  if (isCheckGap(candidate, findingRules)) {
    return { skip: { candidate, reason: 'check-gap' } };
  }
  return { candidate };
}

function planMatchedCandidate(candidate, match, policy, state, canonical = false) {
  const { clock, maxComments, cooldownHours, reopenDays, config } = policy;
  const { create, comment, reopen, skipped } = state;
  if (match.state === 'open') {
    if (state.commentsUsed >= maxComments) {
      skipped.push({ candidate, reason: 'comment-cap', issue: match.number });
      return;
    }
    if (commentedRecently(match, clock, cooldownHours)) {
      skipped.push({ candidate, reason: 'comment-cooldown', issue: match.number });
      return;
    }
    comment.push({
      issueNumber: match.number,
      candidate,
      kind: 'update',
      existingBody: match.body,
    });
    state.commentsUsed += 1;
    return;
  }

  const reason = match.stateReason || '';
  if (reason === 'not_planned') {
    skipped.push({
      candidate,
      reason: 'closed-not-planned',
      issue: match.number,
      suggestIgnore: true,
    });
    return;
  }

  const daysClosed = match.closedAt ? (clock - asDate(match.closedAt)) / MS_DAY : Number.POSITIVE_INFINITY;
  if (reason === 'completed' && (canonical || daysClosed <= reopenDays)) {
    if (state.commentsUsed >= maxComments) {
      skipped.push({ candidate, reason: 'comment-cap', issue: match.number });
      return;
    }
    reopen.push({
      issueNumber: match.number,
      candidate,
      labels: [config.labels.regression || 'regression'],
    });
    comment.push({
      issueNumber: match.number,
      candidate,
      kind: 'regression',
      existingBody: match.body,
    });
    state.commentsUsed += 1;
    return;
  }

  if (canonical) {
    skipped.push({ candidate, reason: 'canonical-issue-not-reopenable', issue: match.number });
    return;
  }
  if (state.remaining <= 0) {
    skipped.push({ candidate, reason: 'cap', issue: match.number });
    return;
  }
  create.push({ candidate, previousIssue: match.number });
  state.remaining -= 1;
}

function canonicalIgnore(candidate, entries) {
  const sentryCandidate = (candidate.sources || []).includes('sentry')
    ? { ...candidate, source: 'sentry' } : candidate;
  return entries.find((entry) => entry.recurrence
    && (matchesIgnoreCriteria(candidate, entry) || matchesIgnoreCriteria(sentryCandidate, entry)));
}

function matchRankedCandidates(ranked, existing, entries, skipped) {
  const unmatched = [];
  const matched = [];
  for (const candidate of ranked) {
    // Expiry ends suppression, not canonical issue identity. Never create a
    // duplicate when a pinned issue is missing or has an incompatible marker.
    const pinned = canonicalIgnore(candidate, entries);
    if (pinned) {
      const match = existing.find((issue) => issue.number === pinned.recurrence.issueNumber
        && !issue.pullRequest && parseFilerMarker(issue.body)?.keys.includes(pinned.match.key));
      if (match) matched.push({ candidate, match, canonical: true });
      else skipped.push({ candidate, reason: 'canonical-issue-missing', issue: pinned.recurrence.issueNumber });
      continue;
    }
    const match = findMatch(candidate, existing);
    if (match) {
      matched.push({ candidate, match });
    } else {
      unmatched.push(candidate);
    }
  }

  return { unmatched, matched };
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

  const eligibility = { loop, minOccurrences, active, findingRules };
  for (const candidate of candidates) {
    const classified = classifyCandidate(candidate, eligibility);
    if (classified.skip) skipped.push(classified.skip);
    else eligible.push(classified.candidate);
  }

  const ranked = rankCandidates(eligible);
  const create = [];
  const comment = [];
  const reopen = [];
  let remaining = remainingCap({ existing, config, loop, now: clock, maxIssues });
  const commentsUsed = 0;
  const maxComments = config.caps?.commentsPerRun ?? 10;
  const cooldownHours = config.match?.commentCooldownHours ?? 24;
  const reopenDays = config.match?.closedCompletedReopenDays ?? 30;
  const stormThreshold = config.caps?.stormThreshold ?? 5;

  const { unmatched, matched } = matchRankedCandidates(ranked, existing, [...active, ...expiredIgnores], skipped);

  const createQueue = queueUnmatchedCreates({
    unmatched,
    existing,
    loop,
    now: clock,
    stormThreshold,
    skipped,
    matched,
  });

  const state = { create, comment, reopen, skipped, remaining, commentsUsed };
  const policy = { clock, maxComments, cooldownHours, reopenDays, config };
  for (const { candidate, match, canonical } of matched) {
    planMatchedCandidate(candidate, match, policy, state, canonical);
  }
  remaining = state.remaining;

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
    if (candidate.rule && !known.has(candidate.rule) && (candidate.source === 'review' || isValidFinding({
      level: candidate.level || 'L5',
      rule: candidate.rule,
      repeat: candidate.repeat || 'no',
      file: candidate.file || 'unknown',
      verdict: candidate.verdict || 'nit',
    }))) {
      ids.add(candidate.rule);
    }
  }
  return [...ids].sort((left, right) => left.localeCompare(right));
}
