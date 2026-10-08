# Updating a project’s standards

Run the update command from a clean, reviewed checkout of the **new standards kit**, passing the receiving project’s absolute path. The project must already have `development-standards.lock.json` from installation/adoption. Create an update branch in that project first.

The updater compares three versions: the standards commit recorded in the project’s lock, the project’s current files, and the standards clone’s checked-out commit. It uses the committed snapshots, never an uncommitted source version. It does not fetch, commit, push, enable workflows, or change the project’s Git branch.

## Preview and apply

In the standards clone, fetch the available history and select the commit or tag you have reviewed:

```sh
git fetch origin
git checkout <reviewed-commit-or-tag>
node scripts/update.mjs --target "/absolute/path/to/MyProject" --dry-run
node scripts/update.mjs --target "/absolute/path/to/MyProject"
```

Replace the placeholder with a real version. These commands are run in the kit, not the receiving project. Windows PowerShell example:

```powershell
node scripts/update.mjs --target "C:\Projects\MyProject" --dry-run
node scripts/update.mjs --target "C:\Projects\MyProject"
```

Dry-run performs the same comparisons and reports proposed adds, updates, removals, and merges without changing files. Conflicts return a nonzero exit status. On an actual run with unresolved conflicts, **no project files or version lock are changed**, including files that could have updated cleanly. Conflict markers are never written into project files.

Before applying, finish or commit unrelated target work so the resulting diff is easy to review. The updater can account for local edits, but do not run it concurrently with another process editing the same files. Filesystem/permission errors during application are reported; the lock advances last, and an interrupted write must be inspected before retrying.

## What gets updated

| Situation | Behavior |
| --- | --- |
| Only the standards changed a managed file | Apply the incoming version |
| Only the project changed a file | Preserve the project’s version |
| Both changed different lines | Attempt a Git three-way text merge |
| Both changed overlapping lines | Report a conflict and stop before writes |
| New standards file has no local collision | Add it |
| Removed standards file is unchanged locally | Remove it |
| Removal collides with edits, or a new file collides with project content | Report a conflict |
| Project deleted a file that standards did not change | Preserve that deletion |
| Project has other, unmanaged files | Leave them untouched |

Only the marked shared section of `AGENTS.md` is merged. Project guidance before and after it stays intact. Missing, duplicate, or malformed markers require manual repair. Existing CRLF line endings are retained for updated local files.

Configuration uses the same three-way comparison. For example, a locally changed coverage floor survives an unrelated upstream settings change. If the shared default and the local floor both change on the same line, review that conflict rather than allowing the updater to pick a value. Text merging cannot establish semantic compatibility; inspect configuration and run the application’s checks afterward.

The installation manifest defines managed source/destination paths for each standards commit. The updater also recognizes the known initial v0.1 installer, so projects installed before manifests were introduced can update. Unknown legacy installers, unsupported manifests, unsafe paths, symlinked destinations, dirty source checkouts, and invalid locks are rejected.

## Resolve a conflict

The command lists the conflicting relative paths and reasons. Review the project’s local version and the old/new standards versions. You can inspect a source file in the standards clone with:

```sh
git show <installed-commit>:<source-path>
git show HEAD:<source-path>
```

The installed commit comes from the project’s lock. Source paths are listed in `installation-manifest.json`; for example, `.github/pull_request_template.md` comes from `templates/pull_request_template.md`, and the managed agent section comes from `templates/AGENTS.fragment.md`.

To accept an incoming file, deliberately replace/merge the target content after review and rerun the preview. To retain a reviewed conflicting local file, name it explicitly:

```sh
node scripts/update.mjs --target "/absolute/path/to/MyProject" --dry-run --keep-local config/typescript-coverage.json
node scripts/update.mjs --target "/absolute/path/to/MyProject" --keep-local config/typescript-coverage.json
```

Repeat `--keep-local` for additional reviewed paths. It uses repository-relative destination paths, including `AGENTS.md` for its managed section. Unknown paths are rejected. This option resolves only conflicts; it does not pin a file against every future update. Kept conflicting paths are recorded in the new lock’s `keptLocal` list for that update.

After a manual conflict merge that deliberately differs from the incoming version, Git may still report overlap against the old baseline. Review the result and use `--keep-local` for that resolved path if appropriate. There is no blanket force-overwrite or accept-all-local option. Advancing the lock acknowledges the reviewed result as the project’s local customization of the new baseline.

## Review and publish the project update

After a successful update, return to the receiving project:

```sh
git diff
node scripts/verify.mjs --profile dotnet --base-ref origin/main
```

Use the applicable profile(s) and comparison base, fetching the base first if needed. Run project-specific browser/device proof and safe provider probes. Review changed checks, rules, thresholds, exceptions, and any automatic text merges. CI examples remain under `examples/`; changes there do not alter workflows that were copied into `.github/workflows/`. Port those changes deliberately.

Commit the changed files and `development-standards.lock.json` together, then open an update PR. Include the old/new source commits, any retained local conflicts, and checks run. A repeated update to the same version is a no-op; local customizations remain in place.

## Missing history or provenance

If the installed commit is not present in the standards clone, the updater stops. Fetch its history in the **standards clone**. A shallow clone may require `git fetch --unshallow origin`; a commit from a deleted/squashed feature branch may require fetching that exact available commit/ref. If the commit can no longer be recovered, use reviewed manual migration. Do not invent a base commit or edit the lock to bypass the check.

If a project adopted files manually, confirm that its lock points to the source commit actually used and that the managed agent markers are present. An existing lock is not enough to establish provenance if it was copied from another project or a different kit version.

## Improving the kit from QueenZone or another project

Fix and verify the concrete problem in the application first. If the improvement applies elsewhere, port it into this standards repository with regression tests, review it, then use the updater to bring the approved version back into consuming projects. Keep application architecture, database connections, measured floors, feature maps, and deployment details local.
