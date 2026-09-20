"""Build a further 500 sourced, non-duplicate Queen quiz questions.

The source file is deliberately compact and reviewable. Album lists below follow the
original standard releases, without later bonus tracks. The importer CSV is derived;
edit this file, then rerun it to regenerate questions_more.csv and sources_more.csv.
"""
from __future__ import annotations

import csv
import random
import re
from dataclasses import dataclass
from pathlib import Path

HERE = Path(__file__).resolve().parent
BOOK = "Queen: Complete Works (user-supplied text), Part One: The Albums"
SOLO_BOOK = "Queen: Complete Works (user-supplied text), Part One: B. Solo Albums"
OFFICIAL = "https://www.queenonline.com"

# Original album tracks only. Flash Gordon's short score cues are excluded.
QUEEN_ALBUMS = {
    "Queen": "Keep Yourself Alive|Doing All Right|Great King Rat|My Fairy King|Liar|The Night Comes Down|Modern Times Rock 'n' Roll|Son and Daughter|Jesus|Seven Seas of Rhye (instrumental)",
    "Queen II": "Procession|Father to Son|White Queen (As It Began)|Some Day One Day|The Loser in the End|Ogre Battle|The Fairy Feller's Master-Stroke|Nevermore|The March of the Black Queen|Funny How Love Is|Seven Seas of Rhye",
    "Sheer Heart Attack": "Brighton Rock|Killer Queen|Tenement Funster|Flick of the Wrist|Lily of the Valley|Now I'm Here|In the Lap of the Gods|Stone Cold Crazy|Dear Friends|Misfire|Bring Back That Leroy Brown|She Makes Me (Stormtrooper in Stilettoes)|In the Lap of the Gods... Revisited",
    "A Night at the Opera": "Death on Two Legs|Lazing on a Sunday Afternoon|I'm in Love with My Car|You're My Best Friend|'39|Sweet Lady|Seaside Rendezvous|The Prophet's Song|Love of My Life|Good Company|Bohemian Rhapsody|God Save the Queen",
    "A Day at the Races": "Tie Your Mother Down|You Take My Breath Away|Long Away|The Millionaire Waltz|You and I|Somebody to Love|White Man|Good Old-Fashioned Lover Boy|Drowse|Teo Torriatte (Let Us Cling Together)",
    "News of the World": "We Will Rock You|We Are the Champions|Sheer Heart Attack|All Dead, All Dead|Spread Your Wings|Fight from the Inside|Get Down, Make Love|Sleeping on the Sidewalk|Who Needs You|It's Late|My Melancholy Blues",
    "Jazz": "Mustapha|Fat Bottomed Girls|Jealousy|Bicycle Race|If You Can't Beat Them|Let Me Entertain You|Dead on Time|In Only Seven Days|Dreamers Ball|Fun It|Leaving Home Ain't Easy|Don't Stop Me Now|More of That Jazz",
    "The Game": "Play the Game|Dragon Attack|Another One Bites the Dust|Need Your Loving Tonight|Crazy Little Thing Called Love|Rock It (Prime Jive)|Don't Try Suicide|Sail Away Sweet Sister|Coming Soon|Save Me",
    "Hot Space": "Staying Power|Dancer|Back Chat|Body Language|Action This Day|Put Out the Fire|Life Is Real (Song for Lennon)|Calling All Girls|Las Palabras de Amor (The Words of Love)|Cool Cat|Under Pressure",
    "The Works": "Radio Ga Ga|Tear It Up|It's a Hard Life|Man on the Prowl|Machines (or Back to Humans)|I Want to Break Free|Keep Passing the Open Windows|Hammer to Fall|Is This the World We Created...?",
    "A Kind of Magic": "One Vision|A Kind of Magic|One Year of Love|Pain Is So Close to Pleasure|Friends Will Be Friends|Who Wants to Live Forever|Gimme the Prize (Kurgan's Theme)|Don't Lose Your Head|Princes of the Universe",
    "The Miracle": "Party|Khashoggi's Ship|The Miracle|I Want It All|The Invisible Man|Breakthru|Rain Must Fall|Scandal|My Baby Does Me|Was It All Worth It",
    "Innuendo": "Innuendo|I'm Going Slightly Mad|Headlong|I Can't Live with You|Don't Try So Hard|Ride the Wild Wind|All God's People|These Are the Days of Our Lives|Delilah|The Hitman|Bijou|The Show Must Go On",
}

# The original UK running order is used for Strange Frontier; the US LP differs.
SOLO_ALBUMS = {
    "Fun in Space (Roger Taylor)": "No Violins|Laugh or Cry|Future Management (You Don't Need Nobody Else)|Let's Get Crazy|My Country I & II|Good Times Are Now|Magic Is Loose|Interlude in Constantinople|Airheads|Fun in Space",
    "Strange Frontier (Roger Taylor)": "Strange Frontier|Beautiful Dreams|Man on Fire|Racing in the Street|Masters of War|Abandonfire|Killing Time|Young Love|It's an Illusion|I Cry for You (Love, Hope and Confusion)",
    "Mr. Bad Guy (Freddie Mercury)": "Let's Turn It On|Made in Heaven|I Was Born to Love You|Foolin' Around|Your Kind of Lover|Mr. Bad Guy|Man Made Paradise|There Must Be More to Life Than This|Living on My Own|My Love Is Dangerous|Love Me Like There's No Tomorrow",
    "Barcelona (Freddie Mercury and Montserrat Caballé)": "Barcelona|La Japonaise|The Fallen Priest|Ensueño|The Golden Boy|Guide Me Home|How Can I Go On|Overture Piccante",
    "Back to the Light (Brian May)": "The Dark|Back to the Light|Love Token|Resurrection|Too Much Love Will Kill You|Driven by You|Nothin' But Blue|I'm Scared|Last Horizon|Let Your Heart Rule Your Head|Just One Life|Rollin' Over",
    "Happiness? (Roger Taylor)": "Nazis 1994|Happiness?|Revelations|Touch the Sky|Foreign Sand|Freedom Train|You Had to Be There|The Key|Everybody Hurts Sometime|Loneliness...|Dear Mr Murdoch|Old Friends",
    "Another World (Brian May)": "Space|Business|China Belle|Why Don't We Try Again|On My Way Up|Cyborg|The Guv'nor|Wilderness|Slow Down|One Rainy Wish|All the Way from Memphis|Another World",
    "Electric Fire (Roger Taylor)": "Pressure On|A Nation of Haircuts|Believe in Yourself|Surrender|People on Streets|The Whisperers|Is It Me?|No More Fun|Tonight|Where Are You Now?|Working Class Hero|London Town, C'mon Down",
}

QUEEN_ALBUMS = {name: tracks.split("|") for name, tracks in QUEEN_ALBUMS.items()}
SOLO_ALBUMS = {name: tracks.split("|") for name, tracks in SOLO_ALBUMS.items()}

@dataclass(frozen=True)
class Question:
    text: str
    correct: str
    wrong: tuple[str, str, str]
    category: str
    level: str
    source: str

questions: list[Question] = []

def add(text: str, correct: str, wrong: list[str] | tuple[str, ...], category: str, level: str, source: str) -> None:
    if len(wrong) != 3 or len({correct.casefold(), *(item.casefold() for item in wrong)}) != 4:
        raise ValueError(f"Bad options for {text}: {correct}, {wrong}")
    questions.append(Question(text, correct, tuple(wrong), category, level, source))


def distractors(tracks: list[str], correct: str, excluded: set[str], seed: int) -> list[str]:
    pool = [track for track in tracks if track != correct and track not in excluded]
    if len(pool) < 3:
        raise ValueError("Not enough distractors")
    return random.Random(seed).sample(pool, 3)


def track_questions(albums: dict[str, list[str]], solo: bool) -> None:
    category = "Solo Projects" if solo else "Album Tracks"
    source = SOLO_BOOK if solo else BOOK
    for album, tracks in albums.items():
        if len(set(tracks)) != len(tracks):
            raise ValueError(f"Repeated title in {album}")
        for index in range(len(tracks) - 1):
            before, after = tracks[index:index + 2]
            level = "very hard" if solo else "hard" if index > 4 else "medium"
            add(f"On the original {album} track list, what comes immediately after '{before}'?",
                after, distractors(tracks, after, {before}, index + 11), category, level, source + f"; {album} track list")
        for index in range(1, len(tracks)):
            before, after = tracks[index - 1:index + 1]
            level = "very hard" if solo else "hard"
            add(f"On the original {album} track list, what comes immediately before '{after}'?",
                before, distractors(tracks, before, {after}, index + 101), category, level, source + f"; {album} track list")
        for index in range(1, len(tracks) - 1):
            left, middle, right = tracks[index - 1:index + 2]
            add(f"Which {album} track sits between '{left}' and '{right}'?",
                middle, distractors(tracks, middle, {left, right}, index + 201), category, "very hard", source + f"; {album} track list")

track_questions(QUEEN_ALBUMS, False)
track_questions(SOLO_ALBUMS, True)

# Source-backed facts below are distinct from album-order questions. URL references
# point to the official band's website; the book references identify its section.
FACTS: list[tuple[str, str, str, str, str, str, str, str]] = []

def facts(block: str, category: str, source: str) -> None:
    for line in block.strip().splitlines():
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        parts = [part.strip() for part in line.split("|")]
        if len(parts) != 6:
            raise ValueError(f"Expected six pipe-separated fields: {line}")
        text, correct, a, b, c, level = parts
        FACTS.append((text, correct, a, b, c, category, level, source))

# More factual blocks added below.

# Original individual song credits, excluding songs already asked in the first
# 500-question bank. Joint credits and the post-1988 shared-credit era are omitted.
WRITERS = {
    "Brian May": "The Night Comes Down|Son and Daughter|Father to Son|White Queen (As It Began)|Some Day One Day|Brighton Rock|Now I'm Here|Dear Friends|She Makes Me (Stormtrooper in Stilettoes)|'39|Sweet Lady|The Prophet's Song|Good Company|Tie Your Mother Down|Long Away|White Man|Teo Torriatte (Let Us Cling Together)|All Dead, All Dead|Sleeping on the Sidewalk|It's Late|Fat Bottomed Girls|Dead on Time|Dreamers Ball|Leaving Home Ain't Easy|Dragon Attack|Sail Away Sweet Sister|Save Me|Dancer|Put Out the Fire|Las Palabras de Amor (The Words of Love)|Tear It Up|Hammer to Fall|Who Wants to Live Forever|Gimme the Prize (Kurgan's Theme)",
    "Freddie Mercury": "Great King Rat|My Fairy King|Liar|Jesus|Ogre Battle|The Fairy Feller's Master-Stroke|Nevermore|The March of the Black Queen|Funny How Love Is|Killer Queen|Flick of the Wrist|Lily of the Valley|In the Lap of the Gods|Bring Back That Leroy Brown|In the Lap of the Gods... Revisited|Death on Two Legs|Lazing on a Sunday Afternoon|Seaside Rendezvous|Love of My Life|You Take My Breath Away|The Millionaire Waltz|Somebody to Love|Good Old-Fashioned Lover Boy|Get Down, Make Love|My Melancholy Blues|Mustapha|Jealousy|Bicycle Race|Let Me Entertain You|Don't Stop Me Now|Play the Game|Crazy Little Thing Called Love|Don't Try Suicide|Staying Power|Body Language|Life Is Real (Song for Lennon)|It's a Hard Life|Man on the Prowl|Keep Passing the Open Windows|Princes of the Universe",
    "Roger Taylor": "Modern Times Rock 'n' Roll|The Loser in the End|Tenement Funster|I'm in Love with My Car|Drowse|Sheer Heart Attack|Fight from the Inside|Fun It|More of That Jazz|Rock It (Prime Jive)|Coming Soon|Action This Day|Calling All Girls|A Kind of Magic|Don't Lose Your Head",
    "John Deacon": "Misfire|You and I|Spread Your Wings|Who Needs You|If You Can't Beat Them|In Only Seven Days|Need Your Loving Tonight|Back Chat|One Year of Love",
}
WRITERS = {writer: songs.split("|") for writer, songs in WRITERS.items()}
for writer, songs in WRITERS.items():
    for song in songs:
        if song in {"You and I", "You Take My Breath Away", "Misfire"}:
            continue  # An earlier question already tests this songwriting fact.
        add(f"Who wrote the Queen album track '{song}'?", writer,
            [other for other in WRITERS if other != writer], "Songwriting", "medium" if song in {"Killer Queen", "Somebody to Love", "Save Me", "Don't Stop Me Now", "A Kind of Magic"} else "hard",
            "Queen: Complete Works (user-supplied text), Part Three: The Songs; " + song)

# Different relationships to the same album listings make the pool useful for
# both casual and specialist rounds without recycling the old 'contains X' form.
ALBUM_PAIRS: list[Question] = []
for album, tracks in QUEEN_ALBUMS.items():
    album_choices = [other for other in QUEEN_ALBUMS if other != album]
    for index in range(len(tracks) - 1):
        first, second = tracks[index:index + 2]
        rng = random.Random(4000 + index + len(album))
        ALBUM_PAIRS.append(Question(
            f"Which Queen studio album has both '{first}' and '{second}' on its original track list?",
            album, tuple(rng.sample(album_choices, 3)), "Albums", "easy" if index < 4 else "medium" if index < 7 else "hard",
            BOOK + f"; {album} track list"))

ALBUM_ENDS: list[Question] = []
for album, tracks in {**QUEEN_ALBUMS, **SOLO_ALBUMS}.items():
    solo = album in SOLO_ALBUMS
    pool = SOLO_ALBUMS if solo else QUEEN_ALBUMS
    all_tracks = [track for name, items in pool.items() if name != album for track in (items[0], items[-1])]
    for position, answer in (("opens", tracks[0]), ("closes", tracks[-1])):
        if album == "Queen II" and position == "opens":
            continue  # Already asked in the earlier bank.
        rng = random.Random(5000 + len(album) + len(answer))
        # Some tracks open or close several releases. Only ask where the options
        # are other album endpoints and do not duplicate this answer string.
        choices = list(dict.fromkeys(item for item in all_tracks if item != answer))
        ALBUM_ENDS.append(Question(
            f"Which track {position} the original {album} album?", answer,
            tuple(rng.sample(choices, 3)), "Solo Projects" if solo else "Albums",
            "hard" if solo else "easy" if album in {"Queen", "Queen II", "A Night at the Opera", "The Game", "Innuendo"} else "medium", (SOLO_BOOK if solo else BOOK) + f"; {album} track list"))

facts("""
What was Freddie Mercury's birth name?|Farrokh Bulsara|Frederick Bulsara|Freddie Farrokh|Feroz Bulsara|easy
What was Freddie Mercury's father's first name?|Bomi|Rustom|Jim|Arthur|medium
What was Freddie Mercury's mother's first name?|Jer|Mary|Anita|Ruth|medium
In which country did Freddie Mercury spend much of his childhood?|India|Kenya|England|Iran|easy
At what age did Freddie Mercury begin piano lessons, according to Queen's official biography?|Seven|Five|Nine|Twelve|medium
In which year did the Bulsara family move to Middlesex?|1964|1959|1969|1972|medium
Which band did Freddie Mercury sing in while studying at Ealing College of Art?|Wreckage|The Cross|1984|The Opposition|medium
Which choreographer created Freddie Mercury's surprise 1977 charity ballet routine?|Wayne Eagling|Maurice Béjart|Kenneth MacMillan|Matthew Bourne|hard
At which London venue did Freddie Mercury make his surprise 1977 ballet appearance?|London Coliseum|Royal Albert Hall|Dominion Theatre|Wembley Arena|hard
Which musical did Freddie Mercury record songs for with Dave Clark in 1986?|Time|Chess|Starlight Express|Cats|medium
""", "Band Members", OFFICIAL + "/freddie_mercury")

facts("""
On which July day was Brian May born?|19 July|9 July|26 July|29 July|medium
In which London-area town was Brian May born?|Twickenham|Hounslow|Ealing|Richmond|medium
Who is Brian May's wife named in Queen's official biography?|Anita Dobson|Mary Austin|Chrissie Mullen|Debbie Leng|easy
From what part of Buckingham Palace did Brian May perform 'God Save the Queen' in 2002?|The roof|The throne room|The balcony|The courtyard|easy
For which theatrical work did Brian May compose music at Riverside Studios in 1987?|Macbeth|Hamlet|The Tempest|A Midsummer Night's Dream|hard
What was the name of Brian May's animal-welfare campaign founded in 2010?|Save-Me|Animal Aid|Born Free|Wildlife First|medium
In what year did Brian May receive a PhD in astrophysics?|2007|1992|2002|2017|medium
Which university awarded Brian May his PhD in astrophysics?|Imperial College London|University of Cambridge|University of Oxford|University College London|medium
Which illustrated astronomy book did Brian May co-author with Patrick Moore and Chris Lintott?|BANG! The Complete History of the Universe|A Brief History of Time|Cosmos|The Planets|hard
Who co-authored Brian May's stereoscopic-history book 'A Village Lost and Found'?|Elena Vidal|Anita Dobson|Jane Hawking|Mary Austin|hard
Which French film did Brian May score in 1999?|Furia|Amélie|La Haine|Taxi|hard
In which year was Brian May appointed CBE?|2005|1995|2015|2023|medium
""", "Band Members", OFFICIAL + "/brian_may")

facts("""
What was John Deacon's father's first name?|Arthur|Bomi|Harold|Jim|medium
Which performer was named on the toy guitar John Deacon received at age seven?|Tommy Steele|Cliff Richard|Elvis Presley|Buddy Holly|hard
How did young John Deacon save for his first proper guitar?|A paper round|Washing cars|Delivering groceries|Busking|medium
On what date did John Deacon formally become Queen's fourth member?|1 March 1971|1 March 1970|13 July 1973|24 November 1971|hard
How old was John Deacon when he joined Queen?|19|17|21|23|medium
Which Queen member was the youngest when the classic line-up formed?|John Deacon|Roger Taylor|Brian May|Freddie Mercury|easy
How was John Deacon's name printed in the credits of Queen's debut album?|Deacon John|John D.|Johnny Deacon|John Richard|hard
In which city did John Deacon make his final stage appearance with Queen in 1997?|Paris|London|Montreux|Munich|hard
Who sang with Queen at John Deacon's final stage appearance in 1997?|Elton John|George Michael|Paul Rodgers|Adam Lambert|hard
Which 1997 Queen song marked John Deacon's final studio work with Brian May and Roger Taylor?|No-One But You (Only the Good Die Young)|The Show Must Go On|Let Me Live|Too Much Love Will Kill You|medium
""", "Band Members", OFFICIAL + "/john_deacon")

facts("""
On which July day was Roger Taylor born?|26 July|19 July|6 July|16 July|medium
What was the title of Roger Taylor's 2021 solo album?|Outsider|Electric Fire|Fun on Earth|Strange Frontier|medium
In which year did Roger Taylor release the solo album 'Fun on Earth'?|2013|2003|1998|2021|medium
What was the name of Roger Taylor's 2013 box set covering his solo work and The Cross?|The Lot|The Vault|The Collection|The Works|hard
Which official Queen tribute act has Roger Taylor overseen and produced?|The Queen Extravaganza|The Bohemians|Queen Forever|Killer Queen|medium
Which 2009 solo single marked Roger Taylor's return to recording?|The Unblinking Eye|Radio Ga Ga|Pressure On|Nazis 1994|hard
In which city did Roger Taylor's 2021 Outsider tour begin?|Newcastle|London|Birmingham|Liverpool|hard
At which London venue did Roger Taylor's 2021 Outsider tour end?|O2 Shepherd's Bush Empire|Wembley Arena|Royal Albert Hall|Hammersmith Odeon|hard
""", "Band Members", OFFICIAL + "/roger_taylor")

facts("""
In which month of 1992 was the Freddie Mercury Tribute Concert held?|April|March|June|September|medium
Who sang 'Tie Your Mother Down' with Queen at Freddie Mercury's 1992 tribute concert?|Joe Elliott|Robert Plant|Paul Young|Seal|medium
Which singer performed 'I Want It All' with Queen at Freddie Mercury's tribute concert?|Roger Daltrey|Axl Rose|Elton John|Gary Cherone|medium
Who sang 'Las Palabras de Amor' with Queen at the 1992 tribute concert?|Zucchero|Seal|Paul Young|Robert Plant|hard
Which Extreme singer performed 'Hammer to Fall' with Queen at the 1992 tribute concert?|Gary Cherone|Joe Elliott|James Hetfield|Ian Hunter|hard
Who sang 'Stone Cold Crazy' with Queen at the 1992 tribute concert?|James Hetfield|Axl Rose|Roger Daltrey|Robert Plant|hard
Who performed 'Crazy Little Thing Called Love' with Queen at the 1992 tribute concert?|Robert Plant|Paul Young|Seal|Zucchero|medium
Who sang 'Radio Ga Ga' with Queen at the 1992 tribute concert?|Paul Young|George Michael|Elton John|Seal|medium
Who sang 'Who Wants to Live Forever' with Queen at the 1992 tribute concert?|Seal|Zucchero|Axl Rose|Roger Daltrey|medium
Who sang 'I Want to Break Free' with Queen at the 1992 tribute concert?|Lisa Stansfield|Annie Lennox|Liza Minnelli|Montserrat Caballé|medium
Who joined David Bowie and Queen on 'Under Pressure' at the 1992 tribute concert?|Annie Lennox|Lisa Stansfield|Liza Minnelli|Debbie Harry|medium
Who sang Queen's '39' at the 1992 tribute concert?|George Michael|Elton John|Joe Elliott|Robert Plant|hard
Who led the ensemble finale of 'We Are the Champions' at the 1992 tribute concert?|Liza Minnelli|Annie Lennox|Lisa Stansfield|Elizabeth Taylor|hard
Who sang 'We Will Rock You' with Queen at the 1992 tribute concert?|Axl Rose|James Hetfield|Joe Elliott|Gary Cherone|medium
Which band opened the Freddie Mercury Tribute Concert with 'Enter Sandman'?|Metallica|Guns N' Roses|Extreme|Def Leppard|medium
Which group performed a Queen medley before the main Queen set at the 1992 tribute show?|Extreme|Metallica|The Cross|Def Leppard|hard
Who delivered a speech before the main Queen set at the 1992 tribute concert?|Elizabeth Taylor|Liza Minnelli|Annie Lennox|Montserrat Caballé|hard
Which band performed 'Now I'm Here' with Brian May before the main Queen set at the 1992 tribute?|Def Leppard|Extreme|Metallica|Guns N' Roses|hard
Who sang 'These Are the Days of Our Lives' with Lisa Stansfield at the 1992 tribute?|George Michael|Elton John|Paul Young|Seal|hard
Which singer joined Elton John and Queen on 'Bohemian Rhapsody' at the 1992 tribute concert?|Axl Rose|Robert Plant|Joe Elliott|James Hetfield|medium
Who sang 'Too Much Love Will Kill You' alone during Queen's main tribute set in 1992?|Brian May|Roger Taylor|Elton John|George Michael|medium
""", "After Freddie", OFFICIAL + "/news/press-release-the-freddie-mercury-tribute-concert-the-definitive-edition")

facts("""
Which studio in Montreux did Brian May, Roger Taylor and John Deacon return to in 1993 to finish Freddie-era recordings?|Mountain Studios|Trident Studios|Olympic Studios|Musicland Studios|medium
Which late Freddie vocal was inspired by the scenery around Montreux?|A Winter's Tale|Delilah|Bijou|The Hitman|medium
Which lake appears behind Freddie Mercury's statue on the 'Made in Heaven' cover?|Lake Geneva|Lake Como|Lake Lucerne|Lake Garda|medium
What time of day does the front cover of 'Made in Heaven' depict?|Sunrise|Sunset|Noon|Midnight|hard
To whose spirit did Queen dedicate the 'Made in Heaven' sleeve?|Freddie Mercury|Jim Beach|John Deacon|David Richards|easy
In which month of 1995 was Queen's 'Made in Heaven' released?|November|February|May|August|medium
""", "After Freddie", OFFICIAL + "/news/made-in-heaven30-years-on")

facts("""
Which 2014 Queen compilation introduced a ballad arrangement of Freddie's 'Love Kills'?|Queen Forever|Queen Rocks|Greatest Hits III|The Platinum Collection|medium
Which 2014 collection included the newly completed 'Let Me in Your Heart Again'?|Queen Forever|Made in Heaven|Queen Rocks|On Air|medium
Which 2014 Queen collection included a version of 'There Must Be More to Life Than This'?|Queen Forever|The Miracle|Greatest Hits II|Queen at the Beeb|hard
Which Freddie solo song was revisited as a Queen ballad for 'Queen Forever'?|Love Kills|The Great Pretender|In My Defence|Time|medium
""", "After Freddie", OFFICIAL + "/news/press-release-new-single-face-it-alone-out-now")

facts("""
In which year did Queen release the rediscovered Freddie vocal 'Face It Alone'?|2022|2014|1995|2008|easy
During which album's original sessions was 'Face It Alone' recorded?|The Miracle|Innuendo|Made in Heaven|A Kind of Magic|medium
What month saw the digital release of 'Face It Alone'?|October 2022|April 2022|November 2021|June 2023|hard
Which 2022 Queen box set included 'Face It Alone' and other session discoveries?|The Miracle Collector's Edition|Queen I|On Air|News of the World 40th Anniversary|medium
Which previously unreleased Miracle-era song was listed alongside 'Face It Alone' in the 2022 box set?|Dog With a Bone|Silver Salmon|Hangman|See What a Fool I've Been|hard
""", "After Freddie", OFFICIAL + "/news/watch-queen-release-newly-created-video-for-face-it-alone")

facts("""
Which previously omitted song was restored to the 2024 'Queen I' running order?|Mad the Swine|See What a Fool I've Been|Polar Bear|A Human Body|medium
On 2024's 'Queen I', which song comes immediately before the restored 'Mad the Swine'?|Great King Rat|Doing All Right|Liar|My Fairy King|hard
On 2024's 'Queen I', which song comes immediately after the restored 'Mad the Swine'?|My Fairy King|Liar|The Night Comes Down|Great King Rat|hard
What new title did Queen give the 2024 remix of their debut album?|Queen I|Queen Reborn|Queen 1973|The First Queen|easy
""", "After Freddie", OFFICIAL + "/news/queen-i-queen-remixed-remastered-and-expanded-out-now")

facts("""
On which TV competition's 2009 final did Brian May and Roger Taylor first perform with Adam Lambert?|American Idol|The X Factor|The Voice|Britain's Got Talent|easy
In which city did Queen + Adam Lambert play their first full-length show in 2012?|Kyiv|London|New York|Tokyo|medium
Which 2011 awards show featured an early Queen performance with Adam Lambert?|MTV Europe Music Awards|Brit Awards|Grammy Awards|Ivor Novello Awards|medium
Which 2020 album was the first live release by Queen + Adam Lambert?|Live Around the World|Return of the Champions|Live Magic|Queen Rock Montreal|medium
What was the title of Queen + Adam Lambert's 2020 lockdown charity single?|You Are the Champions|We Are the World|The Show Must Go On|Friends Will Be Friends|easy
Which organisation's COVID-19 response fund benefited from 'You Are the Champions'?|World Health Organization|UNICEF|Mercury Phoenix Trust|Save the Children|medium
At which 2020 Australian benefit did Queen + Adam Lambert recreate the Live Aid set?|Fire Fight Australia|Live Earth|Sound Relief|Band Aid 30|medium
""", "After Freddie", OFFICIAL + "/news/queen-the-greatest-queen-adam-lambert-the-first-gig-episode-46; " + OFFICIAL + "/news/out-now-queen-adam-lambert-live-around-the-world; " + OFFICIAL + "/news/press-release-queen-adam-lambert-release-you-are-the-champions-for-the-world-health-organization")

facts("""
Which singer made a Queen album with Brian May and Roger Taylor titled 'The Cosmos Rocks'?|Paul Rodgers|Adam Lambert|George Michael|Robert Plant|easy
In which New Year's Honours list was Brian May's knighthood announced?|2023|2013|2003|2018|medium
What charity did Queen's surviving members establish after Freddie's death?|Mercury Phoenix Trust|Save-Me|The Prince's Trust|Red Nose Day|easy
""", "After Freddie", OFFICIAL + "/brian_may; " + OFFICIAL + "/news/sir-brian-harold-may")

facts("""
Which 1981 Roger Taylor album was recorded largely at Mountain Studios in Montreux?|Fun in Space|Strange Frontier|Happiness?|Electric Fire|hard
Who produced Roger Taylor's 'Fun in Space' album?|Roger Taylor|Mack|Roy Thomas Baker|David Richards|hard
Which David Richards contribution is credited on Roger Taylor's 'Fun in Space'?|Synthesizers|Lead vocals|Bass guitar|Saxophone|very hard
Which Roger Taylor solo album placed 'My Country I & II' on its original track list?|Fun in Space|Strange Frontier|Happiness?|Electric Fire|hard
Which 1984 Roger Taylor album had a different US vinyl running order from the UK edition?|Strange Frontier|Fun in Space|Electric Fire|Happiness?|very hard
Which Bruce Springsteen song did Roger Taylor cover on 'Strange Frontier'?|Racing in the Street|Born to Run|Dancing in the Dark|Thunder Road|hard
Which Bob Dylan song appears on Roger Taylor's 'Strange Frontier'?|Masters of War|Blowin' in the Wind|Hurricane|Like a Rolling Stone|hard
Which John Lennon song appears on Roger Taylor's 'Electric Fire'?|Working Class Hero|Imagine|Woman|Instant Karma!|hard
Which 1994 Roger Taylor album closes with 'Old Friends'?|Happiness?|Electric Fire|Strange Frontier|Fun in Space|hard
Which Japanese musician worked on Roger Taylor's 'Foreign Sand'?|Yoshiki|Ryuichi Sakamoto|Kitaro|Joe Hisaishi|very hard
""", "Solo Projects", SOLO_BOOK)

facts("""
Which Queen bassist played on Freddie Mercury and Montserrat Caballé's 'How Can I Go On'?|John Deacon|Mike Grose|Barry Mitchell|Neil Murray|hard
Who provided keyboards and arrangements for Freddie Mercury's 'Barcelona' album?|Mike Moran|David Richards|Fred Mandel|Spike Edney|hard
Which lyricist contributed words to 'The Fallen Priest' and 'The Golden Boy'?|Tim Rice|Bernie Taupin|Andrew Lloyd Webber|Stephen Sondheim|very hard
Which track ends the original Freddie Mercury-Montserrat Caballé 'Barcelona' album?|Overture Piccante|How Can I Go On|Guide Me Home|The Golden Boy|hard
Who co-produced Freddie Mercury's 1985 album 'Mr. Bad Guy' with him?|Mack|Roy Thomas Baker|David Richards|John Anthony|hard
Which 'Mr. Bad Guy' track was later re-recorded by Queen for their final Freddie-era album?|I Was Born to Love You|Mr. Bad Guy|Foolin' Around|My Love Is Dangerous|hard
""", "Solo Projects", SOLO_BOOK)

facts("""
Which drummer played on Brian May's 'Back to the Light' album?|Cozy Powell|Roger Taylor|Phil Collins|Ian Paice|medium
Which Brian May solo song opens with the short instrumental 'The Dark' on its parent album?|Back to the Light|Resurrection|Last Horizon|Driven by You|hard
Which guitarist's 1998 album includes the song 'The Guv'nor'?|Brian May|Roger Taylor|Eddie Van Halen|Steve Howe|hard
Which Brian May solo album contains 'All the Way from Memphis'?|Another World|Back to the Light|Star Fleet Project|Furia|hard
Which Jimi Hendrix song did Brian May cover on 'Another World'?|One Rainy Wish|Purple Haze|Hey Joe|Little Wing|very hard
Which Mott the Hoople song did Brian May cover on 'Another World'?|All the Way from Memphis|All the Young Dudes|Roll Away the Stone|Honaloochie Boogie|very hard
""", "Solo Projects", SOLO_BOOK)

# Give direct album-identification questions only to tracks that were not used
# for that same fact in either of the two earlier banks.
def normalize(value: str) -> str:
    return re.sub(r"[^a-z0-9]+", "", value.casefold())

existing_questions: list[str] = []
for filename in ("questions.csv", "questions_extra.csv"):
    with (HERE / filename).open(newline="", encoding="utf-8-sig") as stream:
        existing_questions.extend(dict.fromkeys(row["QuestionText"] for row in csv.DictReader(stream)))
existing_normalized = {normalize(text) for text in existing_questions}
old_album_song_titles = set()
for text in existing_questions:
    if "which queen album contains" in text.casefold():
        match = re.search(r"contains ['‘](.*)['’]\?", text)
        if match:
            old_album_song_titles.add(normalize(match.group(1)))

DIRECT_ALBUMS: list[Question] = []
for album, tracks in QUEEN_ALBUMS.items():
    choices = [name for name in QUEEN_ALBUMS if name != album]
    for index, song in enumerate(tracks):
        if normalize(song) in old_album_song_titles or sum(song in items for items in QUEEN_ALBUMS.values()) != 1:
            continue
        rng = random.Random(6000 + index + len(song))
        DIRECT_ALBUMS.append(Question(
            f"On which Queen studio album is '{song}' found in the original track list?",
            album, tuple(rng.sample(choices, 3)), "Albums",
            "easy" if index < 4 else "medium", BOOK + f"; {album} track list"))


def round_robin(items: list[Question], count: int, key) -> list[Question]:
    by_group: dict[str, list[Question]] = {}
    for item in items:
        by_group.setdefault(key(item), []).append(item)
    for group in by_group.values():
        random.Random(20260920 + len(group)).shuffle(group)
    selected: list[Question] = []
    while len(selected) < count:
        advanced = False
        for group in by_group.values():
            if group and len(selected) < count:
                selected.append(group.pop())
                advanced = True
        if not advanced:
            raise ValueError(f"Only {len(selected)} questions available; wanted {count}")
    return selected

writers = [item for item in questions if item.category == "Songwriting"]
sequence = [item for item in questions if item.category in ("Album Tracks", "Solo Projects")]
selected = [Question(text, correct, (a, b, c), category, level, source)
            for text, correct, a, b, c, category, level, source in FACTS]
selected += round_robin(writers, 50, lambda item: item.correct)
selected += ALBUM_ENDS
selected += round_robin(ALBUM_PAIRS, 55, lambda item: item.correct)
selected += round_robin(DIRECT_ALBUMS, 50, lambda item: item.correct)
for category, forward, backward, between in (("Album Tracks", 75, 42, 13), ("Solo Projects", 35, 20, 7)):
    category_items = [item for item in sequence if item.category == category]
    # The earlier bank already asks for these two Queen II transitions.
    category_items = [item for item in category_items if not (
        "Queen II" in item.text and (
            "immediately after 'The Fairy Feller's Master-Stroke'" in item.text or
            "immediately before 'Nevermore'" in item.text or
            "sits between 'The Fairy Feller's Master-Stroke' and 'The March of the Black Queen'" in item.text or
            "immediately after 'Funny How Love Is'" in item.text or
            "immediately before 'Seven Seas of Rhye'" in item.text or
            "sits between 'The March of the Black Queen' and 'Seven Seas of Rhye'" in item.text
        ))]
    for phrase, count in (("immediately after", forward), ("immediately before", backward), ("sits between", between)):
        pool = [item for item in category_items if phrase in item.text]
        selected += round_robin(pool, count, lambda item: item.source)
assert len(selected) == 500, len(selected)

# Compare exact normalized wording against all previous imports, then ensure
# titles, answers and options are unambiguous and fit the importer limits.
seen: set[str] = set()
for item in selected:
    key = normalize(item.text)
    if key in existing_normalized:
        raise ValueError(f"Duplicate of existing question: {item.text}")
    if key in seen:
        raise ValueError(f"Duplicate new question: {item.text}")
    seen.add(key)
    if len(item.text) > 500 or len(item.correct) > 200 or any(len(w) > 200 for w in item.wrong):
        raise ValueError(f"Text exceeds importer limit: {item.text}")
    if len({normalize(option) for option in (item.correct, *item.wrong)}) != 4:
        raise ValueError(f"Ambiguous answers: {item.text}")

# Balance difficulties across twenty 25-question draft quizzes.
quiz_bins: list[list[Question]] = [[] for _ in range(20)]
for level in ("easy", "medium", "hard", "very hard"):
    group = [item for item in selected if item.level == level]
    random.Random(7000 + len(group)).shuffle(group)
    for item in group:
        eligible = [index for index, quiz in enumerate(quiz_bins) if len(quiz) < 25]
        index = min(eligible, key=lambda candidate: (
            sum(other.level == level for other in quiz_bins[candidate]),
            len(quiz_bins[candidate]), candidate))
        quiz_bins[index].append(item)
assert all(len(items) == 25 for items in quiz_bins)

with (HERE / "questions_more.csv").open("w", newline="", encoding="utf-8") as stream, \
     (HERE / "sources_more.csv").open("w", newline="", encoding="utf-8") as sources_stream:
    output = csv.writer(stream)
    sources = csv.writer(sources_stream)
    output.writerow(["QuizTitle", "QuizDescription", "QuestionText", "Category", "Difficulty", "Points", "OptionText", "IsCorrect"])
    sources.writerow(["QuizTitle", "QuestionText", "EditorialDifficulty", "Source"])
    for part, items in enumerate(quiz_bins, start=1):
        title = f"Queen Quiz: Encore (Part {part})"
        description = "25 new Queen questions spanning the albums, solo projects, band members, and the years after Freddie."
        for question_index, item in enumerate(items):
            options = list(item.wrong)
            options.insert(question_index % 4, item.correct)
            points = {"easy": 1, "medium": 2, "hard": 3, "very hard": 4}[item.level]
            for option_index, option in enumerate(options):
                output.writerow([
                    title, description if question_index == option_index == 0 else "",
                    item.text, item.category if option_index == 0 else "",
                    ("hard" if item.level == "very hard" else item.level) if option_index == 0 else "",
                    points if option_index == 0 else "", option, str(option == item.correct).lower(),
                ])
            sources.writerow([title, item.text, item.level.title(), item.source])

print(f"Wrote {len(selected)} questions in {len(quiz_bins)} quizzes of 25")
from collections import Counter
print("Difficulty:", dict(Counter(item.level for item in selected)))
print("Category:", dict(Counter(item.category for item in selected)))
