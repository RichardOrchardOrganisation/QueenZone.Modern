import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { areaForFile } from '../config.mjs';

const execFileAsync = promisify(execFile);

function readDiffHeader(line, state) {
  const commitMatch = line.match(/^commit\s+([0-9a-f]{7,40})\b/i);
  if (commitMatch) {
    state.commit = commitMatch[1];
    return true;
  }
  if (line.startsWith('Date: ')) {
    const dateText = line.slice('Date: '.length).trim();
    const parsed = Date.parse(dateText);
    state.date = Number.isNaN(parsed) ? dateText : new Date(parsed).toISOString();
    return true;
  }
  if (line.startsWith('diff --git a/')) {
    const separator = line.indexOf(' b/', 'diff --git a/'.length);
    if (separator !== -1) {
      state.file = line.slice(separator + 3);
    }
    return true;
  }
  return false;
}

export function parseSuppressionDiff(patch, patterns, { config } = {}) {
  const groups = new Map();
  const state = { file: '', commit: '', date: '' };
  for (const line of String(patch || '').split(/\r?\n/)) {
    if (readDiffHeader(line, state)) continue;
    if (!line.startsWith('+') || line.startsWith('+++')) {
      continue;
    }
    const added = line.slice(1);
    for (const pattern of patterns || []) {
      if (!added.includes(pattern.includes)) {
        continue;
      }
      const bucket = groups.get(pattern.id) || [];
      bucket.push({ ...state, line: added.trim() });
      groups.set(pattern.id, bucket);
    }
  }

  const candidates = [];
  for (const [id, rows] of groups) {
    const commits = new Set(rows.map((row) => row.commit).filter(Boolean));
    const seen = rows.map((row) => row.date).filter(Boolean).sort();
    candidates.push({
      source: 'suppressions',
      keys: [`suppressions:${id}`],
      rule: `suppressions.${id}`,
      title: `[suppressions] ${id} (${commits.size || rows.length} commits)`,
      area: areaForFile(rows[0]?.file, config?.areas),
      evidence: rows.slice(0, 20).map((row) => ({
        text: `${row.file}: ${row.line}`,
      })),
      count: commits.size || rows.length,
      firstSeen: seen[0] || '',
      lastSeen: seen[seen.length - 1] || '',
      level: 'L2',
      proposedCheck: `Replace ${id} suppressions with a real fix or document an ignore.json entry.`,
    });
  }
  return candidates.sort((left, right) => left.title.localeCompare(right.title));
}

export async function defaultGitLogPatch(since, cwd) {
  const { stdout } = await execFileAsync(
    'git',
    ['log', '-p', `--since=${since.toISOString()}`, '--unified=0', '--', '.'],
    { cwd, maxBuffer: 20 * 1024 * 1024 },
  );
  return stdout;
}

export async function collect(ctx) {
  const patterns = ctx.config?.suppressionPatterns || [];
  try {
    let patch;
    if (ctx.gitLogPatch) {
      patch = await ctx.gitLogPatch(ctx.since);
    } else if (ctx.patch != null) {
      patch = ctx.patch;
    } else {
      patch = await defaultGitLogPatch(ctx.since, ctx.root);
    }
    return parseSuppressionDiff(patch, patterns, { config: ctx.config });
  } catch (error) {
    ctx.warnings?.push(`suppressions: ${error.message}`);
    return [];
  }
}
