# Mobile device reliability pilot (#2471)

Measure the Photos tab → Brian May category → photo 101 → wallpaper/Save controls → close journey, and the signed-in Forum reply → attach → publish → open → return journey. Both must retain all existing taps, assertions and destination checks. Standalone trials remove unrelated suite failures from the path. They do not test state carried over from preceding flows. The attachment journey uses the local Testing host and its seeded topic. No production account, data or device is involved. The fixture injection and opened marker cover the app download path. Native picker and viewer behavior require separate evidence.

The [Maestro flow rules](testing-policy.md#mobile-device-smoke-872-option-a) remain in the testing policy. The flow checker and its shrink-only baseline remain the policy enforcement. This pilot measures two proposed changes to existing journeys and leaves the tracked flow baseline as-is.

## Fixed comparison

- Record one full pilot commit SHA before dispatch. Use one fixed SHA per platform cohort for the workflow, app, Testing host, fixtures and all six modes. The app and original flows start from `2f6e8963f4b54e6c5c53631cb52e983e34744ec3` (includes the attachment download change from #2451).
- Baseline uses the existing 05/10 flows, with 01-launch before standalone Photos. Candidate waits up to 10 seconds for the Photos tab before tapping it and gives the existing attachment-opened assertion 30 seconds instead of 20. Each candidate changes just its own journey. Keep these changes opt-in pending results.
- Defect modes run candidate assertions against temporary app source changes that disable navigation into Photos or fail the fixture download before it can open. A pass, an earlier failure, or an infrastructure failure does not prove that the selected assertion detected the defect. Never publish these builds.
- Use hosted Ubuntu Android (API 34, Google APIs x86_64) and forced hosted macOS 26 iOS. Keep runner class, runner image version, Node, .NET, Java, Maestro, and device runtime versions identical within each platform comparison. Also compare Xcode and Simulator runtime for iOS, and emulator version, ADB and the OS fingerprint for Android. Compare the job logs before admitting pairs. Exclude drift and unrecorded versions from the controlled comparison, and retain their outcomes in the dispatch inventory. Do not substitute the self-hosted Mac.
- Each dispatch builds Release-embedded apps, boots fresh devices and starts the in-memory Testing host. Keep lockfiles and the existing Android/iOS preparation fixed. CI may still select a newer hosted image or Maestro build between runs, so record the installed versions and check them before interpreting results.

## Trial limits and execution

The device budget is **20 device jobs and 1,200 combined device runner minutes** (60 minutes per device job, at most 600 macOS minutes), plus the short iOS runner-selection jobs. Begin with one baseline/candidate pair and one defect check per journey and platform. A pilot setup fault before Maestro may be corrected once with a new fixed SHA, replacing only the affected platform cohort. Count the original jobs against the same device budget. If there is room and the first pairs show a failure worth investigating, reverse the order for a second pair. Stop for uncontrolled drift, failed controls, or insufficient evidence to justify more jobs.

Dispatch only from a branch held at the recorded SHA. Check the `headSha` in each returned run.

```sh
gh workflow run mobile-device-smoke.yml --ref codex/2471-pilot-trials \
  -f suite=journeys -f platform=both -f force_hosted_ios=true \
  -f reliability_trial=photos-baseline
```

Replace the last value with `photos-candidate`, `photos-defect`, `attachment-baseline`, `attachment-candidate` or `attachment-defect` as appropriate. Select `platform=android` for an Android-only cohort. Wait for a dispatch of the same mode to finish before repeating it because the workflow cancels overlapping runs in that slot.

## Recording and deciding

For every platform/job record the run and job link, SHA, trial mode, runner/toolchain, start/end and elapsed time, first Maestro invocation, recovery outcomes, assertion or failure evidence and artifact link. Read `maestro_invocation=… phase=start/end` from the logs and corroborate against JUnit/debug. The first invocation is invocation 1 in the original device step. Another invocation in that step is recovery. An invocation in the fresh Android emulator retry step is also recovery, even when its counter restarts at 1. Within-flow read-only retry in the unchanged Forum flow is part of that invocation. A successful job can conceal recovery. A successful exit without an executed test case is not a pass.

Classify setup/build (toolchain, compile or package), pre-Maestro device (install/boot), fixture/Testing host, device transport or driver startup, app crash (native crash evidence), app assertion/selector miss (with the exact required check), or unknown/cancelled. Use the first actionable failure and corroborating logs. An assertion identifies the symptom and does not on its own establish the app root cause. Publish both job-to-first-Maestro yield and first-attempt pass counts among admitted starts, then list recovery separately. Report missing or interrupted invocations. Keep deliberate defects out of the normal pass counts.

Consider a limited follow-up only if both defect checks fail at the intended assertion on each platform, each platform/journey has two comparable pairs, and at least one first-attempt assertion failure is removed with no new first-attempt failure, masked defect or material slowdown (more than 30 seconds of additional Maestro time per matched run). If all first attempts pass, the pilot has not shown improvement. If the sample is incomplete, incomparable, or the defect is missed, record the result as inconclusive or reject the candidate as appropriate. A tiny pilot supports a follow-up decision, not a reliability rate or a general hardening rule. Keep #2471 open until controlled runs and findings are recorded.

## Diagnostic inventory, 11 November 2026

`mobile-reliability-pilot/initial-inventory.json` records the ten workflow runs inspected on 11 November 2026 (Perth). These runs mix revisions, runners and dispatch purposes. The inventory is diagnostic evidence, **not a controlled baseline or a flake rate**.

`node scripts/collect-mobile-reliability.mjs OUTPUT.json RUN_ID ...` reads Actions job metadata for the selected device runs, including skipped jobs and all workflow attempts. It cannot infer inner Maestro attempts from job metadata. Use job logs and artifacts for trial results.

A follow-up spot check covered 12 workflow runs at attempt 1, two revisions (`9f69f20d`, `f2885bcb`), two scheduled runs and ten manual dispatches, using hosted and self-hosted runners. Of 14 non-skipped device jobs, 9 succeeded and 5 failed. This mixes suites, revisions and runner classes, and includes job-level results that do not measure first attempts.

| Run | Platform and suite | Observed result |
| --- | --- | --- |
| [42907976004](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42907976004) | iOS journeys, self-hosted | Missing `forum-thread-attachment-opened`. Android journeys passed at job level. |
| [42812858927](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42812858927) | Android smoke, self-hosted | Emulator exited before boot. The workflow's final selector message misclassified this job. |
| [42812858927](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42812858927) | iOS smoke, hosted | Missing `photos-screen`. |
| [42785955377](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42785955377) | iOS journeys, self-hosted | Release build failed before Maestro. |
| [42771894471](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42771894471) | iOS journeys, hosted | Missing `forum-screen`. |

The remaining selected runs were [42949154973](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42949154973), 42938362688, 42935567977, 42927720285, 42920234721, 42802963475, 42782892540 and 42780139694. These reported successful iOS extended jobs and require invocation logs to determine first-attempt outcomes.

## Controlled trial record, 11 November 2026

The hosted iOS cohort ran at `73852d4f027aa6f6f018ec9e54c465249443b9bb`. All six initial Android jobs on that revision booted the emulator, then stopped before Maestro. The pilot's version probe started a separate emulator process without the boot command's `-noaudio` setting. It exited 127 because `libpulse.so.0` was unavailable. The workflow's selector-failure message was incorrect.

The corrected Android cohort ran at `cd54c67dbb90ac73ec6910dbd76226d193377c5e`, which reads the installed emulator revision from SDK package metadata. The six aborted jobs count against the device budget and are excluded from the controlled Android pass counts. The corrected revision uses the same app and base flows and incorporates the merged Testing host configuration from #2472. All baseline/candidate comparisons are within one platform and SHA.

- **iOS toolchain** · Hosted `macos26/20261102.0473.1`, Xcode 26.6 (17F113), iOS 26.5 Simulator (23F77), Java 21.0.13, Node 24.22.2, .NET SDK 10.0.404 and Maestro 2.12.1.
- **Android toolchain** · Hosted `ubuntu26/20261101.186.1`, Java 17.0.21, Node 24.22.2, .NET SDK 10.0.404, Maestro 2.12.1, emulator 37.2.13 and ADB 37.0.1-15733141. The API 34 Google APIs x86_64 fingerprint was `google/sdk_gphone64_x86_64/emu64xa:14/UE1A.230829.050/12077443:userdebug/dev-keys`.

Each passing journey below has a successful invocation marker and one passing JUnit case. Each deliberate defect has one failing JUnit case at the named assertion. The iOS Photos candidate in pair 1 produced no JUnit case on either invocation. XCTest driver startup timed out both before and after the simulator reboot. The normal journey therefore did not reach the app. Job times are in UTC and first-invocation seconds include Maestro driver startup.

| Journey and mode | Platform | Job UTC | Job seconds | Job result | First Maestro result | First seconds | Recovery | Evidence |
| --- | --- | --- | ---: | --- | --- | ---: | --- | --- |
| Photos baseline | Android | 06:54:45–07:31:02 | 2177 | failure | No start | — | None | [Job](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975713306/job/130374203049), [artifact](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975713306/artifacts/13836658947) |
| Photos baseline | iOS | 06:54:56–07:37:18 | 2542 | success | Pass | 272 | None | [Job](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975713306/job/130374264464), [artifact](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975713306/artifacts/13836643872) |
| Attachment baseline | Android | 06:54:48–07:25:13 | 1825 | failure | No start | — | None | [Job](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975718030/job/130374217469), [artifact](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975718030/artifacts/13837185361) |
| Attachment baseline | iOS | 06:54:56–07:34:55 | 2399 | success | Pass | 271 | None | [Job](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975718030/job/130374261931), [artifact](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975718030/artifacts/13837476405) |
| Photos candidate | Android | 06:54:50–07:24:15 | 1765 | failure | No start | — | None | [Job](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975721896/job/130374231037), [artifact](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975721896/artifacts/13836901185) |
| Photos candidate | iOS | 06:54:58–07:44:39 | 2981 | failure | Driver startup timeout | 140 | Failed | [Job](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975721896/job/130374280035), [artifact](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975721896/artifacts/13838250509) |
| Attachment candidate | Android | 06:54:52–07:34:07 | 2355 | failure | No start | — | None | [Job](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975725969/job/130374243488), [artifact](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975725969/artifacts/13837795414) |
| Attachment candidate | iOS | 06:55:02–07:43:25 | 2903 | success | Pass | 348 | None | [Job](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975725969/job/130374296935), [artifact](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975725969/artifacts/13838115834) |
| Photos defect | Android | 06:54:54–07:25:10 | 1816 | failure | No start | — | None | [Job](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975729867/job/130374256091), [artifact](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975729867/artifacts/13836359232) |
| Photos defect | iOS | 06:55:06–07:23:44 | 1718 | failure | Missing `photos-screen` | 140 | None | [Job](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975729867/job/130374324640), [artifact](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975729867/artifacts/13836766733) |
| Attachment defect | Android | 06:54:56–07:30:39 | 2143 | failure | No start | — | None | [Job](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975734421/job/130374269434), [artifact](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975734421/artifacts/13837480758) |
| Attachment defect | iOS | 07:23:46–08:08:13 | 2667 | failure | Missing `forum-thread-attachment-opened` | 324 | None | [Job](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975734421/job/130374335177), [artifact](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42975734421/artifacts/13838428801) |
| Photos baseline (corrected) | Android | 07:27:43–07:54:48 | 1625 | success | Pass | 37 | None | [Job](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42979654159/job/130386821981), [artifact](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42979654159/artifacts/13837229298) |
| Attachment baseline (corrected) | Android | 07:27:45–08:05:19 | 2254 | success | Pass | 88 | None | [Job](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42979657997/job/130386832887), [artifact](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42979657997/artifacts/13838213534) |
| Photos candidate (corrected) | Android | 07:27:47–08:06:37 | 2330 | success | Pass | 51 | None | [Job](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42979661890/job/130386847826), [artifact](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42979661890/artifacts/13839061090) |
| Attachment candidate (corrected) | Android | 07:27:49–08:07:23 | 2374 | success | Pass | 87 | None | [Job](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42979665796/job/130386860568), [artifact](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42979665796/artifacts/13838378882) |
| Photos defect (corrected) | Android | 07:27:51–08:06:10 | 2299 | failure | Missing `photos-screen` | 62 | None | [Job](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42979669858/job/130386874112), [artifact](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42979669858/artifacts/13838587791) |
| Attachment defect (corrected) | Android | 07:27:53–08:07:09 | 2356 | failure | Missing `forum-thread-attachment-opened` | 114 | None | [Job](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42979673948/job/130386887208), [artifact](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42979673948/artifacts/13838732730) |
| Photos candidate (pair 2) | iOS | 07:45:55–08:28:48 | 2573 | success | Pass | 276 | None | [Job](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42981847987/job/130393926488), [artifact](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42981847987/artifacts/13840475935) |
| Photos baseline (pair 2) | iOS | 07:45:58–08:32:15 | 2777 | success | Pass | 301 | None | [Job](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42981852305/job/130393930663), [artifact](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/42981852305/artifacts/13839174960) |

### Outcome and decision

| Platform | Normal jobs starting Maestro | Baseline first passes | Candidate first passes | Deliberate defects detected | Recovery |
| --- | ---: | ---: | ---: | ---: | --- |
| Android, corrected cohort | 4/4 | 2/2 | 2/2 | 2/2 | None |
| iOS | 6/6 | 3/3 | 2/3 | 2/2 | One driver restart, unsuccessful |
| Total | 10/10 | 5/5 | 4/5 | 4/4 | 0/1 successful |

Including the first Android cohort, 14 of 20 device jobs started Maestro. The six setup failures, one driver failure, four deliberate assertion failures, and nine normal passes account for all device jobs. There were no unexpected app assertion failures or native app crashes in the controlled jobs. Every normal journey that reached the app passed. The second iOS Photos pair ran candidate first, then baseline, and both passed on their first invocations.

The pilot used all 20 device-job slots and 45,879 device-job seconds (about 765 runner minutes) across 14 dispatches. This includes the initial Android setup failures and excludes the short iOS chooser jobs. There were no Actions reruns. The platform-only replacements used the original device-job and runner-minute budget.

Do not adopt either candidate on this evidence. The original app-level failures did not recur in the isolated journeys, the iOS driver failure never reached the Photos flow, and the budget allowed one complete app-level pair per platform/journey. On iOS, the attachment candidate's first invocation took 348 seconds versus 271 for baseline, although the reported flow was shorter (177 versus 259 seconds). The job evidence does not attribute that difference to the changed assertion deadline. These results support preserving the required assertions and recording startup failures separately. Broader reliability rules or a long-term failure rate require different evidence.

The measured revisions predate the harness cleanup and flow checker in #2473. The findings branch preserves that work and verifies the combined scripts locally. Artifact links point to the fixed trial revisions.
