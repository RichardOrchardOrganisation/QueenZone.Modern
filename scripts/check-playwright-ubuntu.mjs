#!/usr/bin/env node
/**
 * Fail when a GitHub Actions job that installs Playwright browsers still
 * uses ubuntu-latest. ubuntu-latest is Ubuntu 26.04, and Microsoft.Playwright
 * 1.55.0 cannot install Chromium there (#2518).
 *
 *   node scripts/check-playwright-ubuntu.mjs
 */
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export const PIN_HINT = 'pin runs-on to ubuntu-24.04 (see #2518)';

const JOB_HEADER = /^  ([A-Za-z0-9_-]+):\s*$/gm;
const RUNS_ON_LINE = /^\s{4}runs-on:\s*(.+)$/m;
const RUNS_ON_LIST = /^\s{4}runs-on:\s*\n((?:\s{6}.+\n?)+)/m;

export function listWorkflowJobs(source) {
  const jobsHeader = source.match(/^jobs:\s*$/m);
  if (!jobsHeader) {
    return [];
  }

  const rest = source.slice(jobsHeader.index + jobsHeader[0].length);
  const headers = [];
  for (const match of rest.matchAll(JOB_HEADER)) {
    headers.push({ name: match[1], index: match.index });
  }

  return headers.map((header, index) => {
    const end = index + 1 < headers.length ? headers[index + 1].index : rest.length;
    return { name: header.name, body: rest.slice(header.index, end) };
  });
}

export function jobInstallsPlaywright(body) {
  return /\bplaywright\b/i.test(body) && /\binstall\b/i.test(body);
}

export function jobRunsOn(body) {
  const line = body.match(RUNS_ON_LINE);
  if (line) {
    return line[1].trim();
  }

  const list = body.match(RUNS_ON_LIST);
  if (!list) {
    return '';
  }

  return list[1]
    .split('\n')
    .map((entry) => entry.replace(/^\s*-\s*/, '').trim())
    .filter(Boolean)
    .join(', ');
}

export function usesUbuntuLatest(runsOn) {
  const value = String(runsOn || '').replace(/#.*$/, '').trim();
  return /(^|[\[,]\s*)ubuntu-latest(\s*[,\]]|$)/.test(value);
}

export function formatDiagnostic({ file, job, runsOn }) {
  return `QZ-PLAYWRIGHT-UBUNTU: ${file} job ${job} installs Playwright on ${runsOn || 'ubuntu-latest'}; ${PIN_HINT}`;
}

export function findUbuntuLatestPlaywrightJobs(source, file = 'workflow.yml') {
  return listWorkflowJobs(source)
    .filter((job) => jobInstallsPlaywright(job.body) && usesUbuntuLatest(jobRunsOn(job.body)))
    .map((job) => ({
      file,
      job: job.name,
      runsOn: jobRunsOn(job.body) || 'ubuntu-latest',
    }));
}

export function listWorkflowSources(root) {
  const dir = path.join(root, '.github', 'workflows');
  return readdirSync(dir)
    .filter((name) => name.endsWith('.yml') || name.endsWith('.yaml'))
    .sort((left, right) => left.localeCompare(right))
    .map((name) => {
      const file = `.github/workflows/${name}`;
      return { file, source: readFileSync(path.join(dir, name), 'utf8') };
    });
}

export function findRepoOffenders(root) {
  return listWorkflowSources(root).flatMap(({ file, source }) => findUbuntuLatestPlaywrightJobs(source, file));
}

export function main({
  root = process.cwd(),
  log = console.log,
  error = console.error,
} = {}) {
  const offenders = findRepoOffenders(root);
  if (offenders.length === 0) {
    log('Playwright install jobs are not on ubuntu-latest.');
    return 0;
  }

  for (const offender of offenders) {
    error(formatDiagnostic(offender));
  }
  return 1;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  process.exitCode = main();
}
