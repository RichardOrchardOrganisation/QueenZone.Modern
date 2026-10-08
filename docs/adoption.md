# How to bring this into another project

Adoption is a normal change in the receiving project: install the portable files, configure them for that project, run its checks, and review the result through a pull request. The shared rules are useful across languages; the included executable profiles support .NET/Coverlet and TypeScript with Jest plus Node coverage.

## 1. Prepare the two repositories

Keep the standards kit and the receiving project in separate directories. For example:

```text
Projects/
  development-standards/  # source kit; run the installer here
  MyProject/              # receiving project; run verification here
```

The target directory must already exist. For a new project, create its application scaffold and Git repository first. Make an initial commit before using changed-line coverage so there is a valid comparison base. For an existing project, finish or set aside unrelated edits before adoption so the diff is easy to review.

In the receiving project, create a feature branch:

```sh
cd /path/to/MyProject
git switch -c codex/adopt-development-standards
```

The `codex/` prefix is an example for work performed by Codex. Use the agent or team prefix required by the receiving project.

From a separate parent directory, clone the kit and choose a reviewed version:

```sh
git clone https://github.com/RichardOrchardOrganisation/development-standards.git
cd development-standards
git checkout <reviewed-commit-or-tag>
```

Replace `<reviewed-commit-or-tag>` with an actual commit SHA or published tag. Keep that checkout clean: the installer records its Git commit, not a snapshot of uncommitted edits.

While [the initial kit PR](https://github.com/RichardOrchardOrganisation/development-standards/pull/2) is open, `main` only contains the repository’s initialization commit. To try that PR’s kit:

```sh
git switch --track origin/codex/portable-development-standards
git rev-parse HEAD
```

After reviewing the displayed commit, you can pin it with `git checkout <that-commit-sha>`. Once the PR is merged, use a reviewed commit from `main` or a published release tag. Check the PR’s current state before using the pre-merge instructions.

## 2. Preview and install

Requirements:

| Use | Required tools |
| --- | --- |
| Installer and common checks | Git, Node 24 |
| .NET verification | PowerShell 7 (`pwsh`), the receiving project’s .NET SDK, its test/coverage packages |
| TypeScript verification | The project’s npm dependencies and configured typecheck/lint/coverage scripts |
| UI proof or provider probes | The browser, device, emulator, or safe test database specified by the project |

The kit’s Node scripts have no npm dependencies; installation does not require `npm install` in the kit. Install or restore the receiving project’s own dependencies using its established process.

Run these commands from the **standards kit root**.

macOS/Linux:

```sh
node scripts/install.mjs --target "/Users/you/Projects/MyProject" --dry-run
node scripts/install.mjs --target "/Users/you/Projects/MyProject"
```

Windows PowerShell:

```powershell
node scripts/install.mjs --target "C:\Projects\MyProject" --dry-run
node scripts/install.mjs --target "C:\Projects\MyProject"
```

Use the real absolute path, including quotes when it contains spaces. Dry-run lists planned files without writing them. Installation:

- Copies policy into `docs/` and portable checks into `scripts/`.
- Adds project configuration and empty suppression/feature-map baselines.
- Appends one marked shared-policy section to `AGENTS.md`, preserving existing guidance.
- Adds a PR template and CI examples under `examples/`.
- Preserves the receiving project’s `LICENSE`; kit attribution goes into `docs/development-standards-LICENSE`.
- Records the source commit and kit version in `development-standards.lock.json`.

The installer does not change project dependencies, enable GitHub workflows, configure branch protection, or create browser/device proof. Those are adoption steps below.

### Existing projects and file conflicts

The installer refuses to replace a different existing file, including a PR template, coverage settings, a policy document, or project configuration. It checks conflicts before writing. It also rejects symlinked destination paths. Do not delete local policy merely to make installation pass.

For a conflict, use manual adoption:

1. Create an empty temporary directory and install the kit into it with `--target`.
2. Compare that staged output with the receiving project.
3. Copy new files and deliberately merge conflicting files. Preserve project-specific rules, exclusions, feature maps, and baseline rationale.
4. Append the shared `AGENTS.md` section once and keep all referenced documents reachable.
5. Merge the PR template into the project’s template. Preserve the kit license attribution and version lock.
6. If you rename scripts or policy documents, update their imports, paths, agent links, and CI commands together. Some scripts expect the documented `scripts/`, `config/`, and `docs/` layout.

There is no force-overwrite mode. Review the resulting target diff before committing. For an already installed, locked project, use the [update command](updating.md) rather than reinstalling.

## 3. Configure your project

Run subsequent commands from the **receiving repository root**. Edit `development-standards.json`; retain `"version": 1` and remove profiles you do not use.

### .NET example

For a solution named `MyProject.sln` with Razor Pages under `src/MyProject.Web/Pages/`:

```json
{
  "version": 1,
  "dotnet": {
    "solution": "MyProject.sln",
    "globalLine": 91,
    "changedLine": 70,
    "reports": "TestResults"
  },
  "uiPaths": ["src/MyProject.Web/Pages/"]
}
```

Use your real solution path. Ensure your test projects have a compatible `coverlet.collector` package and the usual test SDK/runner dependencies. The runner uses the XPlat Code Coverage collector with `coverlet.runsettings`. Review its generated-code exclusions against your project.

C# numeric floors live in the `dotnet` object. A non-UI library can explicitly use `"uiPaths": []`. Add other UI source directories, such as `wwwroot`, if they need proof coverage, and map them in the next step.

### TypeScript example

For an application in `app/`, with production code under `app/src/`:

```json
{
  "version": 1,
  "typescript": {
    "projectRoot": "app",
    "reports": "app/coverage",
    "floors": "config/typescript-coverage.json"
  },
  "uiPaths": ["app/src/screens/", "app/src/navigation/"]
}
```

For an application at the repository root, set `projectRoot` to `"."`, reports to `"coverage"`, and use UI paths such as `"src/screens/"`. A nested application can use a path such as `"clients/mobile"`. The configured application directory must contain `src/`.

Set TypeScript numeric floors in `config/typescript-coverage.json`:

```json
{
  "globalLine": 90,
  "globalBranch": 70,
  "changedLine": 70
}
```

The application’s `package.json` must provide:

| Script | Contract |
| --- | --- |
| `typecheck` | Type checks the application and exits nonzero on failure |
| `lint` | Runs the application’s lint rules and exits nonzero on failure |
| `test:coverage` | Runs both coverage suites and writes the reports below |

Configure the existing test runners to produce:

```text
<reports directory>/
  jest/
    coverage-final.json       # or cobertura-coverage.xml
  node/
    lcov.info
```

Jest must include every production `src/**/*.ts` and `src/**/*.tsx` file in its coverage universe, including files not imported by tests. The gate excludes declaration files, `*.test.ts`/`*.test.tsx`, and `src/test/` fixtures. Review this adapter’s exclusions if your project uses other test naming conventions. Node hits are overlaid onto that universe; branch coverage uses Jest’s branch map. Consumer contracts and device smoke remain separate suites.

Both report directories are required. If the application uses only Jest or only Node, adopt the policies now and implement/review an appropriate coverage adapter before enabling this profile. An empty dummy second report is not a substitute.

### Mixed projects and initial floors

For a repository containing .NET and TypeScript, retain both objects and run both profiles. Include all relevant UI paths.

For an existing application, measure current deterministic coverage before setting the global floors. Review an initial baseline with its rationale. Keep changed-code expectations explicit and improve the baseline over time. Do not copy another application’s low baseline or change floors merely to hide failing tests.

## 4. Map UI features and verification

Populate `config/feature-map.json` for configured UI paths. Example for the .NET configuration above:

```json
{
  "features": [
    {
      "id": "home",
      "sources": ["src/MyProject.Web/Pages/"],
      "command": "Run the project’s homepage browser journey and capture proof",
      "platform": "browser"
    }
  ]
}
```

Replace that descriptive command with the project’s actual test/capture command or reproducible journey. Use precise source files or directory prefixes with a trailing `/`. Every configured UI file needs a mapping; referenced sources must exist and IDs must be unique. Consider separate features for separate journeys instead of assigning a whole application to one broad ID.

For native mobile work, specify the actual Android/iOS device, emulator, or simulator journey. Expo web does not establish native behavior. Configure proof storage and safe test accounts in the receiving project.

UI PRs need a `## Verification` section with affected feature IDs, `Command:`, `Platform:`, `Result:`, and a proof URL or `artifacts/proof/` path. If a required check cannot run, use `Not verified: <check and reason>`. An opt-out needs both the `no-ui-verification` label and `Verification-skip-reason:`. The checker validates documentation shape; reviewers assess the evidence and whether skipped verification is acceptable.

For a non-UI project, set `uiPaths` to an empty array explicitly. The starter feature map can then remain empty.

## 5. Run and review local checks

From the receiving repository root, fetch the default branch and run the appropriate profile:

```sh
git fetch origin main
node scripts/verify.mjs --profile dotnet --base-ref origin/main
```

For TypeScript:

```sh
node scripts/verify.mjs --profile typescript --base-ref origin/main
```

Replace `main` if the receiving repository uses a different default branch. For a new local repository without a remote yet, use an existing base commit SHA with `--base-ref`. Missing base objects fail closed.

Use fresh generated coverage directories for a verification run. The runner does not clean old reports automatically; stale reports could inflate merged coverage. Keep generated results out of Git using the project’s `.gitignore`, including the configured coverage directory, `TestResults/`, and `coverage-report/` as applicable.

You can run common checks separately:

```sh
node scripts/check-suppressions.mjs
node scripts/check-feature-map.mjs
```

Existing projects may contain unlinked suppressions. Review and fix them or establish an explicitly audited initial baseline. `node scripts/check-suppressions.mjs --write` records the current counts, including additions, so review the baseline diff carefully. After later removals, use it to lower the baseline. Never copy QueenZone’s baseline into the new project.

Read generated CRAP hotspots and uncovered changed lines. Add useful behavior tests or simplify complex methods; do not weaken assertions or add exclusions to satisfy a percentage. Run project-specific browser/device proof and safe provider probes separately, and record what passed or was not run.

## 6. Connect CI and open the adoption PR

The installer places these examples in the receiving repository:

| Installed example | Suggested workflow destination |
| --- | --- |
| `examples/development-standards-quality.yml` | `.github/workflows/quality.yml` |
| `examples/development-standards-pr-verification.yml` | `.github/workflows/pr-verification.yml` |

Review and adapt them before moving/copying them into `.github/workflows/`. Merge with existing workflows where appropriate rather than creating duplicate jobs.

The quality example is for .NET. It reads SDK selection from `global.json`, collects reports from `TestResults/` and `coverage-report/`, and compares against the PR event base SHA, merge-group base SHA, or push’s prior SHA. Configure the SDK step and artifact paths for your project. Its push comparison requires a real prior commit; an initial push with an all-zero `before` SHA needs an explicit valid base or a separate initial-baseline run.

For TypeScript CI, use Node 24, install the application’s locked dependencies, and run:

```sh
node scripts/verify.mjs --profile typescript --base-ref "$BASE_SHA"
```

That example command is for a POSIX CI shell; in PowerShell use `$env:BASE_SHA`. Set the base from the workflow event, retain a full checkout (`fetch-depth: 0`), and ensure the base object is available. Add the profile for each application you intend to verify. The supplied configuration has one TypeScript profile; multiple independently configured applications need their own explicit orchestration.

The PR verification example checks the feature map and PR evidence when the PR changes or its description/labels are edited. It calls `checkPullRequestVerification({ github, context, core })` from `scripts/check-pr-verification.mjs`, which exempts Dependabot and uses read-only GitHub permissions. Pass `label: true` to also add or remove a `needs-verification` label; that needs `issues: write` and `pull-requests: write`.

Configure repository rules so the relevant quality/proof jobs are required before merging. Workflow files alone do not create branch protection or a merge queue. Add project-specific browser/device jobs, safe provider probes, dependency/security checks, smoke checks, and deployment protections where needed. Do not import QueenZone’s database connections or runner labels.

Review the installed diff, then commit and push the adoption branch and open a PR under the project’s normal process. Include:

- The authoring agent and standards source commit/version.
- What was adopted, the selected floors, and any project-specific exceptions.
- Checks run and results, with real-data probes distinguished from deterministic tests.
- UI proof or explicit skipped checks and reasons.
- A plain-text issue line referencing an existing issue, such as `Relates to #123`.

For projects using an assistant that does not read `AGENTS.md`, add a short instruction in that tool’s supported project file pointing to the shared policy documents. Keep one policy source rather than copying the full rules into several tool files.

## 7. Update an adopted project

Installed files are committed copies. The receiving project does not load a changing remote branch during CI. `development-standards.lock.json` records the source commit and kit version.

Create an update branch in the receiving project, then select a clean, newly reviewed standards commit/tag in the separate kit clone. From the **kit root**:

```sh
node scripts/update.mjs --target "/absolute/path/to/MyProject" --dry-run
node scripts/update.mjs --target "/absolute/path/to/MyProject"
```

The updater compares the installed baseline, local project files, and incoming standards. Compatible text changes are merged; local-only changes are kept. On unresolved conflicts, no files or lock are changed. After review, `--keep-local relative/path` can explicitly retain a conflicting local version. The versioned manifest allows additions/removals to be tracked and supports the known original v0.1 installations.

Review the project diff, run the applicable profiles and project-specific verification, and commit the updated files and lock in one PR. CI example changes must be ported into active workflows separately. See [the full updater guide](updating.md) for conflict handling, missing history, and provenance checks.

For unknown older installers, renamed managed files, or unrecoverable baseline commits, stage a fresh install and perform a reviewed manual migration. Never replace the lock with an invented baseline merely to make an update pass.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| Installer says target does not exist | Create the project directory first and supply its real absolute path |
| Installer refuses to overwrite a file | Use staged/manual adoption and merge the local version deliberately |
| Installer reports a symlink destination | Choose a checkout whose destination directories are real directories, or stage and manually merge |
| Node APIs such as `globSync` are unavailable | Run the kit with Node 24 |
| .NET solution or `global.json` is missing | Set the real solution path and adapt the CI SDK selection |
| Coverage collector cannot run | Check the target test projects’ collector and test SDK dependencies |
| TypeScript reports are missing | Make `test:coverage` produce both suites in the configured layout |
| Production TypeScript files are missing from coverage | Include the full source universe in Jest coverage and confirm `projectRoot` |
| Changed-line comparison cannot find its base | Fetch the base/history and supply a real commit or available remote ref |
| Feature-map check reports missing/unmapped paths | Correct `uiPaths` and map every affected UI file to an existing feature source |
| Suppression baseline fails | Inspect the listed lines; fix additions or commit a reviewed lower baseline after removals |
| CI is absent after installation | The examples stay under `examples/` until you adapt and enable them |
| Checks pass but merging is not protected | Configure required status checks in GitHub repository rules |
