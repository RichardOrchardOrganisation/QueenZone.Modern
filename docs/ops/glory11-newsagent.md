# Glory11 News Agent

Run the News Agent worker from the **prod tag** GitHub Release zip, not a git checkout. DEV-only commits on `main` never run against production news.

This page is the one-time setup for Richard. **Do not change the Glory11 scheduled task until Richard approves it.**

Related: issue #2386, [news-agent-scheduling.md](../architecture/news-agent-scheduling.md), [release notes](../release/README.md).

## What the release publishes

Pushing a `vYYYY.MM.DD.N` tag still creates the GitHub Release (issue #2379). The same non-blocking notes workflow then publishes `QueenZone.NewsAgent.Worker` (Release, win-x64, framework-dependent) and attaches:

```text
newsagent-<tag>.zip
```

The zip includes `version.txt` with that tag. Unreleased draft refreshes and notes backfills do **not** attach a zip.

Until the next prod tag after this lands, attach a zip by hand from the tagged commit:

```powershell
dotnet publish src/QueenZone.NewsAgent.Worker/QueenZone.NewsAgent.Worker.csproj `
  --configuration Release --runtime win-x64 --self-contained false `
  --output $env:TEMP\newsagent-publish
Set-Content -LiteralPath $env:TEMP\newsagent-publish\version.txt -Value 'vYYYY.MM.DD.N'
Compress-Archive -Path $env:TEMP\newsagent-publish\* -DestinationPath "newsagent-vYYYY.MM.DD.N.zip"
gh release upload vYYYY.MM.DD.N "newsagent-vYYYY.MM.DD.N.zip"
```

## One-time machine setup

1. Install the **.NET 10** win-x64 runtime (framework-dependent publish, not self-contained).
2. Install [GitHub CLI](https://cli.github.com/) (`gh`) on the scheduled-task account PATH. The wrapper can use the REST API instead, but `gh` is the preferred download path.
3. Create a fine-grained PAT (or classic token) with **Contents: Read** on this repository. Store it as a user environment variable `GH_TOKEN` for the scheduled-task account, or as a Task Scheduler environment. Never commit it and never put it in the script. The wrapper reads the repo from `-Repository`, `GITHUB_REPOSITORY`, or `gh repo view`.
4. Create the install root, for example `C:\QueenZone\NewsAgent`. Keep local config there, not inside a git worktree:

   ```text
   C:\QueenZone\NewsAgent\appsettings.Local.json
   C:\QueenZone\NewsAgent\versions\<tag>\
   C:\QueenZone\NewsAgent\current          (junction to versions\<tag>)
   C:\QueenZone\NewsAgent\current.txt
   C:\QueenZone\NewsAgent\newsagent.log
   ```

5. Copy `src/QueenZone.NewsAgent.Worker/appsettings.Local.json.example` to `C:\QueenZone\NewsAgent\appsettings.Local.json` and set `OpenRouter:ApiKey`. Leave the database connection empty. The scheduled task should keep loading Canada East SQL through `scripts/Import-NewsAgentProductionConnection.ps1` (`BWS_ACCESS_TOKEN` on that account, same as today).
6. Keep a checkout of this repo only for the wrapper scripts (`scripts/glory11/Run-NewsAgent.ps1` and `Import-NewsAgentProductionConnection.ps1`). The worker binaries come from the release zip.

## First download (before changing the task)

From an elevated or scheduled-task-account prompt:

```powershell
$env:GH_TOKEN = [Environment]::GetEnvironmentVariable('GH_TOKEN', 'User')
pwsh -NoProfile -File C:\path\to\QueenZone.Modern\scripts\glory11\Run-NewsAgent.ps1 `
  -InstallRoot C:\QueenZone\NewsAgent `
  -LoadProductionConnection `
  -- process-news-requests --runner-id glory11
```

Windows PowerShell 5.1 is also supported (`powershell.exe`). Confirm `current\version.txt` matches the latest prod tag and `newsagent.log` records that version plus the exit code.

## Scheduled task (Richard only)

The current task still runs `scripts/Run-NewsAgentDiscovery.ps1` from a stale checkout. After the first zip exists and a manual run works, change the actions to:

**Gathering** (example, keep the existing 6-hour trigger):

- Program: `powershell.exe`
- Arguments: `-NoProfile -ExecutionPolicy Bypass -File "C:\path\to\QueenZone.Modern\scripts\glory11\Run-NewsAgent.ps1" -InstallRoot C:\QueenZone\NewsAgent -LoadProductionConnection -- discover-news --scheduled`
- Start in: `C:\QueenZone\NewsAgent`

**Queued requests** (example, keep the existing hourly trigger):

- Program: `powershell.exe`
- Arguments: `-NoProfile -ExecutionPolicy Bypass -File "C:\path\to\QueenZone.Modern\scripts\glory11\Run-NewsAgent.ps1" -InstallRoot C:\QueenZone\NewsAgent -LoadProductionConnection -- process-news-requests --runner-id glory11`
- Start in: `C:\QueenZone\NewsAgent`

The wrapper:

- Selects the newest **published** release whose tag matches `vYYYY.MM.DD.N` (not drafts, prereleases, `unreleased`, or `main`).
- Downloads only when that tag differs from `current\version.txt`.
- On download failure, logs a warning and runs the already-installed version.
- Copies `appsettings.Local.json` from the install root into the version directory.
- Logs the version and worker exit code on every run, and returns that exit code.

Older zips stay under `versions\`. To pin an older build, retarget `current` at that folder and write the tag into `current.txt`.

## Follow-up

Not in this change:

- A `.sha256` asset or `actions/attest-build-provenance` for the zip
- Pin the publish SDK with `global.json` (upload no longer uses `--clobber`)
- A real pin switch so the next run does not replace an intentionally older `current`
