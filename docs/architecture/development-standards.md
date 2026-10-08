# Development standards adoption

QueenZone consumes [RichardOrchardOrganisation/development-standards](https://github.com/RichardOrchardOrganisation/development-standards) as the versioned source for shared development policy and portable quality checks (#2116). The kit was extracted from QueenZone; QueenZone keeps its architecture, operational safeguards, measured floors, feature maps, and platform-specific verification.

## Pinned version

| | |
| --- | --- |
| Source | `https://github.com/RichardOrchardOrganisation/development-standards` |
| Commit | `c958b458725b30590445f3e6fa3033e439cd5587` (merge of development-standards PR #4; `main` at adoption) |
| Kit version | `0.2.0` |
| Lock | [`development-standards.lock.json`](../../development-standards.lock.json), written by the kit's installer into a staging directory |
| Attribution | [`docs/development-standards-LICENSE`](../development-standards-LICENSE); QueenZone's own `LICENSE` is unchanged |

The commit was reviewed before adoption: it contains the initial kit (development-standards PR #2) and the three-way updater (development-standards PR #4). CI never loads the kit at run time; everything it uses is a committed copy.

## Sources of truth

| Setting | Single source | Read by |
| --- | --- | --- |
| C# global / changed-line floors | `dotnet.globalLine`, `dotnet.changedLine` in [`development-standards.json`](../../development-standards.json) | `scripts/Test-CoverageGate.ps1` (when no threshold is passed), `scripts/verify.mjs` |
| Mobile floors | [`scripts/mobile-coverage-floors.json`](../../scripts/mobile-coverage-floors.json), named by `typescript.floors` | `scripts/Test-MobileCoverageGate.mjs` (`configuredFloorsPath`), `scripts/verify.mjs` |
| UI paths for PR proof | `uiPaths` in `development-standards.json` | `scripts/check-pr-verification.mjs` (`loadUiPaths`) |
| Feature map and IDs | [`docs/feature-map/`](../feature-map/README.md) | `scripts/check-feature-map.mjs`, `check-pr-verification.mjs`, mobile device proof, issue filer, verify skills |
| Suppression baseline | [`config/suppression-baseline.json`](../../config/suppression-baseline.json) with [workaround-audit.md](workaround-audit.md) | `scripts/check-suppressions.mjs` |
| CRAP baselines | `config/crap-baseline.dotnet.json`, `config/crap-baseline.mobile.json` | `Get-CrapReport.ps1`, `Get-MobileCrapReport.mjs` |
| Shared agent rules | The managed section at the end of [`AGENTS.md`](../../AGENTS.md) | Agents; merged by the updater |

`scripts/development-standards.test.mjs` fails if the lock, the managed `AGENTS.md` markers, or this table's configuration drift: it checks the solution and UI paths exist, that CI and `verify.mjs` pass no numeric C# floor, that the mobile gate resolves its floors through `development-standards.json`, and that the competing kit files below stay absent.

## Managed files

Every path in the kit's `installation-manifest.json` is a managed path. The updater three-way merges each one (locked kit version, QueenZone's file, incoming kit version), so a QueenZone customisation stays in place and only conflicts when upstream changes the same lines.

| Managed path | Decision | Reason |
| --- | --- | --- |
| `docs/development-standards-LICENSE` | Adopted as installed | Kit attribution |
| `docs/testing.md`, `docs/coverage.md`, `docs/verification.md`, `docs/suppressions.md` | Adopted as installed | Shared policy. QueenZone specifics stay in [testing-policy.md](testing-policy.md) and `AGENTS.md`, which link here |
| `docs/adoption.md`, `docs/updating.md`, `docs/provenance.md` | Adopted as installed | Kit installation, update, and provenance reference |
| `examples/development-standards-quality.yml`, `examples/development-standards-pr-verification.yml` | Adopted as installed, **not enabled** | Reference only. `ci.yml` and `pr-verification-check.yml` already cover these jobs; copying them would duplicate the full suite. `examples/` is skip-only in `classify-pipeline-changes.sh` |
| `AGENTS.md` | Managed section appended once | Shared rules between `<!-- development-standards:begin/end -->`. All QueenZone guidance stays outside the markers |
| `development-standards.json` | QueenZone configuration | `QueenZone.sln`, current accepted floors (91 / 70), `src/QueenZone.Mobile` profile, mobile floors file, and the web/mobile `uiPaths` that were previously hard-coded regexes |
| `coverlet.runsettings` | Kept (identical to the kit) | Same generated-code exclusions |
| `config/suppression-baseline.json` | QueenZone content kept | The audited baseline and its rationale. The kit's empty baseline was not copied |
| `scripts/Test-CoverageGate.ps1` | QueenZone version kept, kit floor loading ported | QueenZone's copy is newer (diff-parsing refactor, `Write-Information`, macOS root fix). It now reads `development-standards.json` like the kit; `-ConfigPath` lets the self-test cover that |
| `scripts/Get-CrapReport.ps1` | QueenZone version kept | Adds the `-Baseline` / `-Enforce` / `-WriteBaseline` / `-FromCsv` CRAP ratchet the CI `coverage` job uses (about 300 lines the kit does not have) |
| `scripts/check-suppressions.mjs` | QueenZone version kept | Skips generated `ios/` / `android/` projects, requires two-digit issue links, and points at workaround-audit.md. The kit leaves those as project policy |
| `scripts/check-feature-map.mjs` | QueenZone version kept | Validates the domain maps, exclusions, generated index (`--write`), and `--resolve` / `--flows` used by mobile preflight, device proof, and the issue filer. The kit's version reads `config/feature-map.json` |
| `scripts/check-pr-verification.mjs` | QueenZone version kept, now reads `uiPaths` | `pr-verification-check.yml` imports `checkPullRequestVerification({ github, context, core })`, which manages the `needs-verification` label. The kit exports a different `evaluate()` interface. Tests cover the workflow export |
| `scripts/verify.mjs` | Adopted with one adapter line | The TypeScript profile runs `scripts/Test-MobileCoverageGate.mjs` instead of the kit's gate |
| `.github/pull_request_template.md` | QueenZone template kept, kit wording merged | Keeps issue-link, legacy-probe, UI proof, review-finding, migration, and follow-up sections the QueenZone checks parse |
| `config/feature-map.json` | **Deliberately absent** | Would be a second, hand-maintained feature map beside `docs/feature-map/` |
| `config/typescript-coverage.json` | **Deliberately absent** | Its 90 / 70 / 70 new-project floors would compete with the measured mobile floors |
| `scripts/Test-TypeScriptCoverageGate.mjs` | **Deliberately absent** | A generalised copy of `Test-MobileCoverageGate.mjs`, which is newer (CRAP-driven refactors) and is imported by `Get-MobileCrapReport.mjs` |

The updater keeps a deletion while the kit leaves that file unchanged. If a future kit version changes one of the absent files, the update reports a conflict; review the change, port anything useful into the QueenZone equivalent, then pass `--keep-local <path>` to keep it absent.

### Consumers checked

| Script | Consumers |
| --- | --- |
| `Test-CoverageGate.ps1` | `ci.yml` `coverage` job, `scripts-tests.yml` self-test, `verify.mjs`, `AGENTS.md`, testing-policy, `.cursor/rules/pr-coverage-gate.mdc`, PR template |
| `Get-CrapReport.ps1` | `ci.yml` CRAP report and ratchet steps, `verify.mjs`, testing-policy |
| `Test-MobileCoverageGate.mjs` | `ci.yml` `mobile-js`, `src/QueenZone.Mobile` `coverage:gate`, `Get-MobileCrapReport.mjs` imports, `classify-pipeline-changes.sh`, `verify.mjs` |
| `check-suppressions.mjs` | `suppression-check.yml`, `verify.mjs`, workaround-audit.md |
| `check-feature-map.mjs` | `feature-map-check.yml`, mobile `preflight`, `run-mobile-device-smoke.sh --flows`, `check-pr-verification.mjs`, issue-filer telemetry, verify skills |
| `check-pr-verification.mjs` | `pr-verification-check.yml` via `actions/github-script` |

Command names, flags, outputs, and artifacts are unchanged. Behaviour changes: `Test-CoverageGate.ps1` without threshold arguments now uses the configured floors (previously hard-coded 91 / 70 defaults, the same values), and the mobile gate without `--floors` resolves the file through `development-standards.json` (the same file as before).

## Updating to a new standards version

Keep a separate clone of the kit, for example next to this repository (`C:\workspace\development-standards` or `~/Projects/development-standards`). Never run the updater from a moving branch you have not reviewed.

1. In QueenZone, create an agent-prefixed branch such as `claude/standards-0.3.0`, with a clean working tree.
2. In the kit clone, `git fetch origin`, review the new commit or tag, and `git checkout <reviewed-commit-or-tag>`. The clone must be clean and must contain the locked commit (`git fetch --unshallow origin` if it is shallow).
3. Optionally rehearse against a disposable copy: `node scripts/development-standards-update-proof.mjs --standards <kit clone>` (run in QueenZone) proves the updater still works on the adopted layout.
4. Preview, then apply, from the **kit root**:

   ```powershell
   node scripts/update.mjs --target "C:\workspace\QueenZone.Modern" --dry-run
   node scripts/update.mjs --target "C:\workspace\QueenZone.Modern"
   ```

5. On a conflict nothing is written, including files that would update cleanly, and the lock does not move. Compare `git show <locked-commit>:<source>` and `git show HEAD:<source>` in the kit with the QueenZone file. Merge by hand when the upstream change should apply, or keep the QueenZone version with `--keep-local <path>` (repeat per path). `--keep-local` resolves this update only; it is not a permanent opt-out, and the lock records it under `keptLocal`.
6. Review the QueenZone diff. **Always read the `development-standards.json` diff:** QueenZone's C# floors equal the kit's defaults, so an upstream default change can merge into QueenZone's floor without a conflict (scenario 5 below). Expect conflicts on QueenZone-customised scripts whenever upstream edits near QueenZone's own changes. Floors are QueenZone policy; revert an unintended change before committing.
7. The updater never edits active workflows. If `examples/` changed, port what applies into `.github/workflows/ci.yml` or `pr-verification-check.yml` deliberately.
8. Run the default verification in `AGENTS.md`, `node --test $(find scripts -name '*.test.mjs')`, the coverage-gate and mobile-gate self-tests, and the mobile preflight when mobile tooling changed. Commit the files and the new lock together and open an update PR that names the old and new commits, kept-local paths, and checks run.

Unknown older installers, relocated managed files, or an unrecoverable locked commit need a reviewed manual migration. Never edit the lock to point at a commit that was not actually installed.

## Improvement loop

1. Fix and verify the problem in QueenZone first.
2. If it applies to other projects, port the reusable part into development-standards with regression tests and review it there.
3. Bring the approved version back through a QueenZone update PR.

Keep application-only rules (SQL access, mobile state strategy, design tokens, environments, probes, runners, database connections) in QueenZone.

## Update proof

`node scripts/development-standards-update-proof.mjs --standards <kit clone>` reproduces this evidence. It clones the kit and the committed QueenZone `HEAD` into a temporary directory, adds local fixture commits on top of the locked kit commit (no upstream release is touched), and runs the kit's own `scripts/update.mjs`:

1. **Compatible update**: a shared doc change, a new managed rule, a new managed file, a new config key, and an edit to a QueenZone-customised script (`check-suppressions.mjs`, away from its local lines) all apply. The solution path, floors, `uiPaths`, suppression baseline, feature maps, QueenZone-owned scripts, PR template, deliberately absent files, and `AGENTS.md` text outside the markers are unchanged.
2. **Same version again**: no file changes.
3. **Overlapping change**: the config `solution` line, an edit to a deliberately absent file, and an edit next to QueenZone's `Test-CoverageGate.ps1` refactor. Nonzero exit naming all three; no target file or lock changes, including a file that would have updated cleanly.
4. **`--keep-local`**: the reviewed conflicts keep QueenZone's versions, the clean change applies, and the lock records `keptLocal`.
5. **Shared floor defaults**: a kit change to the default `changedLine` merges into QueenZone's floor without a conflict, while a `globalLine` change conflicts only because it sits next to QueenZone's customised `solution` line. This is the review hazard in step 6 above.

Run on 2026-10-08:

```text
QueenZone commit under test: d5f115f4afbf44a7e39c055b2b53aa804f9af3a2
Locked standards: c958b458725b30590445f3e6fa3033e439cd5587 (v0.2.0)

## 1. Compatible update c958b45 -> 11584cc
$ node scripts/update.mjs --target <queenzone> --dry-run
    Standards c958b458725b30590445f3e6fa3033e439cd5587 -> 11584ccaf212d27cc1a6f0f4d8330a27759eaaf8
    update (three-way merge): development-standards.json
    update (three-way merge): scripts/check-suppressions.mjs
    update: docs/testing.md
    add: docs/proof-fixture.md
    update: AGENTS.md
    update: development-standards.lock.json
    Preview only; no files changed.
    (exit 0)
PASS dry-run succeeds and writes nothing
$ node scripts/update.mjs --target <queenzone>
    Standards c958b458725b30590445f3e6fa3033e439cd5587 -> 11584ccaf212d27cc1a6f0f4d8330a27759eaaf8
    update (three-way merge): development-standards.json
    update (three-way merge): scripts/check-suppressions.mjs
    update: docs/testing.md
    add: docs/proof-fixture.md
    update: AGENTS.md
    update: development-standards.lock.json
    Update complete. Review the target diff and run its checks before committing.
    (exit 0)
PASS update applies
PASS lock advances to the fixture commit and version
PASS config merges the upstream key
PASS solution path, floors, mobile floors file, and uiPaths survive
PASS unchanged QueenZone-owned file: config/suppression-baseline.json
PASS unchanged QueenZone-owned file: scripts/check-feature-map.mjs
PASS unchanged QueenZone-owned file: scripts/check-pr-verification.mjs
PASS unchanged QueenZone-owned file: scripts/Test-CoverageGate.ps1
PASS unchanged QueenZone-owned file: scripts/Get-CrapReport.ps1
PASS unchanged QueenZone-owned file: scripts/mobile-coverage-floors.json
PASS unchanged QueenZone-owned file: docs/feature-map/README.md
PASS unchanged QueenZone-owned file: .github/pull_request_template.md
PASS canonical feature maps are untouched
PASS deliberately absent file stays absent: config/feature-map.json
PASS deliberately absent file stays absent: config/typescript-coverage.json
PASS deliberately absent file stays absent: scripts/Test-TypeScriptCoverageGate.mjs
PASS AGENTS.md project guidance outside the markers is unchanged
PASS AGENTS.md managed section gains the upstream rule
PASS customised suppression checker three-way merges: upstream extension plus local generated-path skips
PASS shared doc updated and new managed file added
    changed files: AGENTS.md, development-standards.json, development-standards.lock.json, docs/proof-fixture.md, docs/testing.md, scripts/check-suppressions.mjs
PASS only managed paths and the lock changed

## 2. Repeat the same version
$ node scripts/update.mjs --target <queenzone>
    Standards 11584ccaf212d27cc1a6f0f4d8330a27759eaaf8 -> 11584ccaf212d27cc1a6f0f4d8330a27759eaaf8
    Update complete. Review the target diff and run its checks before committing.
    (exit 0)
PASS repeated update is a no-op

## 3. Conflicting update c958b45 -> 6d47bb4
$ node scripts/update.mjs --target <queenzone>
    Standards c958b458725b30590445f3e6fa3033e439cd5587 -> 6d47bb4363886212d3c988e8bb6cd9c1898f91d9
    update: docs/testing.md
    CONFLICT: development-standards.json: Project and standards changed overlapping lines.
    CONFLICT: config/typescript-coverage.json: Deletion conflicts with changes in the other version.
    CONFLICT: scripts/Test-CoverageGate.ps1: Project and standards changed overlapping lines.
    No project files or lock were changed. Review conflicts; --keep-local can retain an explicitly reviewed local version.
    (exit 1)
PASS conflict exits nonzero
PASS conflict names the overlapping config line
PASS conflict names the edited file QueenZone deliberately removed
PASS conflict names the overlapping edit to a QueenZone-customised script
PASS no target file or lock changed, including docs/testing.md

## 4. Reviewed --keep-local resolution
$ node scripts/update.mjs --target <queenzone> --keep-local development-standards.json --keep-local config/typescript-coverage.json --keep-local scripts/Test-CoverageGate.ps1
    Standards c958b458725b30590445f3e6fa3033e439cd5587 -> 6d47bb4363886212d3c988e8bb6cd9c1898f91d9
    keep-local: development-standards.json
    keep-local: config/typescript-coverage.json
    keep-local: scripts/Test-CoverageGate.ps1
    update: docs/testing.md
    update: development-standards.lock.json
    Update complete. Review the target diff and run its checks before committing.
    (exit 0)
PASS keep-local update applies
PASS kept QueenZone version: development-standards.json
PASS kept QueenZone version: config/typescript-coverage.json
PASS kept QueenZone version: scripts/Test-CoverageGate.ps1
PASS clean change from the same version applies
PASS lock records the kept paths

## 5. Shared floor default changes (review hazard)
$ node scripts/update.mjs --target <queenzone>
    Standards c958b458725b30590445f3e6fa3033e439cd5587 -> 920e7aa31299f2a087c6b150540f0d5b9c09b88a
    update (three-way merge): development-standards.json
    update: development-standards.lock.json
    Update complete. Review the target diff and run its checks before committing.
    (exit 0)
PASS a kit changedLine default change merges into the QueenZone floor without a conflict
$ node scripts/update.mjs --target <queenzone>
    Standards c958b458725b30590445f3e6fa3033e439cd5587 -> ffc6c6032ff57e038aa222c7fcb152d615bd1081
    CONFLICT: development-standards.json: Project and standards changed overlapping lines.
    No project files or lock were changed. Review conflicts; --keep-local can retain an explicitly reviewed local version.
    (exit 1)
PASS a kit globalLine default change conflicts (next to the customised solution line)
    Floors are QueenZone policy: read the development-standards.json diff in every update PR.

All update-proof checks passed.
```

## Known gaps and follow-ups

- QueenZone's copies of the coverage gate, CRAP report, mobile gate, suppression checker, feature-map checker, and PR checker are ahead of the kit. Converging them is upstream work: port QueenZone's refactors, CRAP ratchet, configurable generated-path skips, and the richer feature-map/PR-proof model into development-standards, then take them back through an update PR ([development-standards#6](https://github.com/RichardOrchardOrganisation/development-standards/issues/6)). Until then the managed paths hold reviewed QueenZone customisations.
- The kit ships real floor values in `development-standards.json`, so a default change can reach a project's floors silently (scenario 5 merged a `changedLine` change). A kit change that keeps project floors out of the managed template, or reports them for review, would remove the hazard ([development-standards#5](https://github.com/RichardOrchardOrganisation/development-standards/issues/5)).
- The kit's shared docs and managed `AGENTS.md` section name `config/feature-map.json` and `config/typescript-coverage.json`. The QueenZone mapping section after the markers in `AGENTS.md` gives the QueenZone equivalents.
