# Dev curated snapshot

Issue [#1325](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1325)
defines the isolated dev data contract. Dev uses a small, sanitised production
sample. It is not a production clone.

## Safety boundary

The refresh has four separate credentials:

| Credential | Required scope |
| --- | --- |
| `DEV_SNAPSHOT_SOURCE_SQL_READONLY` | `SELECT` and schema inspection on `queenzone-db`; no insert, update, delete, execute-as-owner, or DDL rights |
| `DEV_SNAPSHOT_TARGET_SQL` | schema and data writes on `queenzone-dev-db` only |
| `DEV_SNAPSHOT_SOURCE_BLOB_READONLY` | account SAS for production Blob Storage with service `b`, resource types needed for list/read, and permissions exactly `rl` |
| `DEV_SNAPSHOT_TARGET_BLOB` | read/write/delete on `queenzonedev` only |

The tool checks the SQL database names and rejects database- or object-level
mutation, DDL/control, or stored-procedure execution rights on production. It
also rejects source Blob account keys or write-capable SAS permissions and
requires the target storage account name `queenzonedev`.
Application runtime credentials are separate. The dev app receives only the dev
SQL and Blob connection strings.

Store the credentials and two generated synthetic-account passwords in the
`Queenzone Development` Bitwarden Secrets Manager project. Map them through the
repository variable `BITWARDEN_DEV_SNAPSHOT_SECRETS` to the six snapshot names
used by `.github/workflows/refresh-dev-snapshot.yml`, plus the existing
`SIXLABORS_LICENSE_KEY` needed to build the refresh tooling. Do not reuse the
production App Service connection string as the read-only source credential.

Protect the `dev-data-refresh` GitHub environment with a required reviewer,
then set repository variable `DEV_SNAPSHOT_APPROVAL_GATE_CONFIGURED=true`.
The workflow fails before secrets or cloud access when that explicit setup flag
is absent. It also requires the operator to type `REFRESH DEV SNAPSHOT`.

The environment uses the same narrowly scoped Azure identity as `dev-deploy`.
Copy its `ARM_CLIENT_ID`, `ARM_TENANT_ID`, and `ARM_SUBSCRIPTION_ID` environment
variables to `dev-data-refresh`, then add one federated credential to the
`QueenZone Dev Deploy` Entra application with subject
`repo:RichardOrchardOrganisation@333232587/QueenZone.Modern@1265145026:environment:dev-data-refresh`. Do not add
or widen an Azure role: the existing Website Contributor assignment on
`queenzone-devbox` is sufficient. The four isolated SQL and Blob credentials,
not this identity, perform the snapshot data work.

## What is selected

Selection rules live in [`config/dev-snapshot.json`](../../config/dev-snapshot.json).
Small public archive/reference tables are copied in full. Larger dated content
uses newest-first limits from the checked-in configuration. Tables with mixed
public/private columns use explicit projections:

- the newest 500 published `NEWS_T` rows and newest 500 published rows from each legacy article
  source are copied. Editorial article overlays for selected legacy articles
  are retained, plus the newest 500 other editorial records. Editor emails are
  replaced.
- `FREDDIE_T.Email` and `Q_STAGE_T.CONTACT` are cleared.
- only referenced `USERS_T` rows are copied; password, email, IP, birth date,
  profile, contact, and transfer fields are cleared or replaced.
- only `MemberAccounts` referenced by sampled forum content are copied; email,
  password hashes, avatars, moderation actor emails, and recovery fields are
  cleared or replaced.
- external logins, auth grants, private messages, mailing lists, IP/security
  data, submissions, tokens, operational queues, and audit rows remain empty.

Forum selection is deterministic, newest-first, and relationally complete. It
selects 500 visible threads by latest activity, reserving space for the newest
thread in every public category, forum topics linked from selected news, and the known public
forum-guidelines topic used by smoke tests. Remaining places are filled by the
newest threads globally. Each chosen thread brings all posts, stored attachment
rows, poll definitions, and required authors. Production's current
`ModernForumThread` schema has no persisted lock column, so lock behaviour
remains covered by synthetic application tests until that state is persisted.

Photo candidates are the newest records in every public category.
Only rows whose original and thumbnail blobs exist and fit the budget are
loaded. Gallery pictures referenced by news are mandatory. Blob references from
sampled forum posts and published editorial content are also mandatory.

`SearchDocument` is never copied. The search worker rebuilds it from the loaded
snapshot.

The two synthetic accounts are `admin@dev.queenzone.invalid` and
`member@dev.queenzone.invalid`. Their generated passwords are separate
Bitwarden secrets. The workflow adds only the synthetic admin address to the
dev allowlist; no production credential or external-login identifier is used.

## Limits

- database used space after migrations and search rebuild: **1,536 MB**
- newest forum threads: **500**, including all posts for each thread
- newest news articles: **500**
- newest rows from each legacy article source: **500**
- newest photos per public category: **100**
- gallery originals and thumbnails: **500 MB**
- forum attachments: **500 MB**

The checked-in config owns these values. A refresh fails before the app is
connected when any limit, privacy rule, required-table check, category coverage
check, or relationship check fails.

## Workflow

`deploy-dev.yml` applies pending EF migrations to `queenzone-dev-db` on every
qualifying push (EF paths changed), whether or not the curated snapshot has
been refreshed (#1600). `DevSnapshot__Ready` only controls which data the app
serves, so dev proves migrations ahead of production. A refresh is an optional
reset, not a prerequisite for a current schema.

Run **Refresh dev curated snapshot** manually. It performs these stages:

1. set App Service `DevSnapshot__Ready=false` and remove the dev database setting;
2. extract a schema-only DACPAC from production;
3. publish the production-compatible schema to `queenzone-dev-db`, excluding
   the known broken legacy views;
4. select, sanitise, and stream curated rows to dev;
5. reconcile dev Blob Storage to the generated manifest, deleting obsolete
   objects and using bounded-parallel Azure server-side copies for changed
   objects;
6. seed synthetic password accounts;
7. apply current EF migrations;
8. rebuild `SearchDocument`;
9. enforce size, privacy, relationship, content, and Blob guards;
10. connect the dev-only SQL and Blob settings;
11. run readiness/public-route smoke plus read-only browser/API journeys;
12. set App Service `DevSnapshot__Ready=true`, allowing future `deploy-dev.yml`
    runs to migrate and retain the verified snapshot, then repeat public-route
    smoke after the resulting App Service restart.

The workflow uploads only `summary.json` for 30 days. It does not upload the
full manifest because forum filenames may contain user-supplied text. Logs and
artifacts never contain connection strings, SAS tokens, passwords, email
addresses, or account keys.

Blob copies use per-object, read-only source SAS URLs that expire after four
hours. The target storage service reads each changed object directly from the
source account; blob content does not pass through the Actions runner. Target
metadata records a hash of the source ETag, so later refreshes skip unchanged
objects while the final inventory check still requires exact names, byte sizes,
source versions, and object count. The first refresh after this mechanism is
introduced copies every selected object once to establish those markers.

On any failure, the final handler removes the database connection and leaves
the public dev site on deterministic sample data. A partial database or Blob
load is never connected to the app.

## Local equivalent

Load the six environment variables from Bitwarden without printing them. Review
the boundaries first:

```powershell
./scripts/Import-SixLaborsLicense.ps1
./scripts/Refresh-DevSnapshot.ps1
```

After approval, run:

```powershell
./scripts/Refresh-DevSnapshot.ps1 -Apply
```

The local script does not change App Service settings. Connecting the resulting
snapshot and running live smoke remains an explicit operator action. Prefer the
approval-gated workflow for the real refresh.

## Operational checks

Record the summary artifact, workflow run, selected forum/post/photo counts,
database size, and Blob totals for each refresh. Before and after counts on
production remain unchanged by construction because both source credentials are
read-only; investigate any permission-boundary failure instead of bypassing it.

After connection, verify `/health/ready`, `/`, `/news`, `/articles`,
`/photography`, `/forum`, the sampled forum-guidelines topic, `/search`,
`/api/v1`, and content list/detail APIs. The workflow's Playwright checks must
also show sampled photography assets resolving from
`queenzonedev.blob.core.windows.net`, never a production Blob origin.
