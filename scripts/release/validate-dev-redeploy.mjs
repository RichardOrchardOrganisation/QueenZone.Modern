#!/usr/bin/env node
import { appendFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export function validateDevRedeploy(env) {
  const force = env.FORCE_REDEPLOY || 'false';
  if (!['true', 'false'].includes(force)) {
    throw new Error('force_redeploy must be true or false.');
  }
  const expected = env.EXPECTED_MAIN_SHA || '';
  if (force === 'false') {
    if (expected) throw new Error('expected_main_sha requires force_redeploy=true.');
    return false;
  }
  if (env.GITHUB_EVENT_NAME !== 'workflow_dispatch' || env.GITHUB_REF !== 'refs/heads/main') {
    throw new Error('Forced DEV redeploy requires workflow_dispatch on main.');
  }
  if (!/^[0-9a-f]{40}$/.test(expected)) {
    throw new Error('expected_main_sha must be a full lowercase commit SHA.');
  }
  for (const key of ['GITHUB_SHA', 'CHECKOUT_SHA', 'CURRENT_MAIN_SHA']) {
    if (env[key] !== expected) {
      throw new Error(`${key} does not match expected_main_sha; refusing stale or mismatched DEV redeploy.`);
    }
  }
  return true;
}

if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) {
  try {
    const forced = validateDevRedeploy(process.env);
    if (!process.env.GITHUB_OUTPUT) throw new Error('GITHUB_OUTPUT is required.');
    appendFileSync(process.env.GITHUB_OUTPUT, `force_redeploy=${forced}\n`);
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
