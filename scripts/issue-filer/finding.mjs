/**
 * Parsers for review tags and filer dedupe markers.
 * Tag contract: .cursor/agents/reviewer.md and #1802.
 */

export const FINDING_RE = /<!--\s*qz-finding\s+v=1\s+([^>]*?)\s*-->/g;
export const FILER_MARKER_RE = /<!--\s*qz-filer\s+v=1\s+([^>]*?)\s*-->/;
export const FILER_COMMENT_RE = /<!--\s*qz-filer\s+v=1\s*-->/;

const RULE_RE = /^[a-z0-9]+(\.[a-z0-9-]+)+$/;
const LEVELS = new Set(['L1', 'L2', 'L3', 'L4', 'L5']);
const REPEATS = new Set(['yes', 'no']);
const VERDICTS = new Set(['blocking', 'nit']);

export function parseFields(text) {
  const fields = {};
  for (const part of String(text || '').trim().split(/\s+/)) {
    const eq = part.indexOf('=');
    if (eq <= 0) {
      continue;
    }
    fields[part.slice(0, eq)] = part.slice(eq + 1);
  }
  return fields;
}

export function isValidFinding(fields) {
  if (!fields || typeof fields !== 'object') {
    return false;
  }
  return (
    LEVELS.has(fields.level) &&
    RULE_RE.test(fields.rule || '') &&
    REPEATS.has(fields.repeat) &&
    typeof fields.file === 'string' &&
    fields.file.length > 0 &&
    !/\s/.test(fields.file) &&
    VERDICTS.has(fields.verdict)
  );
}

export function parseFindings(text, extra = {}) {
  const findings = [];
  const malformed = [];
  const source = String(text || '');
  for (const match of source.matchAll(FINDING_RE)) {
    const fields = parseFields(match[1]);
    if (!isValidFinding(fields)) {
      malformed.push({ raw: match[0], fields, ...extra });
      continue;
    }
    findings.push({
      ...extra,
      ...fields,
      file: fields.file || extra.file,
    });
  }
  return { findings, malformed };
}

export function parseFilerMarker(body) {
  const match = String(body || '').match(FILER_MARKER_RE);
  if (!match) {
    return null;
  }
  const fields = parseFields(match[1]);
  const keys = String(fields.keys || '')
    .split(',')
    .map((key) => key.trim())
    .filter(Boolean);
  if (keys.length === 0 || keys.some((key) => /\s/.test(key))) {
    return null;
  }
  return { keys, source: fields.source || '', fields };
}

export function keysOverlap(left, right) {
  const set = new Set(left || []);
  return (right || []).some((key) => set.has(key));
}

export function isFilerComment(body) {
  return FILER_COMMENT_RE.test(String(body || ''));
}

export function levelRank(level) {
  const rank = { L1: 1, L2: 2, L3: 3, L4: 4, L5: 5 };
  return rank[level] || 6;
}

export function lowestLevel(levels) {
  return [...levels].sort((a, b) => levelRank(a) - levelRank(b))[0] || '';
}
