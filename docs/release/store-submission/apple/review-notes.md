# App Review information

## Contact

- First name: `[REQUIRED]`
- Last name: `[REQUIRED]`
- Phone: `[REQUIRED]`
- Email: `[REQUIRED — monitored during review]`

## Sign-in information

- User name: `[DEDICATED APP REVIEW ACCOUNT]`
- Password: `[ENTER DIRECTLY IN APP STORE CONNECT — do not store in this pack]`

## Notes for reviewer

QueenZone is an independent, fan-run Queen archive and community and is not affiliated with Queen or its representatives.

Most archive, news and photography features are available without signing in. To review member-only functionality, use the supplied review account:

1. Open QueenZone and select the profile avatar from Home.
2. On Sign in, expand **Other ways to sign in** and enter the supplied review email and password. Tap **Go** on the password keyboard or tap **Sign in** to submit. Do not use a social provider for this reviewer account.
3. In Archive, open **Quiz Sprint** to see the 60-second quiz and its daily leaderboard. Guests can play; signing in adds eligible scores to the leaderboard.
4. Forum posting is available from the Forum tab. Please create clearly identified test content and remove it when finished if the UI offers that option.
5. Private messages are available from the member profile. The inbox contains a clearly labelled conversation with **QueenZone Review Partner**, a second safe test account. Its messages can be used to inspect the report and block controls without contacting a real member.
6. Photo submission is available from Photography. Submissions enter moderation and do not publish immediately.
7. News suggestions can be opened from News. Suggestions enter editorial review and do not publish automatically.
8. Notification preferences and account deletion are available in Settings.
9. The “On This Day” widget can be added from the iOS Home Screen widget gallery.

The app uses the camera and photo library only after the user selects a photo-submission or avatar action. It does not record audio. Fan-performance audio is streamed to signed-in members through QueenZone rather than exposed as a public file URL.

Account deletion is available at Settings → Delete my account. After the member types DELETE, the account is disabled and signed out immediately. The backend begins permanent deletion of account data and modern user contributions; a retrying background job completes any outstanding blob removal or Sign in with Apple token revocation. Legacy public archive posts remain disconnected from the modern account.

The app displays an in-app status receipt after sign-out. It can refresh until the backend confirms completion and can open the same private receipt on the website for later checks.

Deploy the backend migration and deletion API before submitting the mobile build. The release-build check should confirm that the production `immediate: true` request returns a null `scheduledDeletionAt` and the account cannot sign in again.

If a backend feature is unavailable during review, contact `[REVIEW EMAIL]` and `[REVIEW PHONE]`.

## Final review-build checks

- Reviewer credentials work on a clean installation.
- The review account can submit from the password keyboard, and the form remains reachable while the keyboard is open.
- The selected TestFlight build cold-launches and completes the typical flow on a physical iPhone running the latest iOS release.
- Sign in with Apple completes successfully.
- Public content loads without authentication.
- Camera/photo permission prompts match the action that triggered them.
- Push opt-in is contextual and denial does not block the app.
- Account deletion is discoverable without leaving the app.
- No development environment labels, localhost URLs or test content appear.
- Sentry points to the production project and contains no secrets or message bodies.

