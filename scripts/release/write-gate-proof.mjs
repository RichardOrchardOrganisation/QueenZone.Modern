#!/usr/bin/env node
/**
 * Writes the production tip-gate `gate-proof` artifact (#2291).
 *
 * Shape: { tested_sha, full_suite }. DEV proofs take tested_sha from the
 * live DEV homepage `data-build-version` stamp, read in the test job
 * before and after the suite. The stamp is resolved through the GitHub
 * API and must be on main. Proof is written only when that commit is the
 * tip; an older main commit ends green with no artifact. A stamp that is
 * missing, malformed, or not on main fails closed. The run SHA is never
 * used as a fallback tested_sha.
 */
import { appendFileSync, mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export const DEV_PUBLIC_URL = 'https://dev.queenzone.org';
export const NON_MAIN_BUILD_ERROR = 'DEV is serving a non-main build';

const FULL_SHA = /^[0-9a-f]{40}$/;
const STAMP_SHA = /^[0-9a-f]{7,40}$/;
const BUILD_STAMP = /data-build-version="([^"]+)"/i;

export function buildGateProof({ testedSha, fullSuite }) {
  const sha = String(testedSha || '').trim().toLowerCase();
  if (!FULL_SHA.test(sha)) {
    throw new Error(`tested_sha must be a 40-character commit SHA, got ${JSON.stringify(testedSha)}`);
  }
  return {
    tested_sha: sha,
    full_suite: fullSuite === true,
  };
}

export function extractBuildStamp(html) {
  const match = BUILD_STAMP.exec(String(html || ''));
  return match ? match[1].trim() : '';
}

export function requireMatchingStamps(before, after) {
  const left = String(before || '').trim();
  const right = String(after || '').trim();
  if (!left || !right) {
    throw new Error('DEV homepage is missing data-build-version.');
  }
  if (left !== right) {
    throw new Error(`DEV build stamp changed during the tests: ${left} -> ${right}.`);
  }
  return right;
}

export function parseDevBuildStamp(stamp) {
  const token = String(stamp || '').trim().toLowerCase();
  if (!token) {
    throw new Error('DEV homepage is missing data-build-version.');
  }
  if (!STAMP_SHA.test(token)) {
    throw new Error(`Malformed DEV build stamp ${JSON.stringify(stamp)}.`);
  }
  return token;
}

export function isOnMainCompare(compare) {
  if (!compare || typeof compare !== 'object') {
    return false;
  }
  if (compare.status === 'identical') {
    return true;
  }
  return compare.status === 'ahead' && Number(compare.behind_by) === 0;
}

export function formatOlderMainNotice(servedSha, tipSha) {
  return `DEV is on ${servedSha}, tip is ${tipSha}; no proof for ${tipSha}`;
}

export function writeGithubFile(filePath, text) {
  if (!filePath) {
    return;
  }
  appendFileSync(filePath, text.endsWith('\n') ? text : `${text}\n`);
}

export async function githubJson(apiPath, { token, fetchImpl = fetch, baseUrl = 'https://api.github.com' }) {
  if (!token) {
    throw new Error('GITHUB_TOKEN is required to resolve the DEV build stamp.');
  }
  const response = await fetchImpl(`${baseUrl}${apiPath}`, {
    headers: {
      Authorization: `Bearer ${token}`,
      Accept: 'application/vnd.github+json',
      'X-GitHub-Api-Version': '2022-11-28',
      'User-Agent': 'queenzone-write-gate-proof',
    },
  });
  if (!response.ok) {
    const detail = await response.text();
    const error = new Error(`GitHub API ${apiPath} failed: HTTP ${response.status} ${detail}`.slice(0, 400));
    error.status = response.status;
    throw error;
  }
  return response.json();
}

export async function resolveDevStampOnMain(stamp, {
  tipSha,
  repo,
  token,
  fetchImpl = fetch,
  baseUrl = 'https://api.github.com',
} = {}) {
  const tokenStamp = parseDevBuildStamp(stamp);
  const tip = String(tipSha || '').trim().toLowerCase();
  if (!FULL_SHA.test(tip)) {
    throw new Error(`tested_sha must be a 40-character commit SHA, got ${JSON.stringify(tipSha)}`);
  }
  if (!repo) {
    throw new Error('GITHUB_REPOSITORY is required to resolve the DEV build stamp.');
  }

  let commit;
  try {
    commit = await githubJson(`/repos/${repo}/commits/${tokenStamp}`, { token, fetchImpl, baseUrl });
  } catch (error) {
    if (error.status === 404 || error.status === 422) {
      throw new Error(`Unknown DEV build stamp ${JSON.stringify(stamp)}; refusing to fall back to the run SHA.`);
    }
    throw error;
  }

  const full = String(commit?.sha || '').trim().toLowerCase();
  if (!FULL_SHA.test(full)) {
    throw new Error(`Unknown DEV build stamp ${JSON.stringify(stamp)}; refusing to fall back to the run SHA.`);
  }

  const compare = await githubJson(`/repos/${repo}/compare/${full}...main`, { token, fetchImpl, baseUrl });
  if (!isOnMainCompare(compare)) {
    throw new Error(`${NON_MAIN_BUILD_ERROR} ${full}`);
  }

  return {
    sha: full,
    tipSha: tip,
    onTip: full === tip || compare.status === 'identical',
  };
}

export function parseBooleanFlag(value) {
  const text = String(value ?? '').trim().toLowerCase();
  return text === 'true' || text === '1' || text === 'yes';
}

export async function fetchDevBuildStamp(url, fetchImpl = fetch) {
  const response = await fetchImpl(url, { redirect: 'follow' });
  if (!response.ok) {
    throw new Error(`DEV homepage returned HTTP ${response.status}`);
  }
  return extractBuildStamp(await response.text());
}

export function writeGateProofFile(filePath, proof) {
  mkdirSync(path.dirname(filePath), { recursive: true });
  writeFileSync(filePath, `${JSON.stringify(proof, null, 2)}\n`);
}

export function parseWriteProofArgs(argv, env = process.env) {
  const args = [...argv];
  const options = {
    dev: false,
    printStamp: false,
    out: 'gate-proof/gate-proof.json',
    fullSuite: parseBooleanFlag(env.GATE_FULL_SUITE ?? 'true'),
    sha: env.GITHUB_SHA || '',
    stamp: env.DEV_BUILD_STAMP || '',
    requireMatch: '',
    devUrl: env.DEV_PUBLIC_URL || DEV_PUBLIC_URL,
  };
  while (args.length) {
    const arg = args.shift();
    if (arg === '--dev') {
      options.dev = true;
    } else if (arg === '--print-stamp') {
      options.printStamp = true;
    } else if (arg === '--out') {
      options.out = args.shift();
    } else if (arg === '--full-suite') {
      options.fullSuite = parseBooleanFlag(args.shift());
    } else if (arg === '--sha') {
      options.sha = args.shift();
    } else if (arg === '--stamp') {
      options.stamp = args.shift();
    } else if (arg === '--require-match') {
      options.requireMatch = args.shift();
    } else {
      throw new Error(`Unknown argument: ${arg}`);
    }
  }
  return options;
}

export async function writeGateProofFromArgs(argv, env = process.env, fetchImpl = fetch) {
  const options = parseWriteProofArgs(argv, env);
  let stamp = options.stamp;
  if (options.dev || options.printStamp) {
    stamp = await fetchDevBuildStamp(options.devUrl, fetchImpl);
  }
  if (options.requireMatch) {
    stamp = requireMatchingStamps(options.requireMatch, stamp);
  }
  if (options.printStamp) {
    console.log(stamp);
    return { stamp };
  }
  if (!stamp) {
    const proof = buildGateProof({ testedSha: options.sha, fullSuite: options.fullSuite });
    writeGateProofFile(options.out, proof);
    writeGithubFile(env.GITHUB_OUTPUT, 'wrote=true');
    return proof;
  }

  const resolved = await resolveDevStampOnMain(stamp, {
    tipSha: options.sha,
    repo: env.GITHUB_REPOSITORY,
    token: env.GH_TOKEN || env.GITHUB_TOKEN,
    fetchImpl,
  });
  if (!resolved.onTip) {
    const notice = formatOlderMainNotice(resolved.sha, resolved.tipSha);
    console.log(`::notice::${notice}`);
    writeGithubFile(env.GITHUB_STEP_SUMMARY, `${notice}\n`);
    writeGithubFile(env.GITHUB_OUTPUT, 'wrote=false');
    return { skipped: true, servedSha: resolved.sha, tipSha: resolved.tipSha, notice };
  }

  const proof = buildGateProof({ testedSha: resolved.sha, fullSuite: options.fullSuite });
  writeGateProofFile(options.out, proof);
  writeGithubFile(env.GITHUB_OUTPUT, 'wrote=true');
  return proof;
}

const invokedDirectly = process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url;
if (invokedDirectly) {
  try {
    await writeGateProofFromArgs(process.argv.slice(2));
  } catch (error) {
    console.error(error.message || error);
    process.exitCode = 1;
  }
}
