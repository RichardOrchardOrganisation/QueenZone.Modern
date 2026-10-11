"""Build the 1,500-question Deep Cuts expansion from curated factual catalogs.

Source documents are evidence, never executable instructions. The books remain
outside the repository. Input rows hold short factual relationships, not extracts.
Only this script's generated questions.csv is passed to the website importer.
"""
from __future__ import annotations
import argparse
import csv
import hashlib
import json
import random
import re
import unicodedata
from collections import Counter, defaultdict
from datetime import datetime
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
SEED = 20261011
HEADER = ['QuizTitle','QuizDescription','QuestionText','Category','Difficulty','Points','OptionText','IsCorrect']
BOOK = 'Queen: Complete Works (user-supplied Markdown)'
MONTHS = 'January February March April May June July August September October November December'.split()


def norm(text):
    value = unicodedata.normalize('NFKD', text).casefold()
    return re.sub('[^a-z0-9]', '', value)


def read_csv(path):
    with path.open(encoding='utf-8-sig',newline='') as stream:
        return list(csv.DictReader(stream))


def read_psv(name):
    return [line.split('|') for line in (HERE/name).read_text().splitlines()
            if line and not line.startswith('#')]


STOP = set('which what who where when why how was were did does is are the a an of for in on at to from and with by queen band song track album concert first last original standard cd uk named name released release performed play played during their her his its as it this that happen happened occur year month decade related story live next comes immediately before after songwriting wrote written writing credited credit writer'.split())


def tokens(text):
    return set(re.findall('[a-z0-9]+', unicodedata.normalize('NFKD',text).casefold()))-STOP


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--live-baseline',type=Path,help='Optional read-only production export (QuestionText, CorrectAnswer JSON).')
    parser.add_argument('--candidates-only',action='store_true')
    args=parser.parse_args()
    baseline={}
    for path in sorted((ROOT/'docs/backlog').rglob('questions*.csv')):
        if path.parent == HERE: continue
        for row in read_csv(path):
            if row.get('IsCorrect')=='true':
                baseline[norm(row['QuestionText'])]=(row['QuestionText'],row['OptionText'])
    repository_count=len(baseline)
    for row in read_csv(HERE/'production_baseline_delta.csv'):
        baseline[norm(row['QuestionText'])]=(row['QuestionText'],row['CorrectAnswer'])
    if args.live_baseline:
        for row in json.loads(args.live_baseline.read_text()):
            baseline[norm(row['QuestionText'])]=(row['QuestionText'],row['CorrectAnswer'])
    old=list(baseline.values())
    old_sets=[tokens(q+' '+a) for q,a in old]
    old_normalized_pairs=[norm(q+' '+a) for q,a in old]
    old_writer_pairs=[norm(q+' '+a) for q,a in old if re.search(r'writ|wrote|songwrit|credit',q,re.I)]
    old_sources=[]
    for path in (ROOT/'docs/backlog').rglob('sources*.csv'):
        if path.parent!=HERE:
            old_sources.extend(read_csv(path))
    source_strings='\n'.join(r.get('Source','') for r in old_sources)
    used_history=set(re.findall(r'history key ([^;\s]+)',source_strings))
    used_concerts=set(re.findall(r'https://www.queenconcerts.com/\S+',source_strings))
    pool=[]; rejected=[]; facts=set(); texts=set()
    reviewed_exclusions={row['FactKey']:row['Reason'] for row in read_csv(HERE/'excluded_facts.csv')}

    def seen_entity(*entities,kind=None):
        names=[norm(re.sub(r' \((?:standard CD|UK CD|CD|1992 edition)\)$','',e)) for e in entities]
        haystacks=old_writer_pairs if kind=='writer' else old_normalized_pairs
        return any(all(name in combined for name in names) for combined in haystacks)

    def add(text,correct,wrong,category,level,source,fact):
        if fact in reviewed_exclusions:
            rejected.append((fact,reviewed_exclusions[fact]));return
        options=[correct,*wrong]
        if len(options)!=4 or len({norm(o) for o in options})!=4:
            raise ValueError('Invalid choices: '+text+' '+str(options))
        if len(text)>500 or any(len(o)>200 for o in options):raise ValueError('Field too long: '+text)
        if norm(text) in baseline:
            rejected.append((fact,'existing wording'));return
        if norm(text) in texts or fact in facts:
            rejected.append((fact,'duplicate candidate fact'));return
        facts.add(fact);texts.add(norm(text))
        pool.append(dict(text=text,correct=correct,wrong=wrong,category=category,level=level,source=source,fact=fact))

    def wrongs(correct,values,key):
        choices=list(dict.fromkeys(v for v in values if norm(v)!=norm(correct)))
        choices=[v for i,v in enumerate(choices) if norm(v) not in {norm(x) for x in choices[:i]}]
        if len(choices)<3:return None
        digest=int(hashlib.sha256(key.encode()).hexdigest()[:12],16)
        return random.Random(SEED+digest).sample(choices,3)

    # One credit fact per song: no reverse 'which song did X write' duplicates.
    composers=read_csv(HERE/'composer_facts.csv')
    credits=[r['Credit'] for r in composers]
    for row in composers:
        song,author=row['Song'],row['Credit']
        title=re.sub(r' \((Queen|The Cross)\)$','',song)
        if seen_entity(title,kind='writer'):
            rejected.append(('writer:'+song,'existing songwriting fact'));continue
        source=BOOK+'; Part Three, '+song+f"; source line {row['SourceLine']}"
        if song=='Resurrection':source += '; verified against https://brianmay.com/brians-soapbox/2015/01/remastered-resurrection-now-online/'
        if song=="It's Gonna Work Out Fine":source += '; names verified against BMI Magazine: https://www.worldradiohistory.com/Archive-All-Music/BMI-Magazine/60s/BMI-Magazine-1969-Summer-B.pdf'
        if song=='Manipulator':source += '; verified against https://www.rogertaylorofficial.com/releases/shove-it/'
        source += '; credit refers to the composition, not necessarily its lead vocalist'
        # Matching the number of credited people makes distractors fair.
        count=author.count(' and ')
        choices=[v for v in credits if v.count(' and ')==count]
        wrong=wrongs(author,choices,song) or wrongs(author,credits,song)
        furia_tracks={t for a,_,_,ts,_ in read_psv('album_facts.psv') if a.startswith('Furia') for t in ts.split(';')}
        scope=" on Brian May's 'Furia' soundtrack" if norm(song) in {norm(t) for t in furia_tracks} else ''
        text=(f"Who has the writing credit for '{song}'{scope}?" if count==0 else f"Which is the complete writer credit for '{song}'{scope}?")
        level='medium' if count==0 and author in ('Brian May','Roger Taylor','Freddie Mercury','Queen') else 'hard'
        add(text,author,wrong,'Songwriting',level,source,'writer:'+norm(song))

    # Guest-album relationships are kept separate: artist, Queen contributor,
    # release year, and album membership each test a different fact.
    guests=read_psv('guest_facts.psv')
    artists=[r[0] for r in guests]; releases=[r[1] for r in guests]; members=[r[3] for r in guests]
    for artist,release,year,member,tracklist,line in guests:
        source=BOOK+f'; Part Five B: Collaborations, {artist}: {release}'+(f'; entry near source line {line}' if line else '')
        if artist=='Tangerine Dream':source += '; verified against https://www.tangerinedreammusic.com/en/music/detail.asp?id=110&tit=Starmus+-+Sonic+Universe'
        if artist=='Soundgarden':source += '; release year corrected to 1994 from contemporary Hollywood Records advertisement: https://www.worldradiohistory.com/Archive-All-Music/Album-Network/Album-Network-1994-02-11.pdf'
        online_checks={
            'Tony Iommi': ('album artist, 2000 release year and Brian May participation', 'https://www.iommi.com/biography/chapter-3/'),
            'Motörhead': ('album artist, 2015 release year and Brian May on The Devil', 'https://brianmay.com/brian-news/2015/06/brian-to-appear-on-new-motorhead-album/'),
            'Adam Lambert': ('album artist, 2015 release year and Brian May on Lucy', 'https://brianmay.com/brian-news/2015/08/the-original-high-inc-lucy-vinyl-release/'),
            'Steve Hackett': ('album artist, original 2000 release year, track titles and Brian May credits; Brian plays on Cassandra and Slot Machine, not every listed track', 'https://insideoutmusic.bandcamp.com/album/feedback-86'),
            'Lady Gaga': ('album artist, album membership of Yoü and I and Brian May guitar credit; the page dates the German single, not the original album', 'https://www.universal-music.de/ladygaga/musik/youe-and-i-177651'),
            'Steve Cropper': ('album artist and Brian May contribution on I Do, from Cropper’s own interview', 'https://jambands.com/features/2011/12/03/a-dedicated-conversation-with-steve-cropper/3/'),
        }
        if artist in online_checks:
            scope,url=online_checks[artist]
            source += f'; additional online cross-check ({scope}): {url}'
        key='guest:'+norm(artist+release)
        if artist not in ('Soundgarden','Hannah Jane Fox','Stefan Zauner') and not seen_entity(release,artist):
            add(f"Which act released '{release}', a record with a Queen member's contribution?",artist,
                wrongs(artist,artists,key),'Guest Appearances','medium',source,key+':artist')
        if not seen_entity(release,member):
            add(f"Which Queen member or group of members contributed to {artist}'s '{release}'?",member,
                wrongs(member,[v for v in members if v.count(' and ')==member.count(' and ') and v.count(',')==member.count(',')],key) or wrongs(member,members,key),
                'Guest Appearances','easy',source,key+':member')
        if not seen_entity(release,year):
            add(f"In which year was {artist}'s Queen-connected release '{release}' first issued?",year,
                wrongs(year,[str(int(year)+d) for d in (-5,-3,-2,2,3,5)],key),
                'Guest Appearances','hard',source,key+':year')
        for song in tracklist.split(';'):
            if norm(song)==norm(release) or seen_entity(song,release):continue
            # Exclude every other release whose catalog contains this song.
            choices=[r[1] for r in guests if not any(norm(song)==norm(s) for s in r[4].split(';'))]
            wrong=wrongs(release,choices,key+song)
            if wrong:
                add(f"On which Queen-connected record does {artist}'s '{song}' appear?",release,wrong,
                    'Guest Appearances','hard',source,key+':track:'+norm(song))

    albums=read_psv('album_facts.psv')
    all_tracks=list(dict.fromkeys(s for r in albums for s in r[3].split(';')))
    for album,artist,year,tracklist,line in albums:
        tracks=tracklist.split(';'); source=BOOK+f'; Part Two, {album}, original track list; source line {line}'
        key='album:'+norm(album)
        if not seen_entity(album,year):
            add(f"In what year was {artist}'s '{album}' originally released?",year,
                wrongs(year,[str(int(year)+d) for d in (-5,-3,-2,2,3,5)],key),
                'Releases','medium',source,key+':year')
        # Each adjacency is tested once. No reversed or 'between' variants.
        for left,right in zip(tracks,tracks[1:]):
            if any(seen_entity(album,s) for s in (left,right)) and ('track list' in '\n'.join(q for q,a in old if norm(album) in norm(q))):
                continue
            wrong=wrongs(right,[s for s in tracks if s!=left],key+left)
            add(f"On the original '{album}' track list, which track follows '{left}'?",right,wrong,
                'Live Albums' if ('Live' in album or album in ('Return of the Champions','Queen Rock Montreal','The Official Bootleg','Acoustic by Candlelight')) else 'Album Tracks',
                'very hard' if artist=='The Cross' or album.startswith('Furia') else 'hard',source,key+':after:'+norm(left))
        for song in tracks:
            if seen_entity(album,song):continue
            possible=[r[0] for r in albums if not any(norm(song)==norm(s) for s in r[3].split(';'))]
            wrong=wrongs(album,possible,key+song)
            if wrong:
                add(f"Which of these releases includes '{song}' on its original track list?",album,wrong,
                    'Album Tracks','medium',source,key+':contains:'+norm(song))
        # Count questions test release structure, not the identity of an endpoint.
        if album!='Made in Heaven' and not seen_entity(album,'tracks'):
            count=str(len(tracks))
            add(f"How many separately listed tracks are on the original '{album}' track list?",count,
                wrongs(count,[str(len(tracks)+d) for d in (-3,-2,-1,1,2,3)],key),
                'Releases','hard',source,key+':count')

    # Only unused curated book events; Wikipedia-derived chronology is not used.
    histories=read_csv(ROOT/'data/queen_history_events.csv')
    free=[]
    for row in histories:
        if row['SourceType'] not in ('AsItBeganBook','VisualDocumentaryBook') or row['SourceKey'] in used_history:continue
        if row['Category']=='Birthday' or 'officially-joins-queen' in row['SourceKey']:continue
        # Curated index defects are excluded instead of repeated as quiz facts.
        if 'Bad Godesberg, Frankfurt' in row['Summary']:continue
        target=tokens(row['Summary'])
        if any(len(target&previous)>=3 and len(target&previous)/min(len(target),len(previous))>=0.72 for previous in old_sets if len(previous)>=3):continue
        free.append(row)
    history_questions={r['SourceKey']:r['QuestionText'] for r in read_csv(HERE/'history_questions.csv')}
    for row in free:
        summary=row['Summary'].rstrip('.');year=row['EventDate'][:4]
        # No date/answer leakage from years written into the prompt.
        if re.search(r'\b(?:19|20)\d{2}\b',summary):continue
        book='Queen: As It Began' if row['SourceType']=='AsItBeganBook' else 'Queen: A Visual Documentary'
        source=book+' (user-supplied Markdown); history key '+row['SourceKey']
        text=history_questions.get(row['SourceKey'],f"In which year did this happen: {summary}?")
        add(text,year,
            wrongs(year,[str(int(year)+d) for d in (-4,-2,-1,1,2,4)],row['SourceKey']),
            'Queen History','medium',source,'history:'+row['SourceKey'])

    # Use only performances absent from earlier ledgers, with one question per
    # event. Country/city prompts are grounded in the cited concert itinerary.
    concerts=read_csv(ROOT/'data/queen_concert_history_events.csv')
    records=[]
    for row in concerts:
        if row['SourceUrl'] in used_concerts or not row['Title'].startswith('Queen at '):continue
        title=re.sub(r' \([^)]*\)$','',row['Title'])
        parts=title[9:].rsplit(', ',2)
        if len(parts)!=3:continue
        venue,city,country=parts
        date=datetime.strptime(row['EventDate'],'%Y-%m-%d').strftime('%-d %B %Y')
        if any(date.lower() in q.lower() for q,a in old):continue
        if country=='Bophuthatswana' or any(len(p)>90 for p in parts):continue
        records.append((row,venue,city,country,date))
    for index,(row,venue,city,country,date) in enumerate(records):
        mode=index%3
        source=row['SourceUrl']+'; curated concert history key '+row['SourceKey']
        if mode==0:
            text=f"Which city hosted Queen at {venue} on {date}?";correct=city;values=[r[2] for r in records];level='hard'
        elif mode==1:
            text=f"In which country was Queen's {date} concert at {venue}, {city}?";correct=country;values=[r[3] for r in records];level='easy'
        else:
            text=f"Which venue hosted Queen in {city} on {date}?";correct=venue;values=[r[1] for r in records if r[0]['EventDate'][:4]==row['EventDate'][:4]];level='hard'
        wrong=wrongs(correct,values,row['SourceKey']) or wrongs(correct,[r[1 if mode==2 else 2 if mode==0 else 3] for r in records],row['SourceKey'])
        if wrong:add(text,correct,wrong,'Concerts',level,source,'concert:'+row['SourceKey'])

    if (HERE/'manual_facts.psv').exists():
        for text,correct,w1,w2,w3,category,level,source,fact in read_psv('manual_facts.psv'):
            add(text,correct,[w1,w2,w3],category,level,source,fact)

    counts=Counter(q['category'] for q in pool)
    print('Candidate categories:',dict(counts),'total',len(pool))
    if args.candidates_only:
        (HERE/'candidate_review.json').write_text(json.dumps(pool,ensure_ascii=False,indent=2)+'\n')
        return
    if len(pool)<1500:raise ValueError(f'Need 1500 validated questions; have {len(pool)}')
    # Category caps prevent the mechanically structured material dominating.
    caps={'Concerts':300,'Songwriting':240,'Guest Appearances':360,'Album Tracks':260,'Live Albums':180,'Releases':65}
    selected=[];groups=defaultdict(list)
    for q in pool:groups[q['category']].append(q)
    for category,group in groups.items():
        random.Random(SEED+sum(map(ord,category))).shuffle(group)
        selected.extend(group[:caps.get(category,len(group))])
    if len(selected)<1500:raise ValueError(f'Category caps leave only {len(selected)} questions')
    # Keep authored narrative facts and fill remaining slots round-robin.
    priority=[q for q in selected if q['category'] not in caps]
    remaining=[q for q in selected if q['category'] in caps]
    buckets=defaultdict(list)
    for q in remaining:buckets[q['category']].append(q)
    selected=priority[:1500]
    while len(selected)<1500:
        for category in sorted(buckets):
            if buckets[category] and len(selected)<1500:selected.append(buckets[category].pop())
    random.Random(SEED).shuffle(selected)
    # Every quiz includes all four editorial levels, not just the whole bank.
    by_level=defaultdict(list)
    for q in selected:by_level[q['level']].append(q)
    quizzes=[]
    for _ in range(60):
        quizzes.append([by_level[level].pop() for level in ('easy','medium','hard','very hard')])
    remainder=[q for group in by_level.values() for q in group]
    random.Random(SEED+1).shuffle(remainder)
    for index,quiz in enumerate(quizzes):
        quiz.extend(remainder[index*21:(index+1)*21])
        random.Random(SEED+index+2).shuffle(quiz)
    selected=[q for quiz in quizzes for q in quiz]
    # Balance answer position precisely while retaining varied distractor order.
    rows=[];ledger=[]
    for index,q in enumerate(selected):
        title=f"Queen Quiz: Deep Cuts (Part {index//25+1:02d})"
        description='New Queen, solo, collaboration and archive questions. Mixed difficulty; source ledger supplied for editorial review.' if index%25==0 else ''
        wrong=list(q['wrong']);random.Random(SEED+index).shuffle(wrong)
        position=index%4;options=wrong[:position]+[q['correct']]+wrong[position:]
        points={'easy':1,'medium':2,'hard':3,'very hard':4}[q['level']]
        for n,option in enumerate(options):
            rows.append([title,description if n==0 else '',q['text'],q['category'], 'hard' if q['level']=='very hard' else q['level'],points,option,str(n==position).lower()])
        ledger.append([title,q['text'],q['correct'],q['level'],q['source'],q['fact']])
    def write(name,header,data):
        with (HERE/name).open('w',encoding='utf-8',newline='') as stream:
            writer=csv.writer(stream);writer.writerow(header);writer.writerows(data)
    write('questions.csv',HEADER,rows)
    write('sources.csv',['QuizTitle','QuestionText','CorrectAnswer','EditorialDifficulty','Source','FactKey'],ledger)
    report=dict(question_count=len(selected),quiz_count=60,questions_per_quiz=25,option_rows=len(rows),
                repository_baseline_questions=repository_count,comparison_questions=len(baseline),
                live_baseline_supplied=bool(args.live_baseline),
                production_snapshot_date='2026-10-11',production_snapshot_questions=1700,
                production_only_wordings=len(read_csv(HERE/'production_baseline_delta.csv')),normalized_wording_matches=0,
                repeated_fact_keys=0,difficulty=dict(Counter(q['level'] for q in selected)),
                categories=dict(Counter(q['category'] for q in selected)),
                correct_answer_positions=dict(Counter(str(i%4+1) for i in range(len(selected)))),
                source_books=['Queen: Complete Works','Queen: As It Began','Queen: A Visual Documentary','Queen: The Early Years'],
                candidate_questions=len(pool),rejected_candidates=len(rejected),
                limitations=['Lexical and explicit relationship checks do not prove universal semantic uniqueness; editorial source review remains necessary.',
                             'Questions are not imported or published by this generator.'])
    write('rejections.csv',['FactKey','Reason'],rejected)
    (HERE/'validation.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report,indent=2))


if __name__=='__main__':main()
