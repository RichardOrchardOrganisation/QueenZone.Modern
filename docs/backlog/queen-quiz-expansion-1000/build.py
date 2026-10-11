"""Build the reviewed 1,000-question CSV using only committed editorial inputs."""
import csv
import hashlib
import json
import re
import random
import unicodedata
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
HEADER = 'QuizTitle,QuizDescription,QuestionText,Category,Difficulty,Points,OptionText,IsCorrect'.split(',')
BOOKS = {
    'songs': 'Benoit Clerc, Queen All the Songs (2020)',
    'may': 'Laura Jackson, Brian May (2007; supplied ebook edition)',
    'freddie': 'Laura Jackson, Freddie Mercury: The Biography (2011)',
    'edge': 'David Bret, The Freddie Mercury Story: Living on the Edge (1996; revised edition)',
    'life': 'David Evans and David Minns, This Was the Real Life (revised 2008; ebook 2015)',
}
QUOTAS = {'easy': 150, 'medium': 450, 'hard': 400}


def normalize(value):
    return re.sub('[^a-z0-9]', '', unicodedata.normalize('NFKD', value).casefold())


def read_csv(path):
    with path.open(encoding='utf-8-sig', newline='') as stream:
        return list(csv.DictReader(stream))


def write_csv(name, header, rows):
    with (HERE / name).open('w', encoding='utf-8', newline='') as stream:
        writer = csv.writer(stream)
        writer.writerow(header)
        writer.writerows(rows)


def baseline():
    result = set()
    for path in (ROOT / 'docs/backlog').rglob('questions*.csv'):
        if path.parent == HERE:
            continue
        for row in read_csv(path):
            if row.get('IsCorrect') == 'true':
                result.add(normalize(row['QuestionText']))
    result.update(normalize(r['QuestionText']) for r in read_csv(HERE / 'production_baseline_delta.csv'))
    return result


def category_group(category):
    value = category.lower()
    if any(word in value for word in ['science', 'beyond music', 'offstage', 'art collections', 'personal stories']):
        return 'Beyond Music'
    if any(word in value for word in ['sleeve', 'artwork', 'photograph', 'books and']):
        return 'Artwork and Photography'
    if 'video' in value:
        return 'Music Videos'
    if any(word in value for word in ['instrument', 'guitar']):
        return 'Instruments and Sound'
    if any(word in value for word in ['guest', 'collaborat', 'creative partnerships']):
        return 'Guest Appearances'
    if 'solo' in value or 'cross recordings' in value:
        return 'Solo Projects'
    if any(word in value for word in ['studio', 'recording', 'producer', 'production']):
        return 'Recording Details'
    if any(word in value for word in ['single', 'release', 'reissue', 'album launches', 'record labels']):
        return 'Releases'
    if 'style' in value and 'musical' not in value:
        return 'Stage Style'
    if any(word in value for word in ['live', 'stage', 'tour', 'tribute', 'performances']):
        return 'Concerts'
    if any(word in value for word in ['film', 'television', 'radio', 'media', 'games', 'documentar', 'advertising', 'queen in culture']):
        return 'Film and Television'
    if 'cover' in value:
        return 'Cover Versions'
    if 'influence' in value:
        return 'Influences'
    if value in ['early life', 'early lives']:
        return 'Early Lives'
    if any(word in value for word in ['early', 'history', 'band', 'crew', 'legacy', 'archive research']):
        return 'Queen History'
    return 'Song Stories'


def main():
    excluded = {r['FactKey']: r['Reason'] for r in read_csv(HERE / 'excluded_facts.csv')}
    existing = baseline()
    facts, keys, rejected = [], set(), []
    for file in ['authored_facts.psv', 'catalog_facts.psv']:
        for line in (HERE / file).read_text().splitlines():
            if not line or line.startswith('#'):
                continue
            fields = line.split('|')
            assert len(fields) == 8, line
            question, answer, wrong, difficulty, category, book, locator, key = fields
            assert key not in keys, key
            keys.add(key)
            assert difficulty in QUOTAS and book in (*BOOKS, 'web')
            assert len(wrong.split(';')) == 3
            if key in excluded:
                rejected.append([key, excluded[key]])
            elif normalize(question) in existing:
                rejected.append([key, 'Normalized question wording already exists'])
            else:
                facts.append(fields)
    # Prefer individually authored stories and the new supplied sources over credit templates.
    def rank(row):
        return (row[4] == 'Studio Credits', row[5] == 'songs', hashlib.sha256(row[7].encode()).hexdigest())
    selected = []
    for level, count in QUOTAS.items():
        pool = sorted((r for r in facts if r[3] == level), key=rank)
        assert len(pool) >= count, (level, len(pool), count)
        selected.extend(pool[:count])
        rejected.extend([r[7], 'Not selected: editorial balance of difficulty and topics'] for r in pool[count:])
    assert len(selected) == 1000
    # Allocate in rounds so rare sources, topics, and repeated song titles are dispersed.
    quizzes = [[] for _ in range(40)]
    for level in QUOTAS:
        pool = sorted((r for r in selected if r[3] == level), key=lambda r: (r[4], r[7]))
        capacities = ([4] * 30 + [3] * 10 if level == 'easy'
                      else [11] * 30 + [12] * 10 if level == 'medium' else [10] * 40)
        for index, row in enumerate(pool):
            available = [i for i, n in enumerate(capacities) if n]
            # Different facts about one song/event stay in different quizzes where possible.
            topic = row[7].split(':')[1] if row[7].startswith(('credit:', 'singlepair:')) else row[7].split(':')[0]
            target = min(available, key=lambda i: (
                sum((x[7].split(':')[1] if x[7].startswith(('credit:', 'singlepair:')) else x[7].split(':')[0]) == topic for x in quizzes[i]),
                sum(x[4] == row[4] for x in quizzes[i]),
                len(quizzes[i]), (i - index) % 40))
            quizzes[target].append(row)
            capacities[target] -= 1
    output, ledger = [], []
    answer_counts, level_counts, category_counts, source_counts = Counter(), Counter(), Counter(), Counter()
    for quiz_index, quiz in enumerate(quizzes):
        assert len(quiz) == 25
        quiz.sort(key=lambda r: hashlib.sha256((r[7] + str(quiz_index)).encode()).hexdigest())
        title = f'Queen Quiz: Stories and Sound (Part {quiz_index + 1:02d})'
        description = '25 new questions about Queen, their recordings, live work, solo careers and collaborators. Easy, medium and hard questions; 1, 2 and 3 points respectively.'
        positions = list(range(4)) * 6 + [quiz_index % 4]
        random.Random(1946 + quiz_index).shuffle(positions)
        for number, row in enumerate(quiz):
            question, answer, wrong, difficulty, category, book, locator, key = row
            category = category_group(category)
            choices = wrong.split(';')
            position = positions[number]
            choices.insert(position, answer)
            for option_index, option in enumerate(choices):
                output.append([title, description if number == option_index == 0 else '', question,
                               category, difficulty, {'easy': 1, 'medium': 2, 'hard': 3}[difficulty],
                               option, 'true' if option_index == position else 'false'])
            source = BOOKS.get(book, 'Official artist, band or record-label website')
            ledger.append([title, number + 1, question, answer, key, difficulty, source, locator, book])
            answer_counts[str(position + 1)] += 1
            level_counts[difficulty] += 1
            category_counts[category] += 1
            source_counts[book] += 1
    write_csv('questions.csv', HEADER, output)
    write_csv('sources.csv', ['QuizTitle', 'QuestionNumber', 'QuestionText', 'CorrectAnswer', 'FactKey',
                             'EditorialDifficulty', 'Source', 'Locator', 'SourceCode'], ledger)
    write_csv('rejected_candidates.csv', ['FactKey', 'Reason'], sorted(rejected))
    report = {'question_count': len(ledger), 'quiz_count': 40, 'option_rows': len(output),
              'difficulty': dict(level_counts), 'correct_answer_positions': dict(answer_counts),
              'categories': dict(sorted(category_counts.items())), 'sources': dict(source_counts),
              'baseline_unique_wordings': len(existing), 'candidate_count': len(keys),
              'rejected_candidates': len(rejected), 'production_baseline_read': '2026-10-11',
              'production_question_count': 1700, 'database_writes': 0}
    (HERE / 'validation.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({k: report[k] for k in ['question_count', 'difficulty', 'sources', 'rejected_candidates']}))


if __name__ == '__main__':
    main()
