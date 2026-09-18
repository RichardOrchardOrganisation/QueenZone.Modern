"""Build 300 additional Queen questions for the PR #1583 bulk importer."""

import csv
import random
from collections import Counter
from pathlib import Path

HERE = Path(__file__).parent
REPO = HERE.parents[2]
TIMELINE = list(csv.DictReader((REPO / "data/queen_history_events.csv").open(encoding="utf-8-sig")))

TITLES = [
    "Queen Quiz: Before the Crown",
    "Queen Quiz: Breakthrough 1973–1976",
    "Queen Quiz: World Stage 1977–1982",
    "Queen Quiz: Works and Magic 1983–1986",
    "Queen Quiz: Solo Paths and Final Albums",
    "Queen Quiz: Studio and Stage Details",
]
URLS = {
    "L70": "https://www.queenonline.com/live",
    **{f"L{year % 100:02}": f"https://www.queenonline.com/live/{year}" for year in (1973,1974,1975,1976,1977,1978,1979,1980,1981,1982,1984,1985,1986)},
    "Q1": "https://www.queenonline.com/news/queen-i-queen-remixed-remastered-and-expanded-out-now",
    "Q2": "https://store-us.queenonline.com/products/queen-ii-2cd-deluxe-edition",
    "SHA": "https://www.queenonline.com/news/sheer-heart-attack50-years-on",
    "RACES": "https://www.queenonline.com/news/a-day-at-the-races-47-years-on",
    "STUDIO": "https://www.queenonline.com/news/studio-collection-album-quotes-from-box-set-book",
    "RAINBOW": "https://www.queenonline.com/news/watch-queen-the-greatest-the-landmark-gig-live-at-the-rainbow-episode-2",
    "JOHN": "https://www.queenonline.com/john_deacon",
    "ROGER": "https://www.queenonline.com/roger_taylor",
    "FREDDIE": "https://www.queenonline.com/freddie_mercury",
    "BRIAN": "https://www.queenonline.com/brian_may",
    "NOTW": "https://www.queenonline.com/news/news-of-the-world47-years-on",
    "JAZZ": "https://www.queenonline.com/news/jazz46-years-on",
    "GAME": "https://www.queenonline.com/news/the-game-44-years-on",
    "FLASH": "https://www.queenonline.com/news/flash-gordon44-years-on",
    "HOT": "https://www.queenonline.com/news/hot-space-42-years-on",
    "MAGIC": "https://www.queenonline.com/news/a-kind-of-magic-38-years-on",
    "MIRACLE": "https://www.queenonline.com/news/the-miracle-35-years-on",
    "INNUENDO": "https://www.queenonline.com/news/innuendo-35-years-on",
    "HEAVEN": "https://www.queenonline.com/news/made-in-heaven30-years-on",
    "TRIBUTE": "https://www.queenonline.com/news/press-release-the-freddie-mercury-tribute-concert-the-definitive-edition",
    "FIVE": "https://www.queenonline.com/news/queen-first-five-albums-re-issued-march-14th",
    "COOL": "https://www.queenonline.com/news/today-cool-cat-released-as-7-single-for-record-store-day",
    "MAINE": "https://www.mancity.com/news/mens/maine-road-100-maine-events-63828483",
}
POOLS = {
    "members": ["Freddie Mercury", "Brian May", "Roger Taylor", "John Deacon"],
    "years": [str(y) for y in range(1963, 1996)],
    "albums": ["Queen", "Queen II", "Sheer Heart Attack", "A Night at the Opera", "A Day at the Races", "News of the World", "Jazz", "The Game", "Flash Gordon", "Hot Space", "The Works", "A Kind of Magic", "The Miracle", "Innuendo", "Made in Heaven"],
    "cities": ["London", "Liverpool", "Truro", "Leicester", "Manchester", "Leeds", "Glasgow", "New York", "Los Angeles", "Tokyo", "Budapest", "Munich", "Paris", "Rio de Janeiro", "Stockholm", "Brussels", "Vienna", "Toronto", "Montreal"],
    "venues": ["Wembley Stadium", "Hammersmith Odeon", "Rainbow Theatre", "Earls Court Arena", "Madison Square Garden", "Budokan", "Knebworth Park", "Hyde Park", "Milton Keynes Bowl", "The Marquee", "Los Angeles Forum", "Rockfield Studios"],
}

records = []

def source_ref(ref):
    if isinstance(ref, int):
        row = TIMELINE[ref]
        label = f"data/queen_history_events.csv: {row['SourceType']} / {row['SourceKey']}"
        return label + (f" / {row['SourceUrl']}" if row['SourceUrl'] else "")
    return URLS[ref]

def add(group, difficulty, category, question, answer, wrong, source):
    assert 0 <= group < 6 and difficulty in {"easy", "medium", "hard", "very hard"}
    if isinstance(wrong, str) and wrong in POOLS:
        pool = POOLS[wrong]
        assert answer in pool, (question, answer)
        i = pool.index(answer)
        wrongs = []
        for delta in (1, -1, 2, -2, 3, -3, 4, -4):
            choice = pool[(i + delta) % len(pool)]
            if choice != answer and choice not in wrongs:
                wrongs.append(choice)
            if len(wrongs) == 3:
                break
    else:
        wrongs = list(wrong)
    assert len(wrongs) == 3 and len({answer.casefold(), *(w.casefold() for w in wrongs)}) == 4, question
    index = len(records) % 4
    options = wrongs.copy()
    options.insert(index, answer)
    records.append({
        "title": TITLES[group], "difficulty": difficulty,
        "category": category, "question": question,
        "options": options, "correct": index,
        "points": {"easy": 1, "medium": 2, "hard": 3, "very hard": 4}[difficulty],
        "source": source_ref(source),
    })

# 1. Before the Crown — childhood groups and the classic line-up's formation.
add(0,"hard","Brian May","In which town did Brian May and his father begin building the Red Special?","Feltham",["Hampton","Twickenham","Ealing"],253)
add(0,"hard","Brian May","What was the venue for 1984's first public gig in October 1964?","St Mary's Church Hall",["Imperial College","Truro City Hall","Royal Albert Hall"],254)
add(0,"medium","Brian May","Which member of Queen played his first public gig with 1984 at St Mary's Church Hall?","Brian May","members",254)
add(0,"hard","Brian May","Which guitarist's band 1984 supported Jimi Hendrix at Imperial College in 1967?","Brian May","members",258)
add(0,"very hard","Brian May","Where did Brian May's band 1984 win a talent contest in 1967?","Top Rank Club, Croydon",["Marquee Club, London","Cavern Club, Liverpool","Rainbow Theatre, London"],259)
add(0,"very hard","Brian May","Which all-night 1967 event put 1984 on a bill with Jimi Hendrix, Pink Floyd and Traffic?","Christmas on Earth",["The 14 Hour Technicolor Dream","Festival of the Flower Children","The British Blues Festival"],260)
add(0,"hard","Roger Taylor","What was the name of Roger Taylor's first childhood group?","The Bubblingover Boys",["The Reaction","The Cousin Jacks","The Hectics"],262)
add(0,"hard","Roger Taylor","Which school gave Roger Taylor a choral scholarship?","Truro Cathedral School",["Truro School","Hampton Grammar School","St Peter's School"],263)
add(0,"hard","Roger Taylor","What was Roger Taylor's Truro band the Cousin Jacks briefly renamed?","The Falcons",["The Hectics","The Reaction","Smile"],265)
add(0,"hard","Roger Taylor","Which Cornish band did Roger Taylor join in 1965 before it became simply Reaction?","Johnny Quale and the Reaction",["The Cousin Jacks","The New Opposition","The Art"],266)
add(0,"very hard","Roger Taylor","After Johnny Quale left, who took over lead vocals in Reaction?","Roger Taylor","members",268)
add(0,"hard","Roger Taylor","Which subject did Roger Taylor start studying in London in 1967?","Dentistry",["Biology","Astronomy","Fine art"],271)
add(0,"medium","Smile","Which future Queen drummer answered Brian May's noticeboard advert?","Roger Taylor","members",273)
add(0,"hard","Smile","Which band did Smile support at their first public gig at Imperial College?","Pink Floyd",["The Who","Led Zeppelin","The Kinks"],276)
add(0,"very hard","Smile","Which band did Smile support at a Richmond Athletic Club gig in early 1969?","Yes",["Free","Traffic","Spooky Tooth"],278)
add(0,"very hard","Smile","Which producer recorded Smile's two Mercury Records tracks at Trident Studios?","John Anthony",["Roy Thomas Baker","Mike Stone","Reinhold Mack"],282)
add(0,"hard","Smile","Which US label released Smile's 'Earth' single?","Mercury Records",["EMI","Elektra","Capitol Records"],284)
add(0,"hard","Smile","Who introduced Freddie Bulsara to a Smile rehearsal while they were art-college friends?","Tim Staffell",["John Anthony","Mike Grose","Barry Mitchell"],283)
add(0,"very hard","Smile","At which London club did Smile play a Mercury Records showcase supporting Kippington Lodge?","The Marquee",["The Roundhouse","The Speakeasy","The Lyceum"],285)
add(0,"medium","Freddie Mercury","At which Indian boarding school did young Freddie Bulsara study?","St Peter's",["Truro School","Hampton Grammar","Beauchamp Grammar"],289)
add(0,"hard","Freddie Mercury","In which Indian town was Freddie's St Peter's boarding school?","Panchgani",["Mumbai","Pune","Goa"],289)
add(0,"hard","Freddie Mercury","Which London college did Freddie attend before Ealing College of Art?","Isleworth Polytechnic",["Imperial College","Chelsea College","London Hospital Medical College"],293)
add(0,"medium","Freddie Mercury","What subject did Freddie study at Isleworth Polytechnic?","Art",["Physics","Biology","Electronics"],293)
add(0,"hard","Freddie Mercury","Which qualification did Freddie gain from Ealing College of Art in 1969?","A diploma in graphic art and design",["A degree in physics","A biology degree","A dentistry diploma"],296)
add(0,"hard","Freddie Mercury","Which London market did Freddie and Roger run a stall in?","Kensington Market",["Camden Market","Portobello Road Market","Covent Garden Market"],297)
add(0,"very hard","Freddie Mercury","Which short-lived band did Freddie audition for in Leatherhead?","Sour Milk Sea",["The Reaction","The Opposition","1984"],300)
add(0,"hard","Freddie Mercury","Which pre-Queen band included Freddie, Mike Bersin and Tupp Taylor?","Wreckage",["Smile","The Art","The Hectics"],301)
add(0,"hard","Early Queen","Who played bass at Queen's first three shows in 1970?","Mike Grose",["Barry Mitchell","Tim Staffell","John Deacon"],"L70")
add(0,"hard","Early Queen","Who replaced Mike Grose on bass in Queen's early 1970 line-up?","Barry Mitchell",["John Deacon","Tim Staffell","Clive Castledine"],"L70")
add(0,"very hard","Early Queen","Which Truro venue hosted the first gig actually billed under the name Queen?","PJ's",["City Hall","The Marquee","Tregye Country Club"],305)
add(0,"very hard","Early Queen","At which college did Barry Mitchell first play a Queen gig in August 1970?","Imperial College",["Chelsea College","Ealing College of Art","Bedford College"],307)
add(0,"hard","Early Queen","Which bassist played his last Queen shows in January 1971?","Barry Mitchell",["Mike Grose","Tim Staffell","John Deacon"],310)
add(0,"very hard","John Deacon","At what kind of event did The Opposition play their first public gig?","A house party",["A school assembly","A radio session","A football match"],314)
add(0,"very hard","John Deacon","On what date did The Opposition play their first major gig at Enderby's Co-operative Hall?","4 December 1965",["4 December 1966","14 December 1965","4 November 1965"],315)
add(0,"hard","Early Queen","Which band headlined Queen's first outdoor gig near Truro in 1971?","Arthur Brown's Kingdom Come",["Mott the Hoople","Pink Floyd","The Who"],327)
add(0,"very hard","John Deacon","How much did John Deacon pay for his first Eko bass?","£22",["£12","£60","£100"],316)
add(0,"hard","John Deacon","Which college awarded John Deacon a first-class honours degree in electronics?","Chelsea College",["Imperial College","Ealing College of Art","Truro School"],333)
add(0,"very hard","John Deacon","At which college disco was John Deacon introduced to Brian May and Roger Taylor?","Maria Assumpta College",["Imperial College","Bedford College","Chelsea College"],323)
add(0,"very hard","John Deacon","What makeshift band name did John Deacon use for a one-off college gig in 1970?","Deacon",["The Art","The New Opposition","Smile"],322)
add(0,"hard","Early Queen","At what type of venue did John Deacon organise a 1972 Queen gig attended by just six people?","Bedford College",["Rainbow Theatre","Hyde Park","Royal Albert Hall"],329)
add(0,"hard","Early Queen","Which production company signed Queen to recording, publishing and management agreements?","Trident Audio Productions",["EMI Records","Mercury Records","Rocket Records"],331)
add(0,"hard","Early Queen","Which Trident executive saw Queen at a Forest Hill hospital dance and offered a deal?","Barry Sheffield",["Jack Nelson","John Anthony","Roy Thomas Baker"],330)
add(0,"very hard","Early Queen","How much weekly pay did Trident agree for each Queen member in 1972?","£20",["£10","£50","£100"],335)
add(0,"medium","Early Queen","Which band member earned a biology degree before Queen's debut album?","Roger Taylor","members",334)
add(0,"hard","Early Queen","What was Queen's first BBC Radio One programme in 1973?","Sounds of the Seventies",["Top Gear","In Concert","The Old Grey Whistle Test"],341)
add(0,"very hard","Early Queen","Which DJ broadcast Queen's 1973 BBC session with new versions of their early songs?","John Peel",["Alan Freeman","Bob Harris","Kenny Everett"],"Q1")
add(0,"hard","Early Queen","Which label first signed Queen for UK and European record releases?","EMI",["Elektra","Mercury","Capitol"],343)
add(0,"hard","Early Queen","Which company released Queen's debut album in the United States?","Elektra Records",["EMI","Mercury Records","Capitol Records"],352)
add(0,"very hard","Early Queen","At which unusual Epsom venue did Queen support Arthur Brown's Kingdom Come in 1971?","A swimming baths",["A racecourse","A cinema","A football ground"],328)
add(0,"hard","Early Queen","Which future Queen member studied graphic design at Ealing College of Art?","Freddie Mercury","members",295)

# 2. Breakthrough 1973–1976 — BBC, first tours, videos and album sessions.
add(1,"very hard","Radio","Which BBC presenter chose 'Keep Yourself Alive' for The Old Grey Whistle Test in 1973?","Mike Appleton",["John Peel","Bob Harris","Kenny Everett"],347)
add(1,"hard","Singles","What song appeared on the B-side of Queen's first UK single, 'Keep Yourself Alive'?","Son and Daughter",["Liar","Doing All Right","Great King Rat"],345)
add(1,"hard","Radio","At which venue did the BBC record Queen's September 1973 In Concert session?","Golders Green Hippodrome",["Hammersmith Odeon","Rainbow Theatre","Royal Albert Hall"],351)
add(1,"hard","Radio","Which instrumental opened Queen's 1973 Golders Green Hippodrome concert?","Procession",["Seven Seas of Rhye","Brighton Rock","God Save the Queen"],351)
add(1,"hard","Touring","In which country outside the UK did Queen play one of their first overseas gigs in 1973?","Luxembourg",["Japan","Australia","Brazil"],"L73")
add(1,"hard","Touring","Which US record label sent an executive to Queen's 1973 Marquee showcase?","Elektra Records",["Atlantic Records","Columbia Records","Warner Bros. Records"],"L73")
add(1,"hard","Touring","Which band did Queen support on a 25-date UK tour in late 1973?","Mott the Hoople",["Slade","10cc","Status Quo"],"L73")
add(1,"very hard","Touring","Which English city opened Queen's first major Mott the Hoople support tour?","Leeds","cities",355)
add(1,"hard","Touring","At which London venue did Queen and Mott the Hoople play two shows in December 1973?","Hammersmith Odeon","venues",356)
add(1,"hard","Touring","At which Australian festival did Queen appear early in 1974?","Sunbury Music Festival",["Reading Festival","Glastonbury Festival","Rock in Rio"],"L74")
add(1,"hard","Touring","Which band was Queen supporting on their first US tour?","Mott the Hoople",["Aerosmith","Thin Lizzy","Uriah Heep"],"L74")
add(1,"very hard","Touring","In which US city did Queen play their first American gig in April 1974?","Denver",["Boston","New York","Chicago"],369)
add(1,"hard","Touring","Which illness forced Brian May home from Queen's 1974 US tour?","Hepatitis",["Pneumonia","Appendicitis","Influenza"],"L74")
add(1,"very hard","Touring","Which designer created some of Queen's 1974 stage costumes?","Zandra Rhodes",["Vivienne Westwood","Ossie Clark","Mary Quant"],"L74")
add(1,"hard","Touring","Which London theatre hosted the finale of Queen's first headlining UK tour?","Rainbow Theatre","venues",367)
add(1,"very hard","Touring","Which act supported Queen on their second headlining UK tour in 1974?","Hustler",["Thin Lizzy","Mott the Hoople","Bow Wow Wow"],"L74")
add(1,"hard","Touring","What happened to Queen's equipment truck during their late-1974 European tour?","It was involved in an accident",["It was stolen","It caught fire","It was held at customs"],"L74")
add(1,"hard","Television","Which song gave Queen their first Top of the Pops appearance in 1974?","Seven Seas of Rhye",["Killer Queen","Keep Yourself Alive","Now I'm Here"],360)
add(1,"very hard","Television","Which guitarist lent studio time for Queen's first Top of the Pops backing track?","Pete Townshend",["Eric Clapton","Mick Ronson","Jeff Beck"],359)
add(1,"hard","Records","What was unusual about the two sides of the original Queen II LP sleeve?","They were labelled black and white",["They were both live recordings","One side had no vocals","Each side had a different band name"],363)
add(1,"hard","Records","Which song became Queen's first UK singles-chart entry?","Seven Seas of Rhye",["Killer Queen","Bohemian Rhapsody","Keep Yourself Alive"],362)
add(1,"hard","Recording","At which Welsh studio did Queen rehearse material for their third album in 1974?","Rockfield Studios",["Trident Studios","Wessex Studios","Sarm East"],373)
add(1,"hard","Recording","Which illness led to Brian May's emergency operation during Sheer Heart Attack sessions?","A duodenal ulcer",["Hepatitis","Appendicitis","Tonsillitis"],375)
add(1,"hard","Singles","Which song was paired with 'Killer Queen' on Queen's first double-A-side single?","Flick of the Wrist",["Now I'm Here","Stone Cold Crazy","Brighton Rock"],377)
add(1,"hard","Recording","Who engineered Queen's third album, Sheer Heart Attack?","Mike Stone",["Reinhold Mack","David Richards","Geoff Emerick"],"SHA")
add(1,"very hard","Artwork","Which photographer shot both the Queen II and Sheer Heart Attack covers?","Mick Rock",["David Bailey","Anton Corbijn","Richard Avedon"],"SHA")
add(1,"very hard","Artwork","What did the band apply before being sprayed with water for the Sheer Heart Attack cover?","Vaseline",["Face paint","Glitter","Flour"],"SHA")
add(1,"hard","Songs","What was John Deacon's first composition released on a Queen album?","Misfire",["You're My Best Friend","Spread Your Wings","Another One Bites the Dust"],"SHA")
add(1,"hard","Touring","Which London venue hosted Queen's professionally filmed November 1974 shows?","Rainbow Theatre","venues","RAINBOW")
add(1,"very hard","Touring","How many November 1974 nights did Queen play at the Rainbow Theatre?","Two",["One","Three","Four"],"RAINBOW")
add(1,"very hard","Touring","What was the ticket price cited by Queen's official site for their November 1974 Rainbow concert?","£1.75",["75p","£3.50","£5"],"RAINBOW")
add(1,"hard","Management","Which lawyer helped Queen negotiate an exit from their Trident agreements?","Jim Beach",["John Reid","Peter Grant","Jack Nelson"],383)
add(1,"hard","Touring","Which country gave Queen an airport welcome of more than 3,000 fans in 1975?","Japan",["Australia","Canada","France"],"L75")
add(1,"hard","Touring","At which Tokyo venue did Queen play to about 10,000 fans on their first Japan tour?","Budokan","venues",389)
add(1,"hard","Awards","For which Queen song did Freddie Mercury receive an Ivor Novello Award in 1975?","Killer Queen",["Bohemian Rhapsody","Love of My Life","Somebody to Love"],390)
add(1,"hard","Management","Who became Queen's manager during the A Night at the Opera era?","John Reid",["Jim Beach","Peter Grant","Lou Reizner"],393)
add(1,"very hard","Videos","At which studios was the 'Bohemian Rhapsody' promotional film made?","Elstree Studios",["Shepperton Studios","Pinewood Studios","Ealing Studios"],394)
add(1,"very hard","Videos","Roughly how long did Queen take to film the 'Bohemian Rhapsody' promotional video?","Four hours",["Four days","One week","Two weeks"],394)
add(1,"hard","Touring","Which British city opened Queen's late-1975 UK headline tour?","Liverpool","cities",395)
add(1,"hard","Touring","On which holiday did Queen's televised 1975 Hammersmith Odeon show take place?","Christmas Eve",["New Year's Eve","Boxing Day","Good Friday"],"L75")
add(1,"medium","Recording","Which two Queen album titles were also Marx Brothers film titles?","A Night at the Opera and A Day at the Races",["Queen II and Jazz","The Game and The Works","The Miracle and Innuendo"],"RACES")
add(1,"hard","Recording","Which Queen album was the first the band produced by themselves?","A Day at the Races","albums","RACES")
add(1,"hard","Recording","Who engineered Queen's self-produced A Day at the Races?","Mike Stone",["Roy Thomas Baker","Reinhold Mack","David Richards"],"RACES")
add(1,"hard","Recording","Which studio was used for A Day at the Races alongside The Manor and Wessex?","Sarm East",["Trident Studios","Rockfield Studios","Musicland Studios"],"RACES")
add(1,"hard","Touring","What threatened Queen's 1976 free Hyde Park concert?","Damage from the summer drought",["A transport strike","A stage collapse","A power failure"],"L76")
add(1,"hard","Touring","Which country did Queen tour properly for the first time after Japan in 1976?","Australia",["Brazil","South Africa","Mexico"],"L76")
add(1,"very hard","Touring","How many 1976 Australian concerts does Queen's official live archive say sold out?","Eight",["Four","Six","Eleven"],"L76")
add(1,"hard","Singles","What was John Deacon's first Queen single as sole songwriter?","You're My Best Friend",["Misfire","Spread Your Wings","I Want to Break Free"],404)
add(1,"hard","Touring","Who became Queen's tour manager for their 1976 American tour?","Gerry Stickells",["Jim Beach","Peter Hince","John Reid"],402)
add(1,"very hard","Recording","What winning horse did Queen all back at a racecourse after finishing A Day at the Races?","Lanzarote",["Red Rum","Arkle","Shergar"],406)

# 3. World Stage 1977–1982 — tours, artwork, singles and live recordings.
add(2,"hard","Touring","Which band supported Queen on most dates of the 1977 'Queen Lizzy' US tour?","Thin Lizzy",["Mott the Hoople","The Blasters","Airrace"],"L77")
add(2,"hard","Touring","Which 1977 royal anniversary inspired the 'Queen Lizzy' tour nickname?","The Silver Jubilee",["The Golden Jubilee","A coronation anniversary","A royal wedding"],"L77")
add(2,"hard","Touring","Which New York arena did Queen first headline in February 1977?","Madison Square Garden","venues","L77")
add(2,"hard","Touring","Which London arena hosted Queen's two filmed June 1977 shows?","Earls Court Arena","venues","L77")
add(2,"hard","Videos","Which song's 1977 promo used Queen fan-club members as an audience?","We Are the Champions",["We Will Rock You","Spread Your Wings","It's Late"],"L77")
add(2,"very hard","Videos","At which London theatre did invited Queen fans help film the 'We Are the Champions' promo?","New London Theatre",["Rainbow Theatre","Hammersmith Odeon","Lyceum Theatre"],"L77")
add(2,"hard","Lighting","What was the name of Queen's massive 1977 lighting rig?","The Crown",["The Pizza Oven","Fly Swatters","The Comet"],"L77")
add(2,"very hard","Touring","In which Oregon city did Queen's second US leg of 1977 begin?","Portland",["Seattle","Eugene","Salem"],"L77")
add(2,"hard","Records","Which 1977 Queen record was the band's first EP?","Queen's First EP",["Greatest Hits","Queen II","Live Killers"],412)
add(2,"hard","Solo work","What was Roger Taylor's debut solo single?","I Wanna Testify",["Future Management","Radio Ga Ga","Strange Frontier"],414)
add(2,"hard","Artwork","Which science-fiction artist's robot appeared on the News of the World cover?","Frank Kelly Freas",["Mick Rock","Roger Dean","Storm Thorgerson"],417)
add(2,"hard","Music videos","What did Freddie begin wearing over his leotards for some 1978 shows?","A black PVC jacket and trousers",["A military uniform","A white tuxedo","A leather trench coat"],"L78")
add(2,"hard","Lighting","What nickname did Queen give the new light rig on their 1978 North American tour?","Pizza Oven",["Crown","Fly Swatters","Meteor"],"L78")
add(2,"hard","Touring","Which album supplied new songs to Queen's late-1978 tour set?","Jazz","albums","L78")
add(2,"very hard","Touring","Which city hosted Queen's lavish Jazz album launch party after a 1978 concert?","New Orleans",["Dallas","Atlanta","Memphis"],"L78")
add(2,"hard","Touring","What mode of transport did Queen use between US cities for the first time on their 1978 tour?","Their own private plane",["A tour bus","A chartered train","A helicopter"],"L78")
add(2,"hard","Touring","At which California venue did Queen end their 1978 tour with three nights?","Los Angeles Forum","venues","L78")
add(2,"hard","Songs","Which Queen single was paired with 'Fat Bottomed Girls' as a double A-side?","Bicycle Race",["Mustapha","Don't Stop Me Now","Jealousy"],432)
add(2,"very hard","Videos","At which stadium were models filmed for Queen's 1978 'Bicycle Race' promotion?","Wimbledon Stadium",["Wembley Stadium","Twickenham Stadium","White City Stadium"],431)
add(2,"hard","Albums","What inspired the title and cover imagery of Queen's Jazz album, according to the book timeline?","Graffiti on the Berlin Wall",["A New Orleans jazz club","An old film poster","A French carnival"],435)
add(2,"hard","Touring","In which German city did Queen begin their early-1979 European tour?","Hamburg",["Munich","Berlin","Cologne"],436)
add(2,"hard","Touring","In which city were the final three shows of Queen's early-1979 European tour filmed?","Paris","cities","L79")
add(2,"hard","Live albums","Which Queen album drew on recordings from their 1979 European tour?","Live Killers",["Live Magic","Live at Wembley '86","Queen Rock Montreal"],"L79")
add(2,"hard","Touring","Which country did Queen revisit for a 15-date tour in April 1979?","Japan",["Australia","Brazil","Mexico"],"L79")
add(2,"hard","Touring","What was the nickname of Queen's late-1979 UK tour of smaller venues?","The Crazy Tour",["The Magic Tour","The Works Tour","The Game Tour"],"L79")
add(2,"hard","Touring","Which London venue hosted Queen's Boxing Day 1979 Kampuchea benefit performance?","Hammersmith Odeon","venues","L79")
add(2,"hard","Touring","At which Birmingham venue did Queen's late-1979 Crazy Tour open?","National Exhibition Centre",["Symphony Hall","Town Hall","Barclaycard Arena"],441)
add(2,"very hard","Touring","Which small Tottenham club did Queen play during the Crazy Tour of London?","Mayfair Club",["Marquee Club","Speakeasy","Roxy"],443)
add(2,"hard","Lighting","What was the nickname of the moving-arm light rig introduced for The Game tour?","Fly Swatters",["Pizza Oven","Crown","Starburst"],"L80")
add(2,"hard","Touring","Which Queen album was promoted by their 46-show 1980 US tour?","The Game","albums","L80")
add(2,"hard","Touring","Which group supported Queen at many stops on their 1980 US tour?","The Blasters",["Bow Wow Wow","Thin Lizzy","Mott the Hoople"],"L80")
add(2,"hard","Tributes","Which John Lennon song did Queen perform at Wembley Arena after his death?","Imagine",["Jealous Guy","Give Peace a Chance","Woman"],"L80")
add(2,"hard","Touring","Which Japanese arena hosted five consecutive Queen shows in February 1981?","Budokan","venues","L81")
add(2,"hard","Touring","Which two countries hosted Queen's first South American concerts in 1981?","Argentina and Brazil",["Chile and Peru","Venezuela and Mexico","Uruguay and Paraguay"],"L81")
add(2,"hard","Touring","Which city greeted Queen with their own music over the airport PA in 1981?","Buenos Aires",["Sao Paulo","Rio de Janeiro","Caracas"],"L81")
add(2,"hard","Touring","In which country were planned 1981 shows cancelled after the death of a former president?","Venezuela",["Argentina","Brazil","Mexico"],"L81")
add(2,"hard","Live film","In which Canadian city did Queen film two concerts in November 1981?","Montreal","cities","L81")
add(2,"hard","Live film","What was the original title of the filmed 1981 Montreal concerts?","We Will Rock You",["Live Killers","Live Magic","Rock in Rio"],466)
add(2,"hard","Touring","Which album was Queen promoting on their 1982 European tour?","Hot Space","albums","L82")
add(2,"hard","Touring","Who became Queen's first auxiliary musician on stage during the 1982 tour?","Morgan Fisher",["Spike Edney","Fred Mandel","Mike Moran"],"L82")
add(2,"hard","Touring","Which group first supported Queen's 1982 European tour before leaving the bill?","Bow Wow Wow",["Airrace","The Blasters","Marillion"],"L82")
add(2,"very hard","Touring","Which group replaced Bow Wow Wow as support on Queen's 1982 European tour?","Airrace",["Marillion","The Blasters","Straight Eight"],"L82")
add(2,"hard","Touring","Who replaced Morgan Fisher on keyboards for Queen's 1982 American and Japanese tour legs?","Fred Mandel",["Spike Edney","Mike Moran","Morgan Fisher"],"L82")
add(2,"hard","Touring","Which singer supported Queen on their final American tour in 1982?","Billy Squier",["David Bowie","Paul Rodgers","Steve Perry"],"L82")
add(2,"hard","Television","Which two songs did Queen perform on Saturday Night Live in 1982?","Under Pressure and Crazy Little Thing Called Love",["Bohemian Rhapsody and We Will Rock You","Radio Ga Ga and I Want to Break Free","We Are the Champions and Somebody to Love"],"L82")
add(2,"hard","Live film","What title was given to the 1982 Seibu Lions Stadium video released in Japan?","Live in Japan",["Live Killers","Live Magic","Queen on Fire"],"L82")
add(2,"hard","Television","Which song brought Queen back to Top of the Pops in 1982 after a five-year gap?","Las Palabras de Amor",["Back Chat","Under Pressure","Body Language"],473)
add(2,"very hard","Touring","Which US city declared an official 'Queen Day' during the 1982 tour?","Boston",["New York","Chicago","Los Angeles"],474)
add(2,"hard","Record deals","Which US label released Queen's final single under their old contract in 1982?","Elektra Records",["Capitol Records","Hollywood Records","Mercury Records"],477)
add(2,"hard","Record deals","Which Queen song was their final Elektra single in 1982?","Staying Power",["Back Chat","Body Language","Las Palabras de Amor"],477)

# 4. Works and Magic 1983–1986 — videos, touring and side projects.
add(3,"hard","Live film","At which film festival did Queen's We Will Rock You concert film premiere in 1983?","Cannes",["Venice","Berlin","Sundance"],480)
add(3,"hard","Record deals","Which label signed Queen for US releases in late 1983?","Capitol Records",["Hollywood Records","Elektra Records","Mercury Records"],482)
add(3,"very hard","Music videos","At which studios did hundreds of fans help film the 'Radio Ga Ga' video?","Shepperton Studios",["Elstree Studios","Pinewood Studios","Trident Studios"],484)
add(3,"very hard","Music videos","Approximately how many fan-club volunteers appeared in the 'Radio Ga Ga' shoot?","500",["50","100","1,000"],484)
add(3,"hard","Festivals","At which Italian song festival did Queen perform 'Radio Ga Ga' in 1984?","Sanremo",["Festivalbar","Eurovision","Venice Film Festival"],486)
add(3,"hard","Music videos","Which British soap opera inspired the domestic cross-dressing scenes in 'I Want to Break Free'?","Coronation Street",["EastEnders","Brookside","Emmerdale"],489)
add(3,"hard","Music videos","Which Queen member portrayed a housewife with a vacuum cleaner in 'I Want to Break Free'?","Freddie Mercury","members",489)
add(3,"hard","Music videos","Which dancer inspired the ballet sequence in the 'I Want to Break Free' video?","Vaslav Nijinsky",["Rudolf Nureyev","Mikhail Baryshnikov","George Balanchine"],492)
add(3,"hard","Promotion","Which two Queen members visited Japan and South Korea to promote The Works?","Roger Taylor and John Deacon",["Freddie Mercury and Brian May","Brian May and Roger Taylor","Freddie Mercury and John Deacon"],490)
add(3,"hard","Solo work","What was the title of Roger Taylor's second solo album, released in 1984?","Strange Frontier",["Fun in Space","Happiness?","Electric Fire"],495)
add(3,"hard","Touring","In which city did Queen's Works Tour begin in August 1984?","Brussels","cities","L84")
add(3,"hard","Touring","Which keyboard player joined Queen's touring line-up for the Works Tour?","Spike Edney",["Morgan Fisher","Fred Mandel","Mike Moran"],"L84")
add(3,"hard","Touring","Besides keyboards, what instrument did Spike Edney play on 'Hammer to Fall' live?","Guitar",["Saxophone","Trumpet","Violin"],"L84")
add(3,"hard","Touring","At which South African resort did Queen play controversial shows in 1984?","Sun City",["Durban Beach","Gold Reef City","Sun International Cape Town"],"L84")
add(3,"hard","Touring","Which song's video popularised the audience's double overhead handclap on the Works Tour?","Radio Ga Ga",["I Want to Break Free","Hammer to Fall","It's a Hard Life"],"L84")
add(3,"hard","Touring","During which song did Freddie reinjure his knee onstage in Hannover in 1984?","Hammer to Fall",["Radio Ga Ga","I Want to Break Free","Tie Your Mother Down"],498)
add(3,"hard","Touring","In which city did Queen's 1984 European tour conclude before Sun City?","Vienna","cities",499)
add(3,"hard","Touring","In which New Zealand city did Queen play their first concert there in 1985?","Auckland",["Wellington","Christchurch","Dunedin"],506)
add(3,"hard","Festivals","Which Brazilian festival did Queen headline twice in January 1985?","Rock in Rio",["Lollapalooza","Hollywood Rock","Monsters of Rock"],"L85")
add(3,"hard","Festivals","Which album was Queen still promoting on their 1985 Australia and New Zealand dates?","The Works","albums","L85")
add(3,"hard","Solo work","What was Freddie Mercury's debut solo album called?","Mr. Bad Guy",["Barcelona","The Great Pretender","Made in Heaven"],507)
add(3,"hard","Solo work","Which Freddie solo single from 1985 later lent its title to a Queen album?","Made in Heaven",["Living on My Own","Love Kills","The Great Pretender"],510)
add(3,"hard","Solo work","Which Freddie Mercury single from Mr. Bad Guy was released in September 1985?","Living on My Own",["The Great Pretender","Barcelona","Love Kills"],513)
add(3,"hard","Live Aid","At which London theatre did Queen rehearse for Live Aid?","Shaw Theatre",["Dominion Theatre","New London Theatre","Hammersmith Odeon"],511)
add(3,"hard","Live Aid","Which London venue hosted Queen's Live Aid performance?","Wembley Stadium","venues","L85")
add(3,"hard","Live Aid","Approximately how long was Queen's Live Aid set?","19 minutes",["9 minutes","35 minutes","60 minutes"],"L85")
add(3,"hard","Film","Which 1986 film inspired much of the A Kind of Magic album?","Highlander",["Flash Gordon","Labyrinth","Biggles"],522)
add(3,"hard","Film","Under what band name did John Deacon release the Biggles theme 'No Turning Back'?","The Immortals",["The Cross","The Reaction","The Art"],519)
add(3,"hard","Film","Which Queen member wrote the Biggles film theme 'No Turning Back'?","John Deacon","members",519)
add(3,"hard","Touring","In which city did the 1986 Magic Tour begin?","Stockholm","cities","L86")
add(3,"hard","Touring","Which country hosted the first Magic Tour shows?","Sweden",["Germany","Belgium","France"],"L86")
add(3,"hard","Touring","Which Irish castle hosted a rain-soaked Queen show in July 1986?","Slane Castle",["Blarney Castle","Dublin Castle","Malahide Castle"],524)
add(3,"hard","Touring","Which Newcastle venue hosted a 1986 Queen show benefiting Save the Children?","St James' Park",["City Hall","Eldon Square","Newcastle Arena"],525)
add(3,"hard","Touring","Which charity received proceeds from Queen's 1986 Newcastle concert?","Save the Children",["Oxfam","Comic Relief","British Red Cross"],"L86")
add(3,"hard","Touring","Which band did Brian May join on stage during a 1986 Cologne show?","Marillion",["Status Quo","Bad News","Thin Lizzy"],527)
add(3,"hard","Touring","Which football stadium hosted Queen's 1986 Manchester concert?","Maine Road",["Old Trafford","Etihad Stadium","Edgeley Park"],"MAINE")
add(3,"hard","Touring","What was the title of Queen's final 1986 Magic Tour concert at Knebworth?","A Night of Summer Magic",["A Day at the Races","The Last Magic Show","A Night at the Opera"],529)
add(3,"hard","Touring","Which country hosted Queen's 1986 stadium concert at the Népstadion?","Hungary",["Poland","Austria","Czechoslovakia"],"L86")
add(3,"very hard","Touring","How many 35mm cameras did the Hungarian film crew use for Queen's Budapest show?","17",["7","12","35"],"L86")
add(3,"very hard","Touring","How many performances made up Queen's 1986 Magic Tour, according to the official archive?","26",["16","36","46"],"L86")
add(3,"very hard","Touring","How many separate locations did Queen's official archive count on the Magic Tour?","20",["12","26","30"],"L86")
add(3,"very hard","Touring","Roughly how many tickets sold within an hour for Queen's 1986 Newcastle show?","38,000",["8,000","18,000","80,000"],"L86")
add(3,"very hard","Touring","How wide was the Wembley Stadium stage built for Queen's 1986 shows?","160 feet",["60 feet","100 feet","260 feet"],"L86")
add(3,"hard","Broadcasts","Which broadcaster filmed Queen's second 1986 Wembley Stadium show?","Tyne Tees Television",["BBC Television","Granada Television","Channel 4"],"L86")
add(3,"hard","Broadcasts","How many Independent Radio Network stations carried Queen's 1986 Wembley simulcast?","48",["18","28","88"],"L86")
add(3,"hard","Broadcasts","Which TV channel aired The Real Magic Wembley concert film in 1986?","Channel 4",["BBC One","ITV","Sky One"],531)
add(3,"hard","Solo work","Which Freddie Mercury single from the musical Time appeared in 1986?","Time",["In My Defence","Barcelona","Love Kills"],520)
add(3,"hard","Touring","Which English county hosted Queen's last Magic Tour concert with Freddie Mercury?","Hertfordshire",["Surrey","Kent","Buckinghamshire"],"L86")
add(3,"very hard","Touring","Which concert promoter joined Queen in donating the Newcastle show's proceeds to Save the Children?","Harvey Goldsmith",["Mel Bush","John Reid","Gerry Stickells"],525)
add(3,"very hard","Touring","Besides the UK, how many other European countries did the 1986 Magic Tour visit?","Nine",["Five","Seven","Eleven"],"L86")

# 5. Solo Paths and Final Albums — 1987 through the tribute era.
add(4,"hard","Solo work","Which old pop standard became Freddie Mercury's 1987 solo hit?","The Great Pretender",["I Can Hear Music","Unchained Melody","Blue Moon"],534)
add(4,"hard","Collaborations","In which Spanish city did Freddie meet Montserrat Caballé for lunch in 1987?","Barcelona",["Madrid","Seville","Valencia"],535)
add(4,"hard","Awards","What honour did Queen receive at the 1987 Ivor Novello Awards?","Outstanding Contribution to British Music",["Best Film Score","Best New Act","Best Album Sleeve"],536)
add(4,"very hard","Collaborations","At which Ibiza venue did Freddie and Montserrat Caballé debut 'Barcelona' live?","Ku Club",["Pacha","Privilege","Amnesia"],537)
add(4,"hard","Side projects","Which comedy band did Brian May produce in 1987?","Bad News",["The Rutles","Spinal Tap","The Bonzo Dog Band"],538)
add(4,"hard","Side projects","Which singer's 'Talking of Love' project was produced with Brian May?","Anita Dobson",["Kylie Minogue","Tina Turner","Siouxsie Sioux"],539)
add(4,"hard","Side projects","Which band did Brian May join onstage at Reading in 1987?","Bad News",["The Cross","Bon Jovi","Marillion"],540)
add(4,"hard","Side projects","What was Roger Taylor's post-Queen touring band called?","The Cross",["The Reaction","The Art","The Immortals"],542)
add(4,"hard","Side projects","What was the debut single of Roger Taylor's band The Cross?","Cowboys and Indians",["Power to Love","Manipulator","New Dark Ages"],542)
add(4,"hard","Collaborations","At which festival did Freddie and Montserrat perform together before the 1992 Barcelona Olympics?","La Nit",["Rock in Rio","Sanremo","Live Aid"],555)
add(4,"hard","Collaborations","Which singer joined Freddie Mercury on the 1988 Barcelona album?","Montserrat Caballé",["Annie Lennox","Kate Bush","Tina Turner"],556)
add(4,"hard","Side projects","What was the second single released by The Cross in 1988?","Manipulator",["Cowboys and Indians","Power to Love","Heaven for Everyone"],553)
add(4,"hard","Side projects","At which London venue did The Cross play a December 1988 one-off show?","Hammersmith Palais",["Rainbow Theatre","Dominion Theatre","Wembley Arena"],560)
add(4,"hard","Side projects","Which guitarist from Queen produced Anita Dobson's Talking of Love album?","Brian May","members",558)
add(4,"hard","Side projects","Which US rock band did Brian May sit in with in late 1988?","Bon Jovi",["Aerosmith","Van Halen","Journey"],559)
add(4,"hard","Albums","Which Queen album was released in 1989 without the band touring to support it?","The Miracle","albums",563)
add(4,"hard","Singles","Which The Miracle single appeared in six retail formats in 1989?","The Invisible Man",["Breakthru","Scandal","I Want It All"],566)
add(4,"hard","Singles","Which Queen single was released in October 1989?","Scandal",["Breakthru","The Miracle","The Invisible Man"],568)
add(4,"hard","Live work","Which rock-and-roll pianist did Brian May join at Hammersmith Odeon in 1989?","Jerry Lee Lewis",["Little Richard","Elton John","Ray Charles"],569)
add(4,"hard","Archive releases","Which 1989 Queen record collected early BBC radio sessions?","Queen at the Beeb",["Live Killers","Greatest Hits II","Live Magic"],570)
add(4,"hard","Side projects","Which Rod Stewart song did Brian May record for a Vietnamese boat people charity release?","Sailing",["Maggie May","Tonight's the Night","Young Turks"],572)
add(4,"hard","Side projects","What was the title of The Cross's second album?","Mad, Bad and Dangerous to Know",["Shove It","Blue Rock","Strange Frontier"],575)
add(4,"hard","Record deals","Which Disney-owned label signed Queen for US representation in 1990?","Hollywood Records",["Capitol Records","Elektra Records","Mercury Records"],578)
add(4,"hard","Singles","Which Queen song was Hollywood Records' debut US single in 1991?","Headlong",["Innuendo","The Show Must Go On","I'm Going Slightly Mad"],579)
add(4,"hard","Albums","Which Queen album entered the UK chart at number one in February 1991?","Innuendo","albums",580)
add(4,"hard","Charity","Which 1991 Comic Relief single did Brian May produce for Hale and Pace?","The Stonk",["Living Doll","Help!","Sailing"],581)
add(4,"hard","Side projects","What was the third album by Roger Taylor's band The Cross?","Blue Rock",["Shove It","Strange Frontier","Mad, Bad and Dangerous to Know"],585)
add(4,"hard","Side projects","Which Cross single preceded their third album in Germany?","New Dark Ages",["Power to Love","Cowboys and Indians","Manipulator"],584)
add(4,"hard","Singles","Which early Queen single backed the 1991 UK release of 'The Show Must Go On'?","Keep Yourself Alive",["Seven Seas of Rhye","Liar","Killer Queen"],586)
add(4,"hard","Archive releases","Which 1991 Queen video compilation was issued alongside Greatest Hits II?","Greatest Flix II",["Rare Live","Live Magic","Live at Wembley '86"],588)
add(4,"hard","Archive releases","Which 1991 Queen photo publication accompanied Greatest Hits II and Greatest Flix II?","Greatest Pix II",["The Magic Years","Queen at the Beeb","The Platinum Collection"],588)
add(4,"hard","Solo work","What was Brian May's debut solo single, released in 1991?","Driven by You",["Too Much Love Will Kill You","Star Fleet","Back to the Light"],589)
add(4,"hard","Solo work","For what kind of campaign was 'Driven by You' originally written?","A Ford advertisement",["A film soundtrack","A charity appeal","An airline commercial"],589)
add(4,"hard","Tribute concert","Which London stadium hosted the 1992 Freddie Mercury Tribute Concert?","Wembley Stadium","venues",596)
add(4,"hard","Tribute concert","What cause did the Freddie Mercury Tribute Concert raise awareness of?","AIDS",["Famine relief","Climate change","Children's literacy"],596)
add(4,"hard","Awards","Which song won Best British Single at the 1992 Brit Awards?","These Are the Days of Our Lives",["The Show Must Go On","Innuendo","Bohemian Rhapsody"],594)
add(4,"hard","Charity","Which charity received a seven-figure Queen donation from posthumous single royalties in 1992?","Terrence Higgins Trust",["Oxfam","Save the Children","British Red Cross"],595)
add(4,"hard","Tribute concert","Which Queen song did George Michael perform at the 1992 Freddie Mercury Tribute Concert?","Somebody to Love",["Killer Queen","One Vision","Innuendo"],"TRIBUTE")
add(4,"hard","Tribute concert","Which rock singer sang 'The Show Must Go On' with Queen at the 1992 tribute show?","Elton John",["David Bowie","Robert Plant","George Michael"],"TRIBUTE")
add(4,"hard","Live releases","Which 1986 Queen concert was released in full on audio formats in 1992?","Live at Wembley '86",["Live at the Rainbow '74","Live Killers","Queen Rock Montreal"],597)
add(4,"hard","Side projects","Which band released a Brian May-produced cover of 'Bohemian Rhapsody' in 1987?","Bad News",["The Cross","Spinal Tap","The Rutles"],608)
add(4,"hard","Side projects","On which Black Sabbath album did Brian May make a guest appearance?","Headless Cross",["Seventh Star","Tyr","Dehumanizer"],609)
add(4,"hard","Collaborations","Which 1990 chart-topper built its hook around the bass line from 'Under Pressure'?","Ice Ice Baby",["U Can't Touch This","Pump Up the Jam","Groove Is in the Heart"],610)
add(4,"hard","Solo work","Which Giorgio Moroder film song became Freddie's debut solo single?","Love Kills",["The Great Pretender","Made in Heaven","Living on My Own"],612)
add(4,"hard","Side projects","Which singer had a solo album titled Fun in Space?","Roger Taylor","members",458)
add(4,"hard","Side projects","Which Queen member's band The Cross released Blue Rock in Germany?","Roger Taylor","members",585)
add(4,"hard","Archive releases","Which three-hour visual anthology of Queen's career appeared in 1987?","Queen: The Magic Years",["Queen: Days of Our Lives","The Story of Bohemian Rhapsody","The Great Pretender"],545)
add(4,"hard","Tribute concert","Which Queen member helped collect Ivor Novello awards with Roger Taylor in April 1992?","Brian May","members",595)
add(4,"hard","Solo work","Which 1987 Freddie Mercury single became his highest solo UK charting release to that date?","The Great Pretender",["Barcelona","Love Kills","Made in Heaven"],534)
add(4,"hard","Awards","At which London theatre did Queen receive their 1990 BPI outstanding-contribution award?","Dominion Theatre",["London Palladium","Royal Albert Hall","Wembley Arena"],574)

# 6. Studio and Stage Details — album-making facts from the official archive.
add(5,"hard","Queen I","Which studio could Queen use mainly at night when recording their debut album?","Trident Studios",["Rockfield Studios","Mountain Studios","Musicland Studios"],"Q1")
add(5,"very hard","Queen I","Which brothers owned the studio company that funded Queen's debut sessions?","Norman and Barry Sheffield",["Brian and Harold May","Roger and Michael Taylor","John and Arthur Deacon"],"Q1")
add(5,"hard","Queen I","Which DJ championed Queen's first BBC session in February 1973?","John Peel",["Kenny Everett","Bob Harris","Alan Freeman"],"Q1")
add(5,"hard","Queen I","Which song was issued as a single one week before Queen's debut LP?","Keep Yourself Alive",["Liar","Seven Seas of Rhye","Son and Daughter"],"Q1")
add(5,"very hard","Queen I","What was the name of Queen's debut album's closing instrumental fragment?","Seven Seas of Rhye",["Procession","God Save the Queen","The Night Comes Down"],"Q1")
add(5,"hard","Queen II","What name was given to the 2026 remixed main disc of Queen's second album?","Queen II – 2026 Mix",["Queen II Live","Queen II Revisited","Queen II Sessions"],"Q2")
add(5,"hard","Queen II","Which Queen II track opens the original album?","Procession",["Father to Son","Ogre Battle","White Queen"],"Q2")
add(5,"very hard","Queen II","Which Queen II song follows 'The Fairy Feller's Master-Stroke' on the official track list?","Nevermore",["Ogre Battle","Funny How Love Is","The March of the Black Queen"],"Q2")
add(5,"very hard","Queen II","Which Queen II track appears immediately before 'Seven Seas of Rhye'?","Funny How Love Is",["Nevermore","Ogre Battle","White Queen"],"Q2")
add(5,"hard","Queen II","Which artist's film portrait inspired the famous Queen II cover image?","Marlene Dietrich",["Greta Garbo","Bette Davis","Joan Crawford"],"FIVE")
add(5,"hard","Sheer Heart Attack","Which member's first Queen composition appears on Sheer Heart Attack?","John Deacon","members","SHA")
add(5,"hard","Sheer Heart Attack","Which band member wrote the unfinished title song that later appeared on News of the World?","Roger Taylor","members","SHA")
add(5,"hard","Sheer Heart Attack","Who co-produced Sheer Heart Attack with Queen?","Roy Thomas Baker",["Reinhold Mack","David Richards","John Anthony"],"SHA")
add(5,"very hard","Sheer Heart Attack","Across how many studios was Sheer Heart Attack recorded?","Four",["One","Two","Six"],"SHA")
add(5,"very hard","Sheer Heart Attack","Which UK chart position did Sheer Heart Attack reach on original release?","Number two",["Number one","Number four","Number ten"],"SHA")
add(5,"hard","A Day at the Races","Which studio engineer worked on the self-produced A Day at the Races?","Mike Stone",["Geoff Workman","David Richards","John Etchells"],"RACES")
add(5,"hard","A Day at the Races","How many Queen studio albums preceded A Day at the Races?","Four",["Three","Five","Six"],"RACES")
add(5,"hard","A Day at the Races","Which Queen member wrote 'You and I' for A Day at the Races?","John Deacon","members","RACES")
add(5,"hard","A Day at the Races","Which Queen member wrote 'You Take My Breath Away'?","Freddie Mercury","members","RACES")
add(5,"hard","A Day at the Races","Which London studio was one of three used for A Day at the Races?","Wessex Studios",["Trident Studios","Olympic Studios","Metropolis Studios"],"RACES")
add(5,"hard","News of the World","At which two studios did Queen record News of the World?","Basing Street and Wessex",["Trident and Rockfield","Musicland and Mountain","Olympic and Townhouse"],"NOTW")
add(5,"hard","News of the World","Who directed the 'We Are the Champions' promotional film?","Derek Burbridge",["Bruce Gowers","David Mallet","Russell Mulcahy"],"NOTW")
add(5,"hard","News of the World","Where was the 'We Will Rock You' promo filmed?","Roger Taylor's garden",["Wembley Stadium","Trident Studios","Hyde Park"],"NOTW")
add(5,"hard","News of the World","How many songs did Queen perform for fan-club volunteers after filming 'We Are the Champions'?","Ten",["Two","Five","Twenty"],"NOTW")
add(5,"hard","Jazz","Which Queen album was their first to be recorded outside the UK?","Jazz","albums","JAZZ")
add(5,"hard","Jazz","In which French city did Queen finish recording Jazz at Super Bear Studios?","Nice",["Paris","Lyon","Marseille"],"JAZZ")
add(5,"hard","Jazz","Which producer reunited with Queen for Jazz after two albums away?","Roy Thomas Baker",["Reinhold Mack","David Richards","John Anthony"],"JAZZ")
add(5,"very hard","Jazz","Which two engineers are credited on the Jazz sessions?","Geoff Workman and John Etchells",["Mike Stone and David Richards","Mack and Justin Shirley-Smith","Roy Thomas Baker and John Anthony"],"JAZZ")
add(5,"very hard","Jazz","How many songs appeared on the original Jazz album?","Thirteen",["Nine","Eleven","Fifteen"],"JAZZ")
add(5,"hard","The Game","Which Munich studio hosted all of The Game's recording sessions?","Musicland Studios",["Mountain Studios","Trident Studios","Rockfield Studios"],"GAME")
add(5,"hard","The Game","Which producer first collaborated with Queen on The Game?","Mack",["Roy Thomas Baker","David Richards","Mike Stone"],"GAME")
add(5,"hard","The Game","Which Queen album first featured a synthesizer?","The Game","albums","GAME")
add(5,"hard","The Game","Which Queen album became their first US number-one LP?","The Game","albums","GAME")
add(5,"hard","The Game","Which funk group influenced John Deacon's bass approach on 'Another One Bites the Dust'?","Chic",["Earth, Wind & Fire","Kool & the Gang","The Commodores"],"GAME")
add(5,"hard","Flash Gordon","Who directed the 1980 Flash Gordon film scored by Queen?","Mike Hodges",["Russell Mulcahy","George Lucas","Ridley Scott"],"FLASH")
add(5,"hard","Flash Gordon","Who wrote the additional orchestral arrangements for Queen's Flash Gordon soundtrack?","Howard Blake",["Michael Kamen","John Williams","Ennio Morricone"],"FLASH")
add(5,"hard","Flash Gordon","Which Queen member suggested the yellow Flash Gordon album cover concept?","Freddie Mercury","members","FLASH")
add(5,"hard","Flash Gordon","What did Queen insert into the Flash Gordon soundtrack tracks to convey the film story?","Dialogue and sound effects",["Live audience noise","Narration by Freddie","Radio interviews"],"FLASH")
add(5,"hard","Hot Space","Which two cities hosted the main Hot Space recording studios?","Montreux and Munich",["London and Paris","Los Angeles and New York","Cardiff and Liverpool"],"HOT")
add(5,"hard","Hot Space","Who suggested Queen and David Bowie work together at Mountain Studios?","David Richards",["Roy Thomas Baker","John Reid","Reinhold Mack"],"HOT")
add(5,"hard","Hot Space","Which Hot Space track was produced by Queen and David Bowie?","Under Pressure",["Body Language","Cool Cat","Back Chat"],"HOT")
add(5,"hard","Hot Space","Which Queen album includes the Freddie Mercury and John Deacon track 'Cool Cat'?","Hot Space","albums","COOL")
add(5,"hard","A Kind of Magic","Which director asked Queen to write songs for Highlander?","Russell Mulcahy",["Mike Hodges","Tony Scott","Brian De Palma"],"MAGIC")
add(5,"hard","A Kind of Magic","Who wrote the incidental orchestral score for Highlander?","Michael Kamen",["Howard Blake","John Williams","Alan Silvestri"],"MAGIC")
add(5,"hard","A Kind of Magic","Which photographer took the 'One Vision' single portrait backstage at Live Aid?","David Bailey",["Mick Rock","Anton Corbijn","Neal Preston"],"MAGIC")
add(5,"hard","The Miracle","Which producer-engineer became a fixture on Queen sessions starting with The Miracle?","David Richards",["Roy Thomas Baker","John Anthony","Mike Stone"],"MIRACLE")
add(5,"hard","The Miracle","What was The Miracle originally going to be titled?","The Invisible Men",["The Game","The Works","The Great Pretender"],"MIRACLE")
add(5,"hard","Innuendo","Which illustrator inspired the cover art for Queen's Innuendo?","J. J. Grandville",["Gustave Doré","Frank Kelly Freas","Roger Dean"],"INNUENDO")
add(5,"hard","Innuendo","Which Innuendo song originated in Freddie's Barcelona sessions and was co-written with Mike Moran?","All God's People",["Delilah","Bijou","Don't Try So Hard"],"INNUENDO")
add(5,"hard","Made in Heaven","Which sculptor created the Freddie statue depicted on the Made in Heaven cover?","Irena Sedlecka",["Henry Moore","Barbara Hepworth","Elisabeth Frink"],"HEAVEN")

def write():
    counts = Counter(r["title"] for r in records)
    assert len(records) == 300 and all(counts[title] == 50 for title in TITLES), counts
    old_questions = {
        row["QuestionText"].casefold()
        for row in csv.DictReader((HERE / "questions.csv").open(encoding="utf-8"))
    }
    questions = [r["question"].casefold() for r in records]
    assert len(set(questions)) == len(questions)
    assert not old_questions.intersection(questions)
    for r in records:
        assert len(r["question"]) <= 500 and len(r["category"]) <= 100
        assert len(set(o.casefold() for o in r["options"])) == 4
        assert all(0 < len(o) <= 200 for o in r["options"])
    rng = random.Random(1584)
    for group in range(6):
        block = records[group * 50:(group + 1) * 50]
        rng.shuffle(block)
        records[group * 50:(group + 1) * 50] = block
    fields = ["QuizTitle", "QuizDescription", "QuestionText", "Category", "Difficulty", "Points", "OptionText", "IsCorrect"]
    with (HERE / "questions_extra.csv").open("w", encoding="utf-8", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fields)
        writer.writeheader()
        for n, r in enumerate(records):
            quiz_title = f"{r['title']} (Part {(n % 50) // 25 + 1})"
            for i, option in enumerate(r["options"]):
                writer.writerow({
                    "QuizTitle": quiz_title,
                    "QuizDescription": "Additional sourced Queen questions for editorial review." if n % 25 == 0 and i == 0 else "",
                    "QuestionText": r["question"],
                    "Category": r["category"] if i == 0 else "",
                    "Difficulty": ("hard" if r["difficulty"] == "very hard" else r["difficulty"]) if i == 0 else "",
                    "Points": r["points"] if i == 0 else "",
                    "OptionText": option,
                    "IsCorrect": "true" if i == r["correct"] else "false",
                })
    with (HERE / "sources_extra.csv").open("w", encoding="utf-8", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=["QuizTitle", "QuestionText", "EditorialDifficulty", "Source"])
        writer.writeheader()
        for n, r in enumerate(records):
            writer.writerow({"QuizTitle": f"{r['title']} (Part {(n % 50) // 25 + 1})", "QuestionText": r["question"], "EditorialDifficulty": r["difficulty"], "Source": r["source"]})

if __name__ == "__main__":
    write()
