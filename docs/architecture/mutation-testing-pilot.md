# Mutation testing pilot assessment

Targeted mutation testing found useful assertion gaps in pagination, mobile cache behaviour and SSRF validation. Keep it opt-in under the [testing policy](testing-policy.md#mutation-testing); these runs do not justify a repository-wide score threshold or CI gate.

## Scope and results

The pilots ran on Windows on **17–18 October 2026**. The production source baseline was `cff0d6c032503d59e7649d2db6f9a4740e2496f7`; follow-up runs added exploratory assertions against the same production code. [PR #2350](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/pull/2350) adopts **39 cases**: six pagination, three mobile cache and 30 SSRF cases.

| Selected source | Normal cases before → after | Mutation score before → after | Wall time before → after |
| --- | ---: | ---: | ---: |
| .NET `ArchivePagination.cs` | 33 → 39 | 81.61% → 95.40% | 79.68 s → 58.58 s |
| Mobile `externalStore.ts` and `formatDuration.ts` | 12 → 15 | 87.50% → 96.67% | 29.96 s → 15.13 s |
| .NET `OutboundUrlSafety.cs` | 78 → 108 | 79.63% → 96.30% | 140.27 s → 67.44 s |

These are observed workstation wall times, including mutation-tool build/startup and reporting but excluding tool installation. Later runs used warm caches; the first SSRF run overlapped npm installation. The shorter follow-up times do **not** establish a performance improvement from adding assertions or predict CI runtime.

| Run | Killed | Timeout | Survived | Uncovered | Scored total |
| --- | ---: | ---: | ---: | ---: | ---: |
| Pagination before | 67 | 4 | 12 | 4 | 87 |
| Pagination after | 79 | 4 | 4 | 0 | 87 |
| Mobile before | 105 | 0 | 15 | 0 | 120 |
| Mobile after | 116 | 0 | 4 | 0 | 120 |
| SSRF before | 85 | 1 | 13 | 9 | 108 |
| SSRF after | 103 | 1 | 2 | 2 | 108 |

Stryker counts timeouts as detected mutations. Pagination's four timeouts reverse loop increments; SSRF's timeout negates the mapped-IPv4 guard and causes unbounded recursion. They are separate from assertion-based kills.

The pagination target also contained 16 tool-ignored mutations and one compile error; the SSRF target contained 18 tool-ignored mutations and no compile errors. They are outside the scored totals. No mutation exclusions or suppressions were added to raise the scores. Mutations in other source files were outside the selected scope.

## Findings adopted into tests

**Pagination:** six cases detected 12 additional mutations. They protect negative counts, empty/count-unavailable totals, preservation of a known total on an empty page, the disabled last-page next link and the boundary before the final edge cluster.

**Mobile cache:** three cases detected 11 additional mutations. Multiple listeners must coexist, unsubscribing one must retain the others, and prefix versions must count ancestor/descendant invalidations without counting unrelated prefixes. Existing cases checked notification counts without asserting the version values collected in those notifications.

**SSRF:** 30 cases detected 18 additional mutations. They cover the inclusive URL-length limit, success errors and normalization, the throwing validation wrapper, direct internal/metadata hostname checks, public hostname controls and supported/missing content types.

These findings show missing protection in the selected assertions. They do not establish defects in the unchanged production implementation or gaps across every other test suite.

## Remaining mutations

- **Pagination:** four survivors appear equivalent for valid inputs and the existing branch constraints. The zero-count boundary still returns zero, and the middle-page branch already constrains its upper window and final gap comparisons.
- **Mobile audio:** removing the null check is equivalent under the typed input contract because `Number.isFinite` still rejects null and undefined.
- **Mobile cache:** three survivors leave empty listener sets in the map rather than deleting them. They concern internal cleanup/resource retention without a current observable API difference. Private-map assertions would not strengthen the public contract.
- **SSRF metadata hostname:** the explicit `metadata.google.internal` check duplicates the `.internal` suffix check.
- **SSRF mapped IPv4:** removing explicit conversion survived a separate recheck with coverage analysis off and mutant mixing disabled. On this runtime, `IPNetwork.Contains` itself recognizes mapped IPv4. The **34.37-second** recheck exercised the surviving block removal and recursive timeout; it was not a whole-module score comparison.
- **SSRF empty host:** two mutations remain uncovered behind successful absolute HTTP(S) URI parsing. No reachable string input was identified. They remain visible rather than being excluded to obtain 100%.

## Tooling and reproducibility

All runs used **two workers**, .NET SDK **10.0.302**, Node **24.15.0**, Stryker.NET **5.0.0** or StrykerJS **10.0.0**, and a failure threshold of **0**. Tool installations and generated HTML/JSON reports stayed local under the ignored `.tools/mutation-pilot/` directory; they are not portable PR attachments or CI evidence.

For the C# runs, execute Stryker.NET from the relevant test project directory with these settings:

| Setting | Pagination | SSRF |
| --- | --- | --- |
| `project` | `QueenZone.Search.Shared.csproj` | `QueenZone.NewsAgent.csproj` |
| `mutate` | `Routes/ArchivePagination.cs` | `OutboundUrlSafety.cs` |
| `test-case-filter` | `FullyQualifiedName~Pagination` | `FullyQualifiedName~OutboundUrlSafetyTests` |
| `configuration` | `Release` | `Release` |
| `coverage-analysis` | `perTest` | `perTest` |
| `concurrency` | `2` | `2` |

Use HTML/JSON reporters and a separate local output directory. The mapped-address recheck selected the source span by **character offsets**, as required by [Stryker.NET's configuration](https://stryker-mutator.io/docs/stryker-net/configuration/#mutate-glob). An initial attempt using line numbers selected zero mutations and was discarded as evidence.

The mobile run used exact-source copies of two modules, their unchanged Node tests and `keys.ts`, with matching SHA256 hashes. StrykerJS used `testRunner: "command"`, `coverageAnalysis: "off"`, `concurrency: 2`, `timeoutMS: 2000` and `timeoutFactor: 1.5`. Its command was:

```text
node --experimental-strip-types --disable-warning=MODULE_TYPELESS_PACKAGE_JSON --test src/cache/externalStore.test.ts src/audio/formatDuration.test.ts
```

The command runner treats each successful command as one test; the actual Node case counts are shown above. It reruns the selected suite for each mutant without per-test coverage selection. No TypeScript checker, Expo/Jest component integration, native code or device journey was exercised by this mutation pilot. Later app/test typechecks and normal Jest preflight are separate verification.

The normal pagination baseline took **2.52 seconds** warm without a build; its first whole-project restore/build/test took **34.38 seconds**. The mobile selected normal tests took **0.52 seconds**. The original SSRF selected restore/build/test took **16.43 seconds**. These differing setup conditions prevent a direct comparison of mutation overhead between stacks.

## Verification and decision

When adopting the assertions, solution restore, Release build, formatting and **5,979 .NET cases** passed. Mobile preflight passed with **822 Node cases**, **1,350 Jest cases across 163 suites**, typechecks, lint and **21 Expo Doctor checks**.

After updating from `main`, another Release build and **67 focused web cases** passed. Refreshed mobile dependencies passed typechecks, lint, **11 cache cases**, **8 sharing cases** and all **21 Doctor checks**. The local coverage gate passed at **91.11%** using the full run plus supplementary post-update coverage. The full suites were not repeated after that update; CI checks the current PR head separately.

No real database/provider probes or browser/device proof were needed for these test-only changes. The selected-module mutation results do not establish SQL Server behaviour, native behaviour or repository-wide assertion quality.

Adopt the useful assertions and retain opt-in runs for higher-risk logic. Before scheduling mutation testing or introducing a merge gate, measure it on the intended runner and agree how to handle equivalent, unreachable and unresolved mutations.
