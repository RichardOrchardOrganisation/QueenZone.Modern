# Mobile device reliability pilot

Establish whether focused test changes reduce avoidable failures while still detecting broken behaviour. This is an opt-in experiment, not a new CI gate.

**Status:** protocol and collector ready; controlled baseline, candidate runs and defect checks NOT RUN. Tracking issue: [#2471](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/2471). Delivery Manager owns the adoption decision; the implementing engineer records evidence and recommendations.

## Scope and protected outcomes

Use the existing Release-embedded app and isolated Testing host. Never supply a real database connection or production credentials.

| Journey | Existing execution | Required outcome | Deliberate defect check |
| --- | --- | --- | --- |
| Photos navigation | `suite=smoke`, flow `05-photography.yaml`, Android and iOS | Open Photos, Brian May collection and photo 101; see viewer controls; close to collection | On a disposable candidate revision, disable the Photos navigation action. The journey must fail at the missing destination. |
| Forum attachment | `suite=journeys`, flow `10-forum-attach.yaml`, Android and iOS | Attach fixture, submit reply, find attachment and observe successful opening | On a disposable candidate revision, make attachment download fail. The journey must fail at its required opening outcome. |

Run the full existing suites to retain preceding navigation context. Record whether each target was reached: an earlier suite failure is not a target pass or a target failure. Do not add a third Android journey without evidence that it is a repeat offender.

Attachment fixture injection does not prove the native file picker works. Inspect screenshots and actual downloaded/opened content separately; a status marker alone must not be described as native viewer proof. Candidate assertions must remain at least as strong as the baseline.

## Diagnostic inventory

`mobile-reliability-pilot/initial-inventory.json` records the ten workflow runs inspected on 11 November 2026 (Perth). These runs mix revisions, runners and dispatch purposes. The inventory is diagnostic evidence, **not a controlled baseline or a flake rate**.

The collector found 50 device-job records: 38 skipped, eight successful and four failed. The 12 executed jobs include an Android smoke failure as well as the three iOS failures below. First Maestro attempts remain unknown until original artifacts are reviewed.

Observed failures motivating selection:

- [42785955377](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42785955377): iOS native build missing `Sentry/Sentry.h`; tests did not establish an app failure.
- [42812858927](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42812858927): required `photos-screen` assertion failed; root cause remains unclassified.
- [42907976004](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42907976004): required `forum-thread-attachment-opened` assertion failed; root cause remains unclassified.
- [Android smoke in 42812858927](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42812858927/job/129818406817): wrapper reported a selector/assertion miss; the specific failed action and root cause still need artifact review.

## Controlled experiment

1. Choose a baseline commit containing the latest intended fixes. Record its full SHA and inspect existing failures before proposing another repair. Create fixed baseline and candidate branches; no commits during a measurement block.
2. Record runner class, OS image, device/runtime, Xcode, Java, Node, Maestro and dependency-lock hashes. Use hosted iOS consistently (`force_hosted_ios=true`) for the initial comparison. Keep Android smoke and journeys as separate runner strata: their existing workflows use different environments. Verify actual versions from each run; version drift invalidates a matched comparison and must be reported.
3. Run three fresh workflow dispatches per cell: Android smoke, iOS smoke, Android journeys and iOS journeys. Each dispatch starts a new Testing host with deterministic data and a freshly prepared device. Inspect logs to confirm reset behaviour rather than assuming app `clearState` resets the host.
4. Investigate each failure using screenshots, hierarchy, device/app logs and failed command. Classify build/dependency, simulator/driver, test interaction/state, app defect, provider/network, or unknown. A selector miss alone is not proof of test flakiness.
5. Make the smallest evidenced candidate change. Keep app/runtime fixes separate from test-only changes so their effects are distinguishable. No blanket retries, removed assertions or general timeout increases. If no test weakness reproduces, report inconclusive instead of inventing a change.
6. Run the same three dispatches per candidate cell. Alternate baseline/candidate dispatches where practical after the candidate exists; record order and elapsed dates. Do not combine different runner strata or suite inputs.
7. Run each deliberate defect check once per platform on disposable branches. Record the patch, SHA, failed assertion and artifact link. An unrelated failure before the intended assertion is inconclusive. Restore the candidate and require its passing run after each check. Never merge the deliberate defects.

**Budget:** initial comparison is at most 24 fresh workflow dispatches (12 baseline and 12 candidate), plus at most eight deliberate-defect/recovery dispatches. Run serially; stop at **12 cumulative device-job hours**, three occurrences of the same failure signature, any real-data safety concern, or unavailable diagnostics. These are ceilings, not a promise to complete the matrix within the budget. Count failed builds and reruns against the budget.

Three repetitions are a diagnostic pilot, not evidence of a low long-term flake rate. Extending to ten repetitions per cell requires a recorded follow-up decision with runtime and cost evidence. A stopped or incomplete matrix is reported as incomplete, never as improved reliability.

## Commands and receipts

Dispatch one cell at a time using an unchanged branch. Replace the example ref with the recorded baseline or candidate branch; wait for completion before the next dispatch. `suite=smoke` and `suite=journeys` are distinct cells; do not use `both` for measurement.

```powershell
gh workflow run mobile-device-smoke.yml --ref codex/pilot-baseline -f suite=smoke -f platform=ios -f force_hosted_ios=true
gh run list --workflow mobile-device-smoke.yml --branch codex/pilot-baseline --limit 5 --json databaseId,headSha,status,createdAt
gh run watch <run-id> --exit-status
node scripts/collect-mobile-reliability.mjs <new-output.json> <run-id> <another-run-id>
```

Match each run's `headSha` and dispatch time to the receipt. Do not infer run identity from "latest" alone. The collector is read-only, retrieves every Actions attempt and job page, and refuses to overwrite an existing output. API failure aborts collection; it does not fabricate missing results.

The collector resolves GitHub CLI only from standard absolute installation paths. For another trusted installation, set `GH_EXECUTABLE` to its absolute path; it never searches `PATH`.

For each cell/repetition, copy and complete this receipt alongside the collected JSON:

```json
{
  "phase": "baseline",
  "repetition": 1,
  "sha": "REQUIRED",
  "runId": "REQUIRED",
  "jobId": "REQUIRED",
  "platform": "ios",
  "suite": "smoke",
  "runnerClass": "hosted macos",
  "toolchainAndDevice": "REQUIRED from logs",
  "targetReached": "unknown",
  "firstMaestroAttempt": "unknown",
  "recoveryCount": null,
  "finalOutcome": "unknown",
  "failureClass": "unknown",
  "failureSignature": "",
  "jobSeconds": null,
  "maestroSeconds": null,
  "artifactLinks": [],
  "visualReview": "NOT RUN",
  "notes": ""
}
```

Retain original-attempt artifacts, JUnit, harness logs, screenshots and job links. Download expiring evidence into the repository's approved evidence storage and link it before expiry; do not commit raw logs or secrets. Unknown, missing, cancelled and skipped evidence remains explicit.

## Measures and decision

Report each platform/suite/runner cell separately with raw counts and denominators:

- **Job completion:** successes, failures, cancellations and skips across all planned dispatches. Missing planned runs remain missing.
- **First Maestro pass:** first invocations passing / invocations that actually began. Builds that fail before Maestro are reported separately. Recovery within an Actions job is still recovery; a green job alone cannot establish a first-pass result.
- **Target outcome:** pass/fail/not reached/unknown for the selected journey. Do not divide by whole-suite job counts without identifying which targets ran.
- **Recovery:** recovered passes / started invocations, with recovery type and original failure retained. Actions reruns are attempts of the original trial, not independent samples.
- **Runtime:** median and maximum job and Maestro durations, sample size, and total device-job hours including failures. Job duration includes setup/build; it is not test duration or queue time.
- **Sensitivity:** deliberate defects detected at the intended assertion / executed defect checks. All four journey/platform checks must detect their defect before adoption.

Adopt a candidate only when the identified test failure stops reproducing, protected outcomes and defect checks pass on both platforms, and no new failure class appears. Investigate a median runtime increase above 20%; do not trade silent retries for a better pass count. If the baseline already passes or toolchains differ, the result is inconclusive about reliability improvement.

Record the final recommendation, limitations and follow-up under #2471. Broad CI rules follow measured findings; this pilot does not change merge protection or waive native proof.

## Rules being evaluated

- Own the starting state and deterministic data for each independently executed journey.
- Use unique selectors and bounded waits for observable readiness and outcomes.
- Make taps clear of keyboards, overlays and persistent navigation controls.
- Keep required outcomes mandatory; shortcuts only prepare unrelated prerequisites.
- Retry only documented infrastructure recovery, preserving first-attempt evidence.
- Prove assertions detect intended breakage before standardising the pattern.

Promote repeated, mechanically detectable mistakes into narrow automated checks after the pilot. Keep behaviour rules in [testing-policy.md](testing-policy.md), not duplicated across agent prompts.
