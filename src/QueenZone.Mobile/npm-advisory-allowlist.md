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

`node-forge` (GHSA-86w9-cpqp-85rv) is allowlisted because every published
version (<=1.4.0) is affected and the latest SDK 57 `@expo/cli` still depends
on it. It is developer-CLI only: `@expo/cli` and
`@expo/code-signing-certificates` use it to generate and self-verify
code-signing keys, and app code never imports it. Remove the row once a
patched `node-forge` or an `@expo/cli` without it is available.
