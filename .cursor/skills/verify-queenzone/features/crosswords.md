# Queen crosswords

Visitors browse published puzzles and solve them with keyboard or touch, check or reveal selections, and resume device-local progress. Members can sync progress and keep their first completion.

## Sub-features

- `web.crosswords.list`: published puzzle cards, difficulty and size filters, member progress labels.
- `web.crosswords.play`: accessible grid, keyboard navigation, checks, reveals, timer, pause, completion, blank printing and offline restoration.

## How to get to it (user POV)

- Choose Crosswords in the desktop or mobile navigation.
- Choose Play crossword or Continue crossword on the homepage.
- Open `/crosswords`, then choose a puzzle card.

## Driving it with the browser

Preconditions: isolated Testing host with `CrosswordBrowserFixture__Enabled=true`; ten published in-memory samples. The normal Testing catalogue has drafts only. Automated journeys use `scripts/run-crossword-web-tests.sh` with `QZ_CROSSWORD_PUBLISH_DIR` pointing at licensed published Web output and a built E2E project. It creates a PID-scoped Testing host on a random loopback port, never an existing shared listener. `QZ_TEST_BROWSER` chooses Chromium, Firefox or WebKit; `QZ_TEST_FILTER=TestCategory=Deterministic` runs the ordinary browser regression suite.

- Browse: open `/crosswords`; confirm Queen crosswords and choose Meet the band. Change filters and check the visible cards.
- Keyboard: select a grid cell, type a letter, use Space, arrows, Tab, Shift+Tab and Backspace. Confirm the active clue and letters. Use Ctrl/Command+Enter to check the word.
- Reveal: open the menu, reveal a word, cancel and then accept confirmation. Confirm triangle markers and their restoration after reload.
- Pause: choose Pause; confirm the grid hides. Resume and confirm the letters remain.
- Completion: fill the sample correctly; confirm time, clean/assisted status, Share and Next crossword.
- Print: open a 13 by 13 sample, use print preview; confirm blank grid, clues and no site chrome.
- Offline: wait for the service worker, reload online, then go offline and reload. Confirm local letters restore and checks say Needs a connection.

## Gotchas

- Only disposable Testing fixtures can publish samples for this recipe; never edit live data to enable proof.
- Cached play HTML is a public shell with no member identity or anti-forgery token. Member identity hints partition local progress and never authorise requests.
- Browser mobile emulation does not prove a real soft keyboard, VoiceOver, TalkBack or native mobile app.
- Chromium captures actual A4/Letter PDFs for pagination and print-media screenshots. Physical PWA installation remains a manual check.

Replay/reset: toolbar Reset current attempt and completion Play again require confirmation. They start blank device-local Practice with a zero timer; practice uses a separate owner/version storage key, never ranked Save/Complete writes and never replaces the first result. Check/reveal still require connection. Reload/offline persistence keeps practice. Resume saved attempt restores the preserved original local/member attempt. Test guests and members with disposable fixtures only; never reset real progress.

### Mobile web keyboard regression

Use a disposable guest Testing puzzle, not a member's saved attempt. On a small phone select 3 Across, type ROG, and confirm horizontal letters, selected next cell and active clue are visible above the soft keyboard. Select 2 Down and verify vertical auto-advance, backspace/retyping, and clue changes while the keyboard is open. A deliberate second tap on the already-selected crossing still toggles direction; an IME space must not do so. Physical Space remains the desktop direction shortcut. Test shrinking/panning visualViewport, because desktop device emulation alone does not exercise iOS caret scrolling. The input focus anchor must follow the selected cell. Inspect dark-mode contrast and actual simulator Safari screenshots; synthetic IME tests do not prove third-party SwiftKey on a physical iPhone.
