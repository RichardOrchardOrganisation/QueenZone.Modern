# ADR 0023: Stay on SonarQube Cloud Automatic Analysis

## Status

Accepted.

## Context

The first SonarQube Cloud analysis of this repo (2026-09-25, commit
`d0249625`) reported 179 vulnerabilities, security rating E, and no
new-code baseline. Triage found 56 real findings, all in
`.github/workflows`, tracked in #1774, #1775, and #1776.
The rest are unshipped prototypes, test/script fixtures, or deliberate
accepted risk.

Issue #1781 asked for analysis scope, a recorded false-positive
policy, and a choice between staying on Automatic Analysis (AA) and
moving to a CI scan (which would import coverage and run a fuller C#
analysis).

Ship A (architecture lock on #1781): stay on AA. Option B (a
`dotnet-sonarscanner` CI job) was not chosen.

## Decision

### 1. Stay on Automatic Analysis

Do not add a CI scanner. We accept AA's lighter C# analysis (it does not
build and imports no coverage) because:

- PR decoration already works on AA, including Dependabot PRs that never
  receive `SONAR_TOKEN`. A CI scan would lose those.
- Coverage is already gated in CI (`scripts/Test-CoverageGate.ps1`: 91%
  global, 70% changed lines, plus the mobile floors). Importing coverage
  would turn on Sonar way's `new_coverage < 80` and fail PRs that pass
  our 70% gate.
- A CI scan does not fit the pipeline yet: `begin/build/end` must wrap a
  build, but CI builds once and runs sharded tests with `--no-build`;
  coverage is Cobertura, not SonarQube format; CI does not run on push
  to `main`.
- The real findings are `githubactions` rules, which AA analyses the
  same way.

Option B would be a non-required job on `pull_request` and on push to
`main` (not `merge_group`). It needs `SONAR_TOKEN`, a rebuild inside the
Sonar job, and a custom gate at 70%. Richard must turn Automatic
Analysis **off** first, or the scan fails.

**Revisit** if we want deeper C# rules or Sonar as the coverage source of
truth.

### 2. Scope lives in one versioned file

[`.sonarcloud.properties`](../../.sonarcloud.properties) at the repo
root is the Automatic Analysis scope file. It excludes `design/**`
(unshipped prototypes) and `docs/backlog/**` (offline quiz generator).
The .NET scanner computes its own initial source and test sets, so
`sonar.sources` and `sonar.tests` are unsupported. The test inclusions
describe the real test layouts:

- `tests/**` — .NET test projects (`QueenZone.*.Tests`, `QueenZone.Web.E2E`)
- `src/QueenZone.Mobile/**/*.test.ts(x)` — colocated mobile unit tests
- `src/QueenZone.Mobile/src/test/**` and `jest.setup.ts` — fixtures and harness
- `src/QueenZone.Mobile/**/__tests__/**` — standard Jest tree if one appears

The same test globs are also listed on `sonar.exclusions` so they do not
enter the source set. A path in both sets fails analysis with "File
can't be indexed twice". SonarCloud's indexed components currently
classify .NET and mobile test files as tests (`UTS`) and mobile product
files as sources (`FIL`).

Python analysis declares the supported Python 3 versions in
`.sonarcloud.properties`. Automatic Analysis lacks the installed Expo
package and warns that it cannot resolve `expo/tsconfig.base` from the
mobile typecheck configs. A checked-in copy of Expo's base removed that
warning but caused 23 unexpected Sonar analyzer failures in the PR scan,
so the mobile configs still extend the package. The normal CI mobile
typecheck and coverage gates remain authoritative. Automatic Analysis
does not expose raw logs, so the NuGet restore warning cannot be traced
to a package there; the full solution restores locally.

The project's Analysis Scope settings in the Sonar UI are empty. Do not
set the same exclusions or test paths there. This file is the single
source of truth.

Keep executable SQL under `docs/sql/` in scope. Its path is documentation-oriented,
but the files are run during migration and import work. Plain Markdown has no
reliability rules to fix.

**SQL dialect mapping:** these scripts use SQL Server T-SQL. SonarCloud defaults
`.sql` to the Oracle PL/SQL analyzer and `.tsql` to T-SQL, which produced 21
above-Low PL/SQL findings on SQL Server scripts (#2080). Keep the project
language settings at `sonar.tsql.file.suffixes = .tsql,.sql` and
`sonar.plsql.file.suffixes = tab,pkb`. File suffix settings are not among the
supported `.sonarcloud.properties` options for Automatic Analysis, so this
language selection lives in Administration → General Settings → Languages.
This is separate from the Analysis Scope fields, which remain empty. All
executable SQL stays in analysis; reassess the T-SQL results after the next scan.

References: [Automatic Analysis configuration](https://docs.sonarsource.com/sonarqube-cloud/analyzing-source-code/automatic-analysis#additional-analysis-configuration)
and [language file suffixes](https://docs.sonarsource.com/sonarqube-cloud/managing-your-projects/project-analysis/setting-analysis-scope/excluding-based-on-file-extension).

Do not exclude `scripts/`, `.github/`, or `infra/` just to lower counts,
and do not drop tests from analysis entirely — they stay in the test
set. `SonarQube.Analysis.xml` and scanner CLI args apply only to a CI
scan.

### 3. Triage policy

Items listed on #1781 are resolved in the Sonar UI as **False positive**
or **Accepted**, each with a one-line reason and an issue link. Do not
disable rules in the quality profile, add `//NOSONAR`, or use blanket
`sonar.issue.ignore` / ignore multicriteria. Do not add a custom quality
gate while we stay on AA.

| Resolution | Meaning | Examples |
| --- | --- | --- |
| False positive | The code is not vulnerable | S4502 (antiforgery validated explicitly), S2077 (parameterised SQL; sequence name allowlisted, #1165), S2245 (non-crypto randomness), S5443 / S4036 in fixtures and developer/CI scripts |
| Accepted | Real risk, deliberate | terraform S6329 / S6380 / S6378 (#1657), yaml S2068 (throwaway CI container), S6505 / S8543 (lockfile / Expo install scripts), S5693 (authenticated size limits), S8482 / S6506 (Maestro, bundletool, and WWDR downloads) |

**S8482 (5) and S6506 (7)** are real supply-chain gaps on the Maestro,
bundletool, and Apple WWDR downloads in signing and smoke jobs. They are
**Accepted**. Richard declined a follow-up issue for checksum pinning /
`curl --proto '=https'`.

The per-finding `file:line` table lives on the implementing PR for #1781
so it can be applied in the Sonar UI after merge.

### 4. Zero-open check stays on #1781

Resolving issues in the Sonar UI, and confirming 0 open vulnerabilities
(or a recorded reason on each leftover) after #1774, #1775, and #1776
land, happen separately. A repo PR that only adds this file and this ADR
does not close #1781.

## Consequences

Benefits:

- Security rating and the Sonar way gate reflect shipped risk instead of
  prototype and fixture noise.
- Dependabot PRs keep decoration without a `SONAR_TOKEN`.
- One coverage gate remains authoritative.

Tradeoffs:

- AA does not import C# coverage or run a compiled C# analysis.
- Accepted supply-chain findings (S8482 / S6506) stay on the record
  rather than being fixed or tracked in a follow-up issue.

## Related

- #1781 — this decision
- #1774, #1775, #1776 — real workflow / hardening fixes
- #1165 — allowlisted sequence names
- #1657 — SQL public access
- [`.sonarcloud.properties`](../../.sonarcloud.properties) — analysis scope
