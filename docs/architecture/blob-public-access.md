# Blob public access

Issue #2211
keeps `allowBlobPublicAccess = true` on `queenzoneprod`, `queenzonedev`, and
`queenzonemobilebuilds`. The public site, mobile image URLs, and mobile build
downloads still depend on anonymous origin reads. `cdn.queenzone.org` is a
plain Cloudflare pass-through to `queenzoneprod` (also the storage custom
domain). `cdn2.queenzone.org` is the `pictures-queenzone-org` Worker fetching
the same origin anonymously. Flipping the account flag today returns 409
`PublicAccessNotPermitted` on gallery photos, album covers, news images,
legacy avatars, `css`, and mobile build links.

A private origin needs a signing Worker (service SAS or a rotating
user-delegation SAS), removal of the storage custom domain, and a rewrite of
stored raw blob URLs. That follow-up is tracked separately.

Until then, OpenTofu rejects any container whose `publicAccess` is not `None`
unless it is named in `public_blob_containers`. Backup, modern UGC,
`songfiles`, and legacy `attachments` must stay private. The leftover prod
`test` container stays in inventory at `None` and is not deleted.

## Public containers

| Container | Account | Access | What serves it |
| --- | --- | --- | --- |
| Photo/archive galleries (`queen`, `freddie-mercury`, `brian-may`, `fan-art`, `fan-pics`, `freddie-tribute-concert`, `john-deacon`, `miscellaneous`, `pre-queen`, `queen-and-adam-lambert`, `queen-and-paul-rodgers`, `queen-memorabillia`, `roger-taylor`, `special-events`, `us-convention-2001`) | `queenzoneprod` / `queenzonedev` | Blob | `cdn.queenzone.org` (and `cdn2` / `pictures-queenzone-org` when that Worker is used). Dev has no custom domain, so some uploads are raw `*.blob.core.windows.net`. |
| `images` | `queenzoneprod` / `queenzonedev` | Blob | `cdn.queenzone.org` for discography and other published images |
| `album-or-single-covers` | `queenzoneprod` / `queenzonedev` | Blob | `cdn.queenzone.org` (`AlbumCoverUrl`) |
| `avatars` | `queenzoneprod` / `queenzonedev` | Blob | `cdn` / `cdn2` for legacy public avatars. Modern member avatars are private `ugc-avatars` via `/account/avatar/*` |
| `css` | `queenzoneprod` / `queenzonedev` | Blob | Raw blob / CDN for published site CSS. Access is Blob so the container is not anonymously listable |
| `forum` | `queenzoneprod` / `queenzonedev` | Blob | No URL builder in the modern app. Stays on the allow-list until 7-day anonymous-read metrics land |
| `mp3` | `queenzoneprod` / `queenzonedev` | Blob | No URL builder in the modern app. Stays on the allow-list until 7-day anonymous-read metrics land |
| `builds` | `queenzonemobilebuilds` | Blob | Direct anonymous blob links for throwaway mobile test builds |

`allowBlobPublicAccess` stays on until the signing-Worker follow-up lands.
App-streamed routes (`/ugc/*`, `/account/avatar/*`, `/forum/attachment/*`,
fan-performance audio) do not depend on the flag.
