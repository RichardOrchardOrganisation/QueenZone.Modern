# Maintenance Jobs Scheduling

Operational guide for the retention and cleanup jobs that used to run as `BackgroundService`s inside the production web app (issue #1677). They now run from `src/QueenZone.Maintenance.Worker` on the operator machine, the same hosting model as the news agent (`news-agent-scheduling.md`, Option D).

## Why they left the web process

The gallery orphan sweep lists every blob in every gallery container and compares it with `PIC_FILES_T`. On the single B1 worker that competed with page requests for CPU, memory, and sockets. The first run after each deploy also landed near the App Service container start probe (#666), so every job waited 5 minutes. A deploy or crash reset each timer, so a retention pass could slip.

## Jobs and schedule

| Job | Selector | What it does | Interval |
|-----|----------|--------------|----------|
| Member account deletion | `member-account-deletion` | Purges accounts 30 days after a scheduled deletion request, retries unfinished avatar blob deletes (including immediate deletions) | 6 hours |
| Gallery orphan sweep | `gallery-orphan-sweep` | Reports (or, with `GalleryOrphanSweep:DryRun=false`, deletes) gallery blobs with no `PIC_FILES_T` row that are older than the grace period | 6 hours |
| Private-message report purge | `private-message-report-purge` | Deletes reports 180 days after a terminal status (ADR 0015) | 24 hours |
| Fan-performance submission purge | `fan-performance-submission-purge` | Deletes pending audio for Rejected/Withdrawn submissions after 30 days | 24 hours |

`six-hourly` runs the first two, `daily` the last two, and `all` runs all four. The job classes are unchanged: the worker and the web hosted services both call `MaintenanceJobs` (`src/QueenZone.Web/Maintenance/MaintenanceJobs.cs`), which calls the existing purge and sweep services.

Each job runs in its own DI scope. A failing job is logged and the remaining jobs still run. The exit code is `1` when any job failed, so Task Scheduler shows the run as failed.

The gallery sweep streams each container listing page by page (`IGalleryPhotoBlobService.ListBlobsAsync` returns `IAsyncEnumerable`) instead of holding a whole container in memory.

## Web host behaviour

`MaintenanceJobs:RunInWebHost` controls the four hosted services:

| Environment | Value | Effect |
|-------------|-------|--------|
| Production, dev App Service, E2E | unset (`false`) | Hosted services are not registered |
| Development (`appsettings.Development.json`) | `true` | Hosted services run in-process so local runs still purge |
| Testing | unset (`false`) | Quiet; tests start a hosted service or call `MaintenanceJobs` directly |

Set `MaintenanceJobs__RunInWebHost=true` as an App Setting only to fall back to in-process jobs, for example while the operator machine is unavailable for a long time.

### Apple token revocation stays in the web app

Account deletion queues Sign in with Apple refresh-token revocations. Those tokens are encrypted with the web app's Data Protection key ring, which lives on the App Service file system (`DataProtectionBootstrap`), so the worker cannot decrypt them. The worker composition omits `AppleAccountTokenService`. When Apple sign-in is configured and the jobs are not in the web host, the web app registers `AppleTokenRevocationHostedService` instead. It revokes pending tokens every 6 hours, after the same 5 minute startup delay, and does no blob or sweep work. Immediate deletions still revoke in the request, as before.

## Worker entry point

```powershell
dotnet run --project src/QueenZone.Maintenance.Worker -- run-jobs six-hourly
dotnet run --project src/QueenZone.Maintenance.Worker -- run-jobs daily
dotnet run --project src/QueenZone.Maintenance.Worker -- run-jobs gallery-orphan-sweep
```

Configuration comes from `appsettings.json`, then `appsettings.Local.json` (git-ignored), then environment variables. The worker needs **both** `ConnectionStrings:QueenZoneLegacy` and `ConnectionStrings:BlobStorage`, or neither for in-memory sample data. It refuses to start with only one. Real SQL with in-memory blobs would mark avatar deletes complete without deleting anything. Real blobs with sample SQL would treat live photos as orphans.

`GalleryOrphanSweep:DryRun` defaults to `true` in the worker's `appsettings.json`, matching production today. Turn deletion on by setting `GalleryOrphanSweep__DryRun=false` for the scheduled task, not on the App Service.

## Windows Task Scheduler (operator machine)

`scripts/Run-MaintenanceJobs.ps1 -Schedule <selector>` loads `ConnectionStrings__QueenZoneLegacyCanadaEast` and `ConnectionStrings__BlobStorageCanadaEast` from the `Queenzone Development` Bitwarden project on every run (`Import-NewsAgentProductionConnection.ps1 -IncludeBlobStorage`). It checks that they target `queenzone-prod-sql` / `queenzone-db` and the `queenzoneprod` storage account. It fails before starting the worker if Bitwarden is unavailable or a secret is wrong. Use `-SampleData` for a local smoke test without Bitwarden.

Smoke-test first:

```powershell
scripts/Run-MaintenanceJobs.ps1 -Schedule all -SampleData
scripts/Run-MaintenanceJobs.ps1 -Schedule gallery-orphan-sweep
```

Create two tasks with the same account as the news tasks (it already has `BWS_ACCESS_TOKEN`):

| Task | Trigger | Arguments |
|------|---------|-----------|
| `QueenZone Maintenance - 6 Hourly` | Daily, repeat every 6 hours indefinitely | `-NoProfile -ExecutionPolicy Bypass -File "C:\path\to\QueenZone.Modern\scripts\Run-MaintenanceJobs.ps1" -Schedule six-hourly` |
| `QueenZone Maintenance - Daily` | Daily at a quiet hour | `-NoProfile -ExecutionPolicy Bypass -File "C:\path\to\QueenZone.Modern\scripts\Run-MaintenanceJobs.ps1" -Schedule daily` |

For both: **Program** `powershell.exe`, **Start in** the repository root, **Settings** stop the task if it runs longer than 2 hours, and if already running, **Do not start a new instance**. The two tasks run different jobs, and every job is idempotent, so a late or repeated run is safe.

PowerShell equivalent:

```powershell
$repo = 'C:\path\to\QueenZone.Modern'
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 2) -StartWhenAvailable
foreach ($task in @(
    @{ Name = 'QueenZone Maintenance - 6 Hourly'; Schedule = 'six-hourly'; Trigger = New-ScheduledTaskTrigger -Once -At '00:30' -RepetitionInterval (New-TimeSpan -Hours 6) },
    @{ Name = 'QueenZone Maintenance - Daily'; Schedule = 'daily'; Trigger = New-ScheduledTaskTrigger -Daily -At '03:30' })) {
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -WorkingDirectory $repo `
        -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$repo\scripts\Run-MaintenanceJobs.ps1`" -Schedule $($task.Schedule)"
    Register-ScheduledTask -TaskName $task.Name -Action $action -Trigger $task.Trigger -Settings $settings
}
```

When the machine is off, jobs wait until it is back (`-StartWhenAvailable` runs a missed trigger at the next opportunity). Nothing on the public site depends on them running on time. Retention is measured from the stored timestamps, not from the last run.

## Related

- `news-agent-scheduling.md` — the hosting model this mirrors
- `member-account-deletion.md` — account deletion and receipts
- `blob-storage-ugc.md` — gallery blob layout
- `docs/decisions/0015-private-message-report-retention-and-audit.md` — report retention
