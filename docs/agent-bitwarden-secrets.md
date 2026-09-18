# Bitwarden Secrets Manager for local agents

Shared local secret store for development agents on Richard’s machines. **This is not the Bitwarden password vault.**

| Tool | Product | Used for QueenZone agents? |
| --- | --- | --- |
| **`bws`** | Bitwarden **Secrets Manager** CLI | **Yes** — project secrets, machine access tokens |
| **`bw`** | Bitwarden **password manager** CLI | **No** for QueenZone App Service-style secrets (agents should not use `bw login` for this) |

## Shared project

| Field | Value |
| --- | --- |
| Secrets Manager project name | `Queenzone Development` |
| Project id | `1c16fd2d-4bfb-4eb7-8357-b49400233490` |
| Auth | User-scoped environment variable **`BWS_ACCESS_TOKEN`** (machine account access token) |
| Canonical secret keys | App Service names plus build secrets, e.g. `ConnectionStrings__QueenZoneLegacy`, `ConnectionStrings__BlobStorage`, `AzureAd__*`, `OPENROUTER_API_KEY`, `SIXLABORS_LICENSE_KEY` |

Do **not** commit the token, paste it into chat, or print secret **values**. Prefer reporting only key names and value **lengths** when verifying.

Bitwarden is a **local/recovery mirror**. Updating Bitwarden does **not** update Azure App Service or GitHub environment secrets.

## Machine accounts (platform-specific)

Create / use the machine account that matches the host OS. Each machine has its own access token stored as **user** environment variable `BWS_ACCESS_TOKEN` on that host.

| Host platform | Bitwarden machine account | Notes |
| --- | --- | --- |
| **Windows** (this workstation: user `me`, tools under `%USERPROFILE%\bin`) | `windows-codex` | Token: User env `BWS_ACCESS_TOKEN`. CLI: `%USERPROFILE%\bin\bws.exe` on PATH via User `Path` (`%USERPROFILE%\bin`). |
| **macOS** | `mac-codex` | Token: user env or shell profile (`~/.zprofile` / `~/.zshrc`). CLI on PATH (Homebrew or `~/bin`). |

Agents running on Windows must use the Windows machine token; agents on Mac must use the Mac machine token. Do not copy tokens between machines in git or chat.

### This Windows computer (current agent host)

Configured once for local agents (Grok, Codex, Claude, etc.):

1. **`BWS_ACCESS_TOKEN`** — User environment variable (not Machine, not Process-only). Length is typically ~90–100 characters; verify with presence/length only.
2. **`bws` CLI** — Installed at `%USERPROFILE%\bin\bws.exe` (e.g. `C:\Users\me\bin\bws.exe`), version **2.1.0** from [sdk-sm releases](https://github.com/bitwarden/sdk-sm/releases) (`bws-x86_64-pc-windows-msvc-*.zip`).
3. **User `Path`** — Includes `%USERPROFILE%\bin` so new shells find `bws` without a full path. **Existing** agent/IDE sessions may need a restart to pick up Path changes.

Quick check (PowerShell, do not print the token):

```powershell
[Environment]::GetEnvironmentVariable("BWS_ACCESS_TOKEN", "User") | ForEach-Object {
  if ($_) { "BWS_ACCESS_TOKEN: present len=$($_.Length)" } else { "BWS_ACCESS_TOKEN: MISSING" }
}
Get-Command bws | Select-Object Source
bws --version
```

### macOS hosts

1. Install `bws` (pick one):
   - Official install: `curl https://bws.bitwarden.com/install | sh` (see [Bitwarden Secrets Manager CLI](https://bitwarden.com/help/secrets-manager-cli/))
   - Or download the macOS zip from [sdk-sm releases](https://github.com/bitwarden/sdk-sm/releases) (`bws-macos-universal-*.zip` or arch-specific) into `~/bin` and ensure `~/bin` is on `PATH`.
2. Create/use machine account **`mac-codex`** and store its access token as **`BWS_ACCESS_TOKEN`** in **`~/.zprofile`** (not `~/.zshrc` — `.zprofile` is sourced by login shells, which is what GUI-launched agents and new Terminal windows both get; `.zshrc` alone is not enough):
   ```bash
   printf '\nexport BWS_ACCESS_TOKEN="paste-token-here"\n' >> ~/.zprofile
   source ~/.zprofile
   ```
3. Verify: `command -v bws && bws --version` and that `BWS_ACCESS_TOKEN` is set in the agent's process environment (see quick check below).

**This token has gone missing before** (found on 2026-08-05: `bws` was installed but `BWS_ACCESS_TOKEN` wasn't in `.zprofile`, `.zshrc`, `.zshenv`, `.profile`, or launchd's user environment — likely lost in a shell-profile reset or new-machine setup that didn't carry it over). If a fresh Claude/agent session on this Mac reports the token missing, don't assume it's still set somewhere unsearched — regenerate a new `mac-codex` token in the Bitwarden Secrets Manager web vault (Queenzone Development project → Machine accounts → `mac-codex`; old tokens can't be viewed again, only revoked) and re-run step 2 above. Agents should never try to read it out of Keychain or other credential stores directly — ask the user to set it and confirm.

**Quick check (macOS, do not print the token):**
```bash
if [ -n "$BWS_ACCESS_TOKEN" ]; then echo "BWS_ACCESS_TOKEN: present len=${#BWS_ACCESS_TOKEN}"; else echo "BWS_ACCESS_TOKEN: MISSING"; fi
bws --version
bws project list
```

## Common agent commands

Load the token into the **current** process, then call `bws` (works the same on Windows PowerShell and macOS once `bws` is on `PATH`):

```powershell
# Windows PowerShell / pwsh
$env:BWS_ACCESS_TOKEN = [Environment]::GetEnvironmentVariable("BWS_ACCESS_TOKEN", "User")
bws project list
bws secret list "1c16fd2d-4bfb-4eb7-8357-b49400233490"
```

```bash
# macOS / Linux shell
export BWS_ACCESS_TOKEN="${BWS_ACCESS_TOKEN:-$(printenv BWS_ACCESS_TOKEN)}"
# If only set in the login keychain/profile, ensure the agent shell sources it first.
bws project list
bws secret list "1c16fd2d-4bfb-4eb7-8357-b49400233490"
```

## Android test-build signing

The stable Android signing material used by
`.github/workflows/publish-android-google-play.yml` is stored in this project:

- `ANDROID_TEST_KEYSTORE_BASE64`
- `ANDROID_TEST_KEYSTORE_PASSWORD`
- `ANDROID_TEST_KEY_PASSWORD`

The repository variable `BITWARDEN_MOBILE_BUILD_SECRETS` maps those Bitwarden
secret IDs to workflow output names. GitHub Actions fetches them at runtime with
the existing `BITWARDEN_SECRETS_MANAGER_ACCESS_TOKEN`; do not copy the signing
values into separate GitHub secrets.

Do not rotate or recreate the key as routine maintenance. Android accepts an
in-place update only when the package identifier and signing key match the
installed app. If this key is lost or changed, uninstall the existing QueenZone
test app before installing the next APK.

## iOS CI runner status token

The unsigned iOS CI runner probe uses an `IOS_RUNNER_ADMIN_TOKEN` entry in
Bitwarden. It is a fine-grained GitHub PAT for this repository with
Administration read-only access, mapped by the repository variable
`BITWARDEN_IOS_RUNNER_SECRETS`. Rotate it in GitHub and Bitwarden together; if
it is absent or expired, CI safely chooses hosted `macos-26`.

## Six Labors build licence

ImageSharp 4 requires the complete Six Labors licence string during restore and
build. The canonical recovery copy is the `SIXLABORS_LICENSE_KEY` entry in the
`Queenzone Development` project. Never print the value, save `sixlabors.lic` in a
repository, or paste either credential into chat.

### Authorised Windows and macOS agents

This path is only for agents running on Richard-controlled machines whose
platform-specific Bitwarden machine account already has access:

```powershell
# Run from the repository root in Windows PowerShell or macOS pwsh.
# Dot-source the script so the environment value remains available to later commands.
. ./scripts/Import-SixLaborsLicense.ps1
dotnet restore QueenZone.sln
dotnet build QueenZone.sln --configuration Release --no-restore
```

The script reports only the key name and value length. It fails if `bws`,
`BWS_ACCESS_TOKEN`, project access, or the exact secret entry is missing.

Windows agents may persist the value for new IDE and agent processes:

```powershell
. ./scripts/Import-SixLaborsLicense.ps1 -PersistForUser
```

Restart existing IDEs, terminals, and agents afterward. On macOS, import the
licence into each new `pwsh` session. Do not write the licence value into
`.zprofile`, `.zshrc`, repository files, logs, or command output.

### Restricted macOS agent sessions

Some local agent sandboxes deny outbound DNS or local socket creation. In that
environment, `dotnet restore` can appear to hang while NuGet quietly retries
`api.nuget.org`, and parallel MSBuild/test workers can fail with
`System.Net.Sockets.SocketException (13): Permission denied` while creating a
named pipe. This is an agent execution-policy restriction, not a damaged .NET
installation or stale MSBuild server.

On the authorised Mac, agents should request the normal out-of-sandbox command
approval for restore/build/test, then import the licence and run the commands in
the same `pwsh` process:

```powershell
. ./scripts/Import-SixLaborsLicense.ps1
dotnet restore QueenZone.sln
dotnet build QueenZone.sln --configuration Release --no-restore
dotnet test QueenZone.sln --configuration Release --no-build
```

Do not weaken macOS security settings, persist the licence in a shell profile,
or treat repeated silent retries as a reason to delete NuGet caches. Confirm the
diagnosis first with a short network check such as
`curl -I https://api.nuget.org/v3/index.json`; a sandbox-only DNS failure should
be rerun with the agent's approved network access.

### Hosted agents and external contributors

- Hosted or cloud agents must receive `SIXLABORS_LICENSE_KEY` through that
  platform's encrypted secret manager before any automatic `dotnet restore`.
  Do not copy `windows-codex`, `mac-codex`, or another local Bitwarden machine
  token into a hosted environment.
- GitHub Actions reads the repository secret `SIXLABORS_LICENSE_KEY`.
- External contributors and untrusted forks are not authorised to use the
  QueenZone community licence. They must obtain their own licence from
  <https://licensing.sixlabors.com/> and configure it locally.
- If an environment cannot securely inject the licence, it cannot restore or
  build ImageSharp 4. Stop and ask the user to configure that environment.

### Renewal

Before the annual licence expires:

1. Replace `SIXLABORS_LICENSE_KEY` in the `Queenzone Development` Bitwarden project.
2. Replace the GitHub repository secret with the same name; GitHub secrets are a
   separate store and do not update from Bitwarden automatically.
3. Re-run `Import-SixLaborsLicense.ps1 -PersistForUser` on Windows machines that
   keep a persistent user value. Session-only Windows/macOS agents receive the
   new value on their next import.
4. Update any hosted-development environment secrets separately.
5. Verify by key name and value length only, then run restore and build. No Azure
   App Service runtime setting is required because enforcement occurs at build time.

Do not overwrite an existing secret blindly during renewal. Confirm the target
project, key name, and intended expiry first.

### Load legacy SQL connection for tools (never print the value)

The Windows NewsAgent scheduling scripts load `ConnectionStrings__QueenZoneLegacyCanadaEast`
automatically on every run. Use that same secret for tools targeting production;
`ConnectionStrings__QueenZoneLegacy` still contains the retired Australia East target.

```powershell
$env:BWS_ACCESS_TOKEN = [Environment]::GetEnvironmentVariable("BWS_ACCESS_TOKEN", "User")
$secrets = bws secret list "1c16fd2d-4bfb-4eb7-8357-b49400233490" --output json | ConvertFrom-Json
$cs = $secrets | Where-Object { $_.key -eq "ConnectionStrings__QueenZoneLegacyCanadaEast" } | Select-Object -First 1
if (-not $cs) { throw "ConnectionStrings__QueenZoneLegacyCanadaEast not found in Bitwarden project" }
$env:ConnectionStrings__QueenZoneLegacy = $cs.value
"loaded ConnectionStrings__QueenZoneLegacy len=$($cs.value.Length)"
# example: photo original-dimension inventory (issue #435)
# dotnet run --project src/QueenZone.Tools -- photo-dim-inventory
```

Related keys that may exist in the same project (names only): `ConnectionStrings__QueenZoneLegacyLive`, `ConnectionStrings__QueenZoneLegacyLocal`, `ConnectionStrings__BlobStorage`, `AzureAd__ClientSecret`, `OPENROUTER_API_KEY`, `QUEENZONE_SQL_EXPRESS_PROBE_PASSWORD`, `QUEENZONE_SQL_EXPRESS_PROBE_USERNAME`, etc.

`QUEENZONE_SQL_EXPRESS_PROBE_USERNAME` / `QUEENZONE_SQL_EXPRESS_PROBE_PASSWORD` are the paired SQL-login credentials (`queenzone_probe`) for connecting to the SQL Express mirror from a non-domain-joined host (e.g. the Mac runner reaching `glory11` over LAN, where Windows Integrated Security isn't usable) — see [issue #540](https://github.com/richardorchard/QueenZone.Modern/issues/540). The username secret was added 2026-08-05 after the password-only secret left the username undocumented anywhere; confirmed working from the Mac: `sqlcmd -S glory11 -U queenzone_probe -C -Q "SELECT 1"`.

### macOS SQL client tooling (sqlcmd)

Needed for any script that connects to the `glory11` SQL Express mirror from macOS (Windows Integrated Security doesn't apply here — use the `queenzone_probe` SQL login above instead):

```bash
brew tap microsoft/mssql-release https://github.com/Microsoft/homebrew-mssql-release
brew trust --taps microsoft/mssql-release   # required: Homebrew refuses third-party taps until trusted
HOMEBREW_ACCEPT_EULA=Y brew install msodbcsql18 mssql-tools18
```

**Use `HOMEBREW_ACCEPT_EULA`, not `ACCEPT_EULA`** — the wrong variable name doesn't error, it silently leaves the formula waiting on an interactive EULA prompt (`STDIN.gets`) that never resolves in a non-interactive shell, so the install just hangs indefinitely with no error output.

`sqlcmd` installs to `/opt/homebrew/opt/mssql-tools18/bin`, which isn't on `PATH` by default — add it in `~/.zprofile`:
```bash
export PATH="/opt/homebrew/opt/mssql-tools18/bin:$PATH"
```

Verify:
```bash
sqlcmd -S glory11 -U queenzone_probe -C -Q "SELECT 1 AS ok"
```

## Install / repair `bws` (Windows)

If `bws` is missing from PATH:

```powershell
$dest = Join-Path $env:USERPROFILE "bin"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
$zip = Join-Path $env:TEMP "bws-windows.zip"
# Pin or bump version from https://github.com/bitwarden/sdk-sm/releases (bws-v* tags)
$ver = "2.1.0"
Invoke-WebRequest -Uri "https://github.com/bitwarden/sdk-sm/releases/download/bws-v$ver/bws-x86_64-pc-windows-msvc-$ver.zip" -OutFile $zip
Expand-Archive -Path $zip -DestinationPath $dest -Force
# Ensure User Path contains %USERPROFILE%\bin (once)
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if (-not (($userPath -split ";") -contains $dest)) {
  [Environment]::SetEnvironmentVariable("Path", "$dest;$userPath", "User")
}
& "$dest\bws.exe" --version
```

Restart the IDE/agent terminal after changing User `Path`.

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| `bws` not recognized | CLI not installed or User `Path` not reloaded in this process |
| Auth / 401 from `bws` | `BWS_ACCESS_TOKEN` missing, wrong machine account for this OS, or expired token |
| `bw status` unauthenticated | Wrong CLI — use **`bws`**, not password-manager **`bw`** |
| Secret key not found | Wrong project id, or key renamed; re-list keys only (no values) |
| Agent on Mac can’t use Windows token | Expected — use `mac-codex` token on Mac hosts |
| ImageSharp reports a missing licence | Import `SIXLABORS_LICENSE_KEY` in the current process before restore/build; restart persistent Windows processes after renewal |
| macOS agent restore is silent after solution validation | Check access to `api.nuget.org`; restricted agent DNS causes quiet NuGet retry delays, so rerun with approved network access rather than clearing caches |
| macOS agent test reports `SocketException (13)` from `NamedPipeServerStream` | The sandbox denied MSBuild's local worker socket; rerun the test with approved out-of-sandbox execution |
| Hosted agent fails during automatic restore | Configure `SIXLABORS_LICENSE_KEY` in that platform's secret manager before startup; do not inject a local Bitwarden machine token |

## See also

- `AGENTS.md` — short agent rules
- `README.md` — local development overview
- `docs/agent-handoff-cheatsheet.md` — multi-session handoffs
- [Bitwarden Secrets Manager CLI help](https://bitwarden.com/help/secrets-manager-cli/)
