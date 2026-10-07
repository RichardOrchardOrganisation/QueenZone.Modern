# Discography Streaming Links (Spotify and Apple Music)

Status: proposed. Covers Phase 1 (curated "Listen on" links) and Phase 2 (admin-only backfill tooling).
Phase 3 (click-to-load embeds) is out of scope here.

Tracking: epic #2176. Phase 1: #2177 (data), #2178 (admin), #2179 (web), #2180 (API + mobile). Phase 2: #2181 (suggest), #2182 (apply).

## Goal

Album pages and song pages link to the matching release on Spotify and Apple Music, on the website and in the mobile app.
Admins can maintain the links, and a tools command suggests matches so the catalogue can be filled without hand-searching every track.

## Non-goals

- **No Spotify Web Playback SDK and no MusicKit JS.** Playback needs a listener's Premium or Apple Music subscription and OAuth sign-in. Spotify's development-mode user cap and extended-quota rules also make a public SDK integration unrealistic for this site. The website and the API never call Spotify or Apple at request time.
- No embedded players or iframes (Phase 3). `frame-src` in `SecurityHeaders.cs` does not change.
- No third-party scripts, cookies, or CSP changes.
- No other providers yet (YouTube Music, Amazon, Deezer, Tidal). The data model allows them later without a schema change.
- No display of provider metadata or artwork. We store IDs and URLs only.
- No song.link / Odesli dependency.

## Decisions

| Topic | Decision | Why |
| --- | --- | --- |
| Where links live | New EF table `DiscographyStreamingLinks`, not new columns on `Q_ALBUM_T` / `Q_ALBUM_SONG_T` | One row per target and provider. Adding a provider needs no migration. Keeps provenance (`Source`, `UpdatedAt`). New write paths default to EF tables (ADR 0006, ADR 0021). |
| Link granularity | Album level (`AlbumSongId` null) and track level (`AlbumSongId` set) | The same song title appears on several albums (studio, live, compilation, remaster), and those are different releases on each provider. |
| Song page links | Song identity is a read-time slug group (`SongCatalog`). The header shows, per provider, the link from the earliest-released appearance that has one. Each appearance row also shows its own links. | No song table exists, and we are not adding one. |
| What we store | Provider, provider `ExternalId`, and a canonical URL normalised on save | The ID enables Phase 3 embeds. The URL lets pages render without per-provider URL logic. |
| Apple storefront | Store the URL as entered after normalisation (usually `/us/` or `/gb/`). Do not rewrite storefronts. | `music.apple.com` links open in the viewer's own storefront app. Storefront logic is not worth it. |
| Mobile | Plain `https://` links via `Linking.openURL` | Universal links / app links open the Spotify or Apple Music app when installed. No native SDK and no new Expo modules. |
| Matching | Human-approved only. Tools suggest; admins approve. | Queen's catalogue has many near-duplicates ("2011 Remaster", deluxe, live, compilations). Wrong links are worse than no links. |

## Phase 1: Curated "Listen on" links

### Data model

EF table `DiscographyStreamingLinks` (EF migration, idempotent like `20261006090000_AddAlbumSongTrackNumberAndCover`):

| Column | Type | Notes |
| --- | --- | --- |
| `Id` | int identity | PK |
| `AlbumId` | int, required | `Q_ALBUM_T.Q_ALBUM_ID` |
| `AlbumSongId` | int, nullable | `Q_ALBUM_SONG_T.Q_ALBUM_SONG_ID`. Null means album-level. |
| `Provider` | varchar(20), required | `spotify`, `apple-music` (`StreamingProvider` enum, stored as string) |
| `ExternalId` | varchar(64), required | Spotify base62 ID, or Apple album/track numeric ID |
| `Url` | varchar(300), required | Canonical normalised URL |
| `Source` | varchar(20), required | `manual` or `imported` |
| `UpdatedAtUtc` | datetime2, required | |
| `UpdatedBy` | nvarchar(100), nullable | Admin identity for manual edits |

- Unique index on (`AlbumId`, `AlbumSongId`, `Provider`). It needs a filtered index or a computed column so that null `AlbumSongId` is unique per album and provider.
- No FK to legacy tables. The admin album and song delete paths in `EfAdminDiscographyRepository` must delete matching link rows in the same transaction.
- Read models get `IReadOnlyList<StreamingLink> StreamingLinks` (provider + URL) on `AlbumDetail`, `AlbumSong`, `SongTrackSource`, `SongAppearance`, and `SongDetail` (the resolved header links). Default to empty, so the in-memory sample data and existing callers keep working.
- In-memory repository and sample data include a few links so `Testing` WAF tests cover rendering.

### URL validation and normalisation

`StreamingLinkUrl.TryParse(provider-agnostic string, expected kind)` in `QueenZone.Data/Discography`. It returns provider, kind, external ID and canonical URL, or a validation error.

| Provider | Accepted input | Canonical form |
| --- | --- | --- |
| Spotify album | `https://open.spotify.com/album/{22 base62}` (optional `/intl-xx/` segment, any query) | `https://open.spotify.com/album/{id}` |
| Spotify track | `https://open.spotify.com/track/{id}`, also `spotify:track:{id}` URIs | `https://open.spotify.com/track/{id}` |
| Apple album | `https://music.apple.com/{cc}/album/{slug}/{numericId}` | Same, query stripped |
| Apple track | `https://music.apple.com/{cc}/album/{slug}/{albumId}?i={trackId}` or `/{cc}/song/{slug}/{trackId}` | `https://music.apple.com/{cc}/song/{slug}/{trackId}` when a slug is known, otherwise keep the `?i=` form with only `i` retained |

Rules:
- Host allowlist only: `open.spotify.com` and `music.apple.com`. Reject `http`, short links (`spotify.link`, `apple.co`), `geo.music.apple.com`, and everything else.
- Strip tracking parameters such as Spotify's `si`.
- Album-level targets must be album URLs. Track-level targets must be track URLs. Provider is inferred from the host and must match the field it was entered in.

### Admin

On `/admin/discography/{album}` and the song edit page (`Pages/Admin/Discography/Album.cshtml`, `Song.cshtml`):

- Two optional fields per target: "Spotify link" and "Apple Music link". Show the current value and source (manual or imported).
- Saving an empty field deletes the link. Saving a value validates and normalises it; an invalid value shows a field error.
- Each saved link has an "Open" link so the admin can check it.
- An album list column or filter, "missing links", so admins can see coverage at a glance.
- Writes go through the existing admin discography repository and audit pattern. They invalidate the discography public query cache (see below).

### Website

- **Album page** (`Pages/Discography/Album.cshtml`): a "Listen on" row near the cover with Spotify and Apple Music buttons for album-level links. Track rows show small provider icons for track-level links.
- **Song page** (`Pages/Songs/Detail.cshtml`): "Listen on" buttons in the header (resolved links), plus per-appearance icons in the appearances list.
- Render nothing when there are no links. No empty state.
- Plain anchors, `rel="noopener external"`, `target="_blank"`. The accessible name includes the destination, for example "Listen to A Night at the Opera on Spotify".
- Follow the Spotify and Apple Music brand guidelines for marks and wording ("Listen on Spotify", "Listen on Apple Music"). Store marks as local SVGs under `wwwroot`. Never hotlink them.
- Optional GA event (`streaming_link_click`, provider, target type) using the existing analytics helper. No new script.

### Caching

`public-query-cache.md` lists discography as TTL-only because there was no write path. The admin discography write path (#2163) and these link writes both change public discography output. This work must call `InvalidateDiscographyCache` on link writes and update that matrix row.

### API and mobile

- Additive change to `/api/v1` (no v2, per ADR 0019): `streamingLinks: [{ provider, url }]` on `AlbumDetailDto`, `AlbumSongDto`, `SongAppearanceDto`, and `SongDetailDto`. Always present, empty when there are none. Update the OpenAPI document and contract tests.
- Mobile `src/api/types.ts`: optional `streamingLinks` so older API responses still parse.
- `AlbumScreen.tsx` and `SongScreen.tsx`: "Listen on" buttons using `Linking.openURL`, with stable `testID`s. Update `docs/feature-map/mobile/*`.

## Phase 2: Admin-only backfill tooling

Two `QueenZone.Tools` commands, following the existing pattern (`check-links`, `backfill-fan-performance-durations`). Both are local and admin-run. Neither runs in App Service.

### `suggest-streaming-links`

```powershell
dotnet run --project .\src\QueenZone.Tools\QueenZone.Tools.csproj -- suggest-streaming-links --out streaming-links.csv [--provider spotify|apple-music] [--album-id 12] [--only-missing]
```

- Reads active albums and tracks from the database (connection string resolved as for `check-links`).
- **Apple Music:** iTunes Search API (`itunes.apple.com/search` and `/lookup`, no auth). Search albums with `artistTerm=Queen`, then `lookup?id={collectionId}&entity=song` for tracks. Throttle to stay under Apple's documented rate (about 20 requests per minute).
- **Spotify:** Web API with the Client Credentials flow. `/v1/search?type=album&q=artist:Queen album:{name}`, then `/v1/albums/{id}/tracks`. Honour `429` with `Retry-After`. One registered development-mode app is enough because only admins use it.
- **Matching heuristics, scored:** normalised title equality (strip "Remastered 2011", "Deluxe", parentheticals for comparison only), release year within ±1, track count, track position. Flag `live`, `remaster`, `deluxe`, and `compilation` in a `flags` column rather than silently choosing.
- **Output:** a CSV with one row per target and provider. Columns: `albumId, albumSongId, title, provider, candidateUrl, externalId, score, flags, existingUrl, approved`. `approved` is blank for an admin to fill in with `yes`. It never writes to the database.
- **Secrets:** `Spotify__ClientId` and `Spotify__ClientSecret`, read from the environment or Bitwarden (`bws`) per `docs/agent-bitwarden-secrets.md`. They are not added to App Service settings or GitHub environments.

### `apply-streaming-links`

```powershell
dotnet run --project .\src\QueenZone.Tools\QueenZone.Tools.csproj -- apply-streaming-links --file streaming-links.csv [--apply] [--overwrite-manual]
```

- Dry run by default. It prints what would be inserted, updated, or skipped.
- Only rows with `approved=yes` are applied. Every URL goes through the same `StreamingLinkUrl` validator as the admin UI.
- Writes `Source = imported`. It never overwrites a `manual` link unless `--overwrite-manual` is passed.
- Idempotent. Re-running the same file is a no-op.
- After applying, remind the operator that the public discography cache expires on TTL, or provide a way to invalidate it.

### Phase 2 docs

Add `docs/architecture/streaming-links-backfill.md`, a runbook covering registering the Spotify app, storing the secrets, the suggest → review → apply loop, and rate-limit notes.

## Testing (per `docs/architecture/testing-policy.md`)

- **Data:** unit tests for `StreamingLinkUrl` (every accepted and rejected form, normalisation, kind mismatch). Tests for song header link resolution in `SongCatalog`. EF repository tests, including link cleanup when an album or song is deleted. A SQL Server shape test for any `SqlQueryRaw` projection touched.
- **Web:** WAF tests for album and song pages with and without links, admin validation, save and delete, and cache invalidation. An API contract test for the new `streamingLinks` fields.
- **Mobile:** Jest tests for the `AlbumScreen` and `SongScreen` buttons and the absent state. Feature-map entries.
- **Tools:** matching heuristics are pure functions with unit tests from recorded JSON fixtures. HTTP calls go behind an interface, so CI never calls Spotify or Apple. `apply` tests cover dry-run, approval filtering, manual-protection, and idempotency.
- **Nightly legacy probes:** the new table is EF-migrated. #2174 already makes probes wait for migration.

## Rollout

1. Data model and validator ship dark (no UI).
2. Admin editing, then web rendering, then API and mobile. Each is safe with zero links.
3. Phase 2 tooling, then a first backfill run against DEV (`dev.queenzone.org`), then production.

## Follow-ups (not in this spec)

- Phase 3: click-to-load Spotify and Apple embeds (CSP `frame-src` change, consent review).
- Periodic link health checks. Re-run `suggest-streaming-links --only-missing`, or add a lookup-by-ID check to the `check-links` schedule.
- Additional providers.
