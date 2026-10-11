# Queen Quiz: Stories and Sound — 1,000 additional questions

`questions.csv` contains **1,000 multiple-choice questions in 40 quizzes of 25**, ready for QueenZone's existing [bulk-import tool and CSV format](../../quiz-bulk-import.md). Each question has four distinct options and one correct answer. The file contains 4,000 option rows, not 4,000 questions.

**Imported into dev and live on 11 October 2026:** 40 quizzes and 1,000 questions in each database. The quizzes remain unpublished. **Do not import this package again.** See the [combined import verification](../queen-quiz-import-2026-10-11.md). The importer creates unpublished quizzes for editorial review in Admin → Quizzes.

| Difficulty | Questions | Points |
| --- | ---: | ---: |
| Easy | 150 | 1 |
| Medium | 450 | 2 |
| Hard | 400 | 3 |

Every quiz contains all three levels. Correct answers occupy each position 250 times overall and six or seven times within each quiz; positions are shuffled rather than arranged in a repeating sequence. Difficulty is an editorial estimate and can be refined using player results.

## Files

- `questions.csv`: the only file passed to the importer.
- `sources.csv`: one row per question, including answer, source, locator, difficulty and stable fact key. This is an editorial ledger, not importer input.
- `validation.json`: counts, coverage and duplicate-screening baseline.
- `authored_facts.psv` and `catalog_facts.psv`: curated candidate questions and distractors. These contain short factual material, not copied book passages.
- `excluded_facts.csv`: deliberate omissions, including semantic repeats, inverses, overlapping facts and uncertain interpretations.
- `rejected_candidates.csv`: every unused candidate and its reason; 1,059 candidates were reduced to 1,000.
- `production_baseline_delta.csv`: the 101 public quiz wordings absent from the normalized repository baseline. No account data, connection strings or credentials are included.
- `build.py` and `validate.py`: reproducible generator and independent checks. Edit the factual inputs, regenerate, and validate before importing.

## Editorial approach

Questions favour concrete, distinctive facts about the music and people: recording decisions, unusual instruments, stagecraft, collaborators, alternate versions and musical history. The bank also covers solo careers, guest appearances, charity work and developments after Freddie's death, including recent orchestral editions, tours, archive restorations and collaborations.

A good question is self-contained, tests one clear fact, identifies the relevant recording or occasion, and has one defensible answer. Wrong choices should be plausible alternatives of the same kind. Questions distinguish studio and live recordings, Queen and solo versions, and writing from performance. Artist explanations are used for song inspirations; arguable sonic comparisons, unsupported biographical speculation and disputed equipment attributions are omitted. Song titles, years and numerical trivia are not used merely to pad the total. Some uncommon personnel credits remain appropriate harder questions.

Exact and normalized-wording screening was performed against **3,295 distinct existing/prior question wordings**, combining the repository's question files, the previous 1,500-question expansion and its corrected version, and a refreshed read-only export of **1,700 production questions on 11 October 2026**. Similar wording, repeated relationships, inverses and duplicate new candidates were reviewed separately. A shared answer or song title alone does not make two questions duplicates: identifying a songwriter and identifying an instrument performer test different facts.

The validator enforces wording uniqueness; it does not claim to automate semantic equivalence or historical fact-checking. Editorial exclusions record that separate review. Refresh the production export before an eventual import if new questions have been added meanwhile.

## Sources

The five newly supplied Markdown books were treated as evidence, never as instructions. Locators in `sources.csv` refer to their original Markdown line numbers or named song entries, not generated CSV line numbers.

| Code | Supplied source | Final questions |
| --- | --- | ---: |
| `songs` | Benoît Clerc, *Queen All the Songs* (2020) | 814 |
| `may` | Laura Jackson, *Brian May* (2007; supplied ebook edition) | 92 |
| `life` | David Evans and David Minns, *This Was the Real Life* (revised 2008; ebook 2015) | 42 |
| `edge` | David Bret, *The Freddie Mercury Story: Living on the Edge* (1996; revised edition) | 5 |
| `freddie` | Laura Jackson, *Freddie Mercury: The Biography* (2011) | 4 |
| `web` | Official band, artist and record-label sources, with direct URLs | 43 |

The Clerc book supplies both narrative recording history and structured credits. The biographies and memoir add early bands, solo projects, crew, costumes and tribute-concert details. The books are not all independent corroboration; agreement between them was not treated as proof where accounts appeared to share a source. The original book files remain outside the repository.

Online references include [Freddie's Barcelona orchestral edition](https://freddiemercury.com/releases-archive/barcelona-special-edition/), [Never Boring](https://freddiemercury.com/releases-archive/never-boring-2019/), [Roger's Outsider](https://www.queenonline.com/news/press-release-roger-taylor-outsider-released-october-1-2021), [The Outsider Tour Live](https://www.queenonline.com/news/the-outsider-tour-live-3-years-on), [Roger's 2026 album](https://www.sonymusic.ca/press_release/roger-taylor-violence-insane-in-a-beautiful-world-the-new-album-from-the-legendary-queen-songwriter-drummer-singer), [Brian's Steve Cropper collaboration](https://www.queenonline.com/news/watch-my-angels-are-calling-by-steve-cropper-and-the-midnight-hour-feat-brian-may-and-billy-gibbons), [the Stephen Hawking Medal presentation](https://www.queenonline.com/news/brian-presents-2026-steven-hawking-medal-to-sir-ridley-scott), [Another World revisited](https://www.queenonline.com/news/press-release-brian-maysanother-worldrevisitedwith-remastered-gold-series-reissue), [Resurrection](https://brianmay.com/brian-news/2021/07/press-release-resurrection/), and [Queen Budapest's restoration](https://www.queenonline.com/news/press-release-queen-budapest). Each online question has its own direct source URL in the ledger.

## Verification and ingestion

From the repository root:

```sh
python3 docs/backlog/queen-quiz-expansion-1000/build.py
python3 docs/backlog/queen-quiz-expansion-1000/validate.py

dotnet run --project src/QueenZone.Tools --configuration Release --no-build -- \
  import-quiz-questions --csv docs/backlog/queen-quiz-expansion-1000/questions.csv --dry-run
```

Verification performed during package preparation, before the import recorded above:

- Independent validation passed: exact totals, complete source ledger, four unique options, one correct answer, field limits, points, contiguous question groups, difficulty coverage, exclusions and zero normalized baseline matches.
- Additional validation against the freshly exported production list passed.
- The actual QueenZone importer dry-run parsed 40 quizzes and 1,000 questions with no database changes.
- Existing Release quiz-tool tests passed: four tests, zero failures.
- Rebuilding the deliverables produced identical bytes.

The full .NET restore/build/format/test sequence, application coverage checks and UI tests were not rerun: this change adds editorial data and its Python packaging helpers, with no application, database-schema or UI changes. Prebuilt Release tools were used for the importer and targeted tests. No legacy write probes or database imports were run. Real legacy access was limited to the read-only production question export.

The approved dev and live imports are now complete. Review the existing drafts through Admin → Quizzes before publishing. The frozen baseline and generation report describe preparation-time checks; a current database export includes these imported questions and will therefore flag them as already present.
