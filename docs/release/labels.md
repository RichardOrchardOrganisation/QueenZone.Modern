# Release-note labels

Automatically generated GitHub Releases group merged pull requests from [`.github/release.yml`](../../.github/release.yml). A pull request is placed in the **first** matching category. Unlabelled pull requests land in **Other**, so nothing is dropped.

| Category | Label | How it is applied |
| --- | --- | --- |
| Features | `feature` | Title prefix `feat` / `feature` (for example `feat: add notes` or `feature/search`). |
| Fixes | `fix` | Title prefix `fix` / `bug` / `bugfix` (for example `Fix the gate`). |
| Mobile | `mobile` | Path: `src/QueenZone.Mobile/**` or `mobile/**`. |
| Infra/CI | `infra` | Path: `.github/**` or `infra/**`. |
| Dependencies | `dependencies` | Dependabot / Renovate (`agent-pr-label.yml`) already apply this. |
| Other | *(none)* | Catch-all `*` in `release.yml`. |

Path labels come from [`actions/labeler`](../../.github/workflows/labeler.yml) and [`.github/labeler.yml`](../../.github/labeler.yml). Feature vs Fix is inferred from the pull-request title. A path category (`mobile`, `infra`, or `dependencies`) wins: the title step does not also add `feature` or `fix`, so a mobile fix stays in Mobile.

You can still add or correct labels by hand before merge. The notes job is non-blocking and does not have to finish before you log a deploy.

Each generated pull-request line is annotated with `(closes #N)` from GitHub's closing-issue references. See the [production release runbook](README.md#release-notes).
