#!/usr/bin/env node
/**
 * Read-only App Insights signal collection for telemetry-triage.yml.
 * Writes alert and evidence JSON. Prints counts only — never workspace IDs,
 * query rows, or secret values.
 */
import { spawn } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { ARG_ALERTS_QUERY, EVIDENCE_KQL, firedRuleNames, parseArgAlerts } from './telemetry.mjs';

function parseArgs(argv) {
  const args = {
    subscription: '',
    alertsOut: '',
    evidenceOut: '',
  };
  const list = [...argv];
  while (list.length > 0) {
    const flag = list.shift();
    if (flag === '--subscription') {
      args.subscription = list.shift();
    } else if (flag === '--alerts-out') {
      args.alertsOut = list.shift();
    } else if (flag === '--evidence-out') {
      args.evidenceOut = list.shift();
    } else {
      throw new Error(`Unknown argument: ${flag}`);
    }
  }
  if (!args.subscription || !args.alertsOut || !args.evidenceOut) {
    throw new Error('--subscription, --alerts-out, and --evidence-out are required');
  }
  return args;
}

export function azArgs(command, extra = []) {
  return [...command, ...extra, '--only-show-errors'];
}

export async function runCommand(bin, args, { exec, timeoutMs = 60_000 } = {}) {
  if (exec) {
    return exec(bin, args);
  }
  return new Promise((resolve, reject) => {
    const child = spawn(bin, args, { stdio: ['ignore', 'pipe', 'pipe'] });
    const stdout = [];
    const stderr = [];
    const timer = setTimeout(() => {
      child.kill('SIGTERM');
      reject(new Error(`${bin} timed out`));
    }, timeoutMs);
    child.stdout.on('data', (chunk) => stdout.push(chunk));
    child.stderr.on('data', (chunk) => stderr.push(chunk));
    child.on('error', (error) => {
      clearTimeout(timer);
      reject(error);
    });
    child.on('close', (code) => {
      clearTimeout(timer);
      resolve({
        code: code ?? 1,
        stdout: Buffer.concat(stdout).toString('utf8'),
        stderr: Buffer.concat(stderr).toString('utf8'),
      });
    });
  });
}

async function ensureExtension(name, options) {
  const shown = await runCommand('az', azArgs(['extension', 'show', '--name', name]), options);
  if (shown.code === 0) {
    return;
  }
  const added = await runCommand('az', azArgs(['extension', 'add', '--name', name, '--allow-preview', 'true']), options);
  if (added.code !== 0) {
    throw new Error(`az extension add ${name} failed`);
  }
}

function parseJson(text, fallback) {
  if (!text || !String(text).trim()) {
    return fallback;
  }
  try {
    return JSON.parse(text);
  } catch {
    return fallback;
  }
}

export async function collectAzureSignals(args, options = {}) {
  const execOptions = { exec: options.exec, timeoutMs: options.timeoutMs };
  await runCommand('az', ['config', 'set', 'extension.use_dynamic_install=yes_without_prompt', '--only-show-errors'], execOptions);
  await runCommand('az', ['config', 'set', 'extension.dynamic_install_allow_preview=true', '--only-show-errors'], execOptions);
  await ensureExtension('resource-graph', execOptions);
  await ensureExtension('log-analytics', execOptions);

  const graph = await runCommand('az', azArgs([
    'graph',
    'query',
    '-q',
    ARG_ALERTS_QUERY,
    '--subscriptions',
    args.subscription,
    '--output',
    'json',
  ]), execOptions);
  if (graph.code !== 0) {
    writeFileSync(args.alertsOut, '[]\n');
    writeFileSync(args.evidenceOut, '{}\n');
    return { alerts: [], evidence: {}, warning: 'azure-graph-failed' };
  }

  const alerts = parseArgAlerts(parseJson(graph.stdout, { data: [] }));
  writeFileSync(args.alertsOut, `${JSON.stringify(alerts)}\n`);
  const rules = firedRuleNames(alerts);
  if (rules.length === 0) {
    writeFileSync(args.evidenceOut, '{}\n');
    return { alerts, evidence: {}, warning: '' };
  }

  const workspace = await runCommand('az', azArgs([
    'monitor',
    'log-analytics',
    'workspace',
    'show',
    '--subscription',
    args.subscription,
    '--resource-group',
    'Queenzone-RG',
    '--workspace-name',
    'queenzone-prod-law',
    '--query',
    'customerId',
    '--output',
    'tsv',
  ]), execOptions);
  const workspaceId = String(workspace.stdout || '').trim();
  if (workspace.code !== 0 || !workspaceId) {
    writeFileSync(args.evidenceOut, '{}\n');
    return { alerts, evidence: {}, warning: 'azure-workspace-failed' };
  }

  const evidence = {};
  for (const rule of rules) {
    const query = EVIDENCE_KQL[rule];
    const result = await runCommand('az', azArgs([
      'monitor',
      'log-analytics',
      'query',
      '--workspace',
      workspaceId,
      '--analytics-query',
      query,
      '--timespan',
      'PT2H',
      '--output',
      'json',
    ]), execOptions);
    evidence[rule] = result.code === 0 ? parseJson(result.stdout, []) : [];
    if (!Array.isArray(evidence[rule])) {
      evidence[rule] = evidence[rule]?.data || evidence[rule]?.tables || [];
      if (!Array.isArray(evidence[rule])) {
        evidence[rule] = [];
      }
    }
  }
  writeFileSync(args.evidenceOut, `${JSON.stringify(evidence)}\n`);
  return { alerts, evidence, warning: '' };
}

export async function main(argv = process.argv.slice(2), deps = {}) {
  const args = parseArgs(argv);
  const result = await collectAzureSignals(args, deps);
  (deps.stdout || console.log)(`Fired qz-prod-* alerts: ${result.alerts.length}`);
  (deps.stdout || console.log)(`Evidence rule buckets: ${Object.keys(result.evidence).length}`);
  if (result.warning) {
    (deps.stdout || console.log)(`Azure collect warning: ${result.warning}`);
  }
  return 0;
}

const invokedDirectly = process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href;
if (invokedDirectly) {
  main().catch((error) => {
    console.error(error.message || error);
    process.exitCode = 1;
  });
}
