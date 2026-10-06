import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { parseMapping, validateOutputs, execute } = require('../.github/actions/bitwarden-retry/guard/index.cjs');
const actionPath = new URL('../.github/actions/bitwarden-retry/action.yml', import.meta.url);
// Read the fixed scalar/step subset of the committed action; tests evaluate its
// actual conditions, rather than using a separate retry policy or real services.
function readAction(text) {
  const action = { outputs: {}, runs: { steps: [] } };
  let section = ''; let output; let step; let nested;
  const scalar = (value) => value.split(' #')[0].trim().replace(/^'(.*)'$/, '$1');
  for (const line of text.split('\n')) {
    if (line === 'outputs:' || line === 'runs:') { section = line; continue; }
    if (section === 'outputs:') {
      const name = /^  ([A-Z_0-9]+):$/.exec(line);
      if (name) { output = name[1]; action.outputs[output] = {}; }
      const value = /^    value: (.+)$/.exec(line);
      if (value) action.outputs[output].value = scalar(value[1]);
    }
    if (section !== 'runs:') continue;
    const start = /^    - (id|name): (.+)$/.exec(line);
    if (start) {
      step = { [start[1]]: scalar(start[2]) }; nested = null;
      action.runs.steps.push(step); continue;
    }
    const field = /^      ([a-z-]+):(?: (.+))?$/.exec(line);
    if (field && step) {
      nested = !field[2] ? field[1] : null;
      step[field[1]] = nested ? {} : scalar(field[2]);
      if (field[1] === 'continue-on-error') step[field[1]] = field[2] === 'true';
    }
    const child = /^        ([A-Za-z_-]+): (.+)$/.exec(line);
    if (child && nested) step[nested][child[1]] = scalar(child[2]);
  }
  assert.equal(action.runs.steps.length, 10);
  return action;
}
const action = readAction(readFileSync(actionPath, 'utf8'));
const mapping = '00000000-0000-0000-0000-000000000001 > AZURE_WEBAPP_PUBLISH_PROFILE\n00000000-0000-0000-0000-000000000002 > MOBILE_AUTH_SIGNING_KEY';
const complete = { AZURE_WEBAPP_PUBLISH_PROFILE: 'fake-profile', MOBILE_AUTH_SIGNING_KEY: 'fake-key' };
function evaluate(expression, steps, cancelled, inputs = {}) {
  if (!expression) return !cancelled;
  const source = expression.replace(/^\$\{\{\s*|\s*\}\}$/g, '');
  return Function('steps', 'cancelled', 'inputs', 'toJSON', `return (${source});`)(steps, () => cancelled, inputs, JSON.stringify);
}
function simulate(replies, { cancelledAt = '', publicationFailure = false, checkFailure = false, inputMapping = mapping } = {}) {
  const steps = new Proxy({}, { get: (target, key) => target[key] || { outputs: {}, outcome: 'skipped' } });
  const attempts = []; const waits = []; const publications = []; const environmentWrites = [];
  let cancelled = false; let failed = false;
  for (const step of action.runs.steps) {
    if (step.id === cancelledAt || step.name === cancelledAt) cancelled = true;
    if (!evaluate(step.if, steps, cancelled)) continue;
    const state = { outputs: {}, outcome: 'success' };
    let payload = '';
    try {
      if (step.run) waits.push(Number(step.run.split(' ')[1]));
      else if (step.uses.startsWith('bitwarden/')) {
        const reply = replies[attempts.length] || { failed: true };
        attempts.push(step.id);
        assert.equal(step.with.set_env, 'false');
        if (reply.timeout) { cancelled = true; throw new Error('whole-fetch timeout'); }
        if (reply.failed) { state.outputs = reply.outputs || {}; throw new Error('fake failure'); }
        state.outputs = reply.outputs || complete;
      } else {
        const phase = step.with.phase;
        const data = step.env ? evaluate(step.env.FETCH_OUTPUTS, steps, cancelled) : undefined;
        execute({ phase, mapping: inputMapping, data, outputPath: 'fake-output', mask: () => {},
          append: (_, value) => {
            payload += value;
            if (phase === 'check' && step.id === 'check1' && checkFailure) throw new Error('fake check write failure');
            if (phase === 'publish') {
              if (publicationFailure) throw new Error('fake partial write');
              publications.push(value);
            }
          } });
        if (payload.includes('complete=true\n')) state.outputs.complete = 'true';
        if (phase === 'publish') Object.assign(state.outputs, complete);
      }
    } catch {
      state.outcome = 'failure';
      if (!step['continue-on-error']) failed = true;
      // Model a runner that still parsed complete from a failed publication:
      // the composite's outcome gate must hide these partial outputs too.
      if (checkFailure && step.id === 'check1') state.outputs.complete = 'true';
      if (publicationFailure && step.id === 'publish') Object.assign(state.outputs, { ...complete, complete: 'true' });
    }
    if (step.id) steps[step.id] = state;
  }
  const exported = Object.fromEntries(Object.entries(action.outputs).map(([name, config]) => [name, evaluate(config.value, steps, cancelled)]));
  return { attempts, waits, exported, publications, environmentWrites, failed, cancelled };
}
for (const [name, replies, attempts, waits] of [
  ['initial success', [{}], 1, []],
  ['failure then success', [{ failed: true }, {}], 2, [20]],
  ['third attempt success', [{ failed: true }, { failed: true }, {}], 3, [20, 60]],
]) test(name, () => {
  const result = simulate(replies);
  assert.equal(result.attempts.length, attempts); assert.deepEqual(result.waits, waits);
  assert.equal(result.exported.MOBILE_AUTH_SIGNING_KEY, complete.MOBILE_AUTH_SIGNING_KEY);
  assert.equal(result.publications.length, 1); assert.equal(result.failed, false);
  assert.deepEqual(result.environmentWrites, []);
});
test('three permanent or transient failures remain a hard failure and export nothing', () => {
  const result = simulate([{ failed: true }, { failed: true }, { failed: true }]);
  assert.equal(result.attempts.length, 3); assert.deepEqual(result.waits, [20, 60]);
  assert.equal(result.failed, true); assert.equal(result.publications.length, 0);
  assert.ok(Object.values(result.exported).every((value) => value === ''));
});
test('malformed/duplicate mappings fail preflight with no fetch or wait', () => {
  for (const inputMapping of ['', 'bad > NAME', mapping + '\n00000000-0000-0000-0000-000000000003 > MOBILE_AUTH_SIGNING_KEY', '00000000-0000-0000-0000-000000000001 > COMPLETE']) {
    const result = simulate([{}], { inputMapping });
    assert.equal(result.failed, true); assert.equal(result.attempts.length, 0); assert.deepEqual(result.waits, []);
    assert.ok(Object.values(result.exported).every((value) => value === ''));
  }
});
test('partial successful exports are discarded and never combined across attempts', () => {
  const result = simulate([{ outputs: { AZURE_WEBAPP_PUBLISH_PROFILE: 'old-partial' } },
    { outputs: { MOBILE_AUTH_SIGNING_KEY: 'other-partial' } }, {}]);
  assert.equal(result.attempts.length, 3); assert.equal(result.publications.length, 1);
  assert.doesNotMatch(result.publications[0], /old-partial|other-partial/);
  assert.deepEqual(validateOutputs(parseMapping(mapping), JSON.stringify(complete)), complete);
});
test('failed attempt partial outputs cannot count as success', () => {
  const result = simulate([{ failed: true, outputs: complete }, {}]);
  assert.equal(result.attempts.length, 2); assert.deepEqual(result.waits, [20]);
});
test('failed completeness validator marker cannot select or stop retries', () => {
  const result = simulate([{}, {}], { checkFailure: true });
  assert.equal(result.attempts.length, 2); assert.deepEqual(result.waits, [20]);
  assert.equal(result.publications.length, 1); assert.equal(result.failed, false);
});
test('partial publication failure is hidden even if runner parsed its complete marker', () => {
  const result = simulate([{}], { publicationFailure: true });
  assert.equal(result.failed, true); assert.ok(Object.values(result.exported).every((value) => value === ''));
});
test('whole-fetch timeout stops retries and never publishes secrets', () => {
  const result = simulate([{ timeout: true }]);
  assert.equal(result.cancelled, true); assert.equal(result.attempts.length, 1);
  assert.equal(result.publications.length, 0); assert.deepEqual(result.waits, []);
  assert.ok(Object.values(result.exported).every((value) => value === ''));
});
test('cancellation before second or third attempt prevents further fetches/publication', () => {
  for (const [cancelledAt, count] of [['Wait before attempt 2', 1], ['Wait before attempt 3', 2]]) {
    const result = simulate([{ failed: true }, { failed: true }, {}], { cancelledAt });
    assert.equal(result.attempts.length, count); assert.equal(result.publications.length, 0);
    assert.ok(Object.values(result.exported).every((value) => value === ''));
  }
});
test('guard validates everything, masks before writing and never writes GITHUB_ENV', () => {
  const events = [];
  execute({ phase: 'publish', mapping, data: JSON.stringify(complete), outputPath: 'output',
    mask: () => events.push('mask'), append: (_, value) => events.push(value) });
  assert.deepEqual(events.slice(0, 2), ['mask', 'mask']); assert.match(events[2], /complete=true\n$/);
  const source = readFileSync(new URL('../.github/actions/bitwarden-retry/guard/index.cjs', import.meta.url), 'utf8');
  assert.doesNotMatch(source, /GITHUB_ENV|console\.log\(outputs/);
  assert.throws(() => validateOutputs(parseMapping(mapping), '{bad'), /invalid/);
  assert.throws(() => validateOutputs(parseMapping(mapping), JSON.stringify({ ...complete, EXTRA: 'value' })), /complete/);
});
test('CLI failure logs only fixed diagnostics, never malformed output payloads', () => {
  const dir = mkdtempSync(join(tmpdir(), 'qz-bitwarden-guard-')); const output = join(dir, 'output'); writeFileSync(output, '');
  let error;
  try { execFileSync(process.execPath, [new URL('../.github/actions/bitwarden-retry/guard/index.cjs', import.meta.url).pathname], {
    env: { INPUT_PHASE: 'publish', INPUT_MAPPING: mapping, FETCH_OUTPUTS: 'PRIVATE_TEST_PAYLOAD', GITHUB_OUTPUT: output },
    encoding: 'utf8', stdio: 'pipe',
  }); } catch (caught) { error = caught; }
  assert.equal(error.status, 1); assert.doesNotMatch(error.stderr, /PRIVATE_TEST_PAYLOAD/); assert.equal(readFileSync(output, 'utf8'), '');
});
test('only scoped consumers change; pinned action, input mappings and permissions remain', () => {
  assert.equal(action.runs.steps.filter((step) => step.uses?.startsWith('bitwarden/')).length, 3);
  for (const step of action.runs.steps.filter((step) => step.uses?.startsWith('bitwarden/'))) {
    assert.equal(step.uses, 'bitwarden/sm-action@1238aae8fc64b212641190a9227c8a734ab1a793');
    assert.equal(step.with.access_token, '${{ inputs.access_token }}'); assert.equal(step.with.secrets, '${{ inputs.secrets }}');
  }
  for (const name of ['deploy-dev.yml', 'nightly-legacy-checks.yml', 'opentofu-drift.yml']) {
    const path = `.github/workflows/${name}`;
    const original = execFileSync('git', ['show', `24b63548d5c0bb7adec0fbac7e115f855475a4db:${path}`], { encoding: 'utf8' });
    const current = readFileSync(new URL(`../${path}`, import.meta.url), 'utf8');
    const inputs = (text) => [...text.matchAll(/access_token:.*|secrets: \$\{\{ vars\.BITWARDEN[^\n]+/g)].map((match) => match[0].trim());
    assert.deepEqual(inputs(current), inputs(original));
    assert.equal((current.match(/timeout-minutes: 8/g) || []).length, (current.match(/uses: \.\/\.github\/actions\/bitwarden-retry/g) || []).length);
    for (const match of current.matchAll(/steps\.bitwarden-secrets\.outputs\.([A-Z_0-9]+)/g)) assert.ok(action.outputs[match[1]]);
  }
});
