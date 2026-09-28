import { mkdtempSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { collectAzureSignals, main } from './collect-azure.mjs';

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
  assert.deepEqual(result.warnings, ['azure-graph-failed']);
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

const firedFiveXx = {
  id: '/alerts/1',
  alertRule: 'qz-prod-server-5xx',
  startDateTime: '2026-09-27T10:00:00Z',
  essentials: {
    monitorCondition: 'Fired',
    dimensions: [{ name: 'Name', value: 'GET /news/1/story' }],
  },
};

function azExec(handlers) {
  return async (bin, args) => {
    if (args[0] === 'graph') {
      return handlers.graph ? handlers.graph(args) : { code: 0, stdout: JSON.stringify({ data: [] }), stderr: '' };
    }
    if (args[0] === 'monitor' && args.includes('workspace')) {
      return handlers.workspace ? handlers.workspace(args) : { code: 0, stdout: 'workspace-id\n', stderr: '' };
    }
    if (args[0] === 'monitor' && args.includes('--analytics-query')) {
      return handlers.kql ? handlers.kql(args) : { code: 0, stdout: '[]', stderr: '' };
    }
    return { code: 0, stdout: '', stderr: '' };
  };
}

test('collectAzureSignals scopes evidence KQL to the fired alert dimension', async () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'telemetry-az-'));
  const queries = [];
  const result = await collectAzureSignals({
    subscription: 'sub',
    alertsOut: path.join(dir, 'alerts.json'),
    evidenceOut: path.join(dir, 'evidence.json'),
  }, {
    exec: azExec({
      graph: () => ({ code: 0, stdout: JSON.stringify({ data: [firedFiveXx] }), stderr: '' }),
      kql: (args) => {
        queries.push(args[args.indexOf('--analytics-query') + 1]);
        return { code: 0, stdout: JSON.stringify([{ Name: 'GET /news/1/story', ResultCode: 500, ItemCount: 2 }]), stderr: '' };
      },
    }),
  });
  assert.equal(result.alerts.length, 1);
  assert.equal(queries.length, 1);
  assert.match(queries[0], /Name == 'GET \/news\/1\/story'/);
  assert.ok(!queries[0].includes('summarize') || queries[0].includes('| where Name =='));
});

test('collectAzureSignals skips unscoped split-rule KQL', async () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'telemetry-az-'));
  const kqlCalls = [];
  const result = await collectAzureSignals({
    subscription: 'sub',
    alertsOut: path.join(dir, 'alerts.json'),
    evidenceOut: path.join(dir, 'evidence.json'),
  }, {
    exec: azExec({
      graph: () => ({
        code: 0,
        stdout: JSON.stringify({
          data: [{
            id: '/alerts/2',
            alertRule: 'qz-prod-exception-new-problem',
            essentials: { monitorCondition: 'Fired' },
          }],
        }),
        stderr: '',
      }),
      kql: (args) => {
        kqlCalls.push(args);
        return { code: 0, stdout: '[]', stderr: '' };
      },
    }),
  });
  assert.equal(kqlCalls.length, 0);
  assert.equal(result.warning, 'azure-kql-unscoped:qz-prod-exception-new-problem');
  assert.deepEqual(result.evidence, {});
});

test('collectAzureSignals warns when evidence KQL fails', async () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'telemetry-az-'));
  const result = await collectAzureSignals({
    subscription: 'sub',
    alertsOut: path.join(dir, 'alerts.json'),
    evidenceOut: path.join(dir, 'evidence.json'),
  }, {
    exec: azExec({
      graph: () => ({ code: 0, stdout: JSON.stringify({ data: [firedFiveXx] }), stderr: '' }),
      kql: () => ({ code: 1, stdout: '', stderr: 'bad kql' }),
    }),
  });
  assert.equal(result.warning, 'azure-kql-failed:qz-prod-server-5xx');
  assert.deepEqual(result.evidence, {});
});

test('main emits a workflow warning and fails closed on Azure collect errors', async () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'telemetry-az-'));
  const err = [];
  const code = await main([
    '--subscription', 'sub',
    '--alerts-out', path.join(dir, 'alerts.json'),
    '--evidence-out', path.join(dir, 'evidence.json'),
    '--warnings-out', path.join(dir, 'warnings.txt'),
  ], {
    exec: azExec({
      graph: () => ({ code: 1, stdout: '', stderr: 'nope' }),
    }),
    stdout: () => {},
    stderr: (line) => err.push(String(line)),
  });
  assert.equal(code, 1);
  assert.match(err.join('\n'), /::warning::Azure collect: azure-graph-failed/);
  assert.match(readFileSync(path.join(dir, 'warnings.txt'), 'utf8'), /azure-graph-failed/);
});
