# Queen concert dates for On This Day

`data/queen_concert_history_events.csv` is a separate, reviewable import for the
existing `QueenHistoryEvents` read model. It adds **637** dated concert entries
from 1970 through 1993 without changing or reimporting
`data/queen_history_events.csv`. Each row has the exact nine-column shape
expected by `import-history` and links to the individual concert listing used
for its date and venue. Source keys are stable for repeatable upserts.

The [official Queen live archive](https://www.queenonline.com/live) supplies
year-by-year context and identifies the 1992 Freddie Mercury Tribute Concert.
It does not enumerate every concert date. The detailed date and venue list comes
from [QueenConcerts](https://www.queenconcerts.com/live/queen/1970-early.html),
with a link to the particular show in every ordinary import row. All titles and
summaries here are new, neutral wording based on the factual listings; no
concert reviews or narrative text were copied. Queen's
[1993 Cowdray Park account](https://www.queenonline.com/features/on-the-spotjason-falloon)
supports the final entry, which explicitly names Roger Taylor and John Deacon
as the performers billed as Queen.

The generator covers the tour itineraries through the 1986 Magic Tour, plus the
live fan-club performance after the 1977 *We Are the Champions* video shoot,
Live Aid, the 1992 tribute, and Cowdray Park in 1993. It omits mimed TV spots,
cancelled dates, and unrelated solo tours. The pre-existing history CSV already
contains exact-date concert entries for 131 calendar dates, including Live Aid
and the 1992 tribute; the generator excludes those dates to avoid duplicate
On This Day cards. Queen played no new tour between the 1986 Magic Tour and the
1992 tribute, so those intervening years have no tour entries here.

Most entries use Importance 30, matching ordinary concert records in the
existing file. The 1993 performance uses 40. The import command publishes
records immediately, so the CSV is supplied for editorial review and has **not**
been run against a database.

## Regenerate and validate

```bash
python3 scripts/Build-QueenConcertHistory.py
dotnet run --project src/QueenZone.Tools -- import-history \
  --csv data/queen_concert_history_events.csv --dry-run
```

The generator reads public tour pages over HTTPS and checks each page's stated
show count, unique source keys, date range, and field lengths. It uses only the
Python standard library. The checked-in CSV is the reviewed output; a future
regeneration should be diffed because the source website can change.
