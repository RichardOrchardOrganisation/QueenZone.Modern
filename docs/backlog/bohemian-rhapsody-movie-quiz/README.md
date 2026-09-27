# Bohemian Rhapsody movie quiz

This package contains 100 multiple-choice questions about the 2018 film *Bohemian Rhapsody*, split into two 50-question quizzes to satisfy the importer limit.

Questions deliberately test the film's story, credited cast and crew, soundtrack, production, and awards. Plot questions describe the film's dramatized version of events and should not be treated as claims about Queen's real history.

Import preview:

```bash
dotnet run --project src/QueenZone.Tools -- import-quiz-questions --csv docs/backlog/bohemian-rhapsody-movie-quiz/questions.csv --dry-run
```

`sources.csv` is an editorial fact-check ledger and is not passed to the importer.
