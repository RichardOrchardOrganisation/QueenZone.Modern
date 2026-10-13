import { execFileSync } from 'node:child_process';
import path from 'node:path';

export function decodeProofArchive(data, root) {
  return JSON.parse(execFileSync('python3', [path.join(root, 'scripts/ui_proof_bundle.py'), 'validate'], {
    input: Buffer.from(data), maxBuffer: 2 * 1024 * 1024, timeout: 20_000,
  }).toString());
}

const producers = {
  '.github/workflows/ci.yml': { web: { 'web-browser-tests': 'Web browser tests' } },
  '.github/workflows/mobile-device-smoke.yml': {
    android: { 'mobile-android-smoke': 'Mobile Android device smoke', 'mobile-android-journeys': 'Mobile Android extended device suite' },
    ios: { 'mobile-ios-smoke': 'Mobile iOS device smoke' },
  },
};

export async function discoverEvidence({ github, repo, headSha, root, decode = decodeProofArchive }) {
  const runs = await github.paginate(github.rest.actions.listWorkflowRunsForRepo, { ...repo, head_sha: headSha, per_page: 100 });
  const evidence = [];
  for (const run of runs) {
    if (run.head_sha !== headSha || !producers[run.path] || !['pull_request', 'workflow_dispatch'].includes(run.event)) continue;
    const jobs = await github.paginate(github.rest.actions.listJobsForWorkflowRun, { ...repo, run_id: run.id, filter: 'all', per_page: 100 });
    const artifacts = await github.paginate(github.rest.actions.listWorkflowRunArtifacts, { ...repo, run_id: run.id, per_page: 100 });
    for (const artifact of artifacts) {
      if (!artifact.name.startsWith('ui-proof-') || artifact.expired || artifact.size_in_bytes <= 0 || artifact.size_in_bytes > 50 * 1024 * 1024) continue;
      let manifest;
      try {
        const response = await github.rest.actions.downloadArtifact({ ...repo, artifact_id: artifact.id, archive_format: 'zip' });
        manifest = decode(response.data, root);
      } catch (error) {
        // Missing, expired, corrupt and invalid archives cannot count as evidence.
        if (error.status && ![404, 410].includes(error.status)) throw error;
        continue;
      }
      if (manifest.headSha !== headSha || String(manifest.runId) !== String(run.id) || Number(manifest.attempt) > run.run_attempt) continue;
      for (const entry of manifest.entries) {
        const expectedJob = producers[run.path][entry.platform]?.[manifest.job];
        const producer = jobs.filter(job => job.name === expectedJob).sort((a, b) => b.run_attempt - a.run_attempt || b.id - a.id)[0];
        if (!producer || producer.conclusion !== 'success' || String(producer.run_attempt) !== String(manifest.attempt)) continue;
        evidence.push({ ...entry, headSha, url: `https://github.com/${repo.owner}/${repo.repo}/actions/runs/${run.id}/artifacts/${artifact.id}` });
      }
    }
  }
  return evidence;
}
