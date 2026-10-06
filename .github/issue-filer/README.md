# Issue filer

Shared, deterministic issue filer for the weekly gardener (#1804) and telemetry triage (#1805). There is no LLM in this path. Neither loop opens pull requests.

## Layout

| Path | Role |
| --- | --- |
| `config.json` | Caps, labels, Sonar project, feature-map area prefixes, suppression patterns |
| `ignore.json` | Documented skip list (not a prompt) |
| `finding-rules.json` | Registered `qz-finding` rule ids (`id`, `title`, `level`, `check`) |
| `backfill/review-findings-60d.json` | Optional 60-day classified review comments at the `.github/issue-filer/` path. The weekly gardener reads this only when `lookback_days` is 60 or more |
| `scripts/issue-filer/review-findings-60d.json` | Classified 60-day review findings for #1802 AC3. Ingested only when `--ingest-findings` is passed |
| `scripts/issue-filer/` | Pure `planFilings` core, injected GitHub client, source collectors, templates |
| `scripts/issue-filer/telemetry.mjs` | Sentry/App Insights parse, route normalisation, correlation, dedupe keys |
| `.github/workflows/telemetry-triage.yml` | Daily poller at 07:17 Perth (26h lookback) that feeds the telemetry loop |

## Ignore list

`ignore.json` is an object with an `entries` array. Each entry:

```json
{
  "match": { "source": "review", "rule": "csharp.regex-timeout" },
  "reason": "Already tracked as a quality-profile change.",
  "owner": "richardorchard",
  "expires": "2027-01-01"
}
```

`reason` and `expires` are required. `owner` is optional. `match` may use `source`, `key`, `rule`, and/or `titleRegex`. An expired entry stops matching and is listed in the job summary so it can be removed.

Optional `maxUsers` and/or `maxEvents` are positive integers. While the matching candidate is at or under every evaluable ceiling it stays ignored. If either count goes past its ceiling, the ignore no longer applies and the normal `planFilings` path runs (file, or reopen/update the existing `sentry:<id>` issue — never a duplicate). The step summary then includes the reason, for example `ignore ceiling exceeded: users 2 > 1`. Entries without these fields behave exactly as before.

`--validate` rejects non-positive or non-integer ceiling values. It does not require a source that publishes those counts, because `match` can omit `source` and correlated telemetry candidates can carry Sentry `userCount` even when an App Insights key also matches. At runtime a ceiling whose count is absent on the candidate (typical App Insights `userCount`) is skipped, not treated as zero; the ignore still applies unless another evaluable ceiling is exceeded.

A Sentry entry may also use `recurrence` to suppress one reviewed occurrence.
This identity guard adds stricter conditions to the general ceilings; entries
without `recurrence` retain their existing ceiling behavior:


```json
"recurrence": {
  "eventId": "6eba4990b88349abacd535e88824c10e",
  "lastSeen": "2026-10-06T03:27:48Z",
  "issueNumber": 2147
}
```

This requires an exact `source: sentry` and `sentry:<issue-id>` key. The
baseline above comes from the original QUEENZONE-MOBILE-E alert and the
[triage of #2147](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/2147#issuecomment-6010359218): one event, one user on iOS build 53. It does not establish a watchdog root cause.

Suppression requires the latest event's documented `eventID` to equal the
baseline, the source issue's `lastSeen` to equal the recorded time, and valid
source counts of at most one event and one user. A different event ID, changed
last-seen time, or more than one event/user uses the normal escalation path.
Missing or invalid observations also stop suppression. The API's separate `id`
field is not substituted for `eventID`; timestamps are compared as instants.

The existing three status queries remain unchanged. When they omit an explicitly
configured recurrence issue, the collector makes one bounded GET of that issue
using the shared paced transport and existing token. Only unresolved issues enter
the existing lookback filter and latest-event collection; resolved or ignored
issues stay quiet. This catches an ongoing recurrence after the issue ages out of
`is:new`. A failed tracked read blocks filer writes through the existing collector
error path. No Sentry status, alert settings, or tokens are changed. [Sentry's event schema](https://docs.sentry.io/api/events/retrieve-an-issue-event/)
documents `eventID` and `dateCreated`. The [project issues API](https://docs.sentry.io/api/events/list-a-projects-issues/)
distinguishes top-level `count`/`userCount`/`lastSeen` from `stats[statsPeriod]`.
The [group serializer](https://github.com/getsentry/sentry/blob/master/src/sentry/api/serializers/models/group.py)
maps occurrence and distinct-user statistics to those top-level fields.
This guard does **not** calculate count deltas or assume lifetime/window
comparability: more than one observed event/user is incompatible with the one
reviewed occurrence in any scope. Returning to a count of one or zero cannot
hide a different event ID or changed last-seen time. Source observations remain
separate when correlation sums Azure and Sentry counts or merges their times.
The exact source match is unchanged: a correlated `source: telemetry` candidate
still follows the normal escalation path, so Azure evidence is not suppressed
by a Sentry-only entry. Its Sentry key still selects the canonical GitHub issue.

`issueNumber` pins the canonical GitHub issue, including after the suppression
expires. The loader reads that issue directly when label/lookback discovery misses
it, and a completed canonical issue may reopen beyond the usual 30-day limit.
The marker must contain the exact Sentry key. An unavailable/mismatched canonical
issue never produces a duplicate; lookup errors propagate before writes. Keep
this entry after expiry while its canonical mapping is needed. `not_planned`
closures, comment caps/cooldowns and collection-failure write blocking still apply.

## Dedupe

Every filed issue ends with:

```text
<!-- qz-filer v=1 keys=<k1>,<k2> source=<src> -->
```

The filer lists issues by its labels (open, plus closed in the last 90 days) and parses markers locally. It does not use GitHub search. A candidate matches when any key overlaps.

## Caps and silence

- Gardener: 2 new issues per week.
- Telemetry: 3 new issues per day.
- At most 10 comments per run.
- More than 5 eligible candidates in one run files a single storm issue instead.
- Caps are counted from issues the bot already filed (by label, since the period start). There is no state file.
- An empty plan writes no issue, no comment, and no mention — only the job summary.
- When something is filed or reopened, one comment on the pinned **Issue filer log** issue mentions `@richardorchard`.

## Dry run

`node scripts/issue-filer/run.mjs --dry-run` prints the plan and writes nothing. Manual `workflow_dispatch` on `.github/workflows/gardener.yml` defaults to dry-run. The Monday 00:00 UTC schedule (08:00 Perth) runs live.

Telemetry triage (`.github/workflows/telemetry-triage.yml`) is a scheduled poll daily at 07:17 Perth plus `workflow_dispatch`. It shares `concurrency: issue-filer` with the gardener. **Scheduled runs default to dry-run** (they log the plan and file nothing) unless repository variable `TELEMETRY_TRIAGE_FILE_ISSUES` is exactly `true`. Dispatch uses the `dry_run` input (default true). Sentry auth is `SENTRY_TRIAGE_TOKEN` from Bitwarden via `BITWARDEN_TELEMETRY_TRIAGE_SECRETS` — never `SENTRY_AUTH_TOKEN`. App Insights reads fired `qz-prod-*` alerts through the `telemetry-read` OIDC identity; zero fired alerts is a clean no-op. A login, Resource Graph, or KQL failure is written as a workflow `::warning::` and a filer-summary warning so it cannot look like that no-op. External Sentry/App Insights text is redacted before it reaches issue titles, comments, or logs. When a later signal matches either key of an existing issue, the new key is appended to the marker and a comment is added.

The weekly job uses a 7-day lookback. It does **not** ingest classified historical findings or classify untagged review comments. For #1802 AC3, dispatch `ingest_findings=true`, `max_issues=2`, and `dry_run=true` first (then the same inputs with `dry_run=false`). Ingested findings use the same dedupe, caps, ignore list, and templates as live candidates, and they are not collapsed into a storm issue when they fit under the cap.

SonarCloud credentials stay out of git. The gardener reads `SONAR_ORGANIZATION` and `SONAR_PROJECT_KEY` from the environment (repository variables) and skips the Sonar source when either is unset.

## Local commands

```bash
node scripts/issue-filer/run.mjs --validate
node scripts/issue-filer/run.mjs --loop telemetry --lookback-hours 2 --max-issues 3 --dry-run
node --test $(find scripts -name '*.test.mjs' | sort)
```
