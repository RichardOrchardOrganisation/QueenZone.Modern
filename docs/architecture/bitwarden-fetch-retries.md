# Bounded Bitwarden fetch retries

Dev deploy, nightly legacy checks and OpenTofu drift use the local composite
`.github/actions/bitwarden-retry`. It runs the **same official
`bitwarden/sm-action@1238aae8fc64b212641190a9227c8a734ab1a793` (v3.0.1)** up to
three times, waiting **20 seconds before attempt two** and **60 seconds before
attempt three**. Token sources and mapping variables are unchanged. No SDK,
CLI, secret service, region, credential or permission is replaced.

The official action has no typed failure output or retry input. This agreed
fallback retries every failed fetch, including permanent authentication and
permission failures. It does not parse secret-bearing error text or claim an
error is transient. After three unsuccessful attempts it fails hard. A complete
fetch succeeds immediately and records the accepted attempt number without
logging secret values. An incomplete successful response also fails validation
and may retry; values from different attempts are never combined.

## Complete outputs before downstream use

A dependency-free Node 24 guard rejects empty/malformed mappings and duplicate
IDs or aliases before the first request. Current mapping aliases are uppercase
identifiers. Every successful fetch must return exactly all requested aliases
as strings; missing, extra, malformed or partial outputs are rejected. Empty
string values remain valid output values, matching the official action; the
existing consumer-specific configuration checks still apply.

Each official attempt uses `set_env: false`: it retains the official masking
behavior but never puts partial aliases into the caller's global environment.
The scoped workflow consumers already use explicit `steps.bitwarden-secrets.outputs`
references, which retain their current names. The composite declares the union
of those current consumer outputs. New consumers must add their aliases to its
output declarations. Mapping entries beyond the declared consumer outputs are
still fetched and completeness-checked, but are not exposed as composite outputs.

Only a complete, successful validation can select an attempt for publication.
The final guard validates the whole response again, masks all values before
writing, and publishes in one command-file append. **Caller-visible output
availability requires both the final guard's success outcome and its complete
marker.** Physical command-file writes can fail partway through; even if a
failed write left parsed values or a marker, that outcome gate exposes empty
outputs to callers. No `GITHUB_ENV` export occurs at any point. Failed fetches,
validation, publication, timeout or cancellation cannot expose a partial set
through the wrapper. Ordinary downstream steps remain blocked by the wrapper's
failure; steps explicitly opting out of normal success gating would still
receive empty wrapper outputs. Do not add `continue-on-error` to the caller.

## Time and cancellation budgets

Each caller has an **eight-minute whole-fetch step limit**, including waits and
validation. Existing job timeouts are unchanged. GitHub's composite metadata
schema does not support per-inner-step timeouts: an unresponsive attempt can
consume the whole budget, so later attempts are not guaranteed after a hang.
Timeout or cancellation stops the wrapper and publishes no caller outputs.
Retries explicitly require a successful preflight and a noncancelled run.

The wrapper adds a checkout to the previously checkout-free dev settings job
so it can resolve local actions, using the already pinned checkout action and
`persist-credentials: false`. Other scoped jobs already checked out source.
Production release/deploy, other workflows and Richard's personal-site repo
remain outside this change.

## Verification and runtime acceptance

Offline tests interpret the committed composite conditions using fake action
results: initial success, second/third success, three failures, malformed
mapping, partial exports, failed validator/publication markers, timeout and
cancellation. They exercise the real guard with synthetic values and confirm
fixed failure diagnostics, masking-before-publication and no global environment
writes. Workflow syntax and composite metadata are checked against the official
runner schema. No real token, Bitwarden request, secret-step log, database,
workflow dispatch or persistent host is used.

A real Actions run is still needed after publication to confirm nested Node 24
execution, GitHub's command-file/output handling and environment payload limits
on the supported runners. Large output JSON may exceed a runner's environment
limit and fail closed. Offline tests do not establish live service recovery or
classify the historical ten fetch failures as transient. Ordinary scheduled
nightly acceptance is tracked separately; this change does not dispatch it.
