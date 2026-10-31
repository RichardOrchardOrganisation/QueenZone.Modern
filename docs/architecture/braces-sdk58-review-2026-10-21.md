# Braces exposure review: Expo SDK 58, 21 October 2026

> **31 October 2026 status:** GitHub [withdrew this advisory on 23 October](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm)
> as not practically exploitable. The current full-graph npm audit no longer reports
> braces, so the obsolete exception has been removed without extending its expiry.
> The assessment and decision below describe **21 October** and are retained as
> historical evidence. See the [current policy](../../src/QueenZone.Mobile/npm-advisory-allowlist.md#braces-recursion-denial-of-service-2016).

Part of [#2016](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/2016).
Reviewed on Veronica against main **88b0fe6c49f7c3663b3b9eb57296f6dad9a917f1**
(SDK 58 upgrade #2353). Node 24.19.0, npm 11.17.0; fresh locked `npm ci`.
This review supersedes the SDK 57 exposure samples for the current dependency graph.

## Decision and policy

No complete supported remediation was found. The [official advisory](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm)
still covers braces <=3.0.3, has no first patched version and is not withdrawn
(`withdrawn_at: null`, advisory updated 2026-10-02). npm reports braces 3.0.3 as
latest and `fixAvailable: false`. [Fix PR72](https://github.com/micromatch/braces/pull/72)
and [advisory dispute PR10132](https://github.com/github/advisory-database/pull/10132)
are closed **unmerged**; their closure is not an advisory withdrawal.

The owner-approved exception remains **2026-10-31 inclusive UTC**, with enforcement
from **2026-11-01 00:00 UTC / 08:00 Perth**. This review neither renews nor removes
it. Review again before expiry, or immediately if external glob patterns or runtime
imports are introduced. If no complete fix exists at expiry, policy must block
pending a fresh owner decision. Keep #2016 open.

## Installed and bundled paths

| Path | Fresh locked evidence |
| --- | --- |
| Metro | Expo 58.0.16 / React Native 0.88.0 retain `metro-file-map@0.87.1 -> micromatch@4.0.8 -> braces@3.0.3`. `@expo/metro` exports forward to Metro private modules; its file-map forwarding modules still resolve the upstream package. |
| Jest | Top-level Jest is 30.5.2, but `@react-native/jest-preset@0.88.0` depends on `babel-jest`, `jest-environment-node` and `@jest/create-cache-key-function` ^29.7.0. Retained `@jest/transform@29.7.0`, `jest-haste-map@29.7.0` and nested `jest-message-util` independently use micromatch. Jest 30 alone does not remove braces. |
| Workspace resolution | `@expo/config` and `@expo/package-manager` retain `resolve-workspace-root@2.0.1`, which embeds its own braces implementation. |
| Doctor | Locked `expo-doctor@1.21.4` embeds workspace resolution and braces inside its compiled bundle. |
| Published alternative | Fresh npm tarball `expo-doctor@1.21.5` still embeds the same recursive compile/expand implementation. It is not a complete fix. |

The fresh full-graph audit reports the standalone package; bundled copies require
separate byte/code inspection. Whole installed bundle SHA-256 values match the
October 17 files exactly:

- resolve-workspace-root 2.0.1 `build/index.js` (95,984 bytes):
  `ee3be37ce5bce6f0de05ee538653d13f8cbf6a7ec8c8f4ed24466025e5e34f2e`.
- expo-doctor 1.21.4 `build/index.js` (895,776 bytes):
  `a8b9c14011f6e1dc828ac826e341db483fa6e85ac529fad7eb4a3a243c53627d`.
- Fresh expo-doctor 1.21.5 `build/index.js` (865,412 bytes):
  `aaeced0f7d6b7ab88b7dc55c2c5dfd1436b1f9c0f5ef8954a2af5a641e7724ab`.

A signature scan of installed JavaScript found the two compiled copies in addition
to the standalone package's split source files. The embedded implementation still
has recursive `walk` calls and a range-length limit, without a recursion-depth
guard. An unrelated `maxDepth` identifier elsewhere in these bundles is not a fix.
No deep crash or resource-exhaustion input was run during this reassessment.

## Caller reachability

Static inspection of the actual locked modules found Metro file watcher calls to
`micromatch.some`, Jest coverage instrumentation calls to `micromatch.any`, and
Jest message formatting calls to normal matching. These matching APIs delegate
to picomatch. In contrast, `micromatch.parse`, `braces` and `braceExpand` enter the
braces implementation. The additional `@expo/metro-file-map@58.0.5` fork uses
extension/name sets and regular expressions in its inspected watcher helpers;
its presence does not remove upstream Metro's installed file-map path.

Fresh bounded instrumentation executed installed Metro and Jest watcher helpers,
the retained Jest coverage matcher, the Expo fork's include/regex helpers, and
the actual workspace matching wrapper extracted from both locked compiled bundles
and the fresh Doctor 1.21.5 tarball. Ordinary test/coverage globs and one capped
101-level, 205-character pattern produced **zero braces API invocations**.
Each standalone/embedded copy had a working positive control: expanding `a{b,c}`
returned `ab, ac` and registered a call. Bundles were instrumented in memory only;
installed files and lockfiles were not patched.

First-party source/config searches found no direct imports of these packages and
no public-content-to-pattern sink. In the inspected helpers, filenames are candidate
strings and patterns come from repository/workspace configuration. This narrows
the observed risk; it does not prove every tooling caller, dependency, parent
workspace, plugin, or configuration is unreachable. Untrusted checkout/workspace
configuration remains a boundary. Build/test availability remains exposed when
vulnerable expansion is reached. Character/range limits alone do not establish
recursion safety. Passing tests or Doctor checks are compatibility evidence, not a
vulnerability fix.

## Generated app graphs

Command, from `src/QueenZone.Mobile`, with the checked-in default configuration:

```sh
npx --no-install expo export --platform ios --platform android --source-maps --no-bytecode --output-dir <local-artifacts>/runtime-export
```

Both local production JavaScript exports passed. Each indexed source map contains
**5,261 sources**, after recursively walking all map sections. Neither map includes
braces, micromatch, resolve-workspace-root, expo-doctor, metro-file-map or
jest-haste-map package paths. No mapped source content contains the combined
`isInvalidBrace` and `rangeLimit` embedded-code fingerprint. Brace-named matches
are three Lucide icon modules, not the braces package. These checks inspect the
current generated JS graphs; they do not inspect store APKs/IPAs, native artifacts,
all build profiles, OTA updates, or already published binaries. No mobile build was
published, and no release/deployment workflow was dispatched.

## Verification and remaining work

- PASS: clean locked install; full graph `npm audit --json` (expected nonzero audit
  result because known advisories remain); actual `npm run audit:gate` accepts only
  the already approved high advisories (braces and node-forge).
- PASS: `node scripts/check-npm-advisories.mjs --self-test`, including the final
  UTC day, next-midnight rejection and unrelated high/critical failure cases.
- PASS: all 491 repository script regression tests with local PowerShell.
- PASS: bounded caller instrumentation and both production JavaScript exports.
- PASS: full `npm run preflight`: feature map, app/test typechecks, lint,
  823 Node tests, 1,351 Jest tests across 162 suites, and Doctor 20/20.
  Watchman emitted a recrawl warning, but the original run finished successfully.
  The separately started equivalent preflight with the runner's supported
  `--no-watchman` option also passed (same test totals and Doctor 20/20).
- NOT RUN: native compile/device/store-binary inspection, real legacy database
  probes, production credentials, mutation testing, and broad coverage runs. This
  change is documentation/description metadata only, with no application behavior,
  UI, schema, dependency version, exception date or gate change.

Local reproducibility evidence is retained outside iCloud under
`~/Developer/BuildArtifacts/2026-10-21-braces/`: install/preflight/export logs,
`advisory.json`, `audit.json`, `audit-gate.log`, `copy-inventory.json`, downloaded
Doctor tarball/pack metadata, `reachability-probe.cjs`, `reachability.log`,
`runtime-source-map-assessment.json`, generated exports and `scripts-tests.log`.
These local files are not uploaded app artifacts or proof for shipped binaries.

## Smallest supported remediation

None is available that covers every path. A future compatible published braces
release could address the standalone dependency, but both workspace-resolver and
Doctor bundles also need rebuilt compatible releases. Update only those owning
packages needed to replace all copies, then rerun full-graph audit, byte inventory,
caller checks, preflight/risk gates and native compatibility verification. Upgrading
Doctor alone, a standalone override, or the already completed SDK 58 upgrade is
insufficient. Adopting an unmerged patch/fork would require separately approved
implementation work, a supported distribution decision and compatibility evidence;
this documentation review does not authorize it. Keep the existing exception
unchanged and do not remove it on the strength of these exposure samples.
