# Queen soundtrack movie quizzes

This package contains two importer-ready multiple-choice quizzes:

- **Flash Gordon: The Movie** — 50 questions
- **Highlander: The Movie** — 50 questions

Each quiz contains 17 easy, 17 medium, and 16 hard questions. Every question has four unique options and exactly one correct answer. Correct-answer positions are balanced across the four slots.

The questions are about the films: their plots, characters, cast and crew, production, and Queen's music for each production. They do not treat a film's fictional events as real Queen history.

Import preview:

```bash
dotnet run --project src/QueenZone.Tools -- import-quiz-questions --csv docs/backlog/queen-soundtrack-movie-quizzes/questions.csv --dry-run
```

`sources.csv` is an editorial fact-check ledger and is not passed to the importer.
