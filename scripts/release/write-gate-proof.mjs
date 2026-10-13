#!/usr/bin/env node
/**
 * Writes the production tip-gate `gate-proof` artifact (#2291).
 *
 * Shape: { tested_sha, full_suite }. DEV proofs take tested_sha from the
 * live DEV homepage `data-build-version` stamp, read in the test job
 * before and after the suite. An unknown or missing stamp fails closed;
 * it is never replaced with the workflow run SHA.
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

export function resolveDevTestedSha(stamp, knownSha) {
  const token = String(stamp || '').trim().toLowerCase();
  const sha = String(knownSha || '').trim().toLowerCase();
  if (!token) {
    throw new Error('DEV homepage is missing data-build-version.');
  }
  if (FULL_SHA.test(token)) {
    return token;
  }
  if (FULL_SHA.test(sha) && sha.startsWith(token) && token.length >= 7) {
    return sha;
  }
  throw new Error(`Unknown DEV build stamp ${JSON.stringify(stamp)}; refusing to fall back to the run SHA.`);
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
  const testedSha = stamp ? resolveDevTestedSha(stamp, options.sha) : options.sha;
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
