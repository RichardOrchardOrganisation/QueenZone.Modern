## Agent

<!-- Which tool authored this PR? Examples: Grok, Claude Code, Codex, Cursor Composer -->

**Agent:**

## Summary

<!-- What changed and why? -->

## Testing

<!-- e.g. dotnet test QueenZone.sln --configuration Release -->

- [ ] `dotnet restore QueenZone.sln`
- [ ] `dotnet build QueenZone.sln --configuration Release --no-restore`
- [ ] `dotnet test QueenZone.sln --configuration Release --no-build`
- [ ] Coverage gate passed locally (`scripts/Test-CoverageGate.ps1` with `-BaseRef origin/main`; see `AGENTS.md`)
- [ ] If this PR changes `src/QueenZone.Mobile`: `npm ci` + `npm run preflight` (combined typecheck of app + tests via `tsconfig.test.json`, lint, unit tests, **Expo Doctor** — not app typecheck + Jest alone)
- [ ] Route/page tests avoid brittle CSS class or exact markup assertions unless markup shape is the contract
- [ ] If this PR touches EF migrations / `QueenZoneDbContext` / `Entities/`: `dotnet ef migrations has-pending-model-changes` passed, and CI **EF migrations (SQL Express mirror)** is green (or you ran `dotnet ef database update` against the SQL Express mirror locally). Production Azure SQL apply is `deploy.yml` migrate after merge.

## Legacy database checks

<!-- Were opt-in legacy SQL Server checks run? If skipped, say why. -->

- [ ] Not required for this change
- [ ] Ran with `RUN_LEGACY_DB_TESTS=true`
- [ ] Skipped (explain below)

## Verification

<!-- Required for PRs that change mobile screens/navigation/UI or web Pages/Views/wwwroot.
Use a docs/feature-map id. Attach or link proof under artifacts/proof/<id>/<platform>/<timestamp>/
or a mobile-device-smoke suite=proof run. If a device is missing, write NOT RUN and name the check. -->

- Feature ids:
- Command:
- Platform(s):
- Result:
- Proof:
- Not verified:

<!-- Opt out of the UI check with the `no-ui-verification` label AND a reason: -->
<!-- Verification-skip-reason: -->

## Review findings

<!-- Orchestrator pastes the reviewer's visible line + qz-finding tag verbatim (see .cursor/agents/reviewer.md). The gardener parses the tags. Leave empty when there are no findings. Nits also stay in Follow-up. -->

## Issues

<!-- If this PR fully resolves an issue, use a real closing keyword so GitHub auto-closes it on merge: "Closes #123" / "Fixes #123" / "Resolves #123". Use "Relates to #123" for issues this PR only touches. A prose mention elsewhere in this PR (e.g. "Implements #123") does NOT auto-close the issue and is checked by CI (pr-issue-link-check). -->

## Follow-up

<!-- Any skipped checks, known limitations, or post-merge work -->
