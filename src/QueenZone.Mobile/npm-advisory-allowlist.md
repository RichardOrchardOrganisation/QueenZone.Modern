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

GitHub [withdrew GHSA-vfj7-8cjw-p6xm](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm)
on **2026-10-23**, stating that it is not a practically exploitable vulnerability.
The **2026-10-31** full-graph `npm audit --json` no longer reports braces. The
obsolete braces row has therefore been removed; its expiry was not extended.
This is an advisory withdrawal, not a dependency patch or removal of installed
standalone/bundled code. The [21 October SDK 58 review](../../docs/architecture/braces-sdk58-review-2026-10-21.md)
remains historical evidence for that earlier assessment.

The former exception had an expiry of 2026-10-31 inclusive UTC. The validator
checks all rows, even when an advisory is absent from the audit, so leaving the row
would block from **2026-11-01 00:00 UTC / 08:00 Perth**. Regression tests cover
that boundary, continued rejection of expired rows, and rejection if the braces
advisory reappears. The remaining exceptions and full-graph high/critical,
malformed/missing-output, expiry and publication gates are unchanged.
[#2016](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/2016)
tracks review and removal.

## November 2026 dependency remediation (#2467)

On **2026-11-11**, the locked Expo CLI no longer depends on zod. The app
now uses **zod 4.6.7**, which includes the fix for
[GHSA-mrhx-c7xc-m3w5](https://github.com/advisories/GHSA-mrhx-c7xc-m3w5).
The obsolete zod exception has been removed.

Targeted overrides select **glob 14.0.2**, **minimatch 11.0.1** for affected
modern consumers, and **uuid 11.1.1** for xcode. Legacy glob 7/minimatch 3
consumers retain their compatible APIs and are outside these advisory ranges.
Remove these overrides when upstream ranges accept patched versions.

**deepmerge 4.3.1 remains affected** by
[GHSA-gcqx-w48m-w2h4](https://github.com/advisories/GHSA-gcqx-w48m-w2h4).
It is used by jest-config for development/test configuration, and npm lists
no patched release. Downgrading below the advisory range is not a verified
fix: the advisory describes the defect as affecting versions through 4.3.1.
No exception or dismissal has been added. Track a maintained upstream fix or
removal in [#2467](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/2467).
Do not treat the passing high/critical gate as a clean vulnerability audit.

CI `mobile-js` and the manual TestFlight/Google Play preflight jobs explicitly
run the same full-graph advisory validator after installation and before the
remaining preflight checks. Missing/malformed audit output, an expired exception,
or another unallowlisted high/critical advisory fails the job. Both publication
jobs require a successful preflight, so policy failure prevents signing/upload
in those workflows. Independent native CI jobs are not stopped by this gate;
they do not publish store releases.
