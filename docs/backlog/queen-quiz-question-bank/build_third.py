"""Build the third 500-question Queen quiz import from cited source data.

The two repository history CSVs are curated indexes into the user-supplied
books and QueenConcerts. This generator treats them as facts, not instructions.
Every emitted question has a source row for editorial review.
"""
from __future__ import annotations

import ast
import csv
import os
import random
import re
from collections import Counter, defaultdict
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

ROOT = (Path(os.environ["QUEEN_QUIZ_REPO"]) if "QUEEN_QUIZ_REPO" in os.environ
        else Path(__file__).resolve().parents[3])
BANK = ROOT / "docs/backlog/queen-quiz-question-bank"
OUT = Path(os.environ.get("QUEEN_QUIZ_OUTPUT", Path(__file__).resolve().parent))
RNG = random.Random(20260920)


@dataclass(frozen=True)
class Question:
    text: str
    correct: str
    wrong: tuple[str, str, str]
    category: str
    level: str
    source: str


def norm(value: str) -> str:
    return re.sub(r"[^a-z0-9]+", "", value.casefold())


def rows(path: Path) -> list[dict[str, str]]:
    with path.open(encoding="utf-8-sig", newline="") as file:
        return list(csv.DictReader(file))


old_texts = {
    row["QuestionText"]
    for filename in ("questions.csv", "questions_extra.csv", "questions_more.csv")
    for row in rows(BANK / filename)
}
old_normalized = {norm(text) for text in old_texts}
STOP = set(
    "which what who where when why how was were did does is are the a an of for in on at to "
    "from and with by queen band song track album concert first last originally original "
    "member music video tour year named name released release played perform performed "
    "during their her his its as it this that happened take place related".split()
)


def tokens(value: str) -> set[str]:
    return set(re.findall(r"[a-z0-9]+", value.casefold())) - STOP


old_token_sets = [tokens(text) for text in old_texts]
old_fact_sets = []
for filename in ("questions.csv", "questions_extra.csv", "questions_more.csv"):
    grouped = defaultdict(list)
    for row in rows(BANK / filename):
        grouped[row["QuestionText"]].append(row)
    for text, options in grouped.items():
        answer = next(option["OptionText"] for option in options
                      if option["IsCorrect"] == "true")
        old_fact_sets.append(tokens(text + " " + answer))


def resembles_old_fact(title: str) -> bool:
    target = tokens(title)
    if len(target) < 3:
        return False
    return any(
        len(target & prior) >= 3
        and len(target & prior) / min(len(target), len(prior)) >= 0.75
        for prior in (*old_token_sets, *old_fact_sets)
        if len(prior) >= 3
    )


questions: list[Question] = []


def add(text: str, correct: str, wrong: list[str] | tuple[str, ...],
        category: str, level: str, source: str) -> None:
    if len(wrong) != 3 or len({norm(option) for option in (correct, *wrong)}) != 4:
        raise ValueError(f"Invalid answers for {text}: {correct}, {wrong}")
    questions.append(Question(text, correct, tuple(wrong), category, level, source))


def round_robin(items, count: int, key):
    groups = defaultdict(list)
    for item in items:
        groups[key(item)].append(item)
    for group in groups.values():
        RNG.shuffle(group)
    result = []
    while len(result) < count:
        moved = False
        for group in groups.values():
            if group and len(result) < count:
                result.append(group.pop())
                moved = True
        if not moved:
            raise ValueError(f"Only {len(result)} candidates available; need {count}")
    return result


def concert_questions() -> None:
    all_rows = rows(ROOT / "data/queen_concert_history_events.csv")
    date_counts = Counter(row["EventDate"] for row in all_rows)
    events = []
    for row in all_rows:
        title = re.sub(r" \([^)]*\)$", "", row["Title"])
        if not title.startswith("Queen at ") or date_counts[row["EventDate"]] != 1:
            continue
        if not ("1970" <= row["EventDate"][:4] <= "1986"):
            continue
        parts = title[9:].rsplit(", ", 2)
        if len(parts) != 3:
            continue
        venue, city, country = parts
        if country == "Bophuthatswana" or any(len(part) > 90 for part in parts):
            continue
        if resembles_old_fact(row["Title"]):
            continue
        events.append((row, venue, city, country))
    RNG.shuffle(events)
    unique = {}
    for event in events:
        unique.setdefault((event[1].casefold(), event[2].casefold()), event)
    events = list(unique.values())
    selected = round_robin(events, 200, lambda e: (e[0]["EventDate"][:4], e[3]))
    type_counts = Counter()
    for index, (row, venue, city, country) in enumerate(selected):
        date = datetime.strptime(row["EventDate"], "%Y-%m-%d").strftime("%-d %B %Y")
        year = row["EventDate"][:4]
        same_era = [e for e in events if e[0]["EventDate"][:4] == year and e[0] is not row]
        mode = min(
            ("venue", "city", "country"),
            key=lambda name: (type_counts[name] / {"venue": 90, "city": 70, "country": 40}[name], name),
        )
        type_counts[mode] += 1
        if mode == "venue":
            pool = list(dict.fromkeys(e[1] for e in same_era if e[1] != venue))
            text = f"At which venue did Queen perform in {city} on {date}?"
            correct = venue
            level = "hard" if index % 2 else "very hard"
        elif mode == "city":
            pool = list(dict.fromkeys(e[2] for e in same_era if e[2] != city))
            text = f"Which city hosted Queen's {date} show at {venue}?"
            correct = city
            level = "hard"
        else:
            pool = list(dict.fromkeys(e[3] for e in events if e[3] != country))
            text = f"In which country did Queen play at {venue} on {date}?"
            correct = country
            level = "medium"
        if len(pool) < 3:
            field = {"venue": 1, "city": 2, "country": 3}[mode]
            pool = list(dict.fromkeys(e[field] for e in events if e[field] != correct))
        if len(pool) < 3:
            raise ValueError(f"Too few concert distractors for {text}")
        wrong = random.Random(index + 11407).sample(pool, 3)
        add(text, correct, wrong, "Concerts", level, row["SourceUrl"])
    print("Concert modes:", dict(type_counts))


def history_questions() -> None:
    all_rows = rows(ROOT / "data/queen_history_events.csv")
    events = []
    for row in all_rows:
        if row["SourceType"] not in {"AsItBeganBook", "VisualDocumentaryBook"}:
            continue
        # This legacy event index says February, whereas Queen's current
        # official biography dates John's formal admission to 1 March 1971.
        if row["SourceKey"] == "john-deacon-officially-joins-queen-as-its-fourth-and-final-member-1971-02":
            continue
        summary = row["Summary"].strip()
        if "Bad Godesberg, Frankfurt" in summary:
            continue  # Bad Godesberg is near Bonn, not Frankfurt.
        if row["Category"] == "Birthday" or not (45 <= len(summary) <= 180):
            continue
        if re.search(r"\b(?:19|20)\d{2}\b", summary) or resembles_old_fact(summary):
            continue
        events.append(row)
    used = set()
    picked = {}
    months = [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    ]
    for mode, count, condition in (
        ("date", 20, lambda row: row["DatePrecision"] == "ExactDate"
         and not re.search(r"\b\d{1,2} (?:" + "|".join(months) + r")\b", row["Summary"], re.I)),
        ("month", 30, lambda row: len(row["EventDate"]) >= 7
         and not any(re.search(r"\b" + month + r"\b", row["Summary"], re.I)
                     for month in months)),
        ("decade", 50, lambda row: True),
        ("year", 100, lambda row: True),
    ):
        pool = [row for row in events if row["SourceKey"] not in used and condition(row)]
        chosen = round_robin(pool, count, lambda row: (row["Category"], row["EventDate"][:4]))
        picked[mode] = chosen
        used.update(row["SourceKey"] for row in chosen)
    for mode, selected in picked.items():
        for index, row in enumerate(selected):
            title = row["Summary"].strip().rstrip(".")
            year = int(row["EventDate"][:4])
            source = (
                ("Queen: As It Began" if row["SourceType"] == "AsItBeganBook"
                 else "Queen: A Visual Documentary")
                + f" (user-supplied text); history key {row['SourceKey']}"
            )
            if mode == "date":
                date = datetime.strptime(row["EventDate"], "%Y-%m-%d").strftime("%-d %B %Y")
                pool = [other for other in events
                        if other["SourceKey"] != row["SourceKey"]
                        and other["EventDate"] != row["EventDate"]
                        and other["EventDate"][:4] == row["EventDate"][:4]
                        and len(other["Summary"]) <= 180]
                if len(pool) < 3:
                    pool = [other for other in events
                            if other["SourceKey"] != row["SourceKey"]
                            and other["EventDate"] != row["EventDate"]
                            and len(other["Summary"]) <= 180]
                wrong_events = random.Random(index + 338).sample(pool, 3)
                wrong = [other["Summary"].strip().rstrip(".") for other in wrong_events]
                source += "; distractors: " + ", ".join(other["SourceKey"] for other in wrong_events)
                add(f"What happened in Queen's story on {date}?", title, wrong,
                    "Queen Timeline", "very hard", source)
            elif mode == "month":
                month = months[int(row["EventDate"][5:7]) - 1]
                wrong = random.Random(index + 428).sample([item for item in months if item != month], 3)
                add(f"In which month of {year} did this happen: {title}?",
                    month, wrong, "Queen Timeline", "hard", source)
            elif mode == "decade":
                decade = year // 10 * 10
                answer = f"{decade}s"
                decades = [f"{item}s" for item in range(1940, 2020, 10) if item != decade]
                wrong = random.Random(index + 520).sample(decades, 3)
                add(f"In which decade did this happen: {title}?",
                    answer, wrong, "Queen Timeline", "easy", source)
            else:
                pool = [str(value) for value in range(max(1946, year - 4), year + 5) if value != year]
                wrong = random.Random(index + 624).sample(pool, 3)
                add(f"In what year did this Queen-related event occur: {title}?",
                    str(year), wrong, "Queen Timeline",
                    "medium" if row["Category"] != "SiteHistory" else "hard", source)
    print("History modes:", {key: len(value) for key, value in picked.items()})


def songwriting_questions() -> None:
    syntax = ast.parse((BANK / "build_more.py").read_text(encoding="utf-8"))
    credits = None
    for node in syntax.body:
        if (isinstance(node, ast.Assign)
                and isinstance(node.targets[0], ast.Name)
                and node.targets[0].id == "WRITERS"
                and isinstance(node.value, ast.Dict)):
            credits = ast.literal_eval(node.value)
            break
    assert credits
    candidates = []
    for writer, songs in credits.items():
        for song in songs.split("|"):
            if any(song.casefold() in old.casefold()
                   and ("wrote" in old.casefold() or "written" in old.casefold())
                   for old in old_texts):
                continue
            candidates.append((writer, song))
    chosen = round_robin(candidates, 40, lambda pair: pair[0])
    for writer, song in chosen:
        other = [name for name in credits if name != writer]
        add(f"Which Queen member has the individual writing credit for '{song}'?",
            writer, other, "Songwriting", "hard",
            "Queen: Complete Works (user-supplied text), Part Three: The Songs; " + song)


def manual_facts() -> None:
    def facts(block: str, category: str, source: str) -> None:
        for line in block.strip().splitlines():
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            parts = [part.strip() for part in line.split("|")]
            if len(parts) != 6:
                raise ValueError(f"Manual fact needs six fields: {line}")
            text, correct, a, b, c, level = parts
            add(text, correct, [a, b, c], category, level, source)

    facts("""
For how many consecutive weeks did 'Bohemian Rhapsody' top the UK chart on its original release?|Nine|Four|Six|Twelve|medium
Which vocal group originally recorded the song Freddie covered as 'The Great Pretender'?|The Platters|The Drifters|The Coasters|The Temptations|medium
What is the Mercury Phoenix Trust's annual dress-up fundraiser launched in 2010 called?|Freddie For A Day|Mercury Week|Queen For A Day|The Freddie Challenge|easy
Which Swiss town hosts the official fan party held in Freddie Mercury's honour on his birthday?|Montreux|Zurich|Geneva|Lucerne|medium
In what year did Freddie Mercury make a one-night stage appearance in the musical 'Time'?|1987|1985|1989|1991|hard
    """, "Freddie Mercury", "https://www.queenonline.com/freddie_mercury")

    facts("""
On which Queen studio song did Freddie Mercury play rhythm acoustic guitar?|Crazy Little Thing Called Love|Killer Queen|Another One Bites the Dust|We Are the Champions|easy
""", "Freddie Mercury", "Queen: Complete Works (user-supplied text), Part Three: The Songs; Crazy Little Thing Called Love")

    facts("""
Which composer wrote the 2002 Winter Olympics opening music 'The Fire Within', featuring Brian May on guitar?|Michael Kamen|Hans Zimmer|John Williams|Vangelis|hard
Which 1996 film received a mini-opera from Brian May?|Pinocchio|The Hunchback of Notre Dame|Space Jam|The Phantom|hard
In what year did Queen receive a star on the Hollywood Walk of Fame?|2002|1992|2006|2012|medium
Which music hall of fame inducted Queen in June 2003?|Songwriters Hall of Fame|Rock and Roll Hall of Fame|UK Music Hall of Fame|Rhythm and Blues Hall of Fame|medium
In which South African city was the first major 46664 concert held in 2003?|Cape Town|Johannesburg|Durban|Pretoria|medium
Which major 2002 event featured Brian May playing from Buckingham Palace's roof?|The Golden Jubilee|Live 8|The Diamond Jubilee|The Royal Variety Performance|easy
Which 1986 fantasy film featured music developed by Queen after their 'Flash Gordon' score?|Highlander|Labyrinth|Legend|Willow|easy
""", "Brian May", "https://www.queenonline.com/brian_may")

    facts("""
Which Queen B-side was Roger Taylor's song 'I'm in Love with My Car'?|Bohemian Rhapsody|Killer Queen|Somebody to Love|Radio Ga Ga|medium
Supporters of which football club did Roger Taylor help during a takeover fight?|Manchester United|Arsenal|Chelsea|Liverpool|hard
What instrument did Roger Taylor first learn as a child before guitar and drums?|Ukulele|Violin|Trumpet|Accordion|medium
Which choir did Roger Taylor join while at school in Cornwall?|Truro Cathedral Choir|King's College Choir|Westminster Abbey Choir|St Paul's Cathedral Choir|hard
How many studio albums did Roger Taylor's band The Cross release?|Three|Two|Four|Five|medium
Which Roger Taylor song for the 46664 project incorporated Nelson Mandela's spoken voice?|Invincible Hope|Say It's Not True|The Unblinking Eye|Foreign Sand|hard
Which Roger Taylor song for 46664 addresses an HIV-positive diagnosis?|Say It's Not True|Invincible Hope|Happiness?|The Key|hard
Which country inspired Roger Taylor's song 'People on Streets' through the inequality he saw there?|India|Brazil|South Africa|Mexico|hard
""", "Roger Taylor", "https://www.queenonline.com/roger_taylor")

    facts("""
Which group inspired young John Deacon after he bought its first two albums?|The Beatles|The Rolling Stones|The Who|The Kinks|easy
Whose 'Hit Parade' radio programme did young John Deacon record on a reel-to-reel tape deck?|Alan Freeman's|John Peel's|Tony Blackburn's|Kenny Everett's|hard
Which ballet marked John Deacon's final onstage appearance with Queen in 1997?|Ballet for Life|Swan Lake|The Nutcracker|Giselle|medium
Which dancer, alongside Freddie Mercury, was honoured by the 1997 'Ballet for Life' production?|Jorge Donn|Rudolf Nureyev|Mikhail Baryshnikov|Carlos Acosta|hard
""", "John Deacon", "https://www.queenonline.com/john_deacon")

    facts("""
How many CDs are in the 2024 'Queen I' Collector's Edition box?|Six|Four|Five|Eight|medium
How many tracks were included in the 2024 'Queen I' 6CD box set?|63|43|50|72|very hard
How many brand-new mixes were included in the 2024 'Queen I' box set?|43|25|63|11|very hard
Which studio's early Queen demos have their own disc in the 2024 'Queen I' set?|De Lane Lea|Abbey Road|Mountain|Musicland|hard
How many pages are in the book supplied with the 2024 'Queen I' Collector's Edition?|108|64|112|200|very hard
Which song appears on the 2024 'Queen I Live' disc in a March 1976 San Diego performance?|Hangman|White Queen|Brighton Rock|Teo Torriatte|very hard
At which London college were two August 1970 live performances featured on the 2024 'Queen I Live' disc recorded?|Imperial College|Ealing College of Art|Chelsea College|King's College|hard
""", "Archive Releases", "https://www.queenonline.com/news/queen-i-queen-remixed-remastered-and-expanded-out-now")

    facts("""
Which original Queen studio album received a new 2026 mix in a 5CD+2LP Collector's Edition?|Queen II|The Game|Jazz|A Day at the Races|easy
How many CDs are in the 2026 'Queen II' Collector's Edition?|Five|Three|Six|Eight|medium
How many pages are in the book included with the 2026 'Queen II' Collector's Edition?|112|64|108|200|very hard
Who engineered the original 'Queen II' album at Trident Studios?|Mike Stone|Geoff Emerick|Reinhold Mack|David Richards|hard
Which producer is credited alongside Queen on the original 'Queen II' album?|Roy Thomas Baker|George Martin|Reinhold Mack|David Richards|medium
Who provided additional production on the original 'Queen II' album?|Robin Geoffrey Cable|John Anthony|Mike Stone|David Richards|very hard
Who is credited for the typography of the original 'Queen II' sleeve?|Ridgeway Watt|Mick Rock|Richard Gray|Roger Taylor|very hard
Which unusual instrument was Roy Thomas Baker credited with playing on 'Queen II'?|Castanets|Bagpipes|Sitar|Theremin|hard
Which pair executive-produced the 2026 'Queen II' Collector's Edition?|Brian May and Roger Taylor|Freddie Mercury and John Deacon|Roy Thomas Baker and Mick Rock|Jim Beach and Mike Stone|medium
""", "Archive Releases", "https://www.queenonline.com/news/queen-ii-out-now")

    facts("""
How many discs make up the 2022 'The Miracle' Collector's Edition box?|Eight|Five|Six|Ten|hard
Which previously omitted song was restored to the 1989 album's original planned LP sequence in the 2022 'Miracle' set?|Too Much Love Will Kill You|Face It Alone|Dog With a Bone|No-One But You|medium
What is the title of the disc of demos and unreleased recordings in the 2022 'Miracle' set?|The Miracle Sessions|The Miracle Live|The Works Sessions|The Lost Years|hard
How many previously unheard songs were promoted for the 2022 'Miracle Sessions' disc?|Six|Three|Ten|Twelve|hard
Which Queen member's 1989 'Breakthru' video-set interview is described as his last in the 2022 'Miracle' release notes?|John Deacon|Brian May|Roger Taylor|Freddie Mercury|hard
Which designer explained the creation of 'The Miracle' album cover for the 2022 box set?|Richard Gray|Mick Rock|David Bailey|Storm Thorgerson|hard
""", "Archive Releases", "https://www.queenonline.com/news/queen-the-miracle-collectors-edition-out-now")

    facts("""
Who portrayed Freddie Mercury in the 2018 film 'Bohemian Rhapsody'?|Rami Malek|Taron Egerton|Gwilym Lee|Sacha Baron Cohen|easy
Who portrayed Brian May in the 2018 film 'Bohemian Rhapsody'?|Gwilym Lee|Rami Malek|Ben Hardy|Joe Mazzello|medium
Who portrayed Roger Taylor in the 2018 film 'Bohemian Rhapsody'?|Ben Hardy|Gwilym Lee|Joe Mazzello|Rami Malek|medium
Who portrayed John Deacon in the 2018 film 'Bohemian Rhapsody'?|Joe Mazzello|Ben Hardy|Gwilym Lee|Rami Malek|medium
Who portrayed Mary Austin in the 2018 film 'Bohemian Rhapsody'?|Lucy Boynton|Aidan Gillen|Michelle Duncan|Gemma Arterton|medium
Which Queen concert appearance forms the climax of the film 'Bohemian Rhapsody'?|Live Aid|The Freddie Mercury Tribute Concert|Rock in Rio|Hyde Park 1976|easy
Which song was added to the film's extended Live Aid sequence alongside 'Crazy Little Thing Called Love'?|We Will Rock You|Under Pressure|A Kind of Magic|The Show Must Go On|hard
Which 2018 Queen film won the 2019 BAFTA for Best Sound?|Bohemian Rhapsody|Rocketman|A Star Is Born|Yesterday|medium
""", "Queen on Screen", "https://www.queenonline.com/news/bohemian-rhapsody-soundtrack-out-today; https://www.queenonline.com/news/press-release-bohemian-rhapsody-digital-dvd-and-blu-ray-release-details; https://www.queenonline.com/news/bohemian-rhapsody-wins-two-baftas")

    facts("""
Which comedian and writer wrote the story for Queen's 'We Will Rock You' musical?|Ben Elton|Richard Curtis|Rowan Atkinson|Stephen Fry|medium
Which Hollywood actor took an early co-producing interest in the 'We Will Rock You' musical?|Robert De Niro|Al Pacino|Tom Hanks|Robert Redford|hard
At which London theatre did 'We Will Rock You' open in May 2002?|Dominion Theatre|London Palladium|Royal Albert Hall|Old Vic|medium
On which May date in 2002 did the original 'We Will Rock You' musical open?|14 May|1 May|21 May|30 May|very hard
""", "Queen on Stage", "https://www.queenonline.com/news/queen-the-greatest-we-will-rock-you-episode-43")

    facts("""
What was the advertised 1974 ticket price for Queen's filmed Rainbow Theatre concerts?|£1.75|£5.00|£2.50|£10.00|very hard
""", "Queen on Stage", "https://www.queenonline.com/news/watch-queen-the-greatest-the-landmark-gig-live-at-the-rainbow-episode-2")


concert_questions()
history_questions()
songwriting_questions()
manual_facts()
assert len(questions) == 500, len(questions)

seen = set()
for item in questions:
    key = norm(item.text)
    if key in old_normalized or key in seen:
        raise ValueError(f"Repeated question: {item.text}")
    seen.add(key)
    if len(item.text) > 500 or len(item.correct) > 200 or any(len(w) > 200 for w in item.wrong):
        raise ValueError(f"Importer text limit exceeded: {item.text}")

bins = [[] for _ in range(20)]
for level in ("easy", "medium", "hard", "very hard"):
    group = [item for item in questions if item.level == level]
    RNG.shuffle(group)
    for item in group:
        available = [i for i, quiz in enumerate(bins) if len(quiz) < 25]
        chosen = min(available, key=lambda i: (
            sum(other.level == level for other in bins[i]), len(bins[i]), i))
        bins[chosen].append(item)
assert all(len(quiz) == 25 for quiz in bins)

with (OUT / "questions_third.csv").open("w", encoding="utf-8", newline="") as file, \
     (OUT / "sources_third.csv").open("w", encoding="utf-8", newline="") as sources_file:
    writer = csv.writer(file)
    sources = csv.writer(sources_file)
    writer.writerow(["QuizTitle", "QuizDescription", "QuestionText", "Category",
                     "Difficulty", "Points", "OptionText", "IsCorrect"])
    sources.writerow(["QuizTitle", "QuestionText", "EditorialDifficulty", "Source"])
    for index, quiz in enumerate(bins, 1):
        title = f"Queen Quiz: Archive Explorer (Part {index})"
        description = "25 fresh questions on Queen concerts, milestones, songwriting and the band's wider story."
        for position, item in enumerate(quiz):
            options = list(item.wrong)
            options.insert(position % 4, item.correct)
            for option_index, option in enumerate(options):
                writer.writerow([
                    title, description if position == option_index == 0 else "",
                    item.text, item.category if option_index == 0 else "",
                    ("hard" if item.level == "very hard" else item.level) if option_index == 0 else "",
                    {"easy": 1, "medium": 2, "hard": 3, "very hard": 4}[item.level]
                    if option_index == 0 else "",
                    option, str(option == item.correct).lower(),
                ])
            sources.writerow([title, item.text, item.level.title(), item.source])

print("Questions:", len(questions))
print("Difficulty:", dict(Counter(item.level for item in questions)))
print("Category:", dict(Counter(item.category for item in questions)))
