"""Generate the editorial Queen quiz CSV from reviewed question records."""

import csv
import random
from pathlib import Path

ROOT = Path(__file__).parent

ALBUMS = [
    "Queen", "Queen II", "Sheer Heart Attack", "A Night at the Opera",
    "A Day at the Races", "News of the World", "Jazz", "The Game",
    "Flash Gordon", "Hot Space", "The Works", "A Kind of Magic",
    "The Miracle", "Innuendo", "Made in Heaven",
]
PEOPLE = ["Freddie Mercury", "Brian May", "Roger Taylor", "John Deacon", "Tim Staffell", "David Bowie", "Spike Edney", "Roy Thomas Baker", "Jim Beach", "Mike Moran", "Eddie Van Halen", "Adam Lambert", "Paul Rodgers"]
PLACES = ["Zanzibar", "Hampton", "King's Lynn", "Leicester", "Truro", "London", "Budapest", "Knebworth", "Wembley Stadium", "Hyde Park", "Rockfield Studios", "Munich", "Montreux", "Tokyo", "Rio de Janeiro"]
SONGS = ["Bohemian Rhapsody", "Killer Queen", "We Will Rock You", "We Are the Champions", "Somebody to Love", "Another One Bites the Dust", "Radio Ga Ga", "Under Pressure", "I Want to Break Free", "Crazy Little Thing Called Love", "Don't Stop Me Now", "The Show Must Go On", "These Are the Days of Our Lives", "Seven Seas of Rhye", "Keep Yourself Alive", "Love of My Life", "Who Wants to Live Forever", "Innuendo", "The Invisible Man", "The Miracle", "A Kind of Magic", "Breakthru", "One Vision", "Friends Will Be Friends", "The Great Pretender", "Barcelona", "No-One but You (Only the Good Die Young)"]
YEARS = [str(y) for y in range(1968, 1997)]
POOLS = {"album": ALBUMS, "person": PEOPLE, "place": PLACES, "song": SONGS, "year": YEARS}

SOURCES = {
    "VD": "Ken Dean and Chris Charlesworth, Queen: A Visual Documentary (user-supplied text)",
    "AI": "Jacky Smith and Jim Jenkins, Queen: As It Began (user-supplied text)",
    "EY": "Mark Hodkinson, Queen: The Early Years (user-supplied text)",
    "OFFICIAL_ALBUMS": "https://store.queenonline.com/",
    "OFFICIAL_HISTORY": "https://www.queenonline.com/freddie_mercury",
    "OFFICIAL_JOHN": "https://www.queenonline.com/john_deacon",
    "OFFICIAL_ROGER": "https://www.queenonline.com/roger_taylor",
    "OFFICIAL_ANNIVERSARY": "https://www.queenonline.com/news/royal-mail-issued-queen-stamps-now-on-general-sale",
    "OFFICIAL_OPERA": "https://www.queenonline.com/news/out-today-bohemian-rhapsody-50th-anniversary-vinyl-reissues",
    "OFFICIAL_SHEER": "https://www.queenonline.com/news/sheer-heart-attack50-years-on",
    "OFFICIAL_TRACKS": "https://www.queenonline.com/news/press-release-queen-greatest-hits-in-japan",
    "BRIAN_GUITAR": "https://brianmay.com/brian-news/brian-features/2002/11/desert-island-discs-transcript-bbc-radio-4/",
    "TIMELINE": "QueenZone.Modern/data/queen_history_events.csv",
}

rows = []

def q(level, category, question, answer, options, source):
    if isinstance(options, str):
        pool = POOLS[options]
        assert answer in pool, (question, answer)
        i = pool.index(answer)
        # Nearby choices make dates and album titles credible distractors.
        offsets = [1, -1, 2, -2, 3, -3, 4, -4]
        wrong = []
        for offset in offsets:
            candidate = pool[(i + offset) % len(pool)]
            if candidate != answer and candidate not in wrong:
                wrong.append(candidate)
            if len(wrong) == 3:
                break
    else:
        wrong = list(options)
    assert len(wrong) == 3 and len(set([answer, *wrong])) == 4, question
    # Rotate the correct position to avoid a predictable answer key.
    index = len(rows) % 4
    choices = wrong.copy()
    choices.insert(index, answer)
    rows.append({
        "QuizTitle": f"Queen Quiz: {level}",
        "Question": question,
        "Option1": choices[0], "Option2": choices[1],
        "Option3": choices[2], "Option4": choices[3],
        "CorrectOption": index + 1,
        "Points": {"Easy": 1, "Medium": 2, "Hard": 3, "Very Hard": 4}[level],
        "Difficulty": level, "Category": category, "Source": SOURCES[source],
    })

# Easy: recognisable people, major songs, and landmark releases.
q("Easy", "Line-up", "Who was Queen's lead singer?", "Freddie Mercury", "person", "OFFICIAL_HISTORY")
q("Easy", "Line-up", "Who played lead guitar in Queen's classic line-up?", "Brian May", "person", "OFFICIAL_HISTORY")
q("Easy", "Line-up", "Who played drums in Queen's classic line-up?", "Roger Taylor", "person", "OFFICIAL_ROGER")
q("Easy", "Line-up", "Who played bass in Queen's classic line-up?", "John Deacon", "person", "OFFICIAL_JOHN")
q("Easy", "Origins", "What was the name of Brian May and Roger Taylor's band before Queen?", "Smile", ["The Opposition", "The Cross", "1984"], "AI")
q("Easy", "Origins", "Who was Smile's singer and bassist before Freddie Mercury joined May and Taylor?", "Tim Staffell", "person", "AI")
q("Easy", "Members", "Which Queen member was born in Zanzibar?", "Freddie Mercury", "person", "OFFICIAL_HISTORY")
q("Easy", "Members", "Which Queen member built the guitar known as the Red Special?", "Brian May", "person", "AI")
q("Easy", "Members", "Which Queen member was nicknamed 'Deaks'?", "John Deacon", "person", "EY")
q("Easy", "Members", "Which member of Queen studied dentistry before turning to biology?", "Roger Taylor", "person", "OFFICIAL_ROGER")
q("Easy", "Albums", "Which Queen album contains 'Bohemian Rhapsody'?", "A Night at the Opera", "album", "OFFICIAL_OPERA")
q("Easy", "Albums", "Which Queen album contains 'Killer Queen'?", "Sheer Heart Attack", "album", "OFFICIAL_ANNIVERSARY")
q("Easy", "Albums", "Which Queen album contains 'We Will Rock You'?", "News of the World", "album", "OFFICIAL_ANNIVERSARY")
q("Easy", "Albums", "Which Queen album contains 'We Are the Champions'?", "News of the World", "album", "OFFICIAL_ANNIVERSARY")
q("Easy", "Albums", "Which Queen album contains 'Another One Bites the Dust'?", "The Game", "album", "OFFICIAL_ANNIVERSARY")
q("Easy", "Albums", "Which Queen album contains 'Radio Ga Ga'?", "The Works", "album", "OFFICIAL_ANNIVERSARY")
q("Easy", "Albums", "Which Queen album contains 'Somebody to Love'?", "A Day at the Races", "album", "OFFICIAL_TRACKS")
q("Easy", "Albums", "Which Queen album contains 'Don't Stop Me Now'?", "Jazz", "album", "AI")
q("Easy", "Albums", "Which Queen album contains 'The Show Must Go On'?", "Innuendo", "album", "OFFICIAL_TRACKS")
q("Easy", "Albums", "Which Queen album contains 'Seven Seas of Rhye' in its finished vocal version?", "Queen II", "album", "OFFICIAL_ANNIVERSARY")
q("Easy", "Songs", "Which Queen song opens with a distinctive stomp-stomp-clap beat?", "We Will Rock You", "song", "OFFICIAL_ANNIVERSARY")
q("Easy", "Songs", "Which Queen song was recorded with David Bowie?", "Under Pressure", "song", "TIMELINE")
q("Easy", "Songs", "Which Queen song was written in a rockabilly style?", "Crazy Little Thing Called Love", "song", "AI")
q("Easy", "Songs", "Which Queen song became famous for its operatic middle section?", "Bohemian Rhapsody", "song", "OFFICIAL_OPERA")
q("Easy", "Songs", "Which Queen song has the title of a radio broadcast sound?", "Radio Ga Ga", "song", "OFFICIAL_ANNIVERSARY")
q("Easy", "Songs", "Which Queen song shares its title with the band's 1991 studio album?", "Innuendo", "song", "OFFICIAL_ALBUMS")
q("Easy", "Collaborations", "Who collaborated with Queen on 'Under Pressure'?", "David Bowie", "person", "TIMELINE")
q("Easy", "Concerts", "At which 1985 benefit concert did Queen give a celebrated set?", "Live Aid", ["Concert for Bangladesh", "Farm Aid", "Live 8"], "VD")
q("Easy", "Concerts", "At which London stadium did Queen play two famous 1986 shows?", "Wembley Stadium", ["Twickenham Stadium", "Emirates Stadium", "Stamford Bridge"], "VD")
q("Easy", "Concerts", "Which 1986 English venue hosted Freddie Mercury's final Queen concert?", "Knebworth", ["Wembley Stadium", "Milton Keynes Bowl", "Hyde Park"], "TIMELINE")
q("Easy", "Concerts", "In which city did Queen play a major 1986 concert behind the Iron Curtain?", "Budapest", ["Prague", "Warsaw", "Belgrade"], "TIMELINE")
q("Easy", "Concerts", "At which London park did Queen give a free concert in 1976?", "Hyde Park", ["Regent's Park", "Battersea Park", "Victoria Park"], "TIMELINE")
q("Easy", "Film", "Which science-fiction film did Queen score in 1980?", "Flash Gordon", ["Highlander", "Metropolis", "The Last Starfighter"], "VD")
q("Easy", "Film", "Which film featured Queen songs including 'A Kind of Magic' and 'Who Wants to Live Forever'?", "Highlander", ["Flash Gordon", "Metropolis", "Labyrinth"], "VD")
q("Easy", "Albums", "Which Queen album is named after a 1980 science-fiction film?", "Flash Gordon", "album", "OFFICIAL_ALBUMS")
q("Easy", "Albums", "What was Queen's debut studio album called?", "Queen", "album", "OFFICIAL_ALBUMS")
q("Easy", "Albums", "Which Queen album was released in 1995, after Freddie Mercury's death?", "Made in Heaven", "album", "OFFICIAL_ALBUMS")
q("Easy", "Albums", "Which Queen album was released in 1986?", "A Kind of Magic", "album", "OFFICIAL_ALBUMS")
q("Easy", "Albums", "Which Queen album was released in 1984?", "The Works", "album", "OFFICIAL_ALBUMS")
q("Easy", "Albums", "Which Queen album was released in 1980 and included 'Another One Bites the Dust'?", "The Game", "album", "OFFICIAL_ANNIVERSARY")
q("Easy", "Members", "Which Queen member designed the band's crest logo?", "Freddie Mercury", "person", "AI")
q("Easy", "Members", "Which Queen member wrote 'Another One Bites the Dust'?", "John Deacon", "person", "AI")
q("Easy", "Members", "Which Queen member wrote 'We Will Rock You'?", "Brian May", "person", "AI")
q("Easy", "Members", "Which Queen member wrote 'Radio Ga Ga'?", "Roger Taylor", "person", "AI")
q("Easy", "Members", "Which Queen member wrote 'Bohemian Rhapsody'?", "Freddie Mercury", "person", "OFFICIAL_OPERA")
q("Easy", "Members", "Which Queen member wrote 'You're My Best Friend'?", "John Deacon", "person", "OFFICIAL_OPERA")
q("Easy", "Albums", "Which Queen collection first appeared in 1981?", "Greatest Hits", ["Greatest Hits II", "Queen Rocks", "The Platinum Collection"], "TIMELINE")
q("Easy", "Band history", "In which decade did Queen release their debut album?", "1970s", ["1960s", "1980s", "1990s"], "OFFICIAL_ALBUMS")
q("Easy", "Band history", "How many musicians were in Queen's classic line-up?", "Four", ["Three", "Five", "Six"], "OFFICIAL_HISTORY")
q("Easy", "Band history", "Who joined last to complete Queen's classic line-up?", "John Deacon", "person", "OFFICIAL_JOHN")

# Medium: album associations and well-documented chronology.
q("Medium", "Albums", "Which Queen album contains 'Keep Yourself Alive'?", "Queen", "album", "OFFICIAL_TRACKS")
q("Medium", "Albums", "Which Queen album contains 'The March of the Black Queen'?", "Queen II", "album", "OFFICIAL_TRACKS")
q("Medium", "Albums", "Which Queen album contains 'You're My Best Friend'?", "A Night at the Opera", "album", "OFFICIAL_OPERA")
q("Medium", "Albums", "Which Queen album contains 'Good Old-Fashioned Lover Boy'?", "A Day at the Races", "album", "OFFICIAL_TRACKS")
q("Medium", "Albums", "Which Queen album contains 'Spread Your Wings'?", "News of the World", "album", "OFFICIAL_TRACKS")
q("Medium", "Albums", "Which Queen album contains 'Teo Torriatte (Let Us Cling Together)'?", "A Day at the Races", "album", "OFFICIAL_TRACKS")
q("Medium", "Albums", "Which Queen album contains 'Bicycle Race'?", "Jazz", "album", "AI")
q("Medium", "Albums", "Which Queen album contains 'Fat Bottomed Girls'?", "Jazz", "album", "AI")
q("Medium", "Albums", "Which Queen album contains 'Play the Game'?", "The Game", "album", "AI")
q("Medium", "Albums", "Which Queen album contains 'Save Me'?", "The Game", "album", "AI")
q("Medium", "Albums", "Which Queen album contains 'Under Pressure'?", "Hot Space", "album", "AI")
q("Medium", "Albums", "Which Queen album contains 'Body Language'?", "Hot Space", "album", "AI")
q("Medium", "Albums", "Which Queen album contains 'I Want to Break Free'?", "The Works", "album", "AI")
q("Medium", "Albums", "Which Queen album contains 'It's a Hard Life'?", "The Works", "album", "AI")
q("Medium", "Albums", "Which Queen album contains 'Hammer to Fall'?", "The Works", "album", "AI")
q("Medium", "Albums", "Which Queen album contains 'Who Wants to Live Forever'?", "A Kind of Magic", "album", "AI")
q("Medium", "Albums", "Which Queen album contains 'Friends Will Be Friends'?", "A Kind of Magic", "album", "AI")
q("Medium", "Albums", "Which Queen album contains 'One Vision'?", "A Kind of Magic", "album", "AI")
q("Medium", "Albums", "Which Queen album contains 'I Want It All'?", "The Miracle", "album", "AI")
q("Medium", "Albums", "Which Queen album contains 'Breakthru'?", "The Miracle", "album", "AI")
q("Medium", "Albums", "Which Queen album contains 'The Invisible Man'?", "The Miracle", "album", "AI")
q("Medium", "Albums", "Which Queen album contains 'These Are the Days of Our Lives'?", "Innuendo", "album", "OFFICIAL_ANNIVERSARY")
q("Medium", "Albums", "Which Queen album contains 'Headlong'?", "Innuendo", "album", "AI")
q("Medium", "Albums", "Which Queen album contains 'I'm Going Slightly Mad'?", "Innuendo", "album", "AI")
q("Medium", "Albums", "Which Queen album contains 'Heaven for Everyone'?", "Made in Heaven", "album", "AI")
q("Medium", "Chronology", "In what year was Queen's debut album released?", "1973", "year", "OFFICIAL_ALBUMS")
q("Medium", "Chronology", "In what year was Queen II released?", "1974", "year", "OFFICIAL_ALBUMS")
q("Medium", "Chronology", "In what year was Sheer Heart Attack released?", "1974", "year", "OFFICIAL_ALBUMS")
q("Medium", "Chronology", "In what year was A Night at the Opera released?", "1975", "year", "OFFICIAL_ALBUMS")
q("Medium", "Chronology", "In what year was A Day at the Races released?", "1976", "year", "OFFICIAL_ALBUMS")
q("Medium", "Chronology", "In what year was News of the World released?", "1977", "year", "OFFICIAL_ALBUMS")
q("Medium", "Chronology", "In what year was Jazz released?", "1978", "year", "OFFICIAL_ALBUMS")
q("Medium", "Chronology", "In what year was The Game released?", "1980", "year", "OFFICIAL_ALBUMS")
q("Medium", "Chronology", "In what year was Hot Space released?", "1982", "year", "OFFICIAL_ALBUMS")
q("Medium", "Chronology", "In what year was The Works released?", "1984", "year", "OFFICIAL_ALBUMS")
q("Medium", "Chronology", "In what year was A Kind of Magic released?", "1986", "year", "OFFICIAL_ALBUMS")
q("Medium", "Chronology", "In what year was The Miracle released?", "1989", "year", "OFFICIAL_ALBUMS")
q("Medium", "Chronology", "In what year was Innuendo released?", "1991", "year", "OFFICIAL_ALBUMS")
q("Medium", "Chronology", "In what year was Made in Heaven released?", "1995", "year", "OFFICIAL_ALBUMS")
q("Medium", "Concerts", "In what year did Queen perform at Live Aid?", "1985", "year", "TIMELINE")
q("Medium", "Concerts", "In what year was Queen's free Hyde Park concert?", "1976", "year", "TIMELINE")
q("Medium", "Concerts", "In what year did Queen play their famous Budapest show?", "1986", "year", "TIMELINE")
q("Medium", "Concerts", "In what year was Queen's final show with Freddie Mercury at Knebworth?", "1986", "year", "TIMELINE")
q("Medium", "Concerts", "In what year was the Freddie Mercury Tribute Concert held?", "1992", "year", "TIMELINE")
q("Medium", "Songs", "In what year was 'Bohemian Rhapsody' first released as a single?", "1975", "year", "TIMELINE")
q("Medium", "Songs", "In what year was 'Killer Queen' released as a single?", "1974", "year", "TIMELINE")
q("Medium", "Songs", "In what year was 'Radio Ga Ga' released as a single?", "1984", "year", "TIMELINE")
q("Medium", "Songs", "In what year was 'Under Pressure' released as a single?", "1981", "year", "TIMELINE")
q("Medium", "Songs", "In what year was 'Another One Bites the Dust' released as a single?", "1980", "year", "TIMELINE")
q("Medium", "Songs", "In what year was 'Innuendo' released as a single?", "1991", "year", "TIMELINE")

# Hard: deep album tracks, early band history, and record-making details.
q("Hard", "Albums", "Which Queen album contains 'Great King Rat'?", "Queen", "album", "AI")
q("Hard", "Albums", "Which Queen album contains 'My Fairy King'?", "Queen", "album", "AI")
q("Hard", "Albums", "Which Queen album contains 'Liar'?", "Queen", "album", "AI")
q("Hard", "Albums", "Which Queen album contains 'Father to Son'?", "Queen II", "album", "AI")
q("Hard", "Albums", "Which Queen album contains 'White Queen (As It Began)'?", "Queen II", "album", "AI")
q("Hard", "Albums", "Which Queen album contains 'Ogre Battle'?", "Queen II", "album", "AI")
q("Hard", "Albums", "Which Queen album contains 'Brighton Rock'?", "Sheer Heart Attack", "album", "AI")
q("Hard", "Albums", "Which Queen album contains 'Tenement Funster'?", "Sheer Heart Attack", "album", "AI")
q("Hard", "Albums", "Which Queen album contains 'Flick of the Wrist'?", "Sheer Heart Attack", "album", "AI")
q("Hard", "Albums", "Which Queen album contains 'Stone Cold Crazy'?", "Sheer Heart Attack", "album", "OFFICIAL_SHEER")
q("Hard", "Albums", "Which Queen album contains 'Death on Two Legs'?", "A Night at the Opera", "album", "AI")
q("Hard", "Albums", "Which Queen album contains 'I'm in Love with My Car'?", "A Night at the Opera", "album", "AI")
q("Hard", "Albums", "Which Queen album contains ''39'?", "A Night at the Opera", "album", "OFFICIAL_TRACKS")
q("Hard", "Albums", "Which Queen album contains 'The Prophet's Song'?", "A Night at the Opera", "album", "OFFICIAL_OPERA")
q("Hard", "Albums", "Which Queen album contains 'Good Company'?", "A Night at the Opera", "album", "OFFICIAL_OPERA")
q("Hard", "Albums", "Which Queen album contains 'Tie Your Mother Down'?", "A Day at the Races", "album", "AI")
q("Hard", "Albums", "Which Queen album contains 'The Millionaire Waltz'?", "A Day at the Races", "album", "AI")
q("Hard", "Albums", "Which Queen album contains 'Drowse'?", "A Day at the Races", "album", "AI")
q("Hard", "Albums", "Which Queen album contains 'All Dead, All Dead'?", "News of the World", "album", "AI")
q("Hard", "Albums", "Which Queen album contains 'My Melancholy Blues'?", "News of the World", "album", "AI")
q("Hard", "Albums", "Which Queen album contains 'Mustapha'?", "Jazz", "album", "AI")
q("Hard", "Albums", "Which Queen album contains 'Dreamers Ball'?", "Jazz", "album", "AI")
q("Hard", "Albums", "Which Queen album contains 'Dragon Attack'?", "The Game", "album", "AI")
q("Hard", "Albums", "Which Queen album contains 'Sail Away Sweet Sister'?", "The Game", "album", "AI")
q("Hard", "Albums", "Which Queen album contains 'Back Chat'?", "Hot Space", "album", "AI")
q("Hard", "Early history", "What was the name of John Deacon's school-age band in Oadby?", "The Opposition", ["Smile", "Ibex", "1984"], "EY")
q("Hard", "Early history", "What was The Opposition briefly renamed in 1966?", "The New Opposition", ["The Art", "The Reaction", "The Cross"], "EY")
q("Hard", "Early history", "What later name did The Opposition use while John Deacon was still a member?", "The Art", ["Smile", "The New Opposition", "Ibex"], "EY")
q("Hard", "Early history", "What was Brian May's pre-Smile band called?", "1984", ["Ibex", "The Opposition", "The Hectics"], "AI")
q("Hard", "Early history", "At which London college did Brian May study physics and astronomy?", "Imperial College", ["Chelsea College", "Ealing College of Art", "King's College"], "AI")
q("Hard", "Early history", "At which art college did Freddie Mercury study graphic design?", "Ealing College of Art", ["Imperial College", "Chelsea College", "Royal College of Music"], "AI")
q("Hard", "Early history", "What was the name of Freddie Mercury's Liverpool band before Queen?", "Ibex", ["Smile", "1984", "The Opposition"], "AI")
q("Hard", "Early history", "Which early Queen bassist had previously sung and played bass in Smile?", "Tim Staffell", "person", "AI")
q("Hard", "Early history", "Which Queen member grew up in Oadby, near Leicester?", "John Deacon", "person", "EY")
q("Hard", "Early history", "In which Cornish town did Roger Taylor grow up?", "Truro", ["Falmouth", "Penzance", "St Austell"], "AI")
q("Hard", "Early history", "Which Queen member was born in King's Lynn?", "Roger Taylor", "person", "AI")
q("Hard", "Equipment", "What nickname is given to John Deacon's small custom amplifier?", "Deacy Amp", ["Red Special", "Treble Booster", "Vox AC30"], "OFFICIAL_JOHN")
q("Hard", "Equipment", "What instrument did Brian May build with his father?", "Red Special guitar", ["Deacy Amp", "Bass drum", "Electric piano"], "AI")
q("Hard", "Equipment", "What old household object supplied wood for the Red Special's neck?", "A fireplace mantel", ["A dining table", "A wardrobe", "A staircase banister"], "BRIAN_GUITAR")
q("Hard", "Recording", "Which producer worked with Queen on A Night at the Opera?", "Roy Thomas Baker", "person", "OFFICIAL_OPERA")
q("Hard", "Recording", "Which Welsh studio was among the locations used for A Night at the Opera?", "Rockfield Studios", ["Trident Studios", "Olympic Studios", "Sarm West Studios"], "OFFICIAL_OPERA")
q("Hard", "Film", "Which 1927 silent film supplied footage for the 'Radio Ga Ga' video?", "Metropolis", ["Nosferatu", "The General", "The Cabinet of Dr. Caligari"], "VD")
q("Hard", "Film", "Who directed the silent film whose footage appears in the 'Radio Ga Ga' video?", "Fritz Lang", ["F. W. Murnau", "Charlie Chaplin", "Sergei Eisenstein"], "VD")
q("Hard", "Film", "Which Queen album is the soundtrack to the 1980 Flash Gordon film?", "Flash Gordon", "album", "VD")
q("Hard", "Concerts", "Which Queen touring keyboardist first approached them about Live Aid?", "Spike Edney", "person", "VD")
q("Hard", "Concerts", "At which venue did Queen end their 1986 Magic Tour?", "Knebworth", ["Wembley Stadium", "Milton Keynes Bowl", "Hyde Park"], "TIMELINE")
q("Hard", "Songs", "Which Queen song was the first credited to all four band members?", "Stone Cold Crazy", ["One Vision", "Under Pressure", "Bohemian Rhapsody"], "OFFICIAL_SHEER")
q("Hard", "Songs", "Which song was intended for Sheer Heart Attack but appeared on News of the World?", "Sheer Heart Attack", ["Brighton Rock", "Stone Cold Crazy", "It's Late"], "OFFICIAL_SHEER")
q("Hard", "Collaborations", "Which guitarist jammed with Brian May for the Star Fleet Project?", "Eddie Van Halen", "person", "VD")
q("Hard", "Collaborations", "Who sang with Freddie Mercury on the album Barcelona?", "Montserrat Caballé", ["Annie Lennox", "Kate Bush", "Tina Turner"], "AI")

# Very Hard: specific, source-backed details that reward close reading.
q("Very Hard", "Origins", "Which school did Brian May attend as a teenager?", "Hampton Grammar School", ["Beauchamp Grammar School", "Truro School", "St Peter's School"], "AI")
q("Very Hard", "Origins", "Which school did Roger Taylor attend in Cornwall?", "Truro School", ["Hampton Grammar School", "Beauchamp Grammar School", "Ealing School of Art"], "AI")
q("Very Hard", "Origins", "Which Leicester-area grammar school did John Deacon attend?", "Beauchamp Grammar School", ["Hampton Grammar School", "Truro School", "St Peter's School"], "EY")
q("Very Hard", "Origins", "Which instrument did Brian May learn before guitar?", "Ukulele", ["Violin", "Clarinet", "Bass guitar"], "AI")
q("Very Hard", "Origins", "What kind of guitar did Brian May receive for his seventh birthday?", "A small Spanish acoustic", ["A Fender Stratocaster", "A Gibson Les Paul", "A steel guitar"], "AI")
q("Very Hard", "Origins", "What subject did Brian May study at Imperial College?", "Physics", ["Dentistry", "Graphic design", "Electronics engineering"], "AI")
q("Very Hard", "Origins", "What field of astronomy did Brian May research at Imperial College?", "Infrared astronomy", ["Radio astronomy", "Planetary geology", "Optical cosmology"], "AI")
q("Very Hard", "Origins", "What was Roger Taylor's original university subject before biology?", "Dentistry", ["Physics", "Graphic design", "Architecture"], "AI")
q("Very Hard", "Origins", "What did John Deacon study at Chelsea College?", "Electronics", ["Dentistry", "Fine art", "Astronomy"], "EY")
q("Very Hard", "Origins", "Which former Opposition member kept a diary of the band's early gigs?", "Richard Young", ["Nigel Bullen", "Clive Castledine", "John Savage"], "EY")
q("Very Hard", "Origins", "Who played drums in John Deacon's early band The Opposition?", "Nigel Bullen", ["Richard Young", "Peter Bartholomew", "John Savage"], "EY")
q("Very Hard", "Origins", "Who was The Opposition's singer and lead guitarist when John Deacon joined?", "Richard Young", ["Nigel Bullen", "Clive Castledine", "Tim Staffell"], "EY")
q("Very Hard", "Origins", "What name did The Opposition briefly adopt after returning to their original name?", "The Art", ["The Cross", "Smile", "The Hectics"], "EY")
q("Very Hard", "Origins", "In which month of 1969 did John Deacon play his final concert with The Art?", "August", ["April", "June", "October"], "EY")
q("Very Hard", "Origins", "Which course took John Deacon from Leicester to Chelsea College?", "Electronics", ["Fine art", "Biology", "Mechanical engineering"], "EY")
q("Very Hard", "Origins", "Which Deep Purple performance did John Deacon and his friends see at the Royal Albert Hall?", "Concerto for Group and Orchestra", ["Made in Japan", "The Butterfly Ball", "Rock Meets Classic"], "EY")
q("Very Hard", "Origins", "Which Queen member's early band was also called The New Opposition for a time?", "John Deacon", "person", "EY")
q("Very Hard", "Equipment", "What was the approximate material cost of Brian May's Red Special?", "£8", ["£80", "£800", "£18"], "VD")
q("Very Hard", "Equipment", "What was recycled for springs in the Red Special's tremolo unit?", "An old motorbike", ["A grandfather clock", "A bicycle", "A bed frame"], "VD")
q("Very Hard", "Equipment", "What wood was the Red Special's neck carved from?", "Mahogany", ["Maple", "Ash", "Oak"], "BRIAN_GUITAR")
q("Very Hard", "Origins", "Before switching to bass in The Opposition, which instrument did John Deacon play?", "Guitar", ["Drums", "Organ", "Saxophone"], "EY")
q("Very Hard", "Equipment", "Which instrument did John Deacon bring to his Queen audition alongside a custom amplifier?", "Bass guitar", ["Electric piano", "Drum kit", "Saxophone"], "OFFICIAL_JOHN")
q("Very Hard", "Early Queen", "Which Queen song did John Deacon begin learning at his audition?", "Son and Daughter", ["Keep Yourself Alive", "Liar", "Seven Seas of Rhye"], "OFFICIAL_JOHN")
q("Very Hard", "Early Queen", "At which college did John Deacon audition for Queen?", "Imperial College", ["Chelsea College", "Ealing College of Art", "Trinity College"], "OFFICIAL_JOHN")
q("Very Hard", "Early Queen", "Which record company released Smile's single 'Earth'?", "Mercury Records", ["EMI", "Elektra", "Island Records"], "VD")
q("Very Hard", "Early Queen", "Who wrote Smile's song 'Earth'?", "Tim Staffell", "person", "VD")
q("Very Hard", "Early Queen", "What was the title of Smile's single backed with 'Step on Me'?", "Earth", ["Doing All Right", "April Lady", "Polar Bear"], "VD")
q("Very Hard", "Early Queen", "Which future Queen member had a band called 1984?", "Brian May", "person", "AI")
q("Very Hard", "Early Queen", "Which future Queen member was in the Liverpool group Ibex?", "Freddie Mercury", "person", "AI")
q("Very Hard", "Early Queen", "Who designed Queen's crest using the members' zodiac signs?", "Freddie Mercury", "person", "AI")
q("Very Hard", "Records", "How many purple-vinyl 'Bohemian Rhapsody' singles did EMI press in 1978 for its Queen's Award to Industry?", "300", ["30", "3,000", "1,000"], "VD")
q("Very Hard", "Records", "What colour was EMI's 1978 commemorative pressing of 'Bohemian Rhapsody'?", "Royal purple", ["Gold", "Silver", "Royal blue"], "VD")
q("Very Hard", "Origins", "Who was replaced on bass in The Opposition when John Deacon switched instruments?", "Clive Castledine", ["Nigel Bullen", "Richard Young", "Peter Bartholomew"], "EY")
q("Very Hard", "Origins", "On what date did The Opposition's diary first record John Deacon playing bass at rehearsal?", "2 April 1966", ["2 April 1965", "2 June 1966", "2 August 1966"], "EY")
q("Very Hard", "Origins", "Which hall in Enderby hosted regular Saturday-night performances by The Opposition?", "Co-op Hall", ["Town Hall", "Corn Exchange", "Civic Hall"], "EY")
q("Very Hard", "Videos", "Which Queen video reused the head-and-shoulders arrangement from the 'Bohemian Rhapsody' clip?", "One Vision", "song", "VD")
q("Very Hard", "Origins", "How much did The Opposition initially earn for a performance at Enderby's Co-op Hall?", "£4", ["£2", "£12", "£40"], "EY")
q("Very Hard", "Videos", "Which film producer negotiated use of Metropolis footage in exchange for a Queen song?", "Giorgio Moroder", ["Dino De Laurentiis", "David Puttnam", "Graham King"], "VD")
q("Very Hard", "Film", "Which producer approached Queen to score Flash Gordon?", "Dino De Laurentiis", ["Giorgio Moroder", "George Lucas", "Michael Kamen"], "VD")
q("Very Hard", "Origins", "Which band did Queen support at Leeds Town Hall on 12 November 1973?", "Mott the Hoople", ["Status Quo", "T. Rex", "Uriah Heep"], "EY")
q("Very Hard", "Live", "Which Queen touring keyboardist also played trombone with the Boomtown Rats?", "Spike Edney", "person", "VD")
q("Very Hard", "Live", "Which David Bowie performance at the 1992 Freddie Mercury Tribute Concert reunited Spiders from Mars and Mott the Hoople players?", "All the Young Dudes", ["Heroes", "Under Pressure", "Ziggy Stardust"], "VD")
q("Very Hard", "Live", "Which Leeds football stadium hosted a relocated Queen concert during the 1982 tour?", "Elland Road", ["Hillsborough", "Villa Park", "St James' Park"], "VD")
q("Very Hard", "Live", "Which Milton Keynes venue hosted a relocated Queen concert in 1982?", "Milton Keynes Bowl", ["National Bowl Arena", "Wembley Arena", "Roundhouse"], "VD")
q("Very Hard", "Collaborations", "Which REO Speedwagon drummer joined Brian May and Eddie Van Halen on the Star Fleet Project?", "Alan Gratzer", ["Neal Smith", "Roger Taylor", "Simon Phillips"], "VD")
q("Very Hard", "Collaborations", "What was the title of Brian May's 1983 mini-album with Eddie Van Halen?", "Star Fleet Project", ["Back to the Light", "Another World", "The Cosmos Rocks"], "VD")
q("Very Hard", "Collaborations", "Which Queen member made the 1983 Star Fleet Project record?", "Brian May", "person", "VD")
q("Very Hard", "Band history", "Which Sex Pistols member reportedly exchanged barbs with Freddie Mercury at a recording studio?", "Sid Vicious", ["Johnny Rotten", "Steve Jones", "Paul Cook"], "VD")
q("Very Hard", "Artwork", "Which photographer created the cover image for Queen II?", "Mick Rock", ["Richard Avedon", "David Bailey", "Anton Corbijn"], "EY")
q("Very Hard", "Band history", "What was the make of John Deacon's first bass guitar after his switch from guitar?", "Eko", ["Fender", "Rickenbacker", "Hofner"], "OFFICIAL_JOHN")

def write():
    assert len(rows) == 200, len(rows)
    assert len({r["Question"].casefold() for r in rows}) == len(rows)
    for r in rows:
        assert len(r["Question"]) <= 500
        assert all(len(r[f"Option{i}"]) <= 200 for i in range(1, 5))
        assert len({r[f"Option{i}"].casefold() for i in range(1, 5)}) == 4
        assert r[f"Option{r['CorrectOption']}"] != ""
    # Interleave topics within each difficulty without breaking importer adjacency.
    for start in range(0, len(rows), 50):
        block = rows[start:start + 50]
        random.Random(1583 + start).shuffle(block)
        rows[start:start + 50] = block
    with (ROOT / "questions.csv").open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=[
            "QuizTitle", "QuizDescription", "QuestionText", "Category",
            "Difficulty", "Points", "OptionText", "IsCorrect",
        ])
        writer.writeheader()
        for n, row in enumerate(rows):
            quiz_title = f"{row['QuizTitle']} (Part {(n % 50) // 25 + 1})"
            for option_index in range(1, 5):
                writer.writerow({
                    "QuizTitle": quiz_title,
                    "QuizDescription": (
                        f"{row['Difficulty']} Queen questions drawn from supplied books and official sources."
                        if n % 25 == 0 and option_index == 1 else ""
                    ),
                    "QuestionText": row["Question"],
                    "Category": row["Category"] if option_index == 1 else "",
                    "Difficulty": (
                        row["Difficulty"].lower() if row["Difficulty"] != "Very Hard" else "hard"
                    ) if option_index == 1 else "",
                    "Points": row["Points"] if option_index == 1 else "",
                    "OptionText": row[f"Option{option_index}"],
                    "IsCorrect": "true" if row["CorrectOption"] == option_index else "false",
                })
    with (ROOT / "sources.csv").open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=[
            "QuizTitle", "QuestionText", "EditorialDifficulty", "Source",
        ])
        writer.writeheader()
        for n, row in enumerate(rows):
            writer.writerow({
                "QuizTitle": f"{row['QuizTitle']} (Part {(n % 50) // 25 + 1})",
                "QuestionText": row["Question"],
                "EditorialDifficulty": row["Difficulty"],
                "Source": row["Source"],
            })

if __name__ == "__main__":
    write()
