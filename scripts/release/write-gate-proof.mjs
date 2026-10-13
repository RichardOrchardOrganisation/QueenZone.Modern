#!/usr/bin/env node
/**
 * Writes the production tip-gate `gate-proof` artifact (#2291).
 *
 * Shape: { tested_sha, full_suite }. DEV proofs take tested_sha from the
 * live DEV homepage `data-build-version` stamp, expanded to the 40-character
 * SHA of this workflow run when the stamp is that SHA's short prefix.
 */
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export const DEV_PUBLIC_URL = 'https://dev.queenzone.org';

const FULL_SHA = /^[0-9a-f]{40}$/;
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

export function resolveDevTestedSha(stamp, headSha) {
  const sha = String(headSha || '').trim().toLowerCase();
  const token = String(stamp || '').trim().toLowerCase();
  if (!FULL_SHA.test(sha)) {
    throw new Error(`Workflow SHA must be a 40-character commit SHA, got ${JSON.stringify(headSha)}`);
  }
  if (!token) {
    throw new Error('DEV homepage is missing data-build-version.');
  }
  if (sha === token || sha.startsWith(token)) {
    return sha;
  }
  if (FULL_SHA.test(token)) {
    return token;
  }
  // Reused CI zip stamps the PR-head short SHA. The workflow_run is the
  // deployed main tip; that tip is the SHA the prod gate will look up.
  return sha;
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
    out: 'gate-proof/gate-proof.json',
    fullSuite: parseBooleanFlag(env.GATE_FULL_SUITE ?? 'true'),
    sha: env.GITHUB_SHA || '',
    devUrl: env.DEV_PUBLIC_URL || DEV_PUBLIC_URL,
  };
  while (args.length) {
    const arg = args.shift();
    if (arg === '--dev') {
      options.dev = true;
    } else if (arg === '--out') {
      options.out = args.shift();
    } else if (arg === '--full-suite') {
      options.fullSuite = parseBooleanFlag(args.shift());
    } else if (arg === '--sha') {
      options.sha = args.shift();
    } else {
      throw new Error(`Unknown argument: ${arg}`);
    }
  }
  return options;
}

export async function writeGateProofFromArgs(argv, env = process.env, fetchImpl = fetch) {
  const options = parseWriteProofArgs(argv, env);
  let testedSha = options.sha;
  if (options.dev) {
    const stamp = await fetchDevBuildStamp(options.devUrl, fetchImpl);
    testedSha = resolveDevTestedSha(stamp, options.sha);
  }
  const proof = buildGateProof({ testedSha, fullSuite: options.fullSuite });
  writeGateProofFile(options.out, proof);
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
