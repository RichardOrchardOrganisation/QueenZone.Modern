"""Build a reviewable Queen concert-history import from public concert itineraries.

Run from the repository root with ``python3 scripts/Build-QueenConcertHistory.py``.
The script reads QueenConcerts' factual date/venue listings, links every row to
the individual show page, and omits dates already represented by a concert in
the curated history CSV. It does not connect to the application database.
"""

import csv
import html
import re
import time
import urllib.request
from collections import Counter
from datetime import datetime
from pathlib import Path
from urllib.parse import urljoin


ROOT = Path(__file__).resolve().parents[1]
BASE = "https://www.queenconcerts.com"
TOURS = [
    "1970-early", "1973-queen1", "1974-queen2", "1975-sha", "1976-anato",
    "1976-summer", "1977-adatrna", "1977-adatreu", "1977-notwna",
    "1978-notweu", "1978-jazz", "1979-killers", "1979-crazy",
    "1980-gamena", "1980-gameeu", "1981-japan", "1981-southam",
    "1981-gluttons", "1981-wwry", "1982-hotspaceeu", "1982-hotspaceus",
    "1984-works", "1985-works", "1986-magic",
]
FIELDS = [
    "Title", "Summary", "EventDate", "DatePrecision", "Category",
    "Importance", "SourceType", "SourceKey", "SourceUrl",
]
SHOW = re.compile(
    r'<td class="qc-tour-date"><a href="(?P<url>[^"]+)"'
    r' title="(?P<title>[^"]+)">(?P<date>[^<]+)</a></td>\s*'
    r'<td class="qc-tour-place"><a href="[^"]+"[^>]*>(?P<place>[^<]+)</a></td>'
)
SPECIAL_SHOWS = [
    # The tour itineraries omit these actual performances. Mimed television
    # appearances and private post-Freddie guest sets are intentionally omitted.
    ("1977-10-06", "New London Theatre", "London, UK",
     "https://www.queenconcerts.com/live/queen/other.html",
     "Queen played a short live set for fans after filming the 'We Are the Champions' video."),
    ("1985-07-13", "Wembley Stadium", "London, UK",
     "https://www.queenconcerts.com/live/queen/other.html",
     "Queen performed at Live Aid at Wembley Stadium in London."),
    ("1992-04-20", "Wembley Stadium", "London, UK",
     "https://www.queenonline.com/live/1992-present",
     "Brian May, Roger Taylor and John Deacon performed with guests at the Freddie Mercury Tribute Concert in London."),
    ("1993-09-18", "Cowdray Park", "Midhurst, UK",
     "https://www.queenonline.com/features/on-the-spotjason-falloon",
     "Roger Taylor and John Deacon performed at Cowdray Park, billed as Queen."),
]


def fetch(path):
    url = urljoin(BASE, path)
    request = urllib.request.Request(url, headers={"User-Agent": "QueenZone-history-editor/1.0"})
    with urllib.request.urlopen(request, timeout=30) as response:
        return response.read().decode("utf-8")


def event(date, venue, place, url, summary=None, show_order=None):
    if summary is None:
        summary = f"Queen performed at {venue} in {place}." if venue else f"Queen performed in {place}."
    title = f"Queen at {venue}, {place}" if venue else f"Queen concert in {place}"
    if date == "1992-04-20":
        title = "The Freddie Mercury Tribute Concert at Wembley"
    elif date == "1993-09-18":
        title = "Roger Taylor and John Deacon at Cowdray Park"
    elif date == "1985-07-13":
        title = "Queen at Live Aid"
    if show_order:
        title += f" ({show_order} show)"
        summary = summary.rstrip(".") + f", the {show_order} show there that day."
    show_id = re.search(r"/detail/live/(\d+)/", url)
    key = f"queenconcerts-{show_id.group(1)}" if show_id else f"queen-concert-special-{date}"
    return {
        "Title": title, "Summary": summary, "EventDate": date,
        "DatePrecision": "ExactDate", "Category": "Concert",
        "Importance": "40" if date == "1993-09-18" else "30",
        "SourceType": "Curated", "SourceKey": key, "SourceUrl": url,
    }


def parse_tour(slug):
    page = fetch(f"/live/queen/{slug}.html")
    match = re.search(r"Tour itinerary \[(\d+) concerts\]", page)
    if not match:
        raise ValueError(f"Missing itinerary count: {slug}")
    rows = []
    for match_show in SHOW.finditer(page):
        raw = {key: html.unescape(value) for key, value in match_show.groupdict().items()}
        date = datetime.strptime(raw["date"][:10], "%d.%m.%Y").date().isoformat()
        place = raw["place"]
        detail = raw["title"].removeprefix("Concert: Queen live at the ")
        city = place.split(", ", 1)[0]
        city_start = detail.rfind(", " + city + ",")
        venue = detail[:city_start] if city_start >= 0 else ""
        if not venue:
            raise ValueError(f"Venue did not match city on {slug}: {raw}")
        order = re.search(r"\((1st|2nd) gig\)", raw["date"])
        show_order = {"1st": "first", "2nd": "second"}[order.group(1)] if order else None
        rows.append(event(date, venue, place, urljoin(BASE, raw["url"]), show_order=show_order))
    if len(rows) != int(match.group(1)):
        raise ValueError(f"Expected {match.group(1)} shows on {slug}, found {len(rows)}")
    return rows


def existing_concert_dates():
    with (ROOT / "data/queen_history_events.csv").open(encoding="utf-8-sig", newline="") as file:
        rows = csv.DictReader(file)
        return {
            row["EventDate"] for row in rows
            if row["Category"] == "Concert" and row["DatePrecision"] == "ExactDate"
        }


def main():
    rows = []
    for slug in TOURS:
        rows.extend(parse_tour(slug))
        time.sleep(0.2)
    rows.extend(event(*record) for record in SPECIAL_SHOWS)
    keys = [row["SourceKey"] for row in rows]
    if len(keys) != len(set(keys)):
        raise ValueError("Duplicate source key in concert listings")
    dates_to_skip = existing_concert_dates()
    rows = [row for row in rows if row["EventDate"] not in dates_to_skip]
    rows.sort(key=lambda row: (row["EventDate"], row["Title"]))
    for row in rows:
        if not 1970 <= int(row["EventDate"][:4]) <= 1993:
            raise ValueError(f"Date outside requested range: {row}")
        if len(row["Title"]) > 200 or len(row["Summary"]) > 1000:
            raise ValueError(f"Field too long: {row}")
    path = ROOT / "data/queen_concert_history_events.csv"
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=FIELDS)
        writer.writeheader()
        writer.writerows(rows)
    print(f"Wrote {len(rows)} concerts to {path}")
    print("By year:", dict(sorted(Counter(row["EventDate"][:4] for row in rows).items())))
    print(f"Skipped {len(dates_to_skip)} existing Queen concert dates")


if __name__ == "__main__":
    main()
