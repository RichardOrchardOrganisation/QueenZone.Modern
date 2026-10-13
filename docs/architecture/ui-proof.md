# Uploaded UI proof

UI PRs must provide reviewable screenshots and passing behavioural tests for the current PR head. The existing required `e2e-test` status enforces this for both PRs and merge groups. No new branch-protection context is needed.

## What counts

Declare affected feature-map IDs in the PR's `## Verification` section. Each changed mapped file needs a relevant declared feature; shared UI files need a representative feature on the affected surface. Web features need all mapped deterministic fixtures to pass and a screenshot of their mapped route. Native mobile features need Android and iOS evidence.

The gate discovers uploaded GitHub Actions artifacts through the API. It accepts only `ui-proof-*` bundles from the CI web-browser job or the existing Mobile device smoke proof jobs. It checks the current PR head, workflow identity, producer-job success, run attempt, artifact expiry, and size. It reads ZIP contents without extracting or executing them, validates PNG structure and checksums, and rechecks TRX/JUnit results. Zero tests, skipped tests, failures and missing screenshots fail closed.

The PR body cannot self-certify evidence. An arbitrary URL, a local `artifacts/proof/` path, an old workflow run, or `Not verified:` does not satisfy the gate. Dependabot UI changes follow the same rule; non-UI changes need no screenshot evidence.

## Web workflow

1. Make the change and declare its feature IDs.
2. CI runs the existing deterministic Playwright suite against Testing. Passing tests capture screenshots and their page URLs; only mapped features with passing fixtures and matching route screenshots enter the bundle.
3. CI uploads the bundle with 30-day retention. The required `e2e-test` job validates it after the browser job completes.
4. The trusted PR verification workflow posts artifact links in an **Uploaded UI verification** comment. Inspect those screenshots and record visual/journey observations in the PR.

If a feature lacks mapped tests or a matching screenshot, add coverage of the changed journey. A screenshot of an unrelated page cannot stand in for it. For visual changes, include before/after views where useful to reviewers. This automation establishes uploaded evidence, not human visual approval.

## Mobile workflow

For each affected feature, dispatch the existing device proof workflow at the current PR branch:

```powershell
gh workflow run mobile-device-smoke.yml --ref <branch> -f suite=proof -f feature=mobile.photos.viewer -f platform=both
```

The existing native build and Maestro flows produce screenshots and JUnit reports. Successful device jobs package and upload validated bundles automatically. When the workflow completes, the trusted publisher adds links and reruns only the inexpensive required evidence job. It does not rebuild or rerun the entire CI suite. Expo web and JS unit tests do not replace device proof.

A new commit invalidates previous proof. Rerun device proof for the new head. Artifacts expire after 30 days; rerun if they expire before merge. Web browser failures still block merge even when proof is waived.

## Explicit waiver

A maintainer must deliberately apply `no-ui-verification` and include a visible, non-empty line:

```text
Verification-skip-reason: <specific reason and outstanding validation>
```

Remove the code fence when putting the line in the PR description. Hidden template comments, empty reasons and a reason without the label do not waive proof. `Not verified:` remains useful disclosure, but is not a waiver. Removing the label or editing the description refreshes the gate; the merge queue checks the latest state again.

## Trust and merge queue

`pr-verification-check.yml` uses `pull_request_target` and `workflow_run` only to read uploaded data, post links and rerun the evidence job. It calls a shared action pinned to reviewed commit `7452829eef908d89aa6e1515369f1c052e25bc54`, which fetches only default-branch JSON configuration; it never checks out application code. Never change it to execute PR code or scripts from artifacts. The CI gate has read-only permissions.

The merge-group gate resolves constituent PRs through squash-commit associations (or the queue ref for a single unindexed commit) and checks their current proof/waivers. An unresolved group fails closed. The combined candidate still runs its own browser tests. A gate-only rerun preserves evidence from an unchanged successful producer job; a newer failed producer attempt invalidates older proof.

The trusted publisher becomes active when this workflow change reaches `main`; a PR cannot activate its own privileged publisher. Before that, CI can test packaging and the required gate on the PR, while publisher behaviour is covered by deterministic API fixtures.
