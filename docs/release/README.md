# Production release runbook

This is the single source of truth for shipping QueenZone to production. `AGENTS.md`, `README.md`, [`testing-policy.md`](../architecture/testing-policy.md) and [`github-environments.md`](../architecture/github-environments.md) all point here.

`deploy.yml` enforces the three tip gates in `scripts/release/check-gates.mjs`. The job is not a pull-request check. It fails closed, with no polling and no sleeps. On failure it prints the exact `gh workflow run` commands below.

## Scope

Diff `main`'s tip against the live production tag and list the pull requests going out:

```bash
git fetch origin main --tags
git log --oneline "$(git describe --tags --abbrev=0 --match 'v*')..origin/main"
gh pr list --search "$(git describe --tags --abbrev=0 --match 'v*')..origin/main" --state merged
```

Call out mobile changes that need a native store build (not an OTA). `src/QueenZone.Mobile` changes that affect generated Android/iOS projects still need Play / TestFlight; a website-only tag does not ship a store build.

A docs-, mobile- or workflow-only range does not run migrate or deploy. `check-gates` still runs, sees `changes.code=false` and `changes.migrations=false`, and succeeds without looking up the three workflows. Missing `changes` outputs fail closed.

## Three tip gates

Freeze `main`. Wait for all three gates to go green on the **exact tip SHA**. There is no ancestor rule and no waive. If a gate is red, stop and tell Richard.

The three gates:

1. **Nightly RealData** — `.github/workflows/nightly-legacy-checks.yml`. All jobs. A dispatch narrowed with `skip_sync` or `category_filter` does not count.
2. **DEV member auth** — `.github/workflows/dev-member-auth-e2e.yml` against `https://dev.queenzone.org`.
3. **DEV journey** — `.github/workflows/dev-journey-e2e.yml` against `https://dev.queenzone.org` (issue #2170).

DEV member-auth and DEV journey keep their 07:00 UTC crons and also run through `on: workflow_run` after a successful `deploy-dev`. Re-dispatch on every new tip if you cannot wait for that path:

```bash
gh workflow run nightly-legacy-checks.yml --ref main
gh workflow run dev-member-auth-e2e.yml --ref main
gh workflow run dev-journey-e2e.yml --ref main
```

Each gate uploads a `gate-proof` artifact `{ tested_sha, full_suite }`. For the DEV gates, `tested_sha` comes from the live DEV `data-build-version` stamp. `deploy.yml` requires `tested_sha` to equal the deploy SHA. Freshness is at most 72 hours, set by the repo variable `PROD_GATE_MAX_AGE_HOURS`.

`check-gates.mjs` finds the run with `actions/workflows/{file}/runs?head_sha=SHA&branch=main`, then fetches `runs/{id}/jobs` and requires the `gate-proof` job itself to have succeeded on the latest attempt. A run whose jobs were all skipped still reports workflow success and does not count.

An older or cherry-picked SHA cannot be gated through `workflow_run`. Shipping one needs the override.

Local / dry-run:

```bash
node scripts/release/check-gates.mjs --sha <full-or-short-sha>
```

`workflow_dispatch` of `deploy.yml` with `gate_only=true` runs the same check and stops before migrate.

## Tag

Use `vYYYY.MM.DD.N` on that SHA as soon as all three gates are green:

```bash
git checkout main
git pull --ff-only origin main
git tag vYYYY.MM.DD.N <gated-sha>
git push origin vYYYY.MM.DD.N
```

Cherry-picked or older commits still need all three gates on that exact SHA.

## Deploy

The `v*` tag starts `.github/workflows/deploy.yml`. Watch that run to completion. The brief outage on in-place deploys is accepted.

`workflow_dispatch` must use a `v*` tag ref. Dispatching `main` no longer deploys. Tag pushes cannot be overridden; re-dispatch the tag with a reason instead.

`gate_only` is a dry run: it runs `check-gates` and skips migrate, configure, deploy, and smoke.

## OpenTofu

If a merge triggers [`opentofu-apply.yml`](../architecture/opentofu-ci-and-runbooks.md), read the plan and summarise it for Richard before he approves. OpenTofu does not manage blob objects or SQL rows. See [`opentofu-contributor-runbook.md`](../architecture/opentofu-contributor-runbook.md) for `prevent_destroy` and rollback by resource class.

## Post-deploy health

After `post-deploy-smoke` is green:

1. Confirm `/health`, `/warmup`, and `data-build-version` on `/` at `https://www.queenzone.org`.
2. Check App Insights errors and latency for the new stamp.
3. Hit the specific routes the release changed.

`scripts/Smoke-LiveSite.ps1` / `scripts/Invoke-PostDeploySmoke.sh` are the cheap canaries. They do not replace the route check.

## Log it

Add a comment to #2222 with environment, tag, commit, PRs, who shipped it, and the health result. DEV deploys get logged too.

A gate override posts its own comment on #2222 **before** migrate. That job needs `issues: write`. If the comment fails, the deploy fails.

## Rollback

Redeploy the previous tag. Do not push the same tag again.

1. Find the previous `v*` tag and its SHA.
2. If that SHA still has three fresh full-suite gates, create a new `vYYYY.MM.DD.N` on it and push.
3. If the gates have expired or the SHA was never gated, re-dispatch the previous tag with `gate_override_reason` (at least 20 characters). That runs the `prod-gate-override` approval and comments on #2222 before migrate.

```bash
gh workflow run deploy.yml --ref vPREVIOUS.TAG
```

Use the Actions UI to pass `gate_override_reason` when needed. The reason is passed via `env`, never interpolated into a script, and is written to the job summary.

## Roles

Anyone following this runbook may tag. Workflow-file pull requests are merged by Richard.

Create and protect `prod-gate-override` in GitHub Settings **before** merging a workflow that first references it. Use the same reviewers as `prod-release`. GitHub auto-creates an unprotected environment on first use; do not let that happen for an override that skips prod gates.

## Override path

`gate_override_reason` is an optional `workflow_dispatch` input, allowed on `v*` tag refs only. When it is set:

1. `gate-override-guard` checks the ref and that the reason is at least 20 characters.
2. `gate-override` runs on `prod-gate-override` (same reviewers as `prod-release`).
3. The reason is written to the job summary and commented on #2222.
4. Migrate starts only after that comment succeeds.

Tag pushes never take this path.
