# Development standards adoption

QueenZone consumes [RichardOrchardOrganisation/development-standards](https://github.com/RichardOrchardOrganisation/development-standards) as the versioned source for shared development policy and portable quality checks (#2116). The kit was extracted from QueenZone; QueenZone keeps its architecture, operational safeguards, measured floors, feature maps, and platform-specific verification.

## Pinned version

| | |
| --- | --- |
| Source | `https://github.com/RichardOrchardOrganisation/development-standards` |
| Commit | `5e1cee668d5784c5146576e413736ed9d19bcb8b` (merge of development-standards PR #12; `main` at update) |
| Kit version | `0.3.0` |
| Lock | [`development-standards.lock.json`](../../development-standards.lock.json), written by the kit's updater; `keptLocal` lists the two reviewed conflicts below |
| Attribution | [`docs/development-standards-LICENSE`](../development-standards-LICENSE); QueenZone's own `LICENSE` is unchanged |

| Kit version | Commit | How QueenZone took it |
| --- | --- | --- |
| 0.2.0 | `c958b458725b30590445f3e6fa3033e439cd5587` | Adoption: staged install and manual reconciliation (initial kit, development-standards PR #2; updater, PR #4) |
| 0.3.0 | `5e1cee668d5784c5146576e413736ed9d19bcb8b` | First update, through `scripts/update.mjs` with `--keep-local` for `scripts/check-pr-verification.mjs` and `scripts/Test-TypeScriptCoverageGate.mjs` |

0.3.0 is development-standards PRs #7–#12, raised from this adoption: floor values became manifest `reviewKeys` (development-standards#5), and QueenZone's newer coverage gate, CRAP ratchet, TypeScript gate refactors, suppression policy settings, and PR-verification entry point moved into the kit (development-standards#6). Each passed kit CI on Linux, macOS, and Windows before merge. CI never loads the kit at run time; everything it uses is a committed copy.

## Sources of truth

| Setting | Single source | Read by |
| --- | --- | --- |
| C# global / changed-line floors | `dotnet.globalLine`, `dotnet.changedLine` in [`development-standards.json`](../../development-standards.json) | `scripts/Test-CoverageGate.ps1` (when no threshold is passed), `scripts/verify.mjs` |
| Mobile floors | [`scripts/mobile-coverage-floors.json`](../../scripts/mobile-coverage-floors.json), named by `typescript.floors` | `scripts/Test-MobileCoverageGate.mjs` (`configuredFloorsPath`), `scripts/verify.mjs` |
| UI paths for PR proof | `uiPaths` in `development-standards.json` | `scripts/check-pr-verification.mjs` (`loadUiPaths`) |
| Feature map and IDs | [`docs/feature-map/`](../feature-map/README.md) | `scripts/check-feature-map.mjs`, `check-pr-verification.mjs`, mobile device proof, issue filer, verify skills |
| Suppression baseline | [`config/suppression-baseline.json`](../../config/suppression-baseline.json) with [workaround-audit.md](workaround-audit.md) | `scripts/check-suppressions.mjs` |
| Suppression scan policy | `suppressions` in `development-standards.json`: skip the generated `src/QueenZone.Mobile/ios` / `android` projects; `(#NN)` links need two digits | `scripts/check-suppressions.mjs` (`loadPolicy`) |
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
| `scripts/Test-CoverageGate.ps1` | Kit version (since 0.3.0) | The kit took QueenZone's refactor, `Write-Information`, macOS fixes, and `-ConfigPath` floor loading |
| `scripts/Get-CrapReport.ps1` | Kit version (since 0.3.0) | The kit took QueenZone's `-Baseline` / `-Enforce` / `-WriteBaseline` / `-FromCsv` ratchet, which the CI `coverage` job uses |
| `scripts/check-suppressions.mjs` | Kit version (since 0.3.0) | QueenZone's native-project skips and two-digit issue-link rule are now the `suppressions` settings in `development-standards.json`. Counts are unchanged. `scripts/check-suppressions.test.mjs` is the kit's test file; `development-standards.test.mjs` pins QueenZone's policy |
| `scripts/check-feature-map.mjs` | QueenZone version kept | Validates the domain maps, exclusions, generated index (`--write`), and `--resolve` / `--flows` used by mobile preflight, device proof, and the issue filer. The kit's version reads `config/feature-map.json` |
| `scripts/check-pr-verification.mjs` | QueenZone version kept (`--keep-local` in 0.3.0), reads `uiPaths` | The kit now has the same `checkPullRequestVerification({ github, context, core })` entry point, but evaluates against `config/feature-map.json`. QueenZone's checker uses the domain maps and always manages the `needs-verification` label. Tests cover the workflow export |
| `scripts/verify.mjs` | Adopted with one adapter line | The TypeScript profile runs `scripts/Test-MobileCoverageGate.mjs` instead of the kit's gate |
| `.github/pull_request_template.md` | QueenZone template kept, kit wording merged | Keeps issue-link, legacy-probe, UI proof, review-finding, migration, and follow-up sections the QueenZone checks parse |
| `config/feature-map.json` | **Deliberately absent** | Would be a second, hand-maintained feature map beside `docs/feature-map/` |
| `config/typescript-coverage.json` | **Deliberately absent** | Its 90 / 70 / 70 new-project floors would compete with the measured mobile floors |
| `scripts/Test-TypeScriptCoverageGate.mjs` | **Deliberately absent** (`--keep-local` in 0.3.0) | Since 0.3.0 it holds the same logic as `Test-MobileCoverageGate.mjs`, but it takes its project root only from the `STANDARDS_TS_PROJECT` environment variable. Every QueenZone call site (CI, the `coverage:gate` npm script, `Get-MobileCrapReport.mjs` imports) would have to set it, so the mobile gate stays until the kit reads `typescript.projectRoot` from `development-standards.json` |

The updater keeps a deletion while the kit leaves that file unchanged. If a future kit version changes one of the absent files, the update reports a conflict; review the change, port anything useful into the QueenZone equivalent, then pass `--keep-local <path>` to keep it absent.

`keptLocal` is a record of one reviewed resolution, not a pin. A later upstream edit that does not overlap QueenZone's lines merges into a kept-local file without a conflict, so diff every kept-local file in an update PR.

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
6. Review the QueenZone diff, including every kept-local file. Since 0.3.0 the floors are manifest `reviewKeys`: an update that would change `dotnet.globalLine` / `dotnet.changedLine` is a conflict naming the old and new values (scenario 5 below). Adopt a new floor only by editing `development-standards.json` deliberately and rerunning. Expect conflicts on QueenZone-customised files whenever upstream edits near QueenZone's own lines.
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

1. **Compatible update**: a shared doc change, a new managed rule, a new managed file, a new config key, and an edit to a QueenZone-customised script (`verify.mjs`, away from its mobile-gate adapter) all apply. The solution path, floors, `uiPaths`, suppression baseline, feature maps, QueenZone-owned scripts, PR template, deliberately absent files, and `AGENTS.md` text outside the markers are unchanged.
2. **Same version again**: no file changes.
3. **Overlapping change**: the config `solution` line, an edit to a deliberately absent file, and an edit inside the kit's `evaluate()` in the kept-local PR checker. Nonzero exit naming all three; no target file or lock changes, including a file that would have updated cleanly.
4. **`--keep-local`**: the reviewed conflicts keep QueenZone's versions, the clean change applies, and the lock records `keptLocal`.
5. **Shared floor defaults**: kit changes to the default `changedLine` and `globalLine` are both refused with no writes. The `changedLine` conflict names `dotnet.changedLine 70 -> 80`. On 0.2.0 the same `changedLine` change merged silently into QueenZone's floor, which is what development-standards#5 fixed.

Run against 0.3.0 on 2026-10-08:

```text
QueenZone commit under test: afbf4028831dcbb36b34e82b8a66702b39d72ff7
Locked standards: 5e1cee668d5784c5146576e413736ed9d19bcb8b (v0.3.0)

## 1. Compatible update 5e1cee6 -> 087d6ff
$ node scripts/update.mjs --target <queenzone> --dry-run
    Standards 5e1cee668d5784c5146576e413736ed9d19bcb8b -> 087d6ff77209843487a3a03bd4994cef3032eb03
    update (three-way merge): development-standards.json
    update (three-way merge): scripts/verify.mjs
    update: docs/testing.md
    add: docs/proof-fixture.md
    update: AGENTS.md
    update: development-standards.lock.json
    Preview only; no files changed.
    (exit 0)
PASS dry-run succeeds and writes nothing
$ node scripts/update.mjs --target <queenzone>
    Standards 5e1cee668d5784c5146576e413736ed9d19bcb8b -> 087d6ff77209843487a3a03bd4994cef3032eb03
    update (three-way merge): development-standards.json
    update (three-way merge): scripts/verify.mjs
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
PASS unchanged: config/suppression-baseline.json
PASS unchanged: scripts/check-feature-map.mjs
PASS unchanged: scripts/check-pr-verification.mjs
PASS unchanged: scripts/check-suppressions.mjs
PASS unchanged: scripts/Test-CoverageGate.ps1
PASS unchanged: scripts/Get-CrapReport.ps1
PASS unchanged: scripts/mobile-coverage-floors.json
PASS unchanged: docs/feature-map/README.md
PASS unchanged: .github/pull_request_template.md
PASS canonical feature maps are untouched
PASS deliberately absent file stays absent: config/feature-map.json
PASS deliberately absent file stays absent: config/typescript-coverage.json
PASS deliberately absent file stays absent: scripts/Test-TypeScriptCoverageGate.mjs
PASS AGENTS.md project guidance outside the markers is unchanged
PASS AGENTS.md managed section gains the upstream rule
PASS customised verify.mjs three-way merges: upstream line plus the local mobile-gate adapter
PASS shared doc updated and new managed file added
    changed files: AGENTS.md, development-standards.json, development-standards.lock.json, docs/proof-fixture.md, docs/testing.md, scripts/verify.mjs
PASS only managed paths and the lock changed

## 2. Repeat the same version
$ node scripts/update.mjs --target <queenzone>
    Standards 087d6ff77209843487a3a03bd4994cef3032eb03 -> 087d6ff77209843487a3a03bd4994cef3032eb03
    Update complete. Review the target diff and run its checks before committing.
    (exit 0)
PASS repeated update is a no-op

## 3. Conflicting update 5e1cee6 -> 10676cd
$ node scripts/update.mjs --target <queenzone>
    Standards 5e1cee668d5784c5146576e413736ed9d19bcb8b -> 10676cd3f37d3a0626ec9292ea52db80b35db4e7
    update: docs/testing.md
    CONFLICT: development-standards.json: Project and standards changed overlapping lines.
    CONFLICT: config/typescript-coverage.json: Deletion conflicts with changes in the other version.
    CONFLICT: scripts/check-pr-verification.mjs: Project and standards changed overlapping lines.
    No project files or lock were changed. Review conflicts; --keep-local can retain an explicitly reviewed local version.
    (exit 1)
PASS conflict exits nonzero
PASS conflict names the overlapping config line
PASS conflict names the edited file QueenZone deliberately removed
PASS conflict names the upstream edit to the kept-local PR checker
PASS no target file or lock changed, including docs/testing.md

## 4. Reviewed --keep-local resolution
$ node scripts/update.mjs --target <queenzone> --keep-local development-standards.json --keep-local config/typescript-coverage.json --keep-local scripts/check-pr-verification.mjs
    Standards 5e1cee668d5784c5146576e413736ed9d19bcb8b -> 10676cd3f37d3a0626ec9292ea52db80b35db4e7
    keep-local: development-standards.json
    keep-local: config/typescript-coverage.json
    keep-local: scripts/check-pr-verification.mjs
    update: docs/testing.md
    update: development-standards.lock.json
    Update complete. Review the target diff and run its checks before committing.
    (exit 0)
PASS keep-local update applies
PASS kept QueenZone version: development-standards.json
PASS kept QueenZone version: config/typescript-coverage.json
PASS kept QueenZone version: scripts/check-pr-verification.mjs
PASS clean change from the same version applies
PASS lock records the kept paths

## 5. Shared floor default changes are refused for review
### changedLine 70 -> 80 (5e1cee6 -> eb9470d)
$ node scripts/update.mjs --target <queenzone>
    Standards 5e1cee668d5784c5146576e413736ed9d19bcb8b -> eb9470dfd0cb4efd3d13db4c3166057c4ffcc868
    CONFLICT: development-standards.json: Project-owned value would change (dotnet.changedLine 70 -> 80). Set it in the project file deliberately, or keep the local file with --keep-local.
    No project files or lock were changed. Review conflicts; --keep-local can retain an explicitly reviewed local version.
    (exit 1)
PASS a kit changedLine default change is refused, naming the floor, with no writes
### globalLine 91 -> 95 (5e1cee6 -> 58d1fcc)
$ node scripts/update.mjs --target <queenzone>
    Standards 5e1cee668d5784c5146576e413736ed9d19bcb8b -> 58d1fcc0a48046f83e085d25788015aa15e751e3
    CONFLICT: development-standards.json: Project and standards changed overlapping lines.
    No project files or lock were changed. Review conflicts; --keep-local can retain an explicitly reviewed local version.
    (exit 1)
PASS a kit globalLine default change is refused
    Floors are QueenZone policy: adopt a new value by editing development-standards.json deliberately.

All update-proof checks passed.
```

## Known gaps and follow-ups

- `Test-TypeScriptCoverageGate.mjs` reads its project root only from `STANDARDS_TS_PROJECT`. Once the kit falls back to `typescript.projectRoot` in `development-standards.json`, QueenZone can replace `Test-MobileCoverageGate.mjs` with it and import its helpers in `Get-MobileCrapReport.mjs`.
- QueenZone keeps its domain feature map and PR checker; the kit records that boundary in `docs/verification.md`. `scripts/verify.mjs` keeps its one-line mobile-gate adapter until the gate above converges.
- The kit's shared docs and managed `AGENTS.md` section name `config/feature-map.json` and `config/typescript-coverage.json`. The QueenZone mapping section after the markers in `AGENTS.md` gives the QueenZone equivalents.
