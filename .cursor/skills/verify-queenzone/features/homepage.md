# Homepage

The homepage leads with live content: a "Happening now" strip, the latest news, forum activity, the mobile apps call to action, new gallery photos, articles, and the 60-second quiz sprint and members' poll. The Queenzone.com history montage and On This Day sit below the fold.

## Sub-features

- `home-live-strip` rotates the newest forum, news, photo and article lines (`[data-home-ticker]`).
- `home-latest-news` shows the lead story, two secondary stories and the Latest news list into `/news`.
- `home-forum` shows the Forum now card and the Live from the forum band (`#forum`).
- `home-apps` shows the `Try out the Mobile Apps` link to `/mobile-apps`.
- `home-play` shows the quiz sprint and, when one is open, the members' poll (`#play`, `#home-poll`).
- `home-archive` shows the era montage (`#qz-hero-archive`) and the archive links.
- `home-brand` shows the Queenzone.org masthead brand link.

## How to get to it (user POV)

- Open `/` in the browser.
- Choose the Queenzone.org brand mark from any public page.

## Driving it with the browser

Preconditions:

- QueenZone is healthy at `http://127.0.0.1:5199`.
- `control-queenzone.ps1 doctor` reports the Testing sample article.

- **Open home.** Navigate to `/`. The level-1 heading is the visually hidden `QueenZone: Queen news, community and archive`.
- **Latest news.** Text `Latest news` is visible, with an `All news` link to `/news`.
- **Apps.** The link `Try out the Mobile Apps` points at `/mobile-apps` and sits directly below the front page.
- **Archive.** Scroll to `#qz-hero-archive`. The heading `Twenty-five years of the Queen internet zone` is visible, and `.qz-home-archive-links a[href='/news']` is visible.
- **Brand.** The masthead link `Queenzone.org` is visible.
- **Proof.** Capture the populated home state to `artifacts/homepage/home.aria.txt` and `artifacts/homepage/home.png`. Both identify Queenzone.org, Latest news and Forum now.

## Gotchas

- The page `h1` is visually hidden; the lead news headline is an `h2` so the page heading stays stable as news changes.
- `Latest news` is a section header, not an `h1`.
- Sample photo thumbnails point at placeholder CDN URLs, so gallery tiles show alt text locally. That is expected.
- The live strip pauses while the tab is hidden or under reduced motion, so it may show only its first line.
