# Quiz bulk importer

Bulk-creates quizzes from a CSV file using the `import-quiz-questions` command in
`QueenZone.Tools`. Intended for loading questions generated from an article, book, or other
text source without hand-typing them one at a time into the admin quiz builder.

Every quiz format is **multiple choice**: each question has 2–4 answer options and exactly one
is correct. There is no other question type (no true/false, free text, or multi-select).

## What it does

- Reads a CSV of question/option rows and groups them into one or more quizzes.
- Creates each quiz as a brand-new, **unpublished** quiz — identical to using
  Admin → Quizzes → New Quiz by hand.
- Never touches an existing quiz, and never publishes anything. An admin reviews the imported
  quiz in [Admin → Quizzes](/admin/quizzes), edits it if needed, and publishes it manually.
- Validates the whole file before writing anything to the database. If any question in the file
  is invalid, nothing is imported — you fix the CSV and re-run.

## Running it

```bash
# Preview only — parses the CSV and reports counts, no database changes.
dotnet run --project src/QueenZone.Tools -- import-quiz-questions --csv <path> --dry-run

# Real import.
dotnet run --project src/QueenZone.Tools -- import-quiz-questions --csv <path> --connection-string <connection-string>
```

The connection string can also come from the `ConnectionStrings__QueenZoneLegacy` environment
variable instead of `--connection-string`, same as `import-quotes`/`import-trivia`/`import-history`.

## CSV format

One row = one answer option. Rows are grouped into questions and quizzes by **adjacency**: all
rows belonging to the same quiz must be next to each other in the file, and within a quiz, all
rows belonging to the same question must be next to each other. This is how a spreadsheet of
generated Q&A naturally comes out — one quiz block, then its questions in order, then each
question's options in order.

Header row (required, exact match):

```
QuizTitle,QuizDescription,QuestionText,Category,Difficulty,Points,OptionText,IsCorrect
```

| Column | Required | Applies to | Notes |
|---|---|---|---|
| `QuizTitle` | Yes | Quiz | Max 200 characters. Starts a new quiz when it changes from the previous row. Reusing a title later in the file (non-contiguous) is rejected. |
| `QuizDescription` | No | Quiz | Max 1000 characters. Only the value on the first row of each quiz is used; leave blank on later rows for that quiz. |
| `QuestionText` | Yes | Question | Max 500 characters. Starts a new question when it changes from the previous row (within the same quiz). Reusing question text later within the same quiz (non-contiguous) is rejected. |
| `Category` | No | Question | Max 100 characters, free text (e.g. `Band Members`, `Albums`). Only the value on the first row of each question is used. |
| `Difficulty` | No | Question | One of `easy`, `medium`, `hard` (case-sensitive, lowercase). Only the value on the first row of each question is used. |
| `Points` | No | Question | Whole number, 1–100. Defaults to 1 if blank. Only the value on the first row of each question is used. |
| `OptionText` | Yes | Option | Max 200 characters. One row per answer option. |
| `IsCorrect` | Yes | Option | `true` or `false`. Exactly one option per question must be `true`. |

Quiz-level limits (same as the admin quiz builder): 1–50 questions per quiz, 2–4 options per
question.

## Example

```csv
QuizTitle,QuizDescription,QuestionText,Category,Difficulty,Points,OptionText,IsCorrect
Queen Trivia,A quiz about Queen,Who was the lead singer?,Band Members,easy,1,Freddie Mercury,true
Queen Trivia,A quiz about Queen,Who was the lead singer?,Band Members,easy,1,Brian May,false
Queen Trivia,A quiz about Queen,What year was the band formed?,,hard,2,1970,true
Queen Trivia,A quiz about Queen,What year was the band formed?,,hard,2,1971,false
Queen Trivia,A quiz about Queen,What year was the band formed?,,hard,2,1973,false
News of the World,An album-focused quiz,Which album features Bohemian Rhapsody?,Albums,medium,1,A Night at the Opera,true
News of the World,An album-focused quiz,Which album features Bohemian Rhapsody?,Albums,medium,1,A Day at the Races,false
```

This creates two unpublished quizzes: "Queen Trivia" (2 questions) and "News of the World"
(1 question).

## Errors

The importer fails fast with a descriptive message and makes no database changes if:

- The header row doesn't match exactly.
- A row has the wrong number of columns.
- A required field is blank, or a text field exceeds its max length.
- `Difficulty` is set to something other than `easy`/`medium`/`hard`.
- `IsCorrect` isn't `true`/`false`, or a question doesn't have exactly one correct option.
- A question has fewer than 2 or more than 4 options, or a quiz has fewer than 1 or more than
  50 questions.
- The same `QuizTitle` or (within a quiz) the same `QuestionText` appears again after other rows
  have already moved on to a different quiz/question — i.e. the rows aren't contiguous.
