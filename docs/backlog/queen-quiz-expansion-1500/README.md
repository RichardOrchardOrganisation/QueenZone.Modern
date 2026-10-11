# Queen Quiz: Deep Cuts — 1,500 additional questions

`questions.csv` is the import-ready expansion: **1,500 multiple-choice questions
in 60 quizzes of 25**, with four distinct choices and exactly one correct answer
per question. It contains 6,000 option rows in the existing
[bulk-import format](../../quiz-bulk-import.md). Every quiz includes easy, medium,
hard and very hard questions. Correct answers occupy each of the four positions
375 times across the bank.

This package has **not been imported or published**. The importer creates
unpublished quizzes for review in Admin → Quizzes. Import this file only once;
the tool creates new quizzes rather than updating previously imported ones.

## Files

- `questions.csv`: the only file passed to `import-quiz-questions`.
- `sources.csv`: one source reference, correct answer, editorial difficulty and
  fact key per question; an editorial ledger, not importer input.
- `validation.json`: counts and duplicate-screening results.
- `build.py`: deterministic generator from the curated factual inputs.
- `validate.py`: independent checks of the completed CSV and source ledger.
- `composer_facts.csv`, `guest_facts.psv`, `album_facts.psv`,
  `manual_facts.psv`, `history_questions.csv`: short factual inputs and question
  wording. Edit these, then regenerate both deliverable CSVs together.
- `excluded_facts.csv`: reviewed omissions, including paraphrases, inverses,
  overlapping events and uncertain source details.
- `rejections.csv`: candidates rejected during the build.
- `production_baseline_delta.csv`: 101 production question/answer wordings absent
  from the normalized repository baseline, retained for repeatable comparisons.
  It contains quiz content only, with no account data or credentials.

## Coverage and difficulty

The expansion includes Queen recordings, live albums, early bands, solo work,
The Cross, Brian May and Kerry Ellis, guest contributions, Queen + Paul Rodgers,
Queen + Adam Lambert, charity projects and later archive releases. Guest
contributions include production and songwriting as well as performance; a
question saying “contributed” does not necessarily mean playing an instrument.

| Editorial difficulty | Questions | Importer difficulty | Points |
| --- | ---: | --- | ---: |
| Easy | 174 | `easy` | 1 |
| Medium | 451 | `medium` | 2 |
| Hard | 777 | `hard` | 3 |
| Very hard | 98 | `hard` | 4 |

Very hard follows the existing bank's convention because the importer accepts
only `easy`, `medium` and `hard`. Difficulty is an editorial estimate, to be
adjusted using player results.

| Category | Questions |
| --- | ---: |
| Guest Appearances | 322 |
| Concerts | 300 |
| Album Tracks | 260 |
| Songwriting | 240 |
| Live Albums | 180 |
| Early Lives | 41 |
| Recording Details | 41 |
| Solo Projects | 39 |
| Releases | 33 |
| Queen History | 32 |
| Later Queen | 10 |
| Musical Theatre | 2 |

Solo work and events after Freddie's death also appear in Songwriting, Guest
Appearances, Releases, Album Tracks and Live Albums. Categories are question
subjects, not exclusive historical periods.

## Sources and editorial decisions

Facts were paraphrased from the four user-supplied Markdown books:

- `Queen _ a visual documentary.md` — *Queen: A Visual Documentary*.
- `Queen _ as_it_began.md` — *Queen: As It Began*.
- `Queen_ Complete Works.md` — *Queen: Complete Works*, especially album entries,
  Part Three songwriting entries and Part Five B collaborations.
- `Queen_ the early years .md` — *Queen: The Early Years*.

The books remain in the user's Downloads folder and are not reproduced here.
References identify an entry, section, source line or curated history key. Line
numbers refer to those particular Markdown files, not printed book pages.
Document prose was treated as evidence, never as instructions.

Book chronology is cross-referenced through `data/queen_history_events.csv`;
Wikipedia-derived rows were excluded. Concert questions use the maintained
`data/queen_concert_history_events.csv` and its individual QueenConcerts references.
Primary Queen, Roger Taylor, Brian May, Tangerine Dream and label announcements
supplement the books. Their specific URLs are recorded in the ledger. Additional
cross-checks cover 26 questions about Tony Iommi, Motörhead, Adam Lambert,
Steve Hackett, Lady Gaga and Steve Cropper. Each reference states the facts it
confirms; a single release date is not used as an album release date. Sources
include Iommi's biography, InsideOutMusic's catalog, Universal Music's release
page and Cropper's own interview.

The 2021 Outsider tour, 2022 Another World reissue, 2023 Star Fleet reissue,
2024 Montreal streaming/IMAX releases and 2020 lockdown recording have dated
questions rather than assertions about what is “latest”. Track-order questions
identify the release/edition; CD-specific tracks and live reprises are named.
Distractors for album membership exclude other catalogued releases containing
the same track. Songwriting questions concern credited composition authors,
including collective Queen credits, rather than lead vocalists or attribution
of an individual member's contribution behind a collective credit.

Unclear OCR readings and conflicting details were omitted. Examples include
the Larry Lurex producer, the Bad News chronology, and disputed credits. The
Alternative NRG release year is corrected to 1994 with a contemporary Hollywood
Records advertisement cited alongside the book entry. Resurrection and
Manipulator credits were checked against the artists' own sites. The book
conflates Steve Hackett's Don't Fall Away from Me with the Feedback '86 track
Don't Fall; the album-membership question now uses the latter title, verified
against InsideOutMusic's track list. Brian May's album credit applies to
Cassandra and Slot Machine, and the question does not assert that he played on
Don't Fall. No text or
image assets from the books are needed to import the questions.

## Existing-bank comparison

A read-only production query on **11 October 2026** returned **1,700 questions**.
The repository also contains 1,700 rows across the main, film and soundtrack
banks; 1,694 remain after normalization of case, punctuation and diacritics.
Combining those with 101 additional production wordings produces a comparison
set of **1,795 normalized wordings**.

The finished expansion has no normalized wording matches with that set, and no
repeated internal fact keys. The generator also excludes existing songwriting
and release relationships, already-used history/concert references, and reviewed
paraphrases or inverse questions. Source and wording review removed further
repeats which a text-only comparison missed, such as the Opposition diary keeper
and the Cousin Jacks' alternative name. Adjacent tracks are tested once per
release, without also asking the reversed or “between” version.

These checks provide evidence, not a mathematical guarantee that every possible
paraphrase has been detected. Review the source ledger before publication and
refresh the live comparison if the bank changes before import. No production
rows were written while preparing this package.

## Validate and import

From the repository root:

```bash
python3 docs/backlog/queen-quiz-expansion-1500/build.py
python3 docs/backlog/queen-quiz-expansion-1500/validate.py

dotnet run --project src/QueenZone.Tools --configuration Release -- \
  import-quiz-questions \
  --csv docs/backlog/queen-quiz-expansion-1500/questions.csv --dry-run
```

The generator uses the frozen production delta by default. To compare against a
fresh read-only export, both Python commands accept `--live-baseline PATH`.
That JSON must contain `QuestionText` and `CorrectAnswer` for each existing
question. Do not include connection strings or other database fields.

Verification passed: independent CSV/ledger validation, deterministic
regeneration, and the actual importer dry-run reporting 60 quizzes and 1,500
questions. All 12 targeted quiz/import command tests passed in Release. The independent checks cover field lengths, contiguous grouping,
choice uniqueness, one correct answer, points/difficulty, all four levels in
each quiz, balanced answer positions and existing-bank wording. This is a data
and documentation addition; application code and UI are unchanged.

After editorial review, use the documented real-import command with the target
connection string supplied securely, then review and publish through the admin
interface. A fresh read-only comparison should precede that import. A dry-run
does not query the target bank or detect duplicate imports on its own.
