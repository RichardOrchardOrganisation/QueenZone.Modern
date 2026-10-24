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

Richard approved the single `GHSA-vfj7-8cjw-p6xm` exception on 2026-10-03
and a further 14 days on 2026-10-17. It accepts unresolved `braces@3.0.3`
build/test-tooling denial-of-service risk; it is not a vulnerability fix.
The [official advisory](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm)
still lists no patched release as of 2026-10-21. [Upstream PR72](https://github.com/micromatch/braces/pull/72)
closed without merging; no release date is assumed.

Metro and Jest independently install braces through micromatch. Both
`resolve-workspace-root@2.0.1` and `expo-doctor@1.21.4` also embed vulnerable
copies; Doctor 1.21.5 still contains the unguarded implementation. A standalone
override or patch does not fix those embedded copies. Direct brace expansion
can exhaust the Node stack with input within the existing character limit.

The inspected Metro/Jest and workspace-resolver callers use micromatch matching
APIs, which delegate to picomatch, rather than its braces expansion APIs.
Instrumented samples invoked no braces APIs, with a working positive control.
The generated production Android/iOS source maps contained none of the affected
packages. No first-party runtime import or public-content-to-pattern sink was
identified. These observations narrow the assessed exposure; they do not prove
all tooling paths, configurations or already published binaries are unaffected.
Repository/workspace configuration and dependencies remain tooling trust
boundaries, and vulnerable code remains installed on build machines.

The [SDK 58 review](../../docs/architecture/braces-sdk58-review-2026-10-21.md)
records fresh caller checks, bundled bytes and Android/iOS source-map evidence at
main `88b0fe6c49f7c3663b3b9eb57296f6dad9a917f1`. Top-level Jest 30 does not
remove the Jest 29.7 dependency retained by React Native 0.88.0. The October 17
SDK 57 checks alone are not evidence for this upgrade. No complete supported
remediation was found; Doctor 1.21.5 alone leaves every other affected path.

Owner: **QueenZone maintainers**. Review again before **2026-10-31**; expires
**2026-10-31** (inclusive UTC date). The unchanged validator rejects the row from
**2026-11-01T00:00:00Z**, or **08:00 Perth**. **No automatic renewal.** Remove
earlier on a complete published compatible fix, including rebuilt bundled
copies, and revalidate full-graph audit, preflight, risk gates and native tooling.
If no complete fix exists at expiry, let policy block and obtain a fresh explicit
decision. Reassess immediately before accepting external glob patterns or adding
runtime imports. [#2016](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/2016)
remains open for review/removal. All other advisory decisions and gates stay active.

## zod oversized constrained collections

`GHSA-mrhx-c7xc-m3w5` affects `zod` below 4.6.6: validating an oversized
array, set or record against a size-constrained schema can exhaust memory.
The [advisory](https://github.com/advisories/GHSA-mrhx-c7xc-m3w5) patches only the
4.x line. `expo@58.0.17` -> `@expo/cli@58.1.14` requires `zod@3.25.76`, and the
app declares `zod@^3.25.76`, so a bump or override would break Expo CLI.

The app imports zod only in `src/api/schemas.ts`. `parseContract` validates
responses from the first-party QueenZone `/api/v1` over HTTPS, including
constrained arrays such as crossword `blocks`/`numbering` (`max(225)`). This
is runtime use, not build-time only, but the input comes from our own API. To
exploit it, an attacker would need to control that API or break TLS, and the
impact is a client-side crash. Expo CLI uses zod on local project config.
This exception accepts that limited exposure and does not fix the vulnerability.

Owner: **QueenZone maintainers**. Expires **2026-12-31**. Remove it once Expo
supports a patched zod and the app moves to it. Reassess before validating
third-party or user-supplied payloads with constrained collections.

CI `mobile-js` and the manual TestFlight/Google Play preflight jobs explicitly
run the same full-graph advisory validator after installation and before the
remaining preflight checks. Missing/malformed audit output, an expired exception,
or another unallowlisted high/critical advisory fails the job. Both publication
jobs require a successful preflight, so policy failure prevents signing/upload
in those workflows. Independent native CI jobs are not stopped by this gate;
they do not publish store releases.
