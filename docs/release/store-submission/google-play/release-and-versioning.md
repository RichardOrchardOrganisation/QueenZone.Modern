# Google Play release and versioning

## Existing pipeline

`.github/workflows/publish-android-google-play.yml`:

1. Runs mobile preflight.
2. Generates the Android project with Expo CNG.
3. Builds and verifies a signed Android App Bundle.
4. Stores the AAB as a short-lived workflow artifact.
5. Optionally uploads it to the selected Google Play testing destination:
   `internal` for Internal testing or `closed` for Closed testing (Google's
   `alpha` API track).

The Play publishing service account can release to testing tracks but deliberately cannot release to production or edit store listings.

## Current testing version scheme

- `versionCode`: GitHub Actions run number.
- `versionName`: `{prefix}.{run}` from `marketingVersionPrefix` in `src/QueenZone.Mobile/apiEnvironments.cjs` (default `0.1`, so `0.1.847`). The 1.x flip is changing that one prefix; integer `versionCode` keeps increasing.

## Recommended promotion sequence

1. Upload the candidate to internal testing.
2. Complete install, upgrade, authentication, notification, widget and member-write checks.
3. Run the workflow again with `closed` selected to publish a new candidate
   directly to closed testing when a broader beta is useful.
4. Finalise listing screenshots and Data safety from that exact candidate.
5. Create the production release as a draft.
6. Use managed publishing for the first launch if coordinated timing matters.
7. Have the product owner review countries, release notes, rollout percentage and all policy declarations.
8. Select **Send for review** only after that final review.

## First-release notes draft

QueenZone for Android brings the archive, current news, photography and community features to a native app, with offline-friendly reading, notifications, photo submission, sharing and an On This Day widget.

Edit this to match the final feature set. Do not claim a feature that is disabled or incomplete in the selected AAB.

## Production checks

- AAB signed by the expected upload key and accepted by Play App Signing.
- `org.queenzone.mobile` matches the existing Play record.
- `targetSdkVersion` satisfies the current Play deadline.
- Production API/Sentry configuration is baked into the bundle.
- No development client, debug menu, localhost endpoint or staging label.
- Native symbols and JavaScript source maps uploaded to Sentry where configured.
- App access credentials tested from a clean Play-delivered install.
- Pre-launch report reviewed, including crashes, accessibility and security findings.

