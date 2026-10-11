# Forum

The forum shows public boards, lets a visitor open The Music, and read the seeded ranking topic with its posts and breadcrumbs.

## Sub-features

- `forum-index` shows the Forum heading, participation guidance, the The Music board card, and latest activity. The Queenzone.com support board remains browsable while its topics are excluded from the cross-board recent table.
- `forum-category` lists topics on `/forum/1/the-music`, including `Ranking every studio album`.
- `forum-topic` shows that topic's heading, breadcrumb, and at least one post.

## How to get to it (user POV)

- Open `/forum`.
- Choose `Forum` from the Community navigation group.
- Open `/forum/1/the-music` from the board card.
- Open `/forum/topic/1002/ranking-every-studio-album` from the category list or latest activity.

## Driving it with the browser

Preconditions:

- QueenZone is healthy at `http://127.0.0.1:5199`.
- `control-queenzone.ps1 doctor` reports the Testing host.

- **Open index.** Navigate to `/forum`. The level-1 heading `Forum` is visible. The card `a.qz-card[href='/forum/1/the-music']` is visible. Heading `Latest activity across boards` is visible. A link named `Ranking every studio album` (exact) is visible.
- **Open board.** Choose the The Music card or navigate to `/forum/1/the-music`. The level-1 heading `The Music` is visible. A link `Ranking every studio album` is visible.
- **Open topic.** Choose that link or navigate to `/forum/topic/1002/ranking-every-studio-album`. The level-1 heading `Ranking every studio album` is visible. Navigation named `Breadcrumb` contains a `Forum` link. `.qz-forum-posts` is visible and contains at least one `.qz-forum-post`.
- **Proof.** Capture the topic state to `artifacts/forum/topic.aria.txt` and `artifacts/forum/topic.png`. Both identify the topic heading and a post.

## Gotchas

- Board cards and the recent-threads table both link to categories. Target `a.qz-card[href='/forum/1/the-music']` for the board, not the first matching href.
- Use an exact name when clicking `Ranking every studio album` on the index so nearby words do not steal the hit.
- Sample topics for boards other than The Music may be empty. Do not treat an empty non-music board as a host failure.
- Signed-in posting is a different surface. This map only covers public read.

## YouTube card proof (`web.forum.youtube`)

Open `/forum/topic/1029/archive-sample-thread-1029` for 30 cards across 15 posts. Players within the 300px preload margin load automatically; far cards make no YouTube requests. Scroll rapidly in both directions: never more than three frames or duplicate frames per card. Leaving the 600px retention range pauses/removes after 250ms; re-entry cancels eviction. A visible playing player and fullscreen player must remain mounted. Use the stubbed deterministic spec for repeatable bridge, spoof rejection, hidden-tab pause, no auto-resume, failure/Retry, layout, cleanup, and bfcache pageshow re-init assertions.

Open `/forum/topic/1030/archive-sample-thread-1030` for the duplicate, quote, inline link, signature and pagination fixture. Verify all original links remain and quote/composer output contains no cards. Verify one privacy notice above the first card. At 320px and larger the player must be at least 200 × 200px without overflow. Check no-JS, offline, blocked, and missing-observer/origin-mismatch click-to-load fallbacks. Focus must remain on a stable external link during mount/eviction without scrolling.

Capture with `pwsh -File .cursor/skills/verify-queenzone/scripts/control-queenzone.ps1 capture-proof -Feature web.forum.youtube`. Record platforms and attach proof paths. Dinesh separately records a real embed on dev.queenzone.org proving bridge state through the sandbox, one-playing, fullscreen retention, and no error 153. Linus reviews; Pat gates release. Stubbed responses do not count as real-player or deployed proof.
