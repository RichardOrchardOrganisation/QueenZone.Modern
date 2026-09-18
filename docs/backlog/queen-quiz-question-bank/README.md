# Queen quiz question bank

This is a reviewable set of **500 multiple-choice questions** in twenty quizzes
of 25 questions each. Every question has four distinct options and one correct
answer. The answer order rotates across questions.

`questions.csv` contains the original 200 questions in eight quizzes (800 option rows).
`questions_extra.csv` contains 300 additional questions (1,200 option rows)
across twelve themed quizzes. Both files use the exact eight-column,
one-option-per-row format described in PR #1583 (`docs/quiz-bulk-import.md`).
The importer creates unpublished quizzes for an editor to review; it does not
publish them.

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
dotnet run --project src/QueenZone.Tools -- import-quiz-questions \
  --csv docs/backlog/queen-quiz-question-bank/questions.csv --dry-run
dotnet run --project src/QueenZone.Tools -- import-quiz-questions \
  --csv docs/backlog/queen-quiz-question-bank/questions_extra.csv --dry-run
```

After editorial review, use the import command with a connection string as
documented in PR #1583. **These questions have already been imported into
production** and the original ten 50-question quizzes were split there into
twenty 25-question parts. The CSVs now match that structure. Do not run a real
import against production again: the importer creates new quizzes rather than
updating existing ones.

To edit a question, change `build.py` or `build_extra.py` and regenerate that
file's questions and sources CSVs together. The scripts check question counts,
uniqueness of question text and options, and field lengths before writing.
