import { discoverEvidence } from './ui-proof-evidence.mjs';
import { verificationSkipReason } from './ui-proof-policy.mjs';
import { isProofUiPath, loadProofConfig } from './ui-proof-config.mjs';

const marker = '<!-- development-standards-uploaded-ui-proof -->';

export async function refreshUiProof({ github, context, core, root, discover = discoverEvidence, policy = loadProofConfig(root) }) {
  const number = context.payload.pull_request?.number || Number(context.payload.inputs?.pr || 0);
  const candidates = number ? [(await github.rest.pulls.get({ ...context.repo, pull_number: number })).data]
    : (await github.paginate(github.rest.pulls.list, { ...context.repo, state: 'open', base: policy.baseBranch, per_page: 100 }))
      .filter(pull => pull.head.sha === context.payload.workflow_run?.head_sha);
  for (const candidate of candidates) {
    await refreshPullProof({ github, context, core, root, discover, policy, number }, candidate);
  }
}

async function refreshPullProof({ github, context, core, root, discover, policy, number }, candidate) {
  const pull = (await github.rest.pulls.get({ ...context.repo, pull_number: candidate.number })).data;
  if (pull.state !== 'open') return;
  const files = await github.paginate(github.rest.pulls.listFiles, { ...context.repo, pull_number: pull.number, per_page: 100 });
  if (!files.some(file => isProofUiPath(file.filename, root))) return;
  const evidence = await discover({ github, repo: context.repo, headSha: pull.head.sha, root });
  const links = [...new Set(evidence.map(item => `- ${item.feature} / ${item.platform}: [uploaded proof](${item.url})`))];
  const reason = verificationSkipReason(pull.body);
  const waived = pull.labels.some(label => label.name === 'no-ui-verification') && reason;
  const status = waived ? `Explicit waiver: ${reason}` : `The required ${policy.gateJob} check validates feature coverage and evidence. Missing evidence blocks merge.`;
  const body = `${marker}\n## Uploaded UI verification\n\nCommit: ${pull.head.sha}\n\n${status}\n\n${links.length ? links.join('\n') : 'No valid uploaded proof found for this commit.'}\n\nArtifacts follow the producer workflow retention period. Screenshots and passing tests do not replace visual review.`;
  const latest = (await github.rest.pulls.get({ ...context.repo, pull_number: pull.number })).data;
  if (latest.head.sha !== pull.head.sha) return;
  await publishProofComment(github, context.repo, pull.number, body);
  await refreshGate({ github, context, core, policy, number }, pull);
}

async function publishProofComment(github, repo, number, body) {
  const comments = await github.paginate(github.rest.issues.listComments, { ...repo, issue_number: number, per_page: 100 });
  const existing = comments.find(comment => comment.user?.login === 'github-actions[bot]' && comment.body?.startsWith(marker));
  if (existing && existing.body !== body) await github.rest.issues.updateComment({ ...repo, comment_id: existing.id, body });
  else if (!existing) await github.rest.issues.createComment({ ...repo, issue_number: number, body });
}

async function refreshGate({ github, context, core, policy, number }, pull) {
  // CI completion only publishes links: rerunning on every CI completion would loop.
  // Body/waiver edits and device-proof completion refresh only the cheap required job.
  const deviceCompleted = context.payload.workflow_run?.path !== policy.ciWorkflow && Boolean(policy.producers[context.payload.workflow_run?.path]);
  const policyEdited = number && (!context.payload.action || ['edited', 'labeled', 'unlabeled', 'reopened'].includes(context.payload.action));
  if (!deviceCompleted && !policyEdited) return;
  const runs = await github.paginate(github.rest.actions.listWorkflowRunsForRepo, { ...context.repo, head_sha: pull.head.sha, per_page: 100 });
  const run = runs.filter(item => item.path === policy.ciWorkflow && item.event === 'pull_request' && item.head_sha === pull.head.sha)
    .sort((a, b) => b.id - a.id)[0];
  if (!run) return;
  const jobs = await github.paginate(github.rest.actions.listJobsForWorkflowRun, { ...context.repo, run_id: run.id, filter: 'latest', per_page: 100 });
  const gate = jobs.find(job => job.name === policy.gateJob);
  if (gate?.status === 'completed') {
    await github.rest.actions.reRunJobForWorkflowRun({ ...context.repo, job_id: gate.id });
    core.info(`Refreshed required evidence gate for PR #${pull.number}.`);
  }
}
