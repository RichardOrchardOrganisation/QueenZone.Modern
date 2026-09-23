# Google Play App content and review draft

## Privacy policy

Use `https://www.queenzone.org/privacy`. Confirm the published policy explicitly covers the native app, push notifications, mobile diagnostics/Sentry, camera/photo selection and widgets before public submission.

## Ads

Recommended answer: **No, this app does not contain ads**, based on the native dependency/configuration audit. Recheck the final build. Website AdSense does not make the native app ad-supported unless those ads are rendered inside the app.

## App access

Most archive, news and photography content is public; member actions are restricted.

Create one Play review access instruction with:

- Name: `QueenZone reviewer account`
- Username/email: `[DEDICATED REVIEW ACCOUNT]`
- Password: enter only in Play Console; never commit it.
- Other instructions: no MFA or one-time code; account must remain active throughout review.

Suggested instructions:

1. Open QueenZone and select the profile avatar from Home.
2. Sign in with the provided account.
3. Forum posting is available from Forum; use clearly identified test content.
4. Private messages are available from the member profile. `[ADD SAFE TEST RECIPIENT]`.
5. Photography submissions enter moderation and do not publish immediately.
6. News suggestions enter editorial review and do not publish automatically.
7. Notification preferences and account deletion are in Settings.

## Target audience

Recommended starting selection: **13–15, 16–17, and 18 and over**. Do not select age groups below 13 unless the product intentionally enters Google Play's Families programme and completes its additional obligations.

The app is not designed primarily for children. It contains public user-generated forum material and private member messaging, so the final target-audience answer must align with moderation controls, terms and the generated IARC rating.

## Content rating questionnaire

Answer conservatively for the content users can encounter, not only editorial content:

- User interaction / online communication: Yes.
- Users exchange content: Yes (forum, messages, submissions).
- User-generated content: Yes.
- Digital purchases, gambling, simulated gambling and loot boxes: No unless features change.
- Violence, sexual content, profanity, substances and mature themes: possible infrequent references in historical journalism and legacy community content; review representative live data before answering frequency questions.
- Location sharing: No feature identified.
- Unrestricted web access: No general-purpose browser; verify final external-link behaviour against the questionnaire wording.

## News declaration

QueenZone includes current news but its primary purpose is a broader music archive and community. Recommended working answer: **not primarily a News & Magazines app**, with the explanation that news is one content section among archive, photography, discography, biography, community and member features.

Because the listing and feature graphic truthfully use the word “news,” Play Console may still request the News app declaration. If it does, complete it rather than removing accurate product information merely to avoid review. Confirm visible article dates, sources/bylines, contact information and the separation of original/editorial versus external material.

## Account deletion

- In-app path: profile/settings → Delete account.
- Web path: `https://www.queenzone.org/data-deletion`.
- Explain immediate account disabling and deletion, plus background retries for blob removal and Sign in with Apple token revocation. Older scheduled requests remain cancellable until their due date.
- Ensure the Play deletion URL is public, functional and specific to QueenZone account deletion.

## Permissions

- Camera and photo access: user-initiated avatars and moderated photo submissions; forum attachment selection may use the photo picker.
- Notifications: optional, contextual permission request.
- Microphone/audio recording: not requested.
- SMS, Call Log, contacts, precise location, accessibility service, VPN and broad file access: none identified.
- Recheck the generated Android manifest from the release AAB and complete any permission declaration Play Console presents.

## User-generated-content safeguards

Before review, verify the selected build provides or clearly links to:

- Terms prohibiting abusive or unlawful material.
- Report mechanisms for forum content and members.
- Blocking/privacy controls for unwanted member contact.
- Moderation processes and reachable support contact.
- Appropriate handling of public legacy content and account deletion/anonymisation.
