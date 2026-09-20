# Bitwarden Secrets Manager (GitHub Actions)

This project uses Bitwarden Secrets Manager as the source of truth for long-lived automation secrets used by GitHub
Actions. Workflows fetch secrets at runtime through `bitwarden/sm-action`; the local Windows machine can read/update
the same project directly through the `bws` CLI (see `AGENTS.md` for the local-development flow).

Do not commit secret values. This file documents secret IDs, environment variable names, machine accounts, and the
repository variable mapping only.

## Bitwarden project

All GitHub Actions secrets for this repo live in the same Bitwarden Secrets Manager project used for local dev:

- Project: `Queenzone Development`
- Project ID: `1c16fd2d-4bfb-4eb7-8357-b49400233490`

## Configuration summary

| Context                | Machine account                       | Token location                    | Env var used by tool                     | Purpose                                                                      |
| ----------------------- | -------------------------------------- | ---------------------------------- | ------------------------------------------ | ------------------------------------------------------------------------------ |
| GitHub Actions          | `github-actions-queenzone` (read-only) | GitHub Actions secret              | `BITWARDEN_SECRETS_MANAGER_ACCESS_TOKEN` | Lets workflows fetch Bitwarden secrets through `bitwarden/sm-action@v3.0.1`. |
| Local Windows machine   | `windows-codex`                        | Windows User environment variable  | `BWS_ACCESS_TOKEN`                       | Lets this computer read/update Bitwarden Secrets Manager with `bws.exe`.     |

The two token environment variable names are intentionally different because they are consumed by different tools:

- `BITWARDEN_SECRETS_MANAGER_ACCESS_TOKEN`: passed to the GitHub Action's `access_token` input.
- `BWS_ACCESS_TOKEN`: read directly by Bitwarden's local Secrets Manager CLI.

## GitHub bootstrap secret

Keep exactly one GitHub Actions secret for Bitwarden access:

- `BITWARDEN_SECRETS_MANAGER_ACCESS_TOKEN`: access token for the `github-actions-queenzone` machine account, scoped
  read-only to the `Queenzone Development` project.

GitHub's built-in `GITHUB_TOKEN` remains GitHub-native because it is minted for each workflow run.

The Bitwarden project also contains `IOS_RUNNER_ADMIN_TOKEN`, a fine-grained
GitHub PAT restricted to this repository with **Administration: read-only**.
`ci.yml` fetches it through the `BITWARDEN_IOS_RUNNER_SECRETS` repository
variable so the runner probe can distinguish an idle Mac from a busy one. The
workflow falls back to `macos-26` if the mapping, token, or API is unavailable.
Rotate the PAT in GitHub, replace its Bitwarden value, and verify a dispatched
mobile run; never copy it into workflow YAML or logs.

Every workflow that needs private settings follows this shape:

```yaml
- name: Fetch Bitwarden deploy secrets
  id: bitwarden-secrets
  uses: bitwarden/sm-action@v3.0.1
  with:
    access_token: ${{ secrets.BITWARDEN_SECRETS_MANAGER_ACCESS_TOKEN }}
    secrets: ${{ vars.BITWARDEN_APP_SERVICE_DEPLOY_SECRETS }}
```

and downstream steps read the outputs by name, e.g. `${{ steps.bitwarden-secrets.outputs.AZURE_WEBAPP_PUBLISH_PROFILE }}`.

The `vars.BITWARDEN_*_SECRETS` value is a repository variable containing mappings from Bitwarden secret IDs to
environment variable names. Those mappings are listed below.

## Bitwarden secret mappings

The Bitwarden action maps secret UUIDs to the environment variable names that the existing workflow steps already
consume:

```yaml
00000000-0000-0000-0000-000000000000 > ENVIRONMENT_VARIABLE_NAME
```

Store these mapping blocks as the named GitHub repository or environment variables. The left-hand side must be the
Bitwarden secret ID, not the secret name. Environment-specific mappings deliberately expose only the credentials
needed by that trust boundary.

### `BITWARDEN_APP_SERVICE_DEPLOY_SECRETS`

Used by `.github/workflows/deploy.yml` (`migrate` and `deploy` on `prod-release`;
`configure-app-settings` on `prod-deploy`), `.github/workflows/nightly-legacy-checks.yml`
(`sync-legacy-db` on `prod-data-read`; `legacy-read-probes` and macOS `ui-e2e-realdata`
via repository-level token/mapping for the SQL Express probe password only), and
`.github/workflows/test-migrations-against-mirror.yml` (`resync-mirror` on `prod-data-read`).
`ci.yml` `ef-migrations` does **not** use this mapping — it applies to the SQL Express
mirror with Integrated Security (issue #1377 Option B). See
[`docs/architecture/github-environments.md`](architecture/github-environments.md):

Repository mapping (nightly mirror probes plus shared fallbacks):

```yaml
a4e79219-d353-4730-ab3b-b4c10048fd97 > AZURE_WEBAPP_PUBLISH_PROFILE
14cd027a-7185-44fb-ab7f-b4c100488980 > QUEENZONE_LEGACY_MIGRATION_CONNECTION_STRING
51484e1f-4393-41e9-8435-b49a00381ec2 > QUEENZONE_SQL_EXPRESS_PROBE_PASSWORD
b6a94e02-3243-411f-8e32-b4af00ce2522 > MOBILE_AUTH_SIGNING_KEY
```

`prod-release` mapping:

```yaml
a4e79219-d353-4730-ab3b-b4c10048fd97 > AZURE_WEBAPP_PUBLISH_PROFILE_CANADA_EAST
14cd027a-7185-44fb-ab7f-b4c100488980 > QUEENZONE_LEGACY_MIGRATION_CONNECTION_STRING_CANADA_EAST
b6a94e02-3243-411f-8e32-b4af00ce2522 > MOBILE_AUTH_SIGNING_KEY
```

`prod-deploy` mapping:

```yaml
b6a94e02-3243-411f-8e32-b4af00ce2522 > MOBILE_AUTH_SIGNING_KEY
```

`prod-data-read` mapping:

```yaml
a4e79219-d353-4730-ab3b-b4c10048fd97 > AZURE_WEBAPP_PUBLISH_PROFILE
14cd027a-7185-44fb-ab7f-b4c100488980 > QUEENZONE_LEGACY_MIGRATION_CONNECTION_STRING
```

The migration aliases map to the Canada East Bitwarden secret
`ConnectionStrings__QueenZoneLegacyCanadaEast`. The live App Service retains the
canonical setting name `ConnectionStrings__QueenZoneLegacy`, but its value is
configured separately in Azure. Updating Bitwarden does **not** update that live
App Service setting automatically.

`QUEENZONE_SQL_EXPRESS_PROBE_PASSWORD` is the password for the `queenzone_probe` SQL login created by
`scripts/Enable-SqlExpressRemoteAccess.ps1` on the Windows self-hosted runner. It authenticates
`legacy-read-probes` (running on the macOS runner) against the SQL Express mirror over the LAN — see
`docs/architecture/testing-policy.md` ("Data Integration Tests") and the comment block at the top of
`nightly-legacy-checks.yml`.

`MOBILE_AUTH_SIGNING_KEY` maps to the `MobileAuth__SigningKey` secret. The deploy workflow reconciles it
to the same-named App Service setting before every web deployment, including rotations, and fails before
deployment if the mapped value is missing or shorter than 32 characters.

### `BITWARDEN_IOS_RUNNER_SECRETS`

Used only by `.github/workflows/ci.yml` (`mobile-ios-runner`). Store this mapping
as a repository variable after creating the Bitwarden secret:

```yaml
<IOS_RUNNER_ADMIN_TOKEN_BITWARDEN_SECRET_ID> > IOS_RUNNER_ADMIN_TOKEN
```

### `BITWARDEN_DEV_AUTH_E2E_SECRETS`

Used by `.github/workflows/dev-member-auth-e2e.yml` (`member-auth` on `dev-deploy`) to fetch the
synthetic curated-snapshot member password for the password `DeployedAuth` browser check
(`https://dev.queenzone.org` only). See [`docs/architecture/testing-policy.md`](architecture/testing-policy.md)
("Deployed dev member authentication").

Store this mapping as a **repository** variable. The left-hand side is the Bitwarden secret ID
for the existing `DEV_SNAPSHOT_MEMBER_PASSWORD` secret in the `Queenzone Development` project
(the same secret the snapshot refresh maps through `BITWARDEN_DEV_SNAPSHOT_SECRETS`):

```yaml
<DEV_SNAPSHOT_MEMBER_PASSWORD_SECRET_UUID> > DEV_SNAPSHOT_MEMBER_PASSWORD
```

The workflow reads `steps.member-secret.outputs.DEV_SNAPSHOT_MEMBER_PASSWORD` into
`DEV_AUTH_E2E_PASSWORD` for the test step only. Do not commit the password, echo it, or upload
Playwright traces, screenshots, videos, or HAR files from that job — those can contain the
password form. This mapping is DEV-only. There is no production password auth e2e.

**Rotation:** update the `DEV_SNAPSHOT_MEMBER_PASSWORD` value in Bitwarden in place. Keep the
secret UUID. Leave `BITWARDEN_DEV_AUTH_E2E_SECRETS` unchanged. After a snapshot refresh that
writes a new synthetic member password, the Bitwarden value and the row in `queenzone-dev-db`
must match; changing one without the other breaks the password job.

### `BITWARDEN_DEV_AUTH_GOOGLE_E2E_SECRETS`

Scaffolding for the planned DEV Google OAuth `DeployedAuth` check
([#1614](https://github.com/richardorchard/QueenZone.Modern/issues/1614)).
**Values are not live.** Richard will designate a dedicated Google account with **2FA disabled**.
Do not create the Bitwarden secrets or the GitHub variable until that account exists. Do not
start Playwright until Bitwarden holds those credentials. No production OAuth e2e.

Intended Bitwarden secret **names** in the `Queenzone Development` project:

| Bitwarden secret name | Workflow output / env name | Purpose |
| --- | --- | --- |
| `DEV_AUTH_GOOGLE_E2E_EMAIL` | `DEV_AUTH_GOOGLE_E2E_EMAIL` | Dedicated Google test account email |
| `DEV_AUTH_GOOGLE_E2E_PASSWORD` | `DEV_AUTH_GOOGLE_E2E_PASSWORD` | That account's password |

Intended GitHub **repository** variable `BITWARDEN_DEV_AUTH_GOOGLE_E2E_SECRETS` (UUID → name;
placeholder IDs until ops fills real secret IDs):

```yaml
<EMAIL_SECRET_UUID> > DEV_AUTH_GOOGLE_E2E_EMAIL
<PASSWORD_SECRET_UUID> > DEV_AUTH_GOOGLE_E2E_PASSWORD
```

**Account rules:** dedicated Google account only; 2FA off on that account; not a personal or
daily account; no app passwords; no interactive 2FA. Do not automate against a personal Google
account.

**Job contract (for the later workflow, not implemented here):** fetch these secrets only
inside a `dev-deploy` (or equivalent) gated job, the same way the password check uses
`bitwarden/sm-action` + `BITWARDEN_DEV_AUTH_E2E_SECRETS`. Keep the Google job or filter
separate from the password `DeployedAuth` path. If this variable or either mapped value is
missing, **skip the Google job cleanly** so the password journey is unaffected. Target
`https://dev.queenzone.org` only.

**Hygiene:** never commit values; never echo; never upload traces, videos, screenshots, or HAR
that can contain the Google password form. Same fixture style as `DeployedMemberAuthTests`
(do not use `E2EPageTest` artifact upload paths).

**Rotation:** when the dedicated Google password (or email) changes, update the Bitwarden
secret **value in place**. Keep the secret UUID. Leave `BITWARDEN_DEV_AUTH_GOOGLE_E2E_SECRETS`
unchanged. Verify by secret name and value length only.

**Out of scope:** Microsoft / Discord / GitHub / Apple provider e2e; production OAuth e2e;
expanding the password journey.

## APNs push credential

The dedicated Apple Push Notification service key for `org.queenzone.mobile` is named `QueenZone APNs 2026` in
Apple Developer **Certificates, Identifiers & Profiles → Keys**. It is production-only and topic-specific to
that bundle ID. The credential is separate from the Apple distribution certificate and provisioning profile
used by the TestFlight build.

Store its four runtime values as separate secrets in the `Queenzone Development` Bitwarden project, using the
App Service setting names verbatim:

| Setting | Value source |
| --- | --- |
| `PushNotifications__Apns__TeamId` | Apple Developer membership Team ID |
| `PushNotifications__Apns__KeyId` | The Key ID shown for `QueenZone APNs 2026` |
| `PushNotifications__Apns__PrivateKeyPem` | Complete one-time `.p8` download, including the PEM header and footer |
| `PushNotifications__Apns__Environment` | `production` |

Copy the same four names and values into the `queenzone-prod` App Service configuration. These settings are
operator-owned under ADR 0008: changing Bitwarden does not update App Service, and the deploy workflow does not
reconcile them. Never add the `.p8` file or any private-key text to the repository, issue, PR, or logs.

### APNs rotation

Apple permits an Auth Key private key to be downloaded only once. Rotate with a create-test-revoke sequence so
push delivery is not interrupted:

1. In Apple Developer, register a replacement key with APNs enabled, environment `Production`, restriction
   `Topic Specific`, and topic `org.queenzone.mobile`. Download its `.p8` immediately to a temporary local file.
2. Update `PushNotifications__Apns__KeyId` and `PushNotifications__Apns__PrivateKeyPem` in Bitwarden. Confirm the
   unchanged Team ID and keep `PushNotifications__Apns__Environment` set to `production`.
3. Apply all four Bitwarden values to the same-named App Service settings, restart the app, and verify push
   delivery to a TestFlight or App Store build. Verify setting names and value lengths only; never print values.
4. Revoke the previous key in Apple Developer only after the replacement has delivered successfully. Delete any
   temporary `.p8` file after Bitwarden and App Service have been verified.
5. Record the rotation date and new Key ID in the tracking issue without recording the private key.

## Sign in with Apple (member login) credential

Apple App Review Guideline 4.8 applies to QueenZone because the mobile app offers third-party login for the
member's primary account. Sign in with Apple is therefore a required equivalent option before public App Store
submission; it is not merely an optional convenience login.

The production registration uses these Apple Developer resources:

- Primary App ID: `org.queenzone.mobile`, with the **Sign in with Apple** capability enabled as a primary App ID.
- Services ID / OAuth client ID: `org.queenzone.web` (`QueenZone Member Login`).
- Web domain: `www.queenzone.org`.
- Return URL: `https://www.queenzone.org/signin-apple` (the ASP.NET Core Apple handler callback).
- Sign in with Apple key: `QueenZone Sign in with Apple 2026 Replacement`, associated with the primary App ID.

Store the four runtime values as separate secrets in the `Queenzone Development` Bitwarden project, using the
App Service setting names verbatim:

| Setting | Value source |
| --- | --- |
| `Authentication__Apple__ClientId` | Services ID (`org.queenzone.web`) |
| `Authentication__Apple__TeamId` | Apple Developer membership Team ID |
| `Authentication__Apple__KeyId` | Key ID shown for the active Sign in with Apple key |
| `Authentication__Apple__PrivateKey` | Complete one-time `.p8` download, including the PEM header and footer |

Copy the same four names and values into the `queenzone-prod` App Service configuration and restart the app.
These settings are operator-owned under ADR 0008: updating Bitwarden does not update App Service and the deploy
workflow does not reconcile them. Verify by setting name and value length only, then confirm Apple appears in
`GET /api/v1/auth/providers` and that `/account/external-login?provider=Apple` redirects to Apple's authorization
endpoint. Never commit, paste, or log the private key.

The mobile client consumes the backend provider list dynamically, so enabling the backend makes Apple available
without a mobile release. Its button deliberately uses Apple's black button treatment, Apple mark, system font,
normal title casing, and the same 48-point height as the other login buttons. This follows Apple's
[Sign in with Apple Human Interface Guidelines](https://developer.apple.com/design/human-interface-guidelines/sign-in-with-apple/),
which require the Apple button to be prominent, no smaller than peer login buttons, and Apple-approved in appearance.

### Sign in with Apple rotation

Apple private keys can be downloaded only once. Rotate with a create-test-revoke sequence:

1. Create a replacement Sign in with Apple key associated with `org.queenzone.mobile` and download its `.p8`
   immediately to a temporary local file.
2. Update `Authentication__Apple__KeyId` and `Authentication__Apple__PrivateKey` in Bitwarden, then apply all four
   Apple settings to App Service and restart it.
3. Verify the provider-list response and a real website/mobile Apple login before revoking the previous key.
4. Revoke the previous key and remove every local `.p8` copy after verification. Record only the replacement key's
   name, Key ID, and rotation date in the tracking issue.

## Android FCM push credential

Android push uses Firebase project `queenzone-mobile` and its registered Android app
`org.queenzone.mobile`. The committed `src/QueenZone.Mobile/google-services.json` contains Firebase project/app
identifiers used by the Android client; Firebase documents this file and its API key as non-secret client
configuration. It must not be confused with the private service-account JSON below.

The backend FCM HTTP v1 sender is the dedicated Google Cloud service account
`queenzone-fcm-sender@queenzone-mobile.iam.gserviceaccount.com`. It has only the **Firebase Cloud Messaging API
Admin** role. Its private key is stored under these matching Bitwarden and Azure App Service names:

- `PushNotifications__Fcm__ProjectId`: `queenzone-mobile` (identifier, not a credential).
- `PushNotifications__Fcm__ServiceAccountJson`: the complete private service-account JSON. Never print, commit,
  or paste this value into an issue or pull request.

To rotate the credential, create a new JSON key on the same dedicated service account. Update Bitwarden first,
then update both matching App Service settings and restart/verify the site. Confirm an authenticated FCM HTTP v1
send succeeds before permanently deleting the previous key in Google Cloud. Verify values only by exact in-memory
comparison or value length; never emit the JSON or its private key.

## Google Play publishing credential

Google Play publishing uses the dedicated Google Cloud service account
`queenzone-play-publisher@queenzone-mobile.iam.gserviceaccount.com`. It has no Google Cloud project IAM roles.
In Play Console it is scoped only to `org.queenzone.mobile`, with read-only app/app-quality access and permission
to release apps to testing tracks. It has no production, financial, policy, store-listing, tester-list, or
user-management permission.

The complete private service-account JSON is stored in Bitwarden as
`GOOGLE_PLAY_SERVICE_ACCOUNT_JSON`. The repository variable `BITWARDEN_MOBILE_BUILD_SECRETS` maps that Bitwarden
secret ID to the same output name for `publish-android-google-play.yml`. Do not reuse
`PushNotifications__Fcm__ServiceAccountJson`: FCM delivery and Play publishing are different identities with
different permissions.

To rotate the publisher credential, create a new JSON key on the same service account, replace the Bitwarden
secret value without changing its secret ID, then run the Google Play workflow from `main`. Delete the previous
Google Cloud key only after an internal-track upload succeeds. Remove every temporary downloaded JSON file after
Bitwarden is verified; never add the JSON directly to GitHub Actions secrets or the repository.


## Rotation and break-glass (App Service settings)

[ADR 0008](decisions/0008-app-service-settings-ownership.md) keeps App Service application settings outside
OpenTofu (Option A of [#618](https://github.com/richardorchard/QueenZone.Modern/issues/618)), so rotation and
break-glass for those settings stay entirely inside the Bitwarden/Azure/GitHub workflow described here — there is
no OpenTofu apply/plan step to run or wait on.

**Normal rotation** (Bitwarden reachable): follow AGENTS.md's Bitwarden section — update the value in Bitwarden
first, then update the same key in Azure App Service configuration (portal or `az webapp config appsettings set`),
then restart/verify. Bitwarden and the live App Service value are two separate stores; updating one never updates
the other automatically, except for `MobileAuth__SigningKey`: the next web deployment reconciles that key from
Bitwarden before deploying. Apply it to App Service directly when the rotation must take effect immediately.
Verify by setting name and value length only, never by printing the value.

**Break-glass** (Bitwarden Secrets Manager unreachable, or the `BWS_ACCESS_TOKEN` / `github-actions-queenzone`
machine account is unavailable):

1. An operator with Azure portal/CLI access to `Queenzone-RG` may set an App Service setting directly
   (`az webapp config appsettings set --name queenzone-prod --resource-group Queenzone-RG --settings
   KEY=VALUE`), bypassing Bitwarden for that one change. This is the same access path
   `.github/workflows/deploy.yml`'s `configure-app-settings` job already uses via OIDC — no new credential to
   provision.
2. Restart the App Service (or let the next deploy's warmup do so) and confirm the app comes up healthy.
3. As soon as Bitwarden access is restored, write the same value back into the `Queenzone Development` project so
   Bitwarden remains the source of truth for automation/local recovery — a break-glass change that never gets
   reconciled back into Bitwarden will silently drift on the next rotation.
4. Record what changed (setting name and reason, never the value) in the PR or issue tracking the incident that
   forced the break-glass path, so `docs/architecture/opentofu-inventory.md`'s setting-name inventory can be
   updated if a name was added or removed. `scripts/Test-AppServiceSettingNames.ps1`
   (`.github/workflows/app-service-setting-names-check.yml`) only checks that required names exist — it does not
   detect drifted values, so a break-glass value change is otherwise invisible to automation.

There is no multi-approver process for break-glass today — QueenZone has a single operator with both Bitwarden and
Azure access. If that changes, revisit this section.

## Migration notes

GitHub Actions secrets cannot be read back in plaintext after they have been saved. GitHub can list secret names and
overwrite secret values, but it cannot export their current values.

For this migration:

- `AZURE_WEBAPP_PUBLISH_PROFILE` was regenerated fresh from Azure (`az webapp deployment list-publishing-profiles
  --name queenzone-prod --resource-group Queenzone-RG --xml`) and written into Bitwarden, since the old GitHub secret
  value could not be recovered.
- The original Bitwarden migration reused `ConnectionStrings__QueenZoneLegacy`. After the #1272 cutover on
  **10 September 2026**, the GitHub mappings above moved to the Canada East publish profile and
  `ConnectionStrings__QueenZoneLegacyCanadaEast` secret. The Australia East credentials are rollback-only.

The old raw GitHub Actions secrets (`AZURE_WEBAPP_PUBLISH_PROFILE`, `QUEENZONE_LEGACY_MIGRATION_CONNECTION_STRING`)
were removed once the deploy workflow (`deploy-app-service.yml` at the time, since renamed to `deploy.yml`) was
verified end-to-end via a real deploy run using the Bitwarden-sourced values. The stale
`CONNECTIONSTRINGS__QUEENZONELEGACY` secret (unused by any workflow) was removed at the same time.
