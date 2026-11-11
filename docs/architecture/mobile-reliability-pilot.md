# Mobile device reliability pilot (#2471)

Measure the Photos tab → Brian May category → photo 101 → wallpaper/Save controls → close journey, and the signed-in Forum reply → attach → publish → open → return journey. Both must retain all existing taps, assertions and destination checks. Standalone trials remove unrelated suite failures from the path. They do not test state carried over from preceding flows. The attachment journey uses the local Testing host and its seeded topic. No production account, data or device is involved. The fixture injection and opened marker cover the app download path. Native picker and viewer behavior require separate evidence.

## Fixed comparison

- Record one full pilot commit SHA before dispatch. Use that same SHA for the workflow, app, Testing host, fixtures and all six modes. The app and original flows start from `2f6e8963f4b54e6c5c53631cb52e983e34744ec3` (includes the attachment download change from #2451).
- Baseline uses the existing 05/10 flows, with 01-launch before standalone Photos. Candidate waits up to 10 seconds for the Photos tab before tapping it and gives the existing attachment-opened assertion 30 seconds instead of 20. Each candidate changes just its own journey. Keep these changes opt-in pending results.
- Defect modes run candidate assertions against temporary app source changes that disable navigation into Photos or fail the fixture download before it can open. A pass, an earlier failure, or an infrastructure failure does not prove that the selected assertion detected the defect. Never publish these builds.
- Use hosted Ubuntu Android (API 34, Google APIs x86_64) and forced hosted macOS 26 iOS. Keep runner class, runner image version, Node, .NET, Java, Maestro, and device runtime versions identical within each platform comparison. Also compare Xcode and Simulator runtime for iOS, and emulator version, ADB and the OS fingerprint for Android. Compare the job logs before admitting pairs. Exclude drift and unrecorded versions from the controlled comparison, and retain their outcomes in the dispatch inventory. Do not substitute the self-hosted Mac.
- Each dispatch builds Release-embedded apps, boots fresh devices and starts the in-memory Testing host. Keep lockfiles and the existing Android/iOS preparation fixed. CI may still select a newer hosted image or Maestro build between runs, so record the installed versions and check them before interpreting results.

## Trial limits and execution

At most two baseline and two candidate dispatches per journey, and one defect dispatch per journey. Each dispatch targets both platforms. This caps the pilot at **10 dispatches, 20 device jobs, 1,200 combined device runner minutes** (60 minutes per device job, at most 600 macOS minutes), plus the short iOS runner-selection jobs. No workflow reruns or replacements outside the cap. Begin with one baseline/candidate pair per journey and the defects. Reverse the order for the second baseline/candidate pair. Stop early for uncontrolled drift, failed controls, or insufficient evidence to justify more jobs. Do not increase the cap to compensate.

Dispatch only from a branch held at the recorded SHA. Check the `headSha` in each returned run.

```sh
gh workflow run mobile-device-smoke.yml --ref codex/2471-pilot-trials \
  -f suite=journeys -f platform=both -f force_hosted_ios=true \
  -f reliability_trial=photos-baseline
```

Replace the last value with `photos-candidate`, `photos-defect`, `attachment-baseline`, `attachment-candidate` or `attachment-defect` as appropriate. Wait for a dispatch of the same mode to finish before repeating it because the workflow cancels overlapping runs in that slot.

## Recording and deciding

For every platform/job record the run and job link, SHA, trial mode, runner/toolchain, start/end and elapsed time, first Maestro invocation, recovery outcomes, assertion or failure evidence and artifact link. Read `maestro_invocation=… phase=start/end` from the logs and corroborate against JUnit/debug. The first invocation is invocation 1 in the original device step. Another invocation in that step is recovery. An invocation in the fresh Android emulator retry step is also recovery, even when its counter restarts at 1. Within-flow read-only retry in the unchanged Forum flow is part of that invocation. A successful job can conceal recovery. A successful exit without an executed test case is not a pass.

Classify setup/build (toolchain, compile or package), pre-Maestro device (install/boot), fixture/Testing host, device transport or driver startup, app crash (native crash evidence), app assertion/selector miss (with the exact required check), or unknown/cancelled. Use the first actionable failure and corroborating logs. An assertion identifies the symptom and does not on its own establish the app root cause. Publish both job-to-first-Maestro yield and first-attempt pass counts among admitted starts, then list recovery separately. Report missing or interrupted invocations. Keep deliberate defects out of the normal pass counts.

Consider a limited follow-up only if both defect checks fail at the intended assertion on each platform, each platform/journey has two comparable pairs, and at least one first-attempt assertion failure is removed with no new first-attempt failure, masked defect or material slowdown (more than 30 seconds of additional Maestro time per matched run). If all first attempts pass, the pilot has not shown improvement. If the sample is incomplete, incomparable, or the defect is missed, record the result as inconclusive or reject the candidate as appropriate. A tiny pilot supports a follow-up decision, not a reliability rate or a general hardening rule. Keep #2471 open until controlled runs and findings are recorded.

## Diagnostic inventory, 11 November 2026

`mobile-reliability-pilot/initial-inventory.json` records the ten workflow runs inspected on 11 November 2026 (Perth). These runs mix revisions, runners and dispatch purposes. The inventory is diagnostic evidence, **not a controlled baseline or a flake rate**.

`node scripts/collect-mobile-reliability.mjs OUTPUT.json RUN_ID ...` reads Actions job metadata for the selected device runs, including skipped jobs and all workflow attempts. It cannot infer inner Maestro attempts from job metadata. Use job logs and artifacts for trial results.

The snapshot contained 12 workflow runs at attempt 1, two revisions (`9f69f20d`, `f2885bcb`), two scheduled runs and ten manual dispatches, using hosted and self-hosted runners. Of 14 non-skipped device jobs, 9 succeeded and 5 failed. This mixes suites, revisions and runner classes, and includes job-level results that do not measure first attempts.

| Run | Platform and suite | Observed result |
| --- | --- | --- |
| [42907976004](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42907976004) | iOS journeys, self-hosted | Missing `forum-thread-attachment-opened`. Android journeys passed at job level. |
| [42812858927](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42812858927) | Android smoke, self-hosted | Emulator exited before boot. The workflow's final selector message misclassified this job. |
| [42812858927](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42812858927) | iOS smoke, hosted | Missing `photos-screen`. |
| [42785955377](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42785955377) | iOS journeys, self-hosted | Release build failed before Maestro. |
| [42771894471](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42771894471) | iOS journeys, hosted | Missing `forum-screen`. |

The remaining selected runs were [42949154973](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42949154973), 42938362688, 42935567977, 42927720285, 42920234721, 42802963475, 42782892540 and 42780139694. These reported successful iOS extended jobs and require invocation logs to determine first-attempt outcomes.

## Controlled trial record

Record the fixed SHA and each job outcome here once the jobs have finished.
