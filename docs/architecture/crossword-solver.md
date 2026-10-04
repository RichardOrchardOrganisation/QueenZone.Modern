# Shared crossword solver

The canonical, solution-free solver is `src/QueenZone.Web/wwwroot/js/crossword-core.js`, a dependency-free ES module. Web pages import it directly; the mobile adapter in `src/QueenZone.Mobile/src/crosswords/core.ts` re-exports the same file. Metro watches that directory. The adjacent declaration file supplies TypeScript types without a second implementation or a server build dependency on Node.

The module handles selected cells and clues, crossing direction changes, letter input, backspace, arrows, next/previous clue wrapping, check and reveal markers, and accessible cell descriptions. Grid shape and clues come from the public API; answers remain server-side. Incorrect markers persist until the letter changes. Reveal and auto-check history are monotonic, including when a revealed letter is later deleted.

Timer state records accumulated milliseconds separately from its running interval. The first input starts it. Manual pause and foreground visibility are independent; changing one cannot silently undo the other. Surfaces bind visibility to Page Visibility or AppState and hide the grid while paused.

Progress snapshots require an opaque nonempty puzzle version. Corrupt, missing-version and obsolete local saves are discarded. The newer whole-grid local/server snapshot wins by `updatedAt`; the result records when to announce restoration from another device. Guest and member storage keys are separate. The storage adapter supports synchronous localStorage and asynchronous mobile storage; read, parse and quota failures return a recoverable result. It neither owns authentication nor accesses global storage. Screens supply storage and implement debounce, offline queue, server calls and sign-in upload confirmation.

Pure Node tests exercise the same adapter and all ten seed grid shapes, including navigation invariants, timer accounting, markers, storage failure and version/conflict rules. Jest tests import it through the mobile transform. Screen interaction, browser accessibility and native device tests are additional requirements for the surface stories; module tests do not replace them.
