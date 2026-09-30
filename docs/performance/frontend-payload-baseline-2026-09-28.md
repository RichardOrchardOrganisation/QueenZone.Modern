# Production frontend payload baseline (28 September 2026)

Issue: [#1481](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1481)

Ran `scripts/Measure-FrontendPerformance.ps1` against `https://www.queenzone.org` with the mobile form factor, one cold Lighthouse run per path, and the advisory budgets in `frontend-performance-budgets.json`. The sample-data forum topic URL returned 404 in production, so the run used `/forum/topic/455095/forum-guidelines`. The local raw reports are not committed; the values below preserve the production baseline.

| Path | Score | LCP (ms) | CLS | Transfer | Requests | Server response (ms) | Budget |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `/` | 91 | 3434 | 0 | 655.8 KB | 22 | 291 | LCP over |
| `/news` | 98 | 2272 | 0 | 1.04 MB | 25 | 647 | Server response over |
| `/news/1003/queenzone-modernisation-begins` | 95 | 2648 | 0 | 387.7 KB | 19 | 296 | LCP over |
| `/forum` | 100 | 1673 | 0 | 382.4 KB | 16 | 290 | Pass |
| `/forum/1/the-music` | 96 | 2428 | 0 | 384.9 KB | 17 | 291 | Pass |
| `/forum/topic/455095/forum-guidelines` | 98 | 2139 | 0 | 386.8 KB | 16 | 292 | Pass |

The mobile budgets are LCP ≤ 2500 ms, CLS ≤ 0.1, transfer ≤ 1.5 MB, requests ≤ 40, and server response ≤ 600 ms. An independent homepage Lighthouse run during this investigation measured LCP at 4844 ms, so a single-run result should not be treated as a stable median. Repeat the affected routes with multiple runs before judging a code change.

## Asset findings

- All six production network traces requested `/css/site.min.css`, not the unminified `/css/site.css`. It transferred about 35.4 KB per route (121.1 KB resource size). `_Layout.cshtml` selects the minified file outside Development.
- None of the six public routes requested `js/editor/quill.js` or `js/admin/cropper.min.js`. Quill is included by `_RichTextEditor.cshtml`; Cropper is included on admin article/news editor pages.
- Lighthouse estimated about 28–32 KB of unused CSS per route and a roughly 150–300 ms potential saving for removing it. Those are lab estimates, not a measured improvement from a safe split. The homepage LCP miss was 934 ms in the six-route run, and the independent run missed by 2344 ms. CSS splitting alone is not established as the fix.

This measurement does not support a Quill/Cropper public-shell change or a minification change. The three over-budget routes remain candidates for focused LCP and server-response investigation under #1481. The advisory budget should not be marked as passed on this baseline.
