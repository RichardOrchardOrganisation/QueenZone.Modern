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
import {
  TELEMETRY_WINDOW_HOURS,
  REQUIRES_DIMENSION,
  argAlertsQuery,
  evidenceKqlForAlert,
  evidenceTimespan,
  parseArgAlerts,
  parseLookbackHours,
} from './telemetry.mjs';

export function parseArgs(argv) {
  const args = {
    subscription: '',
    alertsOut: '',
    evidenceOut: '',
    lookbackHours: TELEMETRY_WINDOW_HOURS,
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
    } else if (flag === '--warnings-out') {
      args.warningsOut = list.shift();
    } else if (flag === '--lookback-hours') {
      args.lookbackHours = parseLookbackHours(list.shift());
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

function collectResult({ alerts = [], evidence = {}, warnings = [] }) {
  return {
    alerts,
    evidence,
    warnings,
    warning: warnings[0] || '',
  };
}

export function isCollectFailure(warning) {
  return warning === 'azure-graph-failed'
    || warning === 'azure-workspace-failed'
    || String(warning).startsWith('azure-kql-failed:');
}

function writeOutputs(args, { alerts, evidence, warnings = [] }) {
  writeFileSync(args.alertsOut, `${JSON.stringify(alerts)}\n`);
  writeFileSync(args.evidenceOut, `${JSON.stringify(evidence)}\n`);
  const warningsOut = args.warningsOut || process.env.TELEMETRY_AZURE_WARNINGS_PATH;
  if (warningsOut && warnings.length > 0) {
    writeFileSync(warningsOut, `${warnings.join('\n')}\n`);
  }
}

function evidenceRows(payload) {
  if (Array.isArray(payload)) {
    return payload;
  }
  if (Array.isArray(payload?.data)) {
    return payload.data;
  }
  if (Array.isArray(payload?.tables)) {
    return payload.tables;
  }
  return [];
}

export async function collectAzureSignals(args, options = {}) {
  const lookbackHours = args.lookbackHours ?? TELEMETRY_WINDOW_HOURS;
  const alertsQuery = argAlertsQuery(lookbackHours);
  const timespan = evidenceTimespan(lookbackHours);
  const execOptions = { exec: options.exec, timeoutMs: options.timeoutMs };
  await runCommand('az', ['config', 'set', 'extension.use_dynamic_install=yes_without_prompt', '--only-show-errors'], execOptions);
  await runCommand('az', ['config', 'set', 'extension.dynamic_install_allow_preview=true', '--only-show-errors'], execOptions);
  await ensureExtension('resource-graph', execOptions);
  await ensureExtension('log-analytics', execOptions);

  const graph = await runCommand('az', azArgs([
    'graph',
    'query',
    '-q',
    alertsQuery,
    '--subscriptions',
    args.subscription,
    '--output',
    'json',
  ]), execOptions);
  if (graph.code !== 0) {
    const result = collectResult({ warnings: ['azure-graph-failed'] });
    writeOutputs(args, result);
    return result;
  }

  const alerts = parseArgAlerts(parseJson(graph.stdout, { data: [] }));
  const queryable = alerts
    .map((alert) => ({ alert, query: evidenceKqlForAlert(alert) }))
    .filter((item) => item.query);
  const warnings = alerts
    .filter((alert) => REQUIRES_DIMENSION.has(alert.rule) && !evidenceKqlForAlert(alert))
    .map((alert) => `azure-kql-unscoped:${alert.rule}`);
  if (queryable.length === 0) {
    const result = collectResult({ alerts, warnings });
    writeOutputs(args, result);
    return result;
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
    const result = collectResult({ alerts, warnings: [...warnings, 'azure-workspace-failed'] });
    writeOutputs(args, result);
    return result;
  }

  const evidence = {};
  for (const { alert, query } of queryable) {
    const kql = await runCommand('az', azArgs([
      'monitor',
      'log-analytics',
      'query',
      '--workspace',
      workspaceId,
      '--analytics-query',
      query,
      '--timespan',
      timespan,
      '--output',
      'json',
    ]), execOptions);
    if (kql.code !== 0) {
      warnings.push(`azure-kql-failed:${alert.rule}`);
      continue;
    }
    const rows = evidenceRows(parseJson(kql.stdout, []));
    evidence[alert.rule] = [...(evidence[alert.rule] || []), ...rows];
  }
  const result = collectResult({ alerts, evidence, warnings });
  writeOutputs(args, result);
  return result;
}

export async function main(argv = process.argv.slice(2), deps = {}) {
  const args = parseArgs(argv);
  const result = await collectAzureSignals(args, deps);
  (deps.stdout || console.log)(`Fired qz-prod-* alerts: ${result.alerts.length}`);
  (deps.stdout || console.log)(`Evidence rule buckets: ${Object.keys(result.evidence).length}`);
  const warn = deps.stderr || console.error;
  for (const warning of result.warnings || []) {
    warn(`::warning::Azure collect: ${warning}`);
  }
  if ((result.warnings || []).some(isCollectFailure)) {
    return 1;
  }
  return 0;
}

const invokedDirectly = process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href;
if (invokedDirectly) {
  try {
    const code = await main();
    if (code) {
      process.exitCode = code;
    }
  } catch (error) {
    console.error(`::warning::Azure collect: ${error.message || error}`);
    process.exitCode = 1;
  }
}
