# Live mobile screenshot capture

The checked-in captures were taken on 9 September 2026 from production-configured release builds against `https://www.queenzone.org`. Remote artwork was allowed to finish loading before each capture.

## Capture sets

- iOS capture archive: `apple/assets/screenshots/raw-ios/iphone-15-pro-max-1290x2796/` (1290 × 2796, RGB PNG)
- iOS upload set: `apple/assets/screenshots/upload-ready/` (10 files, the App Store maximum)
- Android capture archive: `google-play/assets/screenshots/raw-android/phone-1080x1920/` (1080 × 1920, RGB PNG)
- Android upload set: `google-play/assets/screenshots/upload-ready/` (8 files, the Play Console maximum per device type)

The complete numbered sequence covers Home, News, Photography, Archive, Forum, Quote, Discography, Fan performances, Trivia, Biography, photo gallery, and photo viewer. The upload-ready directories contain the strongest allowed subset for each store.

These are clean emulator captures at the final store dimensions. They are not stretched or upscaled.

## Maestro workflow

Install a production-configured release build on the target emulator or simulator, then run:

```sh
maestro --device <device-id> test src/QueenZone.Mobile/maestro/store-screenshots-public.yaml
```

For iOS, use the **iPhone 15 Pro Max** Simulator profile. On the current Xcode runtime its native screenshot output is 1290 × 2796. The iPhone 16 Pro Max and iPhone 17 Pro Max profiles output 1320 × 2868 instead. QueenZone is iPhone-only; do not produce or upload iPad screenshots.

For Android, set and verify the emulator display before capture:

```sh
adb shell wm size 1080x1920
adb shell wm density 420
adb exec-out screencap -p > size-check.png
magick identify size-check.png
```

For the authenticated capture, load the reviewer credentials from Bitwarden Secrets Manager into environment variables without printing them, then run:

```sh
REVIEWER_EMAIL='…' REVIEWER_PASSWORD='…' \
  maestro --device <device-id> test src/QueenZone.Mobile/maestro/store-screenshots-authenticated.yaml
```

Never commit credentials, Maestro debug artifacts, or an inbox screenshot containing real private content. The iOS remaining-screen flow handles native-stack back-navigation differences and is also safe to use on Android.

Run `scripts/validate-store-screenshots.sh` before upload or commit. It checks counts, exact dimensions, PNG colour type and alpha-channel absence.

Widget screenshots remain a separate manual capture: add the installed widget to a clean simulator Home Screen, remove personal widgets and notifications, and follow the platform screenshot plan.

## Repeatable App Review walkthrough

The canonical App Review screenplay is
`src/QueenZone.Mobile/maestro/app-review-video.yaml`. It covers a cold launch,
the public tabs, password sign-in, the seeded two-account private conversation,
message and forum reporting/blocking controls, and the account-deletion
disclosure. It deliberately cancels before reporting or blocking and does not
type the deletion confirmation.

Maestro currently supports local iOS execution on Simulators, not physical
iPhones. Its MP4 is therefore a rehearsal and regression artifact, not the
physical-device recording requested by App Review. Generate the rehearsal from
the repository root with an available iOS Simulator UDID:

```sh
./scripts/record-app-review-video.sh --device <ios-simulator-udid>
```

The wrapper loads `REVIEWER_EMAIL` and `REVIEWER_PASSWORD` from Bitwarden
Secrets Manager without printing them. The video is written to the ignored
`src/QueenZone.Mobile/maestro-results/app-review/` directory. Repeat the same
sequence on the latest TestFlight build while using the physical iPhone's
native Screen Recording control. Keep notifications hidden and stop before the
final account-deletion action.

### Physical-device recording script

Use a clean install of the exact TestFlight build selected in App Store Connect.
Turn on Focus, hide notification previews, use a stable network, and confirm the
reviewer credentials before recording. The recording must start on the iPhone
Home Screen so the first action shown is launching QueenZone.

1. Start iPhone Screen Recording, launch QueenZone, and pause briefly on Home.
2. Open News, Photography, Archive, and Forum to demonstrate the public flow.
   On a build that includes Quiz, open it from Archive and answer one sample question.
3. From Forum, choose the new-topic action. On **Sign in**, expand **Other ways
   to sign in** and use the dedicated reviewer email and password.
4. Dismiss the empty forum composer, return to Home, open the profile, then
   open Messages and the **QueenZone Review Partner** conversation.
5. Open **Report message**, show the form, and cancel. Open **More options**,
   choose **Block member**, show the confirmation, and cancel.
6. In Forum, open **Queenzone.com** and **Reviewer test message**. On the reply
   from **QueenZone Review Partner**, show **Report post** and cancel, then show
   **Block member** and cancel.
7. Return to Profile, open **Account settings**, choose **Delete my account**,
   and show the deletion explanation and confirmation field. Do not enter
   `DELETE` and do not schedule deletion.
8. Stop the recording. Review it end to end for crashes, loading failures,
   exposed notifications, mistyped credentials, or personal information before
   attaching it to App Store Connect.

Do not splice in Simulator footage. If a clean retake is needed, repeat the full
sequence from launch so Apple receives one continuous physical-device recording.
