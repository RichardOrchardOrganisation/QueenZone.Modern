import { createHash } from 'node:crypto';
import { appendFileSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const requireMatch = (value, pattern, name) => {
  if (!pattern.test(String(value || ''))) throw new Error(`Invalid ${name}`);
  return value;
};
export function identity(env) {
  return {
    release_sha: requireMatch(env.RELEASE_SHA, /^[a-f0-9]{40}$/, 'release SHA'),
    run_id: requireMatch(env.GITHUB_RUN_ID, /^[1-9][0-9]*$/, 'run ID'),
    run_attempt: requireMatch(env.GITHUB_RUN_ATTEMPT, /^[1-9][0-9]*$/, 'run attempt'),
  };
}
export function validateInputs(env) {
  identity(env);
  requireMatch(env.ARTIFACT_ID, /^[1-9][0-9]*$/, 'artifact ID');
  requireMatch(env.PACKAGE_SHA256, /^[a-f0-9]{64}$/, 'package SHA-256');
}
export function seal(bytes, env) {
  if (!bytes.length) throw new Error('Empty release package');
  return { version: 1, ...identity(env), package_sha256: digest(bytes) };
}
export function verify(bytes, manifest, env) {
  validateInputs(env);
  const expected = seal(bytes, env);
  if (!manifest || Object.keys(manifest).sort().join(',') !== Object.keys(expected).sort().join(',')) {
    throw new Error('Invalid release manifest fields');
  }
  for (const [key, value] of Object.entries(expected)) {
    if (manifest[key] !== value) throw new Error(`Release manifest mismatch: ${key}`);
  }
  if (expected.package_sha256 !== env.PACKAGE_SHA256) throw new Error('Release archive digest mismatch');
  return expected;
}
export function main(command, env = process.env) {
  if (command === 'validate-inputs') return validateInputs(env);
  const bytes = readFileSync('release-input/release-package.tar.gz');
  if (command === 'create') {
    const manifest = seal(bytes, env);
    writeFileSync('release-input/release-manifest.json', `${JSON.stringify(manifest)}\n`);
    if (!env.GITHUB_OUTPUT) throw new Error('GITHUB_OUTPUT is required');
    appendFileSync(env.GITHUB_OUTPUT, `release_sha=${manifest.release_sha}\npackage_sha256=${manifest.package_sha256}\n`);
    return manifest;
  }
  if (command === 'verify') {
    return verify(bytes, JSON.parse(readFileSync('release-input/release-manifest.json', 'utf8')), env);
  }
  throw new Error('Expected create, verify, or validate-inputs');
}
if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) {
  try { main(process.argv[2]); } catch (error) { console.error(error.message); process.exitCode = 1; }
}
