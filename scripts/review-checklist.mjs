#!/usr/bin/env node
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';
import { resolveGitExecutable } from './lib/git-executable.mjs';

export { resolveGitExecutable };

function normalize(value) { return value.replaceAll('\\', '/'); }
function nonempty(value) { return typeof value === 'string' && value.trim().length > 0; }
function object(value) { return value !== null && typeof value === 'object' && !Array.isArray(value); }

export function parse(args) {
  const options = { rules: 'config/review-checklist.json', format: 'md' };
  const seen = new Set();
  for (let index = 0; index < args.length; index += 1) {
    const flag = args[index];
    if (!['--base', '--rules', '--format'].includes(flag)) throw new Error(`Unknown argument: ${flag}`);
    if (seen.has(flag)) throw new Error(`Specify ${flag} once.`);
    seen.add(flag);
    const value = args[++index];
    if (!nonempty(value) || value.startsWith('--')) throw new Error(`Missing value for ${flag}.`);
    options[flag.slice(2)] = value;
  }
  if (!options.base) throw new Error('Required: --base <ref>');
  if (!['md', 'json'].includes(options.format)) throw new Error('Format must be md or json.');
  options.rules = normalize(options.rules);
  if (path.posix.isAbsolute(options.rules) || path.win32.isAbsolute(options.rules) || /^[A-Za-z]:/.test(options.rules) || options.rules.split('/').includes('..')) {
    throw new Error('Rules path must be repository-relative without .. segments.');
  }
  return options;
}

function validDate(value) {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const date = new Date(`${value}T00:00:00.000Z`);
  return !Number.isNaN(date.valueOf()) && date.toISOString().slice(0, 10) === value;
}

function validateItem(item) {
  if (!object(item) || !nonempty(item.id) || !nonempty(item.check)) throw new Error('Each item requires nonempty id and check strings.');
  if (!Array.isArray(item.paths) || !item.paths.length || !item.paths.every(nonempty)) throw new Error('Each item requires nonempty path globs.');
  if (!object(item.source) || !['incident', 'repeated-finding'].includes(item.source.type) || !nonempty(item.source.ref)) throw new Error('Each item requires a valid source type and ref.');
  if ('automatedCheck' in item && item.automatedCheck !== null && typeof item.automatedCheck !== 'string') throw new Error('automatedCheck must be a string or null.');
  if ('owner' in item && typeof item.owner !== 'string') throw new Error('owner must be a string.');
  if ('expires' in item && !validDate(item.expires)) throw new Error('expires must be a UTC calendar date YYYY-MM-DD.');
}

function readRules(text) {
  let rules;
  try { rules = JSON.parse(text); }
  catch { throw new Error('Invalid rules JSON.'); }
  if (!object(rules) || rules.version !== 1 || !Array.isArray(rules.items)) throw new Error('Rules require version 1 and an items array.');
  rules.items.forEach(validateItem);
  return rules.items;
}

export function match(file, glob) {
  // Use POSIX matching for normalized Git paths on every operating system.
  return path.posix.matchesGlob(normalize(file), normalize(glob));
}

function git(executable, cwd, args) {
  const result = spawnSync(executable, args, { cwd, encoding: 'utf8' });
  if (result.error || result.status !== 0) throw new Error(`Git failed (${args[0]}).`);
  return result.stdout;
}

function baseRules(executable, cwd, base, rules) {
  const result = spawnSync(executable, ['show', `${base}:${rules}`], { cwd, encoding: 'utf8' });
  if (!result.error && result.status === 0) return readRules(result.stdout);
  if (result.error || result.status === null) throw new Error('Git failed (show).');
  const names = git(executable, cwd, ['ls-tree', '-r', '-t', '--name-only', '-z', base]).split('\0');
  if (!names.includes(rules)) return [];
  throw new Error('Git failed (show).');
}

function matchedItem(item, files) {
  const result = { id: item.id, check: item.check, paths: item.paths, source: item.source, files };
  for (const key of ['automatedCheck', 'owner', 'expires']) {
    if (key in item) result[key] = item[key];
  }
  return result;
}

export function run({ base, rules = 'config/review-checklist.json' }, cwd = process.cwd(), today = new Date().toISOString().slice(0, 10)) {
  const executable = resolveGitExecutable();
  const commit = git(executable, cwd, ['rev-parse', '--verify', '--end-of-options', `${base}^{commit}`]).trim();
  const files = git(executable, cwd, ['diff', '--name-only', '--no-renames', '-z', `${commit}...HEAD`]).split('\0').filter(Boolean).map(normalize).sort();
  const result = { items: [], stale: [] };
  const items = baseRules(executable, cwd, commit, rules).sort((a, b) => {
    if (a.id < b.id) return -1;
    if (a.id > b.id) return 1;
    return 0;
  });
  for (const item of items) {
    const matched = files.filter((file) => item.paths.some((glob) => match(file, glob)));
    if (!matched.length) continue;
    result[item.expires && item.expires < today ? 'stale' : 'items'].push(matchedItem(item, matched));
  }
  return result;
}

function markdown(result) {
  const lines = result.items.map((item) => `- [ ] ${item.id}: ${item.check} (${item.files.join(', ')})`);
  if (result.stale.length) {
    lines.push('', '## Stale review items', '', ...result.stale.map((item) => `- ${item.id}: ${item.check} (expired ${item.expires}; ${item.files.join(', ')})`));
  }
  return lines.join('\n').trim();
}

export function main(args = process.argv.slice(2), cwd = process.cwd()) {
  try {
    const options = parse(args);
    const result = run(options, cwd);
    if (options.format === 'json' || result.items.length || result.stale.length) console.log(options.format === 'json' ? JSON.stringify(result, null, 2) : markdown(result));
    return 0;
  } catch (error) {
    console.error(error.message);
    return 1;
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) process.exitCode = main();
