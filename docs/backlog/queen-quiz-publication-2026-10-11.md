# Quiz publication verification — 11 October 2026

All **164 quizzes and 4,200 questions** are published and available to visitors on both [dev](https://dev.queenzone.org/quizzes/sprint) and [live](https://www.queenzone.org/quizzes/sprint). This includes all 100 quizzes and 2,500 questions from the two expansion packages generated in this chat.

Publication followed the user's request to make all quizzes available, after the [dev-first import verification](queen-quiz-import-2026-10-11.md).

| Environment | Published before → after | Newly published | Published questions | Public quiz pages checked |
| --- | ---: | ---: | ---: | ---: |
| Dev | 60 → 164 | 104 | 4,200 | 164 |
| Live | 64 → 164 | 100 | 4,200 | 164 |

Dev's additional four unpublished quizzes were the two Bohemian Rhapsody quizzes, Flash Gordon and Highlander, already published on live. They were published on dev as part of making every quiz available on both sites.

## Database verification

Dev publication and public-access checks passed before live publication started. Each database update ran in a transaction against the exact quiz IDs confirmed by preflight inspection, with an expected unpublished-row count guard. It set `IsPublished=true` and filled only missing `PublishedAt` timestamps, matching the existing repository's publication semantics.

Full before/after read-back confirmed that quiz descriptions, question text, categories, difficulty, points, ordering, option text, correct-answer flags and all IDs were unchanged. Existing publication timestamps were preserved. All quizzes have a publication timestamp. Structural checks confirmed valid assigned difficulty values and points, two to four options, and exactly one correct answer for every question. All 2,500 new questions have easy, medium or hard difficulty assigned; 425 older live questions retain their existing unspecified difficulty. Both databases still contain 16,800 answer options.

## Visitor verification

Initial live HTTP checks timed out during a concurrent production deployment warmup. They were rerun after the website resumed responding; the successful checks below describe the final state.

On each deployed website, without signing in:

- `/health` returned HTTP 200.
- `/quizzes` opened the Quiz Sprint introduction with its Start control available.
- Both pages of `/api/v1/content/quizzes?pageSize=100` listed exactly the 164 database quiz IDs, totalling 4,200 questions.
- All 164 individual `/quizzes/{id}` pages returned HTTP 200 and rendered the expected number of playable questions.
- Public detail API samples from both expansion packages each returned 25 questions with four answer choices and no correct-answer flags.
- Starting an anonymous Quiz Sprint through its public API returned 60 questions, including newly imported questions. No score or quiz attempt was submitted.

The public `/quizzes` route leads to Quiz Sprint. Published questions are available in its random question pool; individual quizzes are also accessible through their direct URLs and the public quiz API.

No application or schema files changed. Full .NET build/tests and opt-in write probes were not rerun for this publication operation; direct database read-back and checks against both deployed websites verified the actual resulting state. Credentials were used only in memory and are absent from the receipt and repository.

Do not reimport either package: publication changes the existing quizzes, and importing the CSVs again would create duplicates.
