#!/usr/bin/env node
/**
 * Maestro flow rules (docs/architecture/testing-policy.md, "Writing device
 * flows that fail only for real reasons").
 *
 * Node 24, no npm dependencies. Line-based: Maestro flows here are plain
 * block YAML, so indentation is enough to find a command's own keys.
 *
 *   start-state       a flow listed in a gating suite starts with launchApp,
 *                     openLink, or a setup subflow (common/*, open-*)
 *   bounded-wait      every extendedWaitUntil has timeout <= 60000
 *   no-optional       suite flows never mark a step optional: true
 *   retry-justified   retry: has maxRetries <= 1 and a comment just above it
 *
 * Existing violations live in config/maestro-flow-baseline.json. A listed
 * violation that no longer occurs fails the check, so the list only shrinks.
 *
 *   node scripts/check-maestro-flows.mjs
 */
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

export const MAESTRO_DIR = 'src/QueenZone.Mobile/maestro';
export const BASELINE_PATH = 'config/maestro-flow-baseline.json';
/** Suites that gate a workflow outcome. Screenshot/video suites are tooling. */
export const GATING_SUITES = ['smoke.yaml', 'journeys.yaml', 'release.yaml'];
export const MAX_WAIT_MS = 60000;
const COMMENT_LOOKBACK = 8;

const indentOf = line => line.length - line.trimStart().length;
const isBlank = line => line.trim() === '' || line.trim().startsWith('#');

/** Lines after the `---` document separator (or all lines when absent). */
function commandLines(text) {
  const lines = text.split(/\r?\n/);
  const sep = lines.findIndex(line => line.trim() === '---');
  return { lines, start: sep === -1 ? 0 : sep + 1 };
}

/** Indices of lines belonging to the block that starts at `index`. */
function blockOf(lines, index) {
  const own = indentOf(lines[index]);
  const body = [];
  for (let i = index + 1; i < lines.length; i++) {
    if (isBlank(lines[i])) continue;
    if (indentOf(lines[i]) <= own) break;
    body.push(i);
  }
  return body;
}

/**
 * Splits a YAML line into `{ item, key, value }` without regex backtracking.
 * `item` is true for a `- key: value` sequence entry. Returns null for
 * comments, blanks and lines that are not a simple `key[: value]`.
 */
export function parseKeyLine(line) {
  let rest = line.trim();
  if (rest === '' || rest.startsWith('#')) return null;
  const item = rest.startsWith('- ');
  if (item) rest = rest.slice(2).trimStart();
  const colon = rest.indexOf(':');
  const key = colon === -1 ? rest : rest.slice(0, colon);
  if (key === '' || /[^A-Za-z]/.test(key)) return null;
  return { item, key, value: colon === -1 ? '' : rest.slice(colon + 1).trim() };
}

const isDigits = value => value !== '' && [...value].every(ch => ch >= '0' && ch <= '9');

/** Direct child `key: value` of the block at `index`, or undefined. */
function childValue(lines, index, key) {
  const body = blockOf(lines, index);
  if (body.length === 0) return undefined;
  const childIndent = indentOf(lines[body[0]]);
  for (const i of body) {
    if (indentOf(lines[i]) !== childIndent) continue;
    const parsed = parseKeyLine(lines[i]);
    if (parsed?.key === key) return parsed.value;
  }
  return undefined;
}

export function suiteFlows(suiteText) {
  return suiteText
    .split(/\r?\n/)
    .filter(line => indentOf(line) === 0)
    .map(parseKeyLine)
    .filter(parsed => parsed?.item && parsed.key === 'runFlow' && parsed.value.endsWith('.yaml'))
    .map(parsed => parsed.value);
}

export function firstCommand(text) {
  const { lines, start } = commandLines(text);
  for (let i = start; i < lines.length; i++) {
    if (indentOf(lines[i]) !== 0) continue;
    const parsed = parseKeyLine(lines[i]);
    if (parsed?.item) return { name: parsed.key, value: parsed.value };
  }
  return null;
}

export function ownsStartState(text) {
  const first = firstCommand(text);
  if (!first) return false;
  if (first.name === 'launchApp' || first.name === 'openLink') return true;
  return first.name === 'runFlow' && (first.value.startsWith('common/') || first.value.startsWith('open-'));
}

function checkWait(lines, i) {
  const timeout = childValue(lines, i, 'timeout');
  if (timeout === undefined) return 'extendedWaitUntil has no timeout';
  if (!isDigits(timeout) || Number(timeout) > MAX_WAIT_MS) return `timeout ${timeout} exceeds ${MAX_WAIT_MS}ms`;
  return null;
}

function checkRetry(lines, i, start) {
  const problems = [];
  const max = childValue(lines, i, 'maxRetries');
  if (max === undefined || !isDigits(max) || Number(max) > 1) {
    problems.push(`retry maxRetries must be 0 or 1 (got ${max ?? 'default'})`);
  }
  const above = lines.slice(Math.max(start, i - COMMENT_LOOKBACK), i);
  if (!above.some(prev => prev.trim().startsWith('#'))) {
    problems.push('retry needs a comment above naming the infrastructure symptom');
  }
  return problems;
}

/** Returns [{ rule, line, message }] for one flow file. */
export function checkFlowText(text, { suiteFlow = false } = {}) {
  const findings = [];
  const add = (rule, line, message) => findings.push({ rule, line, message });
  const { lines, start } = commandLines(text);
  for (let i = start; i < lines.length; i++) {
    const parsed = parseKeyLine(lines[i]);
    if (parsed?.key === 'extendedWaitUntil' && parsed.value === '') {
      const problem = checkWait(lines, i);
      if (problem) add('bounded-wait', i + 1, problem);
    } else if (parsed?.key === 'retry' && parsed.value === '') {
      for (const problem of checkRetry(lines, i, start)) add('retry-justified', i + 1, problem);
    } else if (suiteFlow && parsed?.key === 'optional' && !parsed.item && parsed.value === 'true') {
      add('no-optional', i + 1, 'suite flows must not mark a step optional');
    }
  }
  if (suiteFlow && !ownsStartState(text)) {
    add('start-state', 1, 'suite flow must start with launchApp, openLink, or a common/* or open-* setup subflow');
  }
  return findings;
}

function yamlFiles(dir) {
  const out = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...yamlFiles(full));
    else if (entry.name.endsWith('.yaml')) out.push(full);
  }
  return out.sort((a, b) => a.localeCompare(b));
}

/**
 * Checks every Maestro YAML under `maestroDir`. Returns violations keyed
 * `rule:relative/path.yaml` so the baseline can match them.
 */
export function collectViolations(maestroDir) {
  const suiteSet = new Set();
  for (const suite of GATING_SUITES) {
    const suitePath = path.join(maestroDir, suite);
    if (!existsSync(suitePath)) continue;
    for (const flow of suiteFlows(readFileSync(suitePath, 'utf8'))) suiteSet.add(flow);
  }
  const violations = [];
  for (const file of yamlFiles(maestroDir)) {
    const relative = path.relative(maestroDir, file).split(path.sep).join('/');
    const text = readFileSync(file, 'utf8');
    for (const finding of checkFlowText(text, { suiteFlow: suiteSet.has(relative) })) {
      violations.push({ ...finding, file: relative, key: `${finding.rule}:${relative}` });
    }
  }
  return violations;
}

export function compareToBaseline(violations, baseline) {
  const allowed = new Set(baseline);
  const found = new Set(violations.map(v => v.key));
  return {
    newViolations: violations.filter(v => !allowed.has(v.key)),
    stale: [...allowed].filter(key => !found.has(key)).sort((a, b) => a.localeCompare(b)),
  };
}

export function main(root = process.cwd()) {
  const maestroDir = path.join(root, MAESTRO_DIR);
  const baselineFile = path.join(root, BASELINE_PATH);
  const baseline = existsSync(baselineFile) ? JSON.parse(readFileSync(baselineFile, 'utf8')).allowed ?? [] : [];
  const { newViolations, stale } = compareToBaseline(collectViolations(maestroDir), baseline);
  for (const v of newViolations) {
    console.error(`${MAESTRO_DIR}/${v.file}:${v.line} [${v.rule}] ${v.message}`);
  }
  for (const key of stale) {
    console.error(`${BASELINE_PATH}: "${key}" no longer occurs; remove it so the baseline only shrinks.`);
  }
  if (newViolations.length || stale.length) {
    console.error('See docs/architecture/testing-policy.md, "Writing device flows that fail only for real reasons".');
    return 1;
  }
  console.log(`Maestro flow rules passed (${baseline.length} baselined violation(s) remain).`);
  return 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = main(path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..'));
}
