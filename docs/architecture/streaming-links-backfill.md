# Streaming Links Backfill

How admins fill `DiscographyStreamingLinks` (Spotify and Apple Music "Listen on" links) without
hand-searching every track. The loop is: **suggest, then review, then apply**. Nothing is applied
automatically. Queen's catalogue has many remasters, deluxe editions, live versions and
compilations, so every row needs a human decision.

Spec: [`docs/backlog/discography-streaming-links.md`](../backlog/discography-streaming-links.md).
Issues: #2181 (suggest) and #2182 (apply). Admin editing of single links is on `/admin/discography` (#2178).

## 1. One-time setup: Spotify credentials

Apple Music matching uses the public iTunes Search API, which needs no credentials.

Spotify needs a Web API app using the **Client Credentials** flow, which has no user sign-in.

1. Sign in at <https://developer.spotify.com/dashboard> with the QueenZone account and create an app.
   - The redirect URI is required by the form but unused. `http://127.0.0.1/` is fine.
   - APIs used: **Web API**.
   - Leave the app in **development mode**. Only admins run this tool locally, so the
     development-mode user cap does not matter, and the app does no user sign-in.
2. Store the client id and secret in the `Queenzone Development` Bitwarden Secrets Manager project
   as `Spotify__ClientId` and `Spotify__ClientSecret`. See [`docs/agent-bitwarden-secrets.md`](../agent-bitwarden-secrets.md).
   **Do not** add them to App Service settings or GitHub environments. The website and API never
   call Spotify.

Load them for a session (never print the values):

```powershell
$env:BWS_ACCESS_TOKEN = [Environment]::GetEnvironmentVariable("BWS_ACCESS_TOKEN", "User")
$secrets = bws secret list "1c16fd2d-4bfb-4eb7-8357-b49400233490" --output json | ConvertFrom-Json
foreach ($key in "Spotify__ClientId", "Spotify__ClientSecret", "ConnectionStrings__QueenZoneLegacyCanadaEast") {
  $secret = $secrets | Where-Object { $_.key -eq $key } | Select-Object -First 1
  if (-not $secret) { throw "$key not found in Bitwarden project" }
  $name = if ($key -like "ConnectionStrings__*") { "ConnectionStrings__QueenZoneLegacy" } else { $key }
  Set-Item -Path "Env:$name" -Value $secret.value
}
"loaded Spotify and SQL secrets"
```

For DEV, point `ConnectionStrings__QueenZoneLegacy` at the DEV database instead. Run DEV first.

## 2. Suggest

```powershell
dotnet run --project src/QueenZone.Tools -- suggest-streaming-links --out streaming-links.csv
```

| Option | Meaning |
| --- | --- |
| `--provider spotify` or `--provider apple-music` | One provider only. Apple-only runs need no Spotify secrets. |
| `--album-id <id>` | One album, for a trial run or a re-check. |
| `--only-missing` | Skip albums and tracks that already have a link for that provider. Use this on every run after the first. |
| `--country <cc>` | Apple storefront and Spotify market. The default is `gb`. Links open in the listener's own storefront either way. |
| `--connection-string`, `--settings-file` | As for the other tools. It falls back to `ConnectionStrings__QueenZoneLegacy`, then `src/QueenZone.Web/appsettings.Local.json`. |

The command only reads the database. It writes a CSV and prints a summary.

**Rate limits**
- Apple documents about 20 iTunes Search calls per minute. The client spaces calls 3 seconds apart, so a full catalogue run takes a while.
- Spotify calls are spaced 250 ms apart. `429` responses are retried after `Retry-After`.
- A failed lookup writes an `error` row for that album and provider, and the run continues.

### CSV columns

| Column | Meaning |
| --- | --- |
| `albumId` | `Q_ALBUM_T.Q_ALBUM_ID` |
| `albumSongId` | `Q_ALBUM_SONG_T.Q_ALBUM_SONG_ID`. Empty for the album-level row. |
| `title` | Local album or track title, for the reviewer. Values starting with `=`, `+`, `-` or `@` get a leading `'` so spreadsheets do not run them as formulas. |
| `provider` | `spotify` or `apple-music` |
| `candidateUrl`, `externalId` | The suggested link, already normalised. Empty when nothing matched. |
| `score` | 0–100. It ranks candidates for a reviewer; it is not a confidence threshold. |
| `flags` | `;`-separated: `remaster`, `deluxe`, `edition`, `live`, `compilation`, `no-match`, `error` |
| `existingUrl` | The link stored now, if any |
| `approved` | Blank. The reviewer sets it to `yes`. |

### How matching scores

**Albums**
- Title match: +50 for an exact match, after stripping parentheticals and " - 2011 Remaster" style suffixes. +25 when the local title is contained in the candidate's.
- Release year: +20 for the same year, +10 for ±1.
- Track count: +20 for the same count, +10 for within 2.
- Base: +10.
- Penalties: −10 for deluxe or edition, −15 for live or compilation. The live penalty is skipped when the local title is itself live.
- No title match means no candidate.

**Tracks** (on the chosen album only)
- A normalised title match is required: +60.
- +30 for the same track position, +10 for disc 1.
- −15 for live.

## 3. Review

Open the CSV in a spreadsheet and set `approved` to `yes` on rows to keep. Check:

- **Flags.** A `remaster` flag is normal for Queen, because the 2011 remasters are the main catalogue. Prefer the standard edition over `deluxe` and `edition` candidates, and reject `live` and `compilation` candidates for studio albums.
- **Rows below about 90**, especially when the year or track count is off.
- **`existingUrl`.** Approving a row replaces the stored link. Manual links are protected unless `--overwrite-manual` is passed to apply.
- **Open a sample of links** to confirm they play the right recording.

Leave rows blank to skip them. To fix a link by hand, paste it on the admin page instead.

## 4. Apply

```powershell
# Dry run (the default): prints what would be inserted, updated or skipped. Reads the database, writes nothing.
dotnet run --project src/QueenZone.Tools -- apply-streaming-links --file streaming-links.csv

# Write the changes.
dotnet run --project src/QueenZone.Tools -- apply-streaming-links --file streaming-links.csv --apply
```

| Option | Meaning |
| --- | --- |
| `--file <path>` | The reviewed CSV. Columns are read by header name, so a spreadsheet may reorder them or add its own. `albumId`, `albumSongId`, `provider`, `candidateUrl` and `approved` are required. |
| `--apply` | Write. Without it the run is a dry run. |
| `--overwrite-manual` | Also replace links an admin entered by hand. Off by default. |
| `--connection-string`, `--settings-file` | As for suggest. The dry run needs the database too, to compare with the stored links. |

Rules:
- Only rows with `approved` = `yes` (any case) are considered. Everything else is counted as "not approved" and ignored.
- Every `candidateUrl` is re-validated and normalised with the same `StreamingLinkUrl` validator as the admin page. Album rows (empty `albumSongId`) need an album link and track rows need a track link, from the provider named in `provider`.
- The album must exist and the track must be on that album. A second approved row for the same album or track and provider is rejected as a duplicate; the first one wins.
- Links are written with `Source = imported` and `UpdatedBy = apply-streaming-links`.
- A stored link with the same URL is left alone, so **re-running the same file changes nothing**.
- A stored `imported` link with a different URL is updated. A stored `manual` link is skipped and reported unless `--overwrite-manual` is passed.

Each approved row prints one line (`insert`, `update`, `skip (unchanged)`, `skip (manual link …)` or `skip (invalid)` with the reason), followed by counts.
Invalid rows never stop the run: valid rows are still applied. The exit code is `1` when any approved row was invalid or a write failed, so fix the CSV and re-run. Re-running is safe.

### When the site shows the new links

The public discography pages and the `/api/v1` discography responses are cached in the web app's memory
(`PublicQueryCacheService`, see [`public-query-cache.md`](public-query-cache.md)) for up to **30 minutes**.
The tool runs on an admin's machine and cannot clear that cache. Either wait for the TTL, or clear it now by
saving any album or song on `/admin/discography` (every admin discography write calls `InvalidateDiscographyCache`),
or by restarting the App Service. Admin pages are not cached, so `/admin/discography` shows the new links straight away.

### First backfill

Run the full loop against DEV (`dev.queenzone.org`) first: suggest, review, dry run, `--apply`, then check a few
album and song pages. Repeat against production with a fresh suggest run, because album and song ids are only
guaranteed to match within one database.

## Re-runs

New albums or tracks: `suggest-streaming-links --only-missing`, then review and apply as above.
