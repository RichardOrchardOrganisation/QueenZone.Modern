# Queen quiz question bank

This is a reviewable set of **200 multiple-choice questions** in four draft quizzes
of 50 questions each. Every question has four distinct options and one correct
answer. The answer order rotates across questions.

`questions.csv` uses the exact eight-column, one-option-per-row format described
in PR #1583 (`docs/quiz-bulk-import.md`). It contains 800 data rows. The importer
creates unpublished quizzes for an editor to review; it does not publish them.

`sources.csv` is a separate editorial ledger, keyed by quiz title and question
text. It is not passed to the importer. Book facts are paraphrased from the three
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

The four quiz titles describe the editorial levels. Easy asks about the line-up
and widely known songs; Medium asks about albums and broad chronology; Hard asks
about album tracks, early history, and production; Very Hard asks for precise
details from the supplied books. These are estimates and can be refined after
seeing player results.

PR #1583's importer accepts only `easy`, `medium`, and `hard` in the Difficulty
column. The 50 Very Hard questions use `hard` there and have 4 points; the quiz
title and `sources.csv` retain the distinct Very Hard label. The other levels
use 1, 2, and 3 points respectively.

## Validate and import

```bash
python3 docs/backlog/queen-quiz-question-bank/build.py
dotnet run --project src/QueenZone.Tools -- import-quiz-questions \
  --csv docs/backlog/queen-quiz-question-bank/questions.csv --dry-run
```

The second command is available once PR #1583 lands. After editorial review, use
the import command with a connection string as documented in that PR. Do not run
the real import more than once: each run creates new quizzes with the same titles.

To edit a question, change `build.py` and regenerate both CSV files together.
The script checks the 200-question count, uniqueness of question text and options,
and field lengths before writing.
