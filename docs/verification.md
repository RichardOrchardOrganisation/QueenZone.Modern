# Verification and uploaded proof

UI PRs require uploaded screenshots and passing behavioural reports for their current head commit. Local paths, arbitrary links and `Not verified:` do not satisfy the gate. Dependabot follows the same rule. Non-UI changes need no screenshots.

## Ownership

The kit owns evidence validation, current-commit checks, explicit waivers, PR link publication and merge-queue handling. Projects own capture commands, test fixtures, feature maps, required platforms, runner labels and workflow wiring. Browser automation can use Playwright, Chrome DevTools or another project-supported tool; the evidence contract does not require a particular agent or MCP server. Native proof must come from the native runtime.

The default adapter reads `config/feature-map.json`. Each feature has `id`, `sources`, `command`, and `platform`; optional `platforms` lists every required platform, and `fixtures` lists required TRX fixtures. A richer project map can retain a small `scripts/check-pr-verification.mjs` adapter using `checkProof` and `evaluateProof` from `ui-proof-policy.mjs`. The adapter returns `features` (`id`, `platforms`, optional `fixtures`) and `requirements` (`file`, allowed feature `ids`). Each requirement needs a declared ID. Multiple requirements enforce multiple features; multiple IDs in one requirement allow a representative feature.

## Configuration

Set `uiPaths` and `uiProof` in `development-standards.json`:

```json
{
  "uiProof": {
    "baseBranch": "main",
    "ciWorkflow": ".github/workflows/ci.yml",
    "gateJob": "ui-proof",
    "producers": {
      ".github/workflows/ci.yml": {
        "browser": { "browser-tests": "Browser tests" }
      },
      ".github/workflows/device.yml": {
        "android": { "android-proof": "Android proof" },
        "ios": { "ios-proof": "iOS proof" }
      }
    }
  }
}
```

Producer keys are workflow paths, then platform names, then job IDs mapped to their exact display names. The gate checks successful producer attempts, artifact expiry, run identity and the current PR head. A gate-only rerun preserves valid producer evidence; rerunning a producer invalidates its older attempt. The publisher only reruns the configured gate job in the configured CI workflow, avoiding completion loops.

## Evidence contract

Capture screenshots and test reports in a directory containing `manifest.json`:

```json
{
  "version": 1,
  "headSha": "<40-character PR head SHA>",
  "testedSha": "<40-character tested commit SHA>",
  "runId": "<GITHUB_RUN_ID>",
  "attempt": "<GITHUB_RUN_ATTEMPT>",
  "job": "<GITHUB_JOB>",
  "entries": [{
    "feature": "home",
    "platform": "browser",
    "tests": 3,
    "report": "results.xml",
    "screenshots": ["home.png"]
  }]
}
```

Reports without `fixtures` use JUnit. For TRX, add `fixtures: ["HomeTests"]`; every fixture must contain passing tests and their total must match `tests`. PNG structure and checksums are checked. Empty, skipped or failed reports and missing screenshots fail. Archives have a 50 MiB compressed/uncompressed limit and at most 2,000 unique safe paths. Downloads are read without extracting or executing content. Screenshots remain review evidence, not automated visual approval.

Run `python3 scripts/ui_proof_archive.py pack --source artifacts/ui-proof --output artifacts/ui-proof.zip` to validate and package. Upload the **directory contents** using `actions/upload-artifact`, not a nested ZIP, under `ui-proof-<run-id>-<attempt>`. Windows can use `python`; the evidence gate resolves Python from fixed installation paths. Set `STANDARDS_PYTHON` to an existing absolute executable path for other installations. Python uses only the standard library. Keep secrets and private data out of evidence.

## Workflow integration

Adapt `examples/development-standards-pr-verification.yml` into the existing CI workflow. It illustrates a project-owned capture job and a dependent evidence gate. Preserve the current required check name when possible; otherwise deliberately add the new gate to branch protection/rulesets. Include `merge_group` triggers when using a merge queue. The gate reads current PR proof and waivers again for the combined candidate, and fails closed when constituent PRs cannot be resolved.

Adapt `examples/development-standards-proof-publisher.yml` as a separate trusted publisher. Set its literal owning repository and configure workflow names to include CI and native proof producers. It uses `pull_request_target`/`workflow_run`, checks out only the default branch, posts artifact links, and reruns the gate after body/waiver edits or device completion. Never execute PR code or artifact scripts in this privileged workflow. It becomes active after reaching the default branch. The evidence gate itself has read-only permissions.

## Waivers and adoption

A maintainer must deliberately apply `no-ui-verification` and include a visible non-empty `Verification-skip-reason:` line. Hidden comments and fenced examples do not count. Report unavailable checks honestly, then obtain a waiver when appropriate; agents must not waive their own missing evidence without authorization.

This tightens the previous documentation-only gate. Configure producers, arrange capture/upload, and update required-check wiring in the same adoption PR. The updater installs examples but never modifies active workflows. A project retaining its map adapter should delegate policy to the shared core. Keep project platform requirements explicit; the kit does not impose Android/iOS on a web-only project.
