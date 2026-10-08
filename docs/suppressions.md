# Suppressions and recurring findings

Fix the code rather than adding lint, analyzer, coverage, security, or type-check suppressions to get through CI. A necessary exception must state the reason and an issue that removes it on the same line, for example `// eslint-disable-next-line some/rule -- reason (#1)`.

The checker recognizes common C#, TypeScript, JavaScript, NuGet, and coverage suppression forms, plus HACK/FIXME comments. It is a text check, not a language parser; a link does not establish that the issue exists or that the reason is adequate. Review linked exceptions too.

An existing application may adopt an audited baseline in config/suppression-baseline.json. New projects start empty. Once a suppression is removed, regenerate and commit the smaller baseline using `node scripts/check-suppressions.mjs --write`. Review that diff: --write can also record additions, so never use it as an automatic CI fix or to silently allow new exceptions.

The scanner includes untracked source files and skips hidden directories, build/dependency output, generated EF migrations, docs/design, vendored/minified bundles, and the checker’s own fixtures. Generated native projects are not automatically exempted: document any additional path policy and implement it deliberately.

Correct recurring mistakes at the lowest practical level: code, static check, repository rule, skill, then review guidance. A finding seen twice should normally become an enforceable code/static guardrail tracked by an issue. Do not transfer another project’s suppression baseline or exceptions into a new project.
