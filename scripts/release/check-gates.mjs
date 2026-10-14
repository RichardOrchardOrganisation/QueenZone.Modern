#!/usr/bin/env node
/**
 * Production tip-gate check for deploy.yml (#2291).
 *
 * Finds each gate run with actions/workflows/{file}/runs?head_sha=SHA&branch=main
 * (exact SHA, no ancestor rule), judges only the newest run for that SHA, then
 * requires the named `gate-proof` job to have succeeded on the latest attempt
 * and a matching `gate-proof` artifact.
 */
import { appendFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { inflateRawSync } from 'node:zlib';

export const DEFAULT_MAX_AGE_HOURS = 72;
export const OVERRIDE_MIN_LENGTH = 20;
export const RELEASE_LOG_ISSUE = 2222;
export const GATE_PROOF_ARTIFACT = 'gate-proof';
export const GATE_JOB_NAME = 'gate-proof';

export const GATES = [
  {
    id: 'nightly',
    workflowFile: 'nightly-legacy-checks.yml',
    dispatch: 'gh workflow run nightly-legacy-checks.yml --ref main',
  },
  {
    id: 'dev-member-auth',
    workflowFile: 'dev-member-auth-e2e.yml',
    dispatch: 'gh workflow run dev-member-auth-e2e.yml --ref main',
  },
  {
    id: 'dev-journey',
    workflowFile: 'dev-journey-e2e.yml',
    dispatch: 'gh workflow run dev-journey-e2e.yml --ref main',
  },
];

const FULL_SHA = /^[0-9a-f]{40}$/;
const SHA_PREFIX = /^[0-9a-f]{7,40}$/;

export function isVersionTagRef(ref) {
  return String(ref || '').startsWith('refs/tags/v');
}

export function normalizeSha(sha) {
  const value = String(sha || '').trim().toLowerCase();
  if (!SHA_PREFIX.test(value)) {
    throw new Error(`Invalid commit SHA: ${JSON.stringify(sha)}`);
  }
  return value;
}

export function parseMaxAgeHours(value) {
  if (value == null || value === '') {
    return DEFAULT_MAX_AGE_HOURS;
  }
  const hours = Number(value);
  if (!Number.isFinite(hours) || hours <= 0) {
    throw new Error(`PROD_GATE_MAX_AGE_HOURS must be a positive number, got ${JSON.stringify(value)}`);
  }
  return hours;
}

export function evaluateChanges(code, migrations) {
  if (code == null || code === '' || migrations == null || migrations === '') {
    throw new Error('Missing `changes` outputs; failing closed.');
  }
  if (code !== 'true' && code !== 'false') {
    throw new Error(`Unknown changes.code=${JSON.stringify(code)}; failing closed.`);
  }
  if (migrations !== 'true' && migrations !== 'false') {
    throw new Error(`Unknown changes.migrations=${JSON.stringify(migrations)}; failing closed.`);
  }
  if (code === 'true' || migrations === 'true') {
    return { needed: true };
  }
  return {
    needed: false,
    reason: 'Docs-, mobile- or workflow-only range; tip gates are not required.',
  };
}

export function parseGateProof(text) {
  const proof = JSON.parse(text);
  if (!proof || typeof proof !== 'object' || Array.isArray(proof)) {
    throw new Error('gate-proof artifact is not an object.');
  }
  const testedSha = String(proof.tested_sha || '').trim().toLowerCase();
  if (!FULL_SHA.test(testedSha)) {
    throw new Error('gate-proof.tested_sha must be a 40-character commit SHA.');
  }
  if (typeof proof.full_suite !== 'boolean') {
    throw new TypeError('gate-proof.full_suite must be a boolean.');
  }
  return { tested_sha: testedSha, full_suite: proof.full_suite };
}

export function findNamedJob(jobs, jobName = GATE_JOB_NAME) {
  return jobs?.find((job) => job?.name === jobName) ?? null;
}

export function formatDispatchCommands(gates = GATES) {
  return [
    'Re-run the missing gates on this SHA (do not pass skip_sync or category_filter):',
    ...gates.map((gate) => gate.dispatch),
  ].join('\n');
}

export function formatFailure(errors, gates = GATES) {
  return ['Production tip gates failed.', '', ...errors, '', formatDispatchCommands(gates)].join('\n');
}

export function validateOverride({ reason, ref, eventName }) {
  if (eventName === 'push') {
    throw new Error('Tag pushes cannot override tip gates. Re-dispatch the tag with gate_override_reason.');
  }
  if (eventName !== 'workflow_dispatch') {
    throw new Error('Gate override is only allowed on workflow_dispatch of a v* tag.');
  }
  if (!isVersionTagRef(ref)) {
    throw new Error('Gate override is allowed on v* tag refs only.');
  }
  const text = String(reason ?? '');
  if (text.trim().length < OVERRIDE_MIN_LENGTH) {
    throw new Error(`gate_override_reason must be at least ${OVERRIDE_MIN_LENGTH} characters.`);
  }
  return text;
}

export function buildOverrideComment({ reason, sha, ref, runUrl, actor }) {
  return [
    'Production tip-gate override',
    '',
    `- Env: production`,
    `- Ref: ${ref}`,
    `- Commit: ${sha}`,
    `- Actor: ${actor}`,
    `- Run: ${runUrl}`,
    '',
    'Reason:',
    reason,
  ].join('\n');
}

export function crc32(buffer) {
  let crc = ~0;
  for (const byte of buffer) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit += 1) {
      crc = crc & 1 ? (crc >>> 1) ^ 0xedb88320 : crc >>> 1;
    }
  }
  return (~crc) >>> 0;
}

export function createStoredZip(filename, content) {
  const name = Buffer.from(filename);
  const data = Buffer.from(content);
  const checksum = crc32(data);
  const local = Buffer.alloc(30);
  local.writeUInt32LE(0x04034b50, 0);
  local.writeUInt16LE(20, 4);
  local.writeUInt32LE(checksum, 14);
  local.writeUInt32LE(data.length, 18);
  local.writeUInt32LE(data.length, 22);
  local.writeUInt16LE(name.length, 26);
  const central = Buffer.alloc(46);
  central.writeUInt32LE(0x02014b50, 0);
  central.writeUInt16LE(20, 4);
  central.writeUInt16LE(20, 6);
  central.writeUInt32LE(checksum, 16);
  central.writeUInt32LE(data.length, 20);
  central.writeUInt32LE(data.length, 24);
  central.writeUInt16LE(name.length, 28);
  const eocd = Buffer.alloc(22);
  eocd.writeUInt32LE(0x06054b50, 0);
  eocd.writeUInt16LE(1, 8);
  eocd.writeUInt16LE(1, 10);
  eocd.writeUInt32LE(central.length + name.length, 12);
  eocd.writeUInt32LE(local.length + name.length + data.length, 16);
  return Buffer.concat([local, name, data, central, name, eocd]);
}

function findEndOfCentralDirectory(bytes) {
  const minOffset = Math.max(0, bytes.length - (22 + 0xffff));
  for (let offset = bytes.length - 22; offset >= minOffset; offset -= 1) {
    if (bytes.readUInt32LE(offset) === 0x06054b50) {
      return offset;
    }
  }
  return -1;
}

/**
 * Reads entries via the central directory, so archives that set general-purpose
 * flag bit 3 (data descriptor, as GitHub artifact zips do) work: their local
 * headers carry zero sizes/CRC, while the central directory has the real values.
 */
export function extractZipFiles(buffer) {
  const bytes = Buffer.from(buffer);
  const files = new Map();
  const eocd = findEndOfCentralDirectory(bytes);
  if (eocd < 0) {
    throw new Error('Zip end of central directory not found.');
  }
  const entryCount = bytes.readUInt16LE(eocd + 10);
  let offset = bytes.readUInt32LE(eocd + 16);
  for (let index = 0; index < entryCount; index += 1) {
    if (offset + 46 > bytes.length || bytes.readUInt32LE(offset) !== 0x02014b50) {
      throw new Error('Zip central directory entry is malformed.');
    }
    const method = bytes.readUInt16LE(offset + 10);
    const checksum = bytes.readUInt32LE(offset + 16);
    const compressedSize = bytes.readUInt32LE(offset + 20);
    const uncompressedSize = bytes.readUInt32LE(offset + 24);
    const nameLength = bytes.readUInt16LE(offset + 28);
    const extraLength = bytes.readUInt16LE(offset + 30);
    const commentLength = bytes.readUInt16LE(offset + 32);
    const localOffset = bytes.readUInt32LE(offset + 42);
    const name = bytes.subarray(offset + 46, offset + 46 + nameLength).toString();
    offset += 46 + nameLength + extraLength + commentLength;

    if (localOffset + 30 > bytes.length || bytes.readUInt32LE(localOffset) !== 0x04034b50) {
      throw new Error(`Zip local header for ${name} is malformed.`);
    }
    const localNameLength = bytes.readUInt16LE(localOffset + 26);
    const localExtraLength = bytes.readUInt16LE(localOffset + 28);
    const dataStart = localOffset + 30 + localNameLength + localExtraLength;
    if (dataStart + compressedSize > bytes.length) {
      throw new Error(`Zip entry ${name} is truncated.`);
    }
    const compressed = bytes.subarray(dataStart, dataStart + compressedSize);
    let data;
    if (method === 0) {
      data = compressed;
    } else if (method === 8) {
      data = inflateRawSync(compressed);
    } else {
      throw new Error(`Unsupported zip compression ${method} for ${name}`);
    }
    if (data.length !== uncompressedSize) {
      throw new Error(`Zip entry ${name} size mismatch.`);
    }
    if (crc32(data) !== checksum) {
      throw new Error(`Zip entry ${name} CRC mismatch.`);
    }
    files.set(name, data);
  }
  return files;
}

export function extractGateProofFromZip(buffer) {
  const files = extractZipFiles(buffer);
  const preferred = [...files.entries()].find(([name]) => name.endsWith('gate-proof.json') || name === 'gate-proof');
  const entry = preferred || [...files.entries()][0];
  if (!entry) {
    throw new Error('gate-proof zip did not contain a JSON file.');
  }
  return parseGateProof(entry[1].toString('utf8'));
}

export function parseCliArgs(argv) {
  const args = [...argv];
  const options = {
    sha: '',
    gateOnly: false,
    validateOverride: false,
    commentOverride: false,
  };
  while (args.length) {
    const arg = args.shift();
    if (arg === '--sha') {
      options.sha = args.shift();
    } else if (arg === '--gate-only' || arg === '--gate_only') {
      options.gateOnly = true;
    } else if (arg === '--validate-override') {
      options.validateOverride = true;
    } else if (arg === '--comment-override') {
      options.commentOverride = true;
    } else {
      throw new Error(`Unknown argument: ${arg}`);
    }
  }
  return options;
}

export function createGithubClient({ token, repo, fetchImpl = fetch, baseUrl = 'https://api.github.com' }) {
  if (!token) {
    throw new Error('GH_TOKEN or GITHUB_TOKEN is required.');
  }
  if (!repo) {
    throw new Error('GITHUB_REPOSITORY is required.');
  }

  async function githubFetch(apiPath, { raw = false, method = 'GET', body } = {}) {
    const headers = {
      Authorization: `Bearer ${token}`,
      Accept: 'application/vnd.github+json',
      'X-GitHub-Api-Version': '2022-11-28',
      'User-Agent': 'queenzone-check-gates',
    };
    if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
    }
    const response = await fetchImpl(`${baseUrl}${apiPath}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (!response.ok) {
      const detail = await response.text();
      throw new Error(`GitHub API ${apiPath} failed: HTTP ${response.status} ${detail}`.slice(0, 400));
    }
    return raw ? Buffer.from(await response.arrayBuffer()) : response.json();
  }

  return {
    async resolveSha(sha) {
      const normalized = normalizeSha(sha);
      if (FULL_SHA.test(normalized)) {
        return normalized;
      }
      const commit = await githubFetch(`/repos/${repo}/commits/${normalized}`);
      return normalizeSha(commit.sha);
    },
    async listWorkflowRuns(workflowFile, sha) {
      const query = new URLSearchParams({
        head_sha: sha,
        branch: 'main',
        per_page: '20',
      });
      const payload = await githubFetch(`/repos/${repo}/actions/workflows/${workflowFile}/runs?${query}`);
      return payload.workflow_runs || [];
    },
    async listJobs(runId) {
      const payload = await githubFetch(`/repos/${repo}/actions/runs/${runId}/jobs?filter=latest`);
      return payload.jobs || [];
    },
    async readGateProof(runId) {
      const payload = await githubFetch(`/repos/${repo}/actions/runs/${runId}/artifacts`);
      const artifacts = (payload.artifacts || [])
        .filter((artifact) => artifact.name === GATE_PROOF_ARTIFACT && !artifact.expired)
        .sort((left, right) => Date.parse(right.created_at) - Date.parse(left.created_at));
      if (!artifacts.length) {
        return null;
      }
      const zip = await githubFetch(`/repos/${repo}/actions/artifacts/${artifacts[0].id}/zip`, { raw: true });
      return extractGateProofFromZip(zip);
    },
    async commentOnReleaseLog(body) {
      await githubFetch(`/repos/${repo}/issues/${RELEASE_LOG_ISSUE}/comments`, {
        method: 'POST',
        body: { body },
      });
    },
  };
}

export async function evaluateRun(gate, run, sha, { maxAgeHours, now }) {
  if (run.head_sha !== sha) {
    return { ok: false, error: `run ${run.id} head_sha ${run.head_sha} is not ${sha}.` };
  }
  const job = findNamedJob(run.jobs, GATE_JOB_NAME);
  if (!job) {
    return { ok: false, error: `run ${run.id} has no ${GATE_JOB_NAME} job.` };
  }
  if (job.status !== 'completed' || job.conclusion !== 'success') {
    return {
      ok: false,
      error: `run ${run.id} ${GATE_JOB_NAME} is ${job.conclusion || job.status} on the latest attempt.`,
    };
  }
  const completedAt = Date.parse(job.completed_at || run.updated_at || '');
  if (!Number.isFinite(completedAt)) {
    return { ok: false, error: `run ${run.id} has no completion time.` };
  }
  const ageMs = now() - completedAt;
  if (ageMs > maxAgeHours * 60 * 60 * 1000) {
    return { ok: false, error: `run ${run.id} is older than ${maxAgeHours}h.` };
  }
  if (!run.proof) {
    return { ok: false, error: `run ${run.id} has no ${GATE_PROOF_ARTIFACT} artifact.` };
  }
  if (run.proof.full_suite !== true) {
    return { ok: false, error: `run ${run.id} is not a full suite (skip_sync or category_filter).` };
  }
  if (run.proof.tested_sha !== sha) {
    return { ok: false, error: `run ${run.id} tested_sha ${run.proof.tested_sha} != ${sha}.` };
  }
  return { ok: true };
}

export function selectNewestRun(runs, sha) {
  return [...(runs || [])]
    .filter((run) => run?.head_sha === sha && run.head_branch === 'main')
    .sort((left, right) => Date.parse(right.created_at) - Date.parse(left.created_at))[0] || null;
}

export async function evaluateGate(gate, sha, context, github) {
  const runs = await github.listWorkflowRuns(gate.workflowFile, sha);
  const run = selectNewestRun(runs, sha);
  if (!run) {
    return { ok: false, error: `${gate.id}: no workflow run on ${sha} (main).` };
  }
  const jobs = await github.listJobs(run.id);
  let proof = null;
  try {
    proof = await github.readGateProof(run.id);
  } catch (error) {
    return { ok: false, error: `${gate.id}: newest run ${run.id} artifact: ${error.message}` };
  }
  const verdict = await evaluateRun(gate, { ...run, jobs, proof }, sha, context);
  if (verdict.ok) {
    return { ok: true, runId: run.id };
  }
  return { ok: false, error: `${gate.id}: ${verdict.error}` };
}

export async function checkGates(input, github) {
  const eventName = input.eventName || '';
  const ref = input.ref || '';
  const gateOnly = input.gateOnly === true;
  if (eventName === 'workflow_dispatch' && !gateOnly && !isVersionTagRef(ref)) {
    throw new Error('workflow_dispatch must use a v* tag ref. Re-dispatch the tag.');
  }

  const required = evaluateChanges(input.changesCode, input.changesMigrations);
  if (!required.needed) {
    return { ok: true, skipped: true, reason: required.reason };
  }

  const sha = await github.resolveSha(input.sha);
  const context = {
    maxAgeHours: parseMaxAgeHours(input.maxAgeHours),
    now: input.now || (() => Date.now()),
  };
  const verdicts = await Promise.all(
    GATES.map((gate) => evaluateGate(gate, sha, context, github)),
  );
  const errors = verdicts.filter((verdict) => !verdict.ok).map((verdict) => verdict.error);
  if (errors.length) {
    throw new Error(formatFailure(errors));
  }
  return { ok: true, sha };
}

export function writeOverrideSummary(reason, summaryPath) {
  if (!summaryPath) {
    return;
  }
  appendFileSync(summaryPath, `## Gate override\n\n${reason}\n`);
}

export async function runOverrideComment(input, github) {
  const reason = validateOverride(input);
  writeOverrideSummary(reason, input.summaryPath);
  const body = buildOverrideComment({
    reason,
    sha: input.sha,
    ref: input.ref,
    runUrl: input.runUrl,
    actor: input.actor,
  });
  await github.commentOnReleaseLog(body);
  return { ok: true, body };
}

function envFlag(value) {
  const text = String(value ?? '').trim().toLowerCase();
  return text === 'true' || text === '1' || text === 'yes';
}

export async function main(argv = process.argv.slice(2), env = process.env, fetchImpl = fetch) {
  const options = parseCliArgs(argv);
  const input = {
    sha: options.sha || env.GITHUB_SHA || '',
    eventName: env.GITHUB_EVENT_NAME || env.EVENT_NAME || '',
    ref: env.GITHUB_REF || env.REF || '',
    gateOnly: options.gateOnly || envFlag(env.GATE_ONLY),
    changesCode: env.CHANGES_CODE,
    changesMigrations: env.CHANGES_MIGRATIONS,
    maxAgeHours: env.PROD_GATE_MAX_AGE_HOURS,
    reason: env.GATE_OVERRIDE_REASON,
    summaryPath: env.GITHUB_STEP_SUMMARY,
    runUrl: `${env.GITHUB_SERVER_URL || 'https://github.com'}/${env.GITHUB_REPOSITORY}/actions/runs/${env.GITHUB_RUN_ID || ''}`,
    actor: env.GITHUB_ACTOR || '',
  };
  if (options.validateOverride) {
    validateOverride(input);
    console.log('gate_override_reason is valid.');
    return { ok: true };
  }

  const github = createGithubClient({
    token: env.GH_TOKEN || env.GITHUB_TOKEN,
    repo: env.GITHUB_REPOSITORY,
    fetchImpl,
  });

  if (options.commentOverride) {
    const result = await runOverrideComment(input, github);
    console.log(`Posted override comment on #${RELEASE_LOG_ISSUE}.`);
    return result;
  }
  if (input.gateOnly) {
    console.log('gate_only dry run: checking tip gates, no migrate or deploy.');
  }
  const result = await checkGates(input, github);
  if (result.skipped) {
    console.log(result.reason);
    return result;
  }
  console.log(`All three tip gates passed for ${result.sha}.`);
  return result;
}

const invokedDirectly = process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url;
if (invokedDirectly) {
  try {
    await main();
  } catch (error) {
    console.error(error.message || error);
    process.exitCode = 1;
  }
}
