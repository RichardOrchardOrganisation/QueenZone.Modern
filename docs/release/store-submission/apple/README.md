# QueenZone iOS App Store release pack

Prepared 29 August 2026. This pack is version-neutral and can be used while the first public build remains undecided.

## Recommended release model

- Create the App Store Connect record now and leave it in **Prepare for Submission**.
- Use `1.0` for the first public App Store version unless there is a product reason to expose a pre-1.0 number.
- Continue uploading TestFlight builds with increasing `CFBundleVersion` values. Choose the final build only when release scope is settled.
- The submitted build's `CFBundleShortVersionString` must match the App Store version.
- Use manual release after approval for the first version.

## Pack contents

- `metadata-en-AU.md` — copy-ready English (Australia) listing text.
- `privacy-and-compliance.md` — draft privacy-label answers and compliance checklist.
- `review-notes.md` — reviewer instructions with placeholders for private credentials.
- `screenshot-plan.md` — required sizes, shot list, captions and capture rules.
- `manifest.md` — asset provenance and completion status.

## Known project values

- App name: QueenZone
- Bundle ID: `org.queenzone.mobile`
- App Group: `group.org.queenzone.mobile`
- Current development marketing version: `0.1.0`
- Orientation: portrait
- Device support: iPhone
- Production site/API: `https://www.queenzone.org`

## Do not submit until

- Final iPhone screenshots have been captured from the selected release candidate.
- A release icon without an alpha channel has been installed in the build and verified from the archived IPA.
- The legal seller/copyright name and App Review contact details are confirmed.
- App privacy answers are checked against the final SDK list and production configuration.
- A reviewer account has been created and tested, or the review notes clearly explain how Apple can exercise member-only features.
- Sign in with Apple, account deletion, push notifications and user-generated-content moderation have been exercised in the submitted build.

