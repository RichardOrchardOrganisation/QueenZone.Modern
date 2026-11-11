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

/** Direct child `key: value` of the block at `index`, or undefined. */
function childValue(lines, index, key) {
  const body = blockOf(lines, index);
  if (body.length === 0) return undefined;
  const childIndent = indentOf(lines[body[0]]);
  for (const i of body) {
    if (indentOf(lines[i]) !== childIndent) continue;
    const match = new RegExp(`^\\s*${key}:\\s*(.*)$`).exec(lines[i]);
    if (match) return match[1].trim();
  }
  return undefined;
}

export function suiteFlows(suiteText) {
  return [...suiteText.matchAll(/^- runFlow:\s*(\S+\.yaml)\s*$/gm)].map(match => match[1]);
}

export function firstCommand(text) {
  const { lines, start } = commandLines(text);
  for (let i = start; i < lines.length; i++) {
    const match = /^- ([A-Za-z]+)(?::\s*(.*))?$/.exec(lines[i]);
    if (match) return { name: match[1], value: (match[2] ?? '').trim() };
  }
  return null;
}

export function ownsStartState(text) {
  const first = firstCommand(text);
  if (!first) return false;
  if (first.name === 'launchApp' || first.name === 'openLink') return true;
  return first.name === 'runFlow' && /^(common\/|open-)/.test(first.value);
}

/** Returns [{ rule, line, message }] for the rules that apply to every flow file. */
export function checkFlowText(text, { suiteFlow = false } = {}) {
  const findings = [];
  const { lines, start } = commandLines(text);
  for (let i = start; i < lines.length; i++) {
    const line = lines[i];
    if (/^\s*-?\s*extendedWaitUntil:\s*$/.test(line)) {
      const timeout = childValue(lines, i, 'timeout');
      if (timeout === undefined) {
        findings.push({ rule: 'bounded-wait', line: i + 1, message: 'extendedWaitUntil has no timeout' });
      } else if (!/^\d+$/.test(timeout) || Number(timeout) > MAX_WAIT_MS) {
        findings.push({ rule: 'bounded-wait', line: i + 1, message: `timeout ${timeout} exceeds ${MAX_WAIT_MS}ms` });
      }
    }
    if (/^\s*-?\s*retry:\s*$/.test(line)) {
      const max = childValue(lines, i, 'maxRetries');
      if (max === undefined || !/^\d+$/.test(max) || Number(max) > 1) {
        findings.push({ rule: 'retry-justified', line: i + 1, message: `retry maxRetries must be 0 or 1 (got ${max ?? 'default'})` });
      }
      const above = lines.slice(Math.max(start, i - COMMENT_LOOKBACK), i);
      if (!above.some(prev => prev.trim().startsWith('#'))) {
        findings.push({ rule: 'retry-justified', line: i + 1, message: 'retry needs a comment above naming the infrastructure symptom' });
      }
    }
    if (suiteFlow && /^\s*optional:\s*true\s*$/.test(line)) {
      findings.push({ rule: 'no-optional', line: i + 1, message: 'suite flows must not mark a step optional' });
    }
  }
  if (suiteFlow && !ownsStartState(text)) {
    findings.push({ rule: 'start-state', line: 1, message: 'suite flow must start with launchApp, openLink, or a common/* or open-* setup subflow' });
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
  return out.sort();
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
    stale: [...allowed].filter(key => !found.has(key)).sort(),
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
