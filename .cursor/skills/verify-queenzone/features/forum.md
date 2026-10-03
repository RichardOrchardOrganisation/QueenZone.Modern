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

Open `/forum/1/the-music/page/2`, then `Archive sample thread 1030`. This read-only archive fixture includes two eligible video cards, a duplicate link, a quoted link, an inline sentence link, and a signature URL. Page 2 repeats the card fixture after 15 posts. Artwork is local; each card retains the original link and Watch on YouTube. Before activation there is no iframe or YouTube resource request. Keyboard activation of Load YouTube video keeps focus on the same control (now Unload YouTube video); activating another removes the first frame. At 320px and larger, frames must remain at least 200 x 200px without overflow. Verify pagination, JavaScript disabled and offline fallback. The mapped Playwright spec stubs the player and proves network/Referer, one-player behavior, keyboard/focus, quotes and pagination. Separately smoke-test real playback/fullscreen and record unavailable video or identification error 153 honestly.
