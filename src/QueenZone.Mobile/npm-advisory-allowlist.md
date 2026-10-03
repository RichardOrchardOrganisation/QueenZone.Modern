# Mobile npm advisory policy

CI `mobile-js` parses `npm audit --json` after `npm ci` and **fails closed**
on **high/critical** findings unless the GHSA is listed in
[`npm-advisory-allowlist.json`](./npm-advisory-allowlist.json). Moderate and
low findings print and do not fail this gate.

This is not a license to run `npm audit fix` or `npm audit fix --force`.
Forced majors break the Expo SDK 57 / React Native 0.86.2 matrix.

## Adding an exception

1. Try an Expo-compatible upgrade first, then a narrow `overrides` pin.
   Leave the SDK 57 supported-version matrix intact.
2. If the graph cannot move, add one allowlist row per GHSA:
   `ghsa`, `package`, `via`, `exploitability` (one sentence), `owner`,
   `expires` (`YYYY-MM-DD`, required).
3. Expiry is ~90 days. Expired or malformed rows fail CI. Missing audit
   output fails CI.
4. Do **not** use `--omit=dev` as the only policy. That hides the Metro /
   `image-size` highs this gate exists to own.

The committed `image-size` override pins the latest 1.x Metro 0.84 accepts
(`1.2.1`). `image-size@2.0.3` was never published, and 2.x breaks Metro's
v1 sync `require('image-size')` API, so the two highs stay allowlisted
until Expo ships a patched bundler.

## node-forge signature verification (#1996)

`GHSA-86w9-cpqp-85rv` affects `node-forge` through 1.4.0. On
2026-10-02, npm publishes no newer version and reports `fixAvailable: false`;
the [advisory](https://github.com/advisories/GHSA-86w9-cpqp-85rv) lists no
patched version. An upgrade or version override cannot currently resolve it.

The current graph introduces it through Expo CLI and
`@expo/code-signing-certificates`, used for development/build certificate
handling. QueenZone application source does not import forge or Expo CLI,
and `app.config.ts` has no OTA code-signing configuration. Expo certificate
helpers do use the affected signature-verification APIs: this exception
accepts limited tooling exposure and does not fix the vulnerability.

The exception expires on **2026-12-31**. Remove it once a published patched
release is available through an Expo-compatible update or narrow override;
[#1996](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1996)
tracks that work. Reassess before enabling OTA code signing or processing
externally supplied certificates/signatures in tooling. The full-graph audit
and expiry enforcement remain enabled.

## braces recursion denial of service (#2016)

Richard approved the single `GHSA-vfj7-8cjw-p6xm` exception on 2026-10-03.
It accepts unresolved `braces@3.0.3` tooling risk; it is not a vulnerability fix.
The [official advisory](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm)
lists no patched release. [Upstream PR72](https://github.com/micromatch/braces/pull/72)
is unmerged/unreleased at approval; release timing is unknown.

Expo/React Native CLI and Metro file maps, plus Jest tooling, independently
reach braces through micromatch. Inspected callers match build/test filesystem
and configuration globs. No first-party application import or remote user-content
pattern sink was identified, but complete runtime/minified-tool reachability
has not been established. Expo Doctor and resolve-workspace-root also bundle
matching code: a standalone override would not establish complete remediation.
Deep brace expansion can still exhaust the Node stack and terminate tooling.

Owner: **QueenZone maintainers**. Review on **2026-10-10**; expires
**2026-10-17** (inclusive UTC date), so the unchanged validator rejects it from
**2026-10-18T00:00:00Z**. **No automatic renewal.** Remove earlier on a published
Expo-compatible fixed release and revalidate the full graph and native tooling.
If no fix exists at expiry, let CI block and obtain a fresh explicit decision.
Reassess before processing externally supplied glob patterns or adding runtime
imports. [#2016](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/2016)
tracks review/removal. All other advisories, existing exceptions, full-graph audit,
expiry enforcement and required checks remain active.
