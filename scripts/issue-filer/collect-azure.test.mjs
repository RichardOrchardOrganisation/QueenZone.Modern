import { mkdtempSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { collectAzureSignals } from './collect-azure.mjs';

test('collectAzureSignals writes empty files when Resource Graph fails', async () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'telemetry-az-'));
  const alertsOut = path.join(dir, 'alerts.json');
  const evidenceOut = path.join(dir, 'evidence.json');
  const result = await collectAzureSignals({
    subscription: 'sub',
    alertsOut,
    evidenceOut,
  }, {
    exec: async (bin, args) => {
      if (args[0] === 'graph') {
        return { code: 1, stdout: '', stderr: 'nope' };
      }
      return { code: 0, stdout: '', stderr: '' };
    },
  });
  assert.equal(result.warning, 'azure-graph-failed');
  assert.deepEqual(JSON.parse(readFileSync(alertsOut, 'utf8')), []);
  assert.deepEqual(JSON.parse(readFileSync(evidenceOut, 'utf8')), {});
});

test('collectAzureSignals skips evidence KQL when no qz-prod-* alerts fired', async () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'telemetry-az-'));
  const alertsOut = path.join(dir, 'alerts.json');
  const evidenceOut = path.join(dir, 'evidence.json');
  const queries = [];
  const result = await collectAzureSignals({
    subscription: 'sub',
    alertsOut,
    evidenceOut,
  }, {
    exec: async (bin, args) => {
      queries.push(args[0]);
      if (args[0] === 'graph') {
        return { code: 0, stdout: JSON.stringify({ count: 0, data: [] }), stderr: '' };
      }
      return { code: 0, stdout: '', stderr: '' };
    },
  });
  assert.equal(result.alerts.length, 0);
  assert.deepEqual(result.evidence, {});
  assert.ok(!queries.includes('monitor'));
});
