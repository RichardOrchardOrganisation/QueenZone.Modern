# Coverage and complexity

The executable project configuration is the source of truth for numeric floors. Do not repeat thresholds in agent instructions or embed a separate set in CI. C# floors live in development-standards.json; TypeScript floors live in config/typescript-coverage.json. The starter values are explained in README.md.

Use a global floor to prevent regression and changed-line coverage to protect new work. For an existing project, measure its deterministic suite first and explicitly approve a baseline. Raising a floor is a reviewable change; lowering one must be justified, never used to hide missing tests.

Coverage reports must include the full production source universe, including files never imported by tests. Union line hits across test projects/shards instead of averaging percentages or adding duplicate denominators. Generated code exclusions are explicit, narrow, and reviewed; handwritten code is not excluded merely because it is hard to test. Missing or malformed reports fail the gate.

Use the PR event’s base commit for a PR comparison, the queue base for a merge-group comparison, and a fetched origin/main for local checks. Fetch sufficient history. A missing base is a failure, not permission to skip changed-line coverage. A moving main by itself is not a reason to rewrite a queued feature branch.

The Coverlet CRAP report uses:

`CRAP = complexity² × (1 − line coverage)³ + complexity`

Scores above 30 indicate high change risk under this policy. Before changing a hotspot, inspect its uncovered behavior and simplify or add useful tests. The report preserves asynchronous source-method identities and unions report hits. It is informational: no automatic CRAP merge gate is enabled. Fully covered methods can remain hotspots because complexity itself contributes to the score.

TypeScript coverage overlays Node hits onto Jest’s complete file universe. Branch coverage uses the Jest/Istanbul branch map because V8 branch keys cannot be combined safely. Consumer contracts and device smoke stay outside those totals. The initial adapter supports Jest plus Node and a project containing src/; other layouts/runners require a reviewed adapter.
