# Issue filer

Shared, deterministic issue filer for the weekly gardener (#1804) and, later, telemetry triage (#1805). There is no LLM in this path. The gardener never opens pull requests.

## Layout

| Path | Role |
| --- | --- |
| `config.json` | Caps, labels, Sonar project, feature-map area prefixes, suppression patterns |
| `ignore.json` | Documented skip list (not a prompt) |
| `finding-rules.json` | Registered `qz-finding` rule ids (`id`, `title`, `level`, `check`) |
| `backfill/review-findings-60d.json` | Optional 60-day classified review comments. Created by a separate one-off run; the weekly gardener does not write or require it |
| `scripts/issue-filer/` | Pure `planFilings` core, injected GitHub client, source collectors, templates |

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

The weekly job uses a 7-day lookback. It does **not** run the 60-day backfill or classify untagged review comments. Dispatch `lookback_days=60` and `max_issues=3` later, after the backfill file exists, for #1802 AC3.

SonarCloud credentials stay out of git. The gardener reads `SONAR_ORGANIZATION` and `SONAR_PROJECT_KEY` from the environment (repository variables) and skips the Sonar source when either is unset.

## Local commands

```bash
node scripts/issue-filer/run.mjs --validate
node --test $(find scripts -name '*.test.mjs' | sort)
```
