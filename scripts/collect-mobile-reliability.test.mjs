import { test } from 'node:test';
import assert from 'node:assert/strict';
import { collect, measurement, summarize, resolveGhExecutable } from './collect-mobile-reliability.mjs';
import { resolve } from 'node:path';

test('GitHub CLI resolution never searches PATH and rejects relative overrides', () => {
  assert.throws(() => resolveGhExecutable({ GH_EXECUTABLE: 'gh' }), /absolute path/);
  assert.throws(() => resolveGhExecutable({ PATH: '/untrusted' }, () => false), /trusted GitHub CLI/);
  assert.equal(resolveGhExecutable({ GH_EXECUTABLE: resolve('gh') }), resolve('gh'));
  assert.equal(resolveGhExecutable({}, candidate => candidate === '/usr/bin/gh'), '/usr/bin/gh');
});

const run = { id: 12, head_sha: 'abc', path: '.github/workflows/mobile-device-smoke.yml', run_attempt: 2 };
const job = { id: 3, name: 'Mobile iOS device smoke', status: 'completed', conclusion: 'success',
  started_at: '2026-11-11T00:00:00Z', completed_at: '2026-11-11T00:01:00Z' };

test('successful job does not imply a first-attempt pass or infer proof suite', () => {
  const row = measurement(run, job, 1);
  assert.equal(row.durationSeconds, 60);
  assert.equal(row.firstMaestroAttempt, 'unknown');
  assert.equal(row.recovery, 'unknown');
  assert.equal(row.suite, 'requires-input-review');
});

test('retains failed earlier attempts instead of replacing them with rerun success', () => {
  const report = collect(['12', '12'], endpoint => {
    if (endpoint === 'actions/runs/12') return run;
    return { jobs: [{ ...job, conclusion: endpoint.includes('/1/') ? 'failure' : 'success' }] };
  });
  assert.equal(report.rows.length, 2);
  assert.deepEqual(report.rows.map(row => row.attempt), [1, 2]);
  assert.equal(report.summary[0].failure, 1);
  assert.equal(report.summary[0].success, 1);
});

test('skips, cancellations and missing timestamps cannot become passes or durations', () => {
  const rows = ['skipped', 'cancelled', null].map(conclusion => measurement(run, { ...job, conclusion, completed_at: null }, 1));
  assert.ok(rows.every(row => row.durationSeconds === null));
  assert.equal(summarize(rows)[0].success, 0);
  assert.equal(summarize(rows)[0].skipped, 1);
  assert.equal(summarize(rows)[0].other, 2);
  assert.equal(measurement(run, { ...job, name: 'Choose iOS runner' }, 1), null);
});

test('paginates jobs and keeps commits separate', () => {
  const report = collect(['12'], endpoint => {
    if (endpoint === 'actions/runs/12') return { ...run, run_attempt: 1 };
    return { jobs: endpoint.endsWith('page=1') ? Array.from({ length: 100 }, (_, id) => ({ ...job, id })) : [job] };
  });
  assert.equal(report.rows.length, 101);
  assert.equal(summarize([measurement(run, job, 1), measurement({ ...run, head_sha: 'def' }, job, 1)]).length, 2);
});

test('rejects wrong workflows and incomplete API responses', () => {
  assert.throws(() => collect(['x'], () => run), /Invalid run/);
  assert.throws(() => collect(['12'], () => ({ ...run, path: 'other.yml' })), /not mobile/);
  assert.throws(() => collect(['12'], endpoint => endpoint === 'actions/runs/12' ? run : {}), /Missing jobs/);
});
