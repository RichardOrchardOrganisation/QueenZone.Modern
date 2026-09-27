# User-generated content blob storage

Shared upload infrastructure lives in `QueenZone.Storage`. Web (and future workers/tools) call `IBlobUploadService`; they do not construct `BlobServiceClient` directly.

## Configuration

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings:BlobStorage` | Azure Storage connection string. Empty = app boots with `NullBlobUploadService` (uploads throw). |
| `BlobUpload:DefaultMaxBytes` | Default size limit (10 MB). |
| `BlobUpload:EditorMaxBytes` | Max size for rich-text editor uploads (default 10 MB). Effective limit is `min(EditorMaxBytes, container MaxBytes)`. |
| `BlobUpload:DefaultAllowedContentTypes` | Default MIME allowlist. |
| `BlobUpload:Containers` | Per-container max size / MIME overrides. |
| `BlobUpload:PublicBaseUrl` | Optional CDN/worker base for display URLs. |
| `UploadQuotas:Enabled` | Process-local daily quotas (default true). |
| `UploadQuotas:MaxUploadsPerDay` | Max upload operations per principal per UTC day (default 50). |
| `UploadQuotas:MaxBytesPerDay` | Max total bytes per principal per UTC day (default 100 MiB). |

Per-member quotas (`MemberUploadQuotaService`) apply to editor images, forum attachments, avatars, photo submissions, and pending fan-performance audio. They are **process-local** (`IMemoryCache`) and fit single-instance B1 hosting; container MIME/size allowlists remain enforced separately. Antivirus scanning of uploads is **not planned**. The daily byte cap stays at `UploadQuotas:MaxBytesPerDay` (100 MiB) — do not raise it for 25 MB audio files.

Mobile photo submit (`POST /api/v1/member/photo-submissions`) reuses `PhotoSubmissionService.SubmitAsync` — the same `IBlobUploadService` / `ugc-photos` path and quota consume as website `/submit/photo`. Do not add a second mobile-only limit.

Register in DI:

```csharp
services.AddQueenZoneStorage(configuration);
```

## Containers

Keep UGC separate from the legacy photo archive behind `cdn.queenzone.org`.

| Container | Use |
| --- | --- |
| `ugc-avatars` | Member profile images |
| `ugc-forum` | Forum attachments / pasted images |
| `ugc-photos` | Photo submissions |
| `ugc-articles` | News/article rich-text images |
| `ugc-fan-performances` | Pending member fan-performance audio (MP3/FLAC only, 25 MB). Never write pending audio to `songfiles`. |

`ugc-fan-performances` allow-list is `audio/mpeg`, `audio/mp3`, `audio/flac`, and `audio/x-flac`. **m4a / `audio/mp4` / `audio/x-m4a` / AAC are rejected.** Validation uses magic-byte sniffing (`ID3`, MPEG frame sync, `fLaC`); a `.mp3`/`.flac` name with non-audio bytes is rejected. Oversized payloads fail in `BlobUploadValidator` with a clear `BlobUploadException` (Kestrel’s 30 MB default body cap already fits 25 MB). The submit endpoint in #1293 must set multipart/`RequestSizeLimit` to at least ~26 MB (25 MB + multipart overhead) so FormOptions does not reject first. Do not raise the 25 MB product ceiling or a global `FormOptions` / `MaxRequestBodySize` here.

Containers are created with **no public access** (`PublicAccessType.None`). Do not put new UGC into legacy gallery folders.

## Naming

- Member: `members/{memberId}/{guid}.{ext}`
- Editorial: `editors/{sanitizedEmail}/{guid}.{ext}`
- Fallback: `anonymous/{guid}.{ext}`

Store **container + blob name** in the database. Treat any public/display URL as optional and changeable (SAS or CDN).

## Serve strategy

- Storage is not anonymously listable.
- Do not use permanent raw `*.blob.core.windows.net` URLs as the product contract.
- Prefer short-lived SAS and/or a Cloudflare proxy with controlled origin, on separate UGC containers/routes.
- **Editor / forum pasted images** are served through the web app proxy: `/ugc/{area}/{blobName}` (e.g. `/ugc/forum/editors/…/id.webp`). Optional `?size=thumb` loads the paired `-thumb.webp` blob (thumb max 600 px longest side; full max 1200 px).
- App-proxy every byte only when auth truly requires it; private UGC containers use the proxy by default for HTML embeds.

### Legacy media CDN hostnames

Two Cloudflare hostnames proxy the legacy Azure Blob containers. They behave differently and are not interchangeable:

- **`cdn.queenzone.org`** — straight CDN proxy, no Worker. Azure Storage custom domain on account `queenzoneprod` makes the proxied Host valid. Cannot set custom response headers. Used by `PhotoImageUrl` for photos and images.
- **`cdn2.queenzone.org`** — Cloudflare Worker **script** `pictures-queenzone-org` on route `cdn2.queenzone.org/*`. That script name is historical; the DNS hostname is **cdn2**. Sets cache/CORS/nosniff headers. The Worker snapshot returns 404 for `/songfiles/*` and `/attachments/*`. Retired `pictures.queenzone.org` is a compatibility hostname (Worker `pictures-legacy-redirect` → `cdn`); do not use it for new media URLs.

Fan-performance audio is **not** a public CDN object. The member-authenticated app proxy `GET /fan-performances/{id}/audio` streams from the private `songfiles` container and sets `Content-Disposition`. Do not emit `cdn2.queenzone.org/songfiles/…` or raw blob URLs in HTML.

Legacy forum attachments are **not** a public CDN object. The member-authenticated app proxy `GET /forum/attachment/legacy/{postId}` (and the Bearer alias `GET /api/v1/forum/attachments/legacy/{postId}`) streams from the private `attachments` container and sets `Content-Disposition: attachment`. Do not emit `cdn2.queenzone.org/attachments/…` or raw blob URLs.

**ACL note:** `songfiles` is private (#177). Legacy `attachments` desired ACL is `None` (#1656); the app streams the bytes after the member check. Live Azure stays public blob until a reviewed OpenTofu apply — this repository change does not flip the live container. `css` stays `Container` (published site CSS; anonymous listing is the historical ACL, not a member-upload bucket). Other `Blob` containers (`forum`, `avatars`, `mp3`, and the photo galleries) stay public because they are published archive assets; modern member uploads are the private `ugc-*` containers. Private modern UGC containers present: `ugc-avatars`, `ugc-forum`, `ugc-photos`, and `ugc-articles` (`ugc-fan-performances` is not created in Azure yet; `CreateIfNotExists` uses `PublicAccessType.None`). Pending fan-performance audio must stay in `ugc-fan-performances` until a later review step copies an approved file into `songfiles`.

### Forum attachments

| Kind | Storage | Public HTML link | Download behaviour |
| --- | --- | --- | --- |
| Legacy import (`ModernForumPost.Attachment`) | Private `attachments` blob container | `/forum/attachment/legacy/{legacyPostId}` | Member policy required; stream via app with `Content-Disposition: attachment`. No CDN redirect. |
| New uploads (`ForumPostAttachments`) | Private `ugc-forum` container | `/forum/attachment/{legacyPostId}/{attachmentId}` | Member policy required; stream via app with `Content-Disposition: attachment` and increment `DownloadCount` |

Do not link forum attachments straight to `cdn.queenzone.org` or raw Azure blob URLs in HTML. Inline editor images remain on `/ugc/forum/…` (see serve strategy above).

## Security baseline

- Size and MIME allowlists (per container).
- Magic-byte sniffing where possible; extension must agree when both are known.
- Collision-resistant names.
- Forum / UGC HTML (`UgcHtml`) allows `<img>` only when `src` is an app proxy path (`/ugc/…`) or a configured UGC host / known `ugc-*` Azure container path. Arbitrary external image hosts are stripped.

Follow-ups (not in the foundation service): malware scanning, orphaned-blob cleanup after soft-delete, broader CDN for UGC.

## Testing

- Unit tests cover validation without Azure.
- Opt-in round-trip: set `ConnectionStrings:BlobStorage` and `RUN_BLOB_STORAGE_TESTS=true`.
