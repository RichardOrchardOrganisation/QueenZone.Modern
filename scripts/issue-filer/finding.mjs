/**
 * Parsers for review tags and filer dedupe markers.
 * Tag contract: .cursor/agents/reviewer.md and #1802.
 */

function* tags(text, kind) {
  const source = String(text || '');
  let offset = 0;
  while (offset < source.length) {
    const start = source.indexOf('<!--', offset);
    if (start === -1) {
      return;
    }
    const end = source.indexOf('-->', start + 4);
    if (end === -1) {
      return;
    }
    const content = source.slice(start + 4, end);
    if (!content.includes('>')) {
      const [tagKind, version, ...fields] = content.trim().split(/\s+/);
      if (tagKind === kind && version === 'v=1') {
        yield { raw: source.slice(start, end + 3), fields: fields.join(' ') };
      }
    }
    offset = end + 3;
  }
}

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
  for (const tag of tags(text, 'qz-finding')) {
    const fields = parseFields(tag.fields);
    if (!isValidFinding(fields)) {
      malformed.push({ raw: tag.raw, fields, ...extra });
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
  const marker = tags(body, 'qz-filer').next().value;
  if (!marker) {
    return null;
  }
  const fields = parseFields(marker.fields);
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
  for (const tag of tags(body, 'qz-filer')) {
    if (!tag.fields) {
      return true;
    }
  }
  return false;
}

export function levelRank(level) {
  const rank = { L1: 1, L2: 2, L3: 3, L4: 4, L5: 5 };
  return rank[level] || 6;
}

export function lowestLevel(levels) {
  return [...levels].sort((a, b) => levelRank(a) - levelRank(b))[0] || '';
}
