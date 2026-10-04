# Candidate crossword seeds

These ten solution grids implement the themes and target sizes from
[the accepted content brief](../../docs/backlog/queen-crossword.md#appendix-a--the-10-seed-crosswords).
They are candidate editorial drafts, not a publication sign-off. Grid construction may
drop bank entries to make answers interlock, as the brief permits. No filler answers
outside those banks were added.

The files follow Appendix B. Entry positions and reading-order numbers are derived
from the solution rows by `CrosswordGridValidator`; the JSON parser checks the supplied
answers and enumerations against those runs. Richard approved British style for the
first ten puzzles on 2026-10-04; unchecked-cell and symmetry warnings are allowed.
Per-puzzle editorial fact-check remains pending before publication.

| Puzzle | Size | Entries | Editorial fact-check |
| --- | --- | --- | --- |
| Meet the Band | 7 × 7 | 6 | Pending |
| Bohemian Rhapsody | 11 × 11 | 10 | Pending |
| The Studio Albums | 15 × 15 | 10 | Pending |
| Freddie Mercury | 11 × 11 | 8 | Pending |
| Brian May | 11 × 11 | 9 | Pending |
| Roger and John | 11 × 11 | 8 | Pending |
| Live Aid, 1985 | 9 × 9 | 7 | Pending |
| On Tour | 13 × 13 | 9 | Pending |
| Queen on Screen | 13 × 13 | 9 | Pending |
| Studios and Collaborators | 15 × 15 | 12 | Pending |

Review each retained clue against the biography/discography archive or an authoritative
source and record one editorial sign-off per puzzle on epic #2050 before publication.
Import must default to Draft, never overwrite existing slugs, and validate the entire
batch before writing. The CLI importer and Testing sample-data loading are subsequent
implementation slices; these files alone do not add playable public pages or write data.

`CrosswordSeedJson` enforces the 256 KB interchange limit, required fields, bounded
metadata, direction values and enumeration lengths. It shares grid validation with
the eventual builder/publish action. `CrosswordSeedJsonTests` loads all ten files and
checks validation plus lossless export/import round trips.
