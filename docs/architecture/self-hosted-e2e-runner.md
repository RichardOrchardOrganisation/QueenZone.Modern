# Self-Hosted Runners For Playwright E2E Tests

The `e2e-test` job in `.github/workflows/ci.yml` runs the Playwright smoke suite (`tests/QueenZone.Web.E2E`) on whichever online, idle self-hosted runner has the `e2e` label. The repository currently has Windows and macOS runners, and GitHub assigns the job to the first matching runner that accepts it. Self-hosted runner minutes are not billed, so this keeps the browser-level e2e suite out of the GitHub Actions minutes quota.

The job is a required pull-request merge gate. If both runners are offline, the job remains queued and the pull request cannot merge. It is not a deploy gate: `.github/workflows/deploy.yml` does not rerun e2e after merge.

## One-Time Windows Setup

1. In GitHub, go to the repository's **Settings > Actions > Runners > New self-hosted runner**, choose **Windows**, and copy the generated token/URL (it is single-use and short-lived; re-generate if it expires before you finish).
2. On this machine, open PowerShell and create a runner folder outside the repo, for example:

   ```powershell
   mkdir C:\actions-runner
   cd C:\actions-runner
   ```

3. Download and extract the runner package (use the exact command GitHub shows you on the "New self-hosted runner" page — the version number changes over time):

   ```powershell
   Invoke-WebRequest -Uri https://github.com/actions/runner/releases/download/v2.XXX.X/actions-runner-win-x64-2.XXX.X.zip -OutFile actions-runner.zip
   Expand-Archive -Path actions-runner.zip -DestinationPath .
   ```

4. Configure the runner against this repo, using the token from step 1:

   ```powershell
   .\config.cmd --url https://github.com/richardorchard/QueenZone.Modern --token <TOKEN_FROM_GITHUB>
   ```

   - Accept the default runner group.
   - Give it a recognizable name, e.g. `richard-win11-desktop`.
   - Add the custom `e2e` label. The workflow targets `[self-hosted, e2e]`; the built-in `Windows` label alone will not pick up this job.

5. Install it as a Windows service so it keeps listening for jobs without a logged-in session:

   ```powershell
   .\svc.cmd install
   .\svc.cmd start
   ```

   Run `.\svc.cmd status` any time to check it's listening. Use `.\svc.cmd stop` to pause it (e.g. if you want this machine's CPU/network back for other work) and `.\svc.cmd uninstall` to remove the service entirely.

## One-Time macOS Setup

1. In GitHub, go to the repository's **Settings > Actions > Runners > New self-hosted runner**, choose **macOS**, and follow the displayed download and extraction commands. The package version and registration token change over time.
2. From the extracted runner directory, configure the runner and add the required label:

   ```bash
   ./config.sh --url https://github.com/richardorchard/QueenZone.Modern --token <TOKEN_FROM_GITHUB> --labels e2e
   ```

3. Install and start the runner service so it remains available without an interactive shell:

   ```bash
   ./svc.sh install
   ./svc.sh start
   ```

   Use `./svc.sh status`, `./svc.sh stop`, and `./svc.sh uninstall` to inspect or manage it.

### macOS runner label matrix

Keep the three Mac workloads distinct:

| Label | Workload | Availability behavior |
| --- | --- | --- |
| `e2e` | Playwright browser suite | Can run on the Windows or Mac runner; queues if both are unavailable |
| `ios-signing` | Signed TestFlight archive | Mac-only; Apple credentials remain isolated to the release workflow |
| `ios-build` | Unsigned PR Simulator compile | Uses the Mac only when the status probe sees it online and idle; otherwise uses hosted `macos-26` |

Add `ios-build` to the Apple Silicon runner in **Settings > Actions >
Runners**. If separate runner processes are used for label isolation, serialize
them at the machine level: the M2 Mini has 16 GB RAM and should run only one
Xcode job at a time. Two processes with different labels do not prevent two
simultaneous jobs by themselves.

The iOS status probe needs the Bitwarden-mapped `IOS_RUNNER_ADMIN_TOKEN`
described in `docs/bitwarden-secrets.md`. If the token or API is unavailable,
it deliberately chooses hosted macOS instead of leaving the PR queued.

## Machine Prerequisites

The runner executes the same steps as the workflow locally, so this machine needs:

- **.NET 10 SDK** (`dotnet --version` should report `10.0.x`) available on the runner service's `PATH`.
- **PowerShell 7 on macOS** (`pwsh`; for example, `brew install --cask powershell`) so the workflow can invoke Playwright's generated installer script.
- **Playwright browser binaries** — the workflow installs Chromium itself on each run. No manual browser install is needed, but the first run after a Playwright package bump downloads it again.
- **Outbound internet access** to `github.com`, `actions.githubusercontent.com`, and `cdn.playwright.dev` (for the runner itself and for browser downloads).
- **Port 5099 free** — the e2e job binds the published app to `http://127.0.0.1:5099`. If something else is using that port, the "Wait for app to be ready" step will time out.

## Security Notes

A self-hosted runner executes code from pull requests on the machine. Because this repository currently only has trusted agent-prefixed branches and no public external contributors, the `pull_request` trigger is acceptable. Before accepting untrusted contributions, redesign the e2e trigger and approval boundary so unreviewed fork code cannot execute on either self-hosted runner.

## Running It Manually

The workflow also accepts `workflow_dispatch`, so you can trigger `e2e-test` (along with the rest of CI) on demand from the **Actions** tab, useful for checking that at least one matching runner is online after machine restarts or service updates.

### Local run

Use `scripts/Run-E2E.ps1` (same path CI uses). It restores/builds, installs Chromium via the generated `playwright.ps1`, publishes the app, starts it, runs the suite, and stops the process — including stray-process cleanup so a failed run does not leave `QueenZone.Web` locking files.

```powershell
# Same as the CI merge gate (in-memory Testing host, Deterministic category):
powershell -File ./scripts/Run-E2E.ps1 -Mode Deterministic

# Nightly real-data suite against the SQL Express mirror (requires the connection string).
# Applies pending EF migrations to Express before start.
$env:ConnectionStrings__QueenZoneLegacy = "Server=localhost\SQLEXPRESS;Database=queenzone_legacy_sync;Integrated Security=True;TrustServerCertificate=True"
powershell -File ./scripts/Run-E2E.ps1 -Mode RealData

# Read-only sweep against a deployed site (refuses localhost):
powershell -File ./scripts/Run-E2E.ps1 -Mode LiveSite -BaseUrl https://www.queenzone.org
```

On macOS, invoke with `pwsh` instead of `powershell`. Pass `-SkipAppStart` to attach to an app you already started at `-BaseUrl`.

Failed tests write screenshots (`.png`) and Playwright traces (`.zip`) under `test-results/e2e/`. Open a trace with:

```powershell
npx playwright show-trace test-results/e2e\<name>.zip
```

### Nightly real-data run budget

The `ui-e2e-realdata` job in `nightly-legacy-checks.yml` has a 45-minute timeout on both runners — the Windows box (an i3, the weaker of the two) and the Mac Mini (M2). If a run hits that timeout:

1. Check whether the SQL Express mirror sync (`sync-legacy-db`) actually completed before the UI job started — a stale or partially-synced mirror can make queries and page loads slower than usual, not just fail outright.
2. Check for a stray `QueenZone.Web` process left over from a previous run holding port 5099 or files locked, forcing `Run-E2E.ps1`'s cleanup/retry path to spend time it wouldn't otherwise need — see the `EPERM` entry below.
3. Compare the two OS shards: if only the Windows (i3) shard is slow, it's likely just weaker hardware, not a regression — the Mac timing out too is the stronger signal something changed (a new slow test, a mirror-side data growth, or a network issue reaching the mirror over the LAN).
4. If the budget is consistently too tight as the suite grows, raise `timeout-minutes` on the `ui-e2e-realdata` job rather than trimming test coverage to fit.

### Reproducing a nightly failure locally from a downloaded trace

The `ui-e2e-realdata` job uploads `test-results/e2e/`, `e2e-app.log`, and `e2e-app.err.log` as a build artifact (`e2e-realdata-<os>-<run-id>`, 1-day retention) whenever it fails. To investigate on this machine:

1. Download the artifact from the failed run's **Actions** page (or `gh run download <run-id> -n e2e-realdata-<os>-<run-id>`).
2. Open the relevant `.zip` trace with `npx playwright show-trace <path>.zip` — this replays the DOM, network, and console state at the point of failure without needing the mirror or a running app.
3. Read `e2e-app.log` / `e2e-app.err.log` from the artifact for server-side errors (stack traces, SQL exceptions) that happened around the same timestamp as the test failure.
4. To re-run the same test against a fresh local mirror instead of just replaying the trace, sync a mirror copy (`scripts/Sync-LegacyDbToSqlExpress.ps1`, or reuse an existing one) and run `Run-E2E.ps1 -Mode RealData -CategoryFilter "<TestClassName>"` to scope the run to the failing fixture. Mirror data drifts daily, so a failure that reproduces against a same-day sync is a code issue; one that only reproduced against the original (now-stale) nightly mirror may just be transient real-data shape drift.

Locator-only RealData fixes are not covered by the PR-gate Deterministic suite (in-memory Testing has no compose recipient, so the Message textarea never sits next to the masthead **Messages** icon). After merging such a change, dispatch **Nightly legacy DB checks** with `skip_sync=true` and `category_filter` set to the fixture name instead of waiting for the 03:00 UTC schedule. Playwright accessible-name matching is substring-based unless `Exact = true` — see `docs/architecture/testing-policy.md` ("Selector conventions").

## Troubleshooting

- **Job stuck in "Waiting for a runner to pick up this job"**: neither matching runner is available. Confirm each runner is online in **Settings > Actions > Runners**, has the `e2e` label, and has its service running (`.\svc.cmd status` on Windows or `./svc.sh status` on macOS).
- **Job fails immediately with a missing SDK/tool error**: re-check the prerequisites above; the self-hosted runner uses whatever is already on `PATH` on this machine, unlike GitHub-hosted runners which come preconfigured.
- **Stale Chromium after a Playwright version bump**: remove the runner account's Playwright browser cache and let the next run re-download it (`%USERPROFILE%\AppData\Local\ms-playwright` on Windows; `~/Library/Caches/ms-playwright` on macOS).
- **Checkout fails with `EPERM: operation not permitted, unlink ... QueenZone.Web.exe`**: a previous run failed to terminate the real `QueenZone.Web.exe` process (the child-process PID lookup during app start can occasionally miss it), leaving it orphaned and holding its own exe file locked. `scripts/Run-E2E.ps1` kills leftover `QueenZone.Web` processes by image name both before start and after stop, and the `e2e-test` job still sweeps before checkout, so this should self-heal on the next run. If it still happens, the orphaned process may be running with higher privileges than the runner's own session (this has happened when a run was started or affected by an elevated/admin process) — open an elevated PowerShell on this machine and run `Stop-Process -Name QueenZone.Web -Force`, then re-run the job.
