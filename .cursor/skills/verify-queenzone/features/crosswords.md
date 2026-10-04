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

Preconditions: isolated Testing host with `CrosswordBrowserFixture__Enabled=true`; ten published in-memory samples. The normal Testing catalogue has drafts only. Automated journeys use their own PID-scoped published Testing host, never an existing shared listener.

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
- Print-media screenshots do not establish actual A4/Letter pagination; physical PWA installation remains a manual check.
