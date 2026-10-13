import { discoverEvidence } from './ui-proof-evidence.mjs';
import { isUiChangedPath, verificationSkipReason } from './check-pr-verification.mjs';

const marker = '<!-- queenzone-uploaded-ui-proof -->';

export async function refreshUiProof({ github, context, core, root, discover = discoverEvidence }) {
  const number = context.payload.pull_request?.number || Number(context.payload.inputs?.pr || 0);
  const candidates = number ? [(await github.rest.pulls.get({ ...context.repo, pull_number: number })).data]
    : (await github.paginate(github.rest.pulls.list, { ...context.repo, state: 'open', base: 'main', per_page: 100 }))
      .filter(pull => pull.head.sha === context.payload.workflow_run?.head_sha);
  for (const candidate of candidates) {
    const pull = (await github.rest.pulls.get({ ...context.repo, pull_number: candidate.number })).data;
    if (pull.state !== 'open') continue;
    const files = await github.paginate(github.rest.pulls.listFiles, { ...context.repo, pull_number: pull.number, per_page: 100 });
    if (!files.some(file => isUiChangedPath(file.filename, new Set()))) continue;
    const evidence = await discover({ github, repo: context.repo, headSha: pull.head.sha, root });
    const links = [...new Set(evidence.map(item => `- ${item.feature} / ${item.platform}: [uploaded proof](${item.url})`))];
    const reason = verificationSkipReason(pull.body);
    const waived = pull.labels.some(label => label.name === 'no-ui-verification') && reason;
    const status = waived ? `Explicit waiver: ${reason}` : 'The required e2e-test check validates feature coverage and evidence. Missing evidence blocks merge.';
    const body = `${marker}\n## Uploaded UI verification\n\nCommit: ${pull.head.sha}\n\n${status}\n\n${links.length ? links.join('\n') : 'No valid uploaded proof found for this commit.'}\n\nArtifacts expire after 30 days. Screenshots and passing tests do not replace visual review.`;
    const latest = (await github.rest.pulls.get({ ...context.repo, pull_number: pull.number })).data;
    if (latest.head.sha !== pull.head.sha) continue;
    const comments = await github.paginate(github.rest.issues.listComments, { ...context.repo, issue_number: pull.number, per_page: 100 });
    const existing = comments.find(comment => comment.user?.login === 'github-actions[bot]' && comment.body?.startsWith(marker));
    if (existing && existing.body !== body) await github.rest.issues.updateComment({ ...context.repo, comment_id: existing.id, body });
    else if (!existing) await github.rest.issues.createComment({ ...context.repo, issue_number: pull.number, body });
    // CI completion only publishes links: rerunning on every CI completion would loop.
    // Body/waiver edits and device-proof completion refresh only the cheap required job.
    const deviceCompleted = context.payload.workflow_run?.path === '.github/workflows/mobile-device-smoke.yml';
    const policyEdited = number && (!context.payload.action || ['edited', 'labeled', 'unlabeled', 'reopened'].includes(context.payload.action));
    if (!deviceCompleted && !policyEdited) continue;
    const runs = await github.paginate(github.rest.actions.listWorkflowRunsForRepo, { ...context.repo, head_sha: pull.head.sha, per_page: 100 });
    const run = runs.filter(item => item.path === '.github/workflows/ci.yml' && item.event === 'pull_request' && item.head_sha === pull.head.sha)
      .sort((a, b) => b.id - a.id)[0];
    if (!run) continue;
    const jobs = await github.paginate(github.rest.actions.listJobsForWorkflowRun, { ...context.repo, run_id: run.id, filter: 'latest', per_page: 100 });
    const gate = jobs.find(job => job.name === 'e2e-test');
    if (gate?.status === 'completed') {
      await github.rest.actions.reRunJobForWorkflowRun({ ...context.repo, job_id: gate.id });
      core.info(`Refreshed required evidence gate for PR #${pull.number}.`);
    }
  }
}
