import { readFileSync } from 'node:fs';
import path from 'node:path';

export function loadProofConfig(root = process.cwd()) {
  const config = JSON.parse(readFileSync(path.join(root, 'development-standards.json'), 'utf8'));
  const policy = config.uiProof;
  if (!policy || !policy.baseBranch || !policy.gateJob || !policy.ciWorkflow || !policy.producers) {
    throw new Error('Configure uiProof baseBranch, gateJob, ciWorkflow and producers in development-standards.json.');
  }
  for (const [workflow, platforms] of Object.entries(policy.producers)) {
    if (!/^\.github\/workflows\/[\w.-]+\.ya?ml$/.test(workflow)) throw new Error('Invalid proof workflow path.');
    for (const jobs of Object.values(platforms)) {
      if (!jobs || !Object.keys(jobs).length || Object.values(jobs).some(name => typeof name !== 'string' || !name)) throw new Error('Proof producers require job IDs and display names.');
    }
  }
  return policy;
}

export function isProofUiPath(file, root = process.cwd()) {
  const { uiPaths } = JSON.parse(readFileSync(path.join(root, 'development-standards.json'), 'utf8'));
  if (!Array.isArray(uiPaths)) throw new Error('Configure uiPaths.');
  return !file.endsWith('.md') && !/\.(test|spec)\.[cm]?[jt]sx?$/.test(file)
    && uiPaths.some(prefix => prefix.endsWith('/') ? file.startsWith(prefix) : file === prefix);
}
