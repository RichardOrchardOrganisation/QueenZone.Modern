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
