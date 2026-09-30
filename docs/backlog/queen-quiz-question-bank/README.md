# Queen quiz question bank

This directory contains three sets totalling **1,500 multiple-choice questions**.
The bank contains sixty quizzes of 25 questions. Every question has four
distinct options and one correct answer. The answer order rotates across questions.

`questions.csv` contains the original 200 questions in eight quizzes (800 option rows).
`questions_extra.csv` contains 300 additional questions (1,200 option rows)
across twelve themed quizzes. Both files use the exact eight-column,
one-option-per-row format described in PR #1583 (`docs/quiz-bulk-import.md`).
The importer creates unpublished quizzes for an editor to review; it does not
publish them.

`questions_more.csv` contains the new 500 Encore questions (2,000 option rows)
in twenty 25-question draft quizzes. `build_more.py` generates it and the
matching `sources_more.csv` editorial ledger. This set draws mainly on the
user-supplied *Queen: Complete Works* for original album and solo album track
order, and on [Queen's official site](https://www.queenonline.com) for member
biographies and events after Freddie Mercury's death. The source ledger names
the relevant book section or links to the specific official page for each
question. The book text remains outside the repository.

The Encore set includes band members, songwriting, album tracks, solo projects,
the 1992 tribute concert, Queen's later releases, and Queen with Paul Rodgers
and Adam Lambert. Its editorial `very hard` level is encoded as importer
`hard` with four points, as described below. Difficulty estimates can be
adjusted after reviewing player results.

`questions_third.csv` contains a third set of 500 questions (2,000 option rows)
in twenty 25-question Archive Explorer quizzes. `build_third.py` generates it
and `sources_third.csv`. This set draws on the user-supplied *Queen: As It Began*,
*Queen: A Visual Documentary* and *Queen: Complete Works*, the curated Queen
concert history, and specific pages on Queen's official site. It covers live
venues, band milestones, remaining individual songwriting credits, solo work,
members' lives, archive editions, and the 2018 film. The source ledger links or
names a source for every question.

The third generator excludes normalized wording found in the first 1,000
questions and screens timeline entries for close matches to prior question and
answer pairs. A read-only production comparison on 20 September 2026 found the
same 1,000 questions live and no normalized wording matches with the third set.
As with the Encore set, editorial `very hard` is imported as `hard` with four
points.

Before the Encore import, a read-only production check found 500 questions
matching the first two CSVs. The Encore generator found no normalized-text
matches with them. Each importer run creates new quizzes rather than updating
existing ones, so only import a set once.

`sources.csv` and `sources_extra.csv` are separate editorial ledgers, keyed by
quiz title and question text. They are not passed to the importer. Book facts
are paraphrased from the three
texts supplied for this task:

- `Queen _ a visual documentary.md` — Ken Dean and Chris Charlesworth,
  *Queen: A Visual Documentary*.
- `Queen _ as_it_began.md` — Jacky Smith and Jim Jenkins,
  *Queen: As It Began*.
- `Queen_ the early years .md` — Mark Hodkinson, *Queen: The Early Years*.

The books are in the user's Downloads folder and are intentionally not copied
into the repository. Official Queen and Brian May pages and the repository's
history-event CSV supplement them. Source references are for editorial checking;
they are not text to display in the quiz.

## Difficulty

The first eight quiz titles describe the editorial levels. Easy asks about the line-up
and widely known songs; Medium asks about albums and broad chronology; Hard asks
about album tracks, early history, and production; Very Hard asks for precise
details from the supplied books. The twelve additional quizzes are themed by era
or subject; their per-question difficulty is recorded in `sources_extra.csv`.
These are estimates and can be refined after seeing player results.

PR #1583's importer accepts only `easy`, `medium`, and `hard` in the Difficulty
column. Editorial Very Hard questions use `hard` there and have 4 points; the
quiz title or source ledger retains the distinct Very Hard label. The other
levels use 1, 2, and 3 points respectively.

## Validate and import

```bash
python3 docs/backlog/queen-quiz-question-bank/build.py
python3 docs/backlog/queen-quiz-question-bank/build_extra.py
python3 docs/backlog/queen-quiz-question-bank/build_more.py
python3 docs/backlog/queen-quiz-question-bank/build_third.py
dotnet run --project src/QueenZone.Tools -- import-quiz-questions \
  --csv docs/backlog/queen-quiz-question-bank/questions.csv --dry-run
dotnet run --project src/QueenZone.Tools -- import-quiz-questions \
  --csv docs/backlog/queen-quiz-question-bank/questions_extra.csv --dry-run
dotnet run --project src/QueenZone.Tools -- import-quiz-questions \
  --csv docs/backlog/queen-quiz-question-bank/questions_more.csv --dry-run
dotnet run --project src/QueenZone.Tools -- import-quiz-questions \
  --csv docs/backlog/queen-quiz-question-bank/questions_third.csv --dry-run
```

After editorial review, use the import command with a connection string as
documented in PR #1583. The first two sets (1,000 questions) have already
been imported and published in dev and production. **The third set has not
been imported.** Do not rerun a real import for a set already loaded: the
importer creates new quizzes rather than updating existing ones.

To edit a question, change its generator (`build.py`, `build_extra.py`,
`build_more.py` or `build_third.py`) and regenerate its questions and sources
CSVs together. The scripts check question counts, uniqueness of question text
and options, and field lengths before writing.
