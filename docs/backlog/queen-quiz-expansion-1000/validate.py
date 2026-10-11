"""Independent structural and baseline checks for the QueenZone import package."""
import argparse
import csv
import hashlib
import json
import re
import unicodedata
from collections import Counter
from itertools import groupby
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
HEADER = 'QuizTitle,QuizDescription,QuestionText,Category,Difficulty,Points,OptionText,IsCorrect'.split(',')


def normalize(value):
    return re.sub('[^a-z0-9]', '', unicodedata.normalize('NFKD', value).casefold())


def read(path):
    with path.open(encoding='utf-8-sig', newline='') as stream:
        return list(csv.DictReader(stream))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--live-baseline', type=Path)
    args = parser.parse_args()
    rows = read(HERE / 'questions.csv')
    assert len(rows) == 4000 and list(rows[0]) == HEADER
    baseline = {normalize(r['QuestionText']) for path in (ROOT / 'docs/backlog').rglob('questions*.csv')
                if path.parent != HERE for r in read(path) if r.get('IsCorrect') == 'true'}
    baseline.update(normalize(r['QuestionText']) for r in read(HERE / 'production_baseline_delta.csv'))
    if args.live_baseline:
        baseline.update(normalize(r['QuestionText']) for r in json.loads(args.live_baseline.read_text()))
    ledger = read(HERE / 'sources.csv')
    assert len(ledger) == 1000
    indexed = {(r['QuizTitle'], r['QuestionText']): r for r in ledger}
    assert len(indexed) == len({r['FactKey'] for r in ledger}) == 1000
    exclusions = {r['FactKey'] for r in read(HERE / 'excluded_facts.csv')}
    quiz_titles, question_texts, positions, levels, sources = set(), set(), Counter(), Counter(), Counter()
    candidates = {line.split('|')[7] for file in HERE.glob('*.psv')
                  for line in file.read_text().splitlines() if line and not line.startswith('#')}
    rejected = read(HERE / 'rejected_candidates.csv')
    assert len({r['FactKey'] for r in rejected}) == len(rejected)
    assert candidates == {r['FactKey'] for r in ledger} | {r['FactKey'] for r in rejected}
    assert not ({r['FactKey'] for r in ledger} & {r['FactKey'] for r in rejected})
    total = 0
    for title, grouped_quiz in groupby(rows, lambda r: r['QuizTitle']):
        assert title and len(title) <= 200 and title not in quiz_titles
        quiz_titles.add(title)
        quiz = list(grouped_quiz)
        assert quiz[0]['QuizDescription'] and len(quiz[0]['QuizDescription']) <= 1000
        assert all(not r['QuizDescription'] for r in quiz[1:])
        questions, quiz_levels, quiz_positions = 0, set(), Counter()
        for text, grouped_question in groupby(quiz, lambda r: r['QuestionText']):
            options = list(grouped_question)
            assert len(options) == 4 and 0 < len(text) <= 500
            key = normalize(text)
            assert key not in question_texts and key not in baseline, text
            question_texts.add(key)
            assert len({normalize(r['OptionText']) for r in options}) == 4, text
            assert all(0 < len(r['OptionText']) <= 200 and r['IsCorrect'] in ('true', 'false') for r in options)
            correct = [i for i, r in enumerate(options, 1) if r['IsCorrect'] == 'true']
            assert len(correct) == 1
            position = str(correct[0])
            positions[position] += 1
            quiz_positions[position] += 1
            source = indexed[(title, text)]
            assert source['CorrectAnswer'] == options[correct[0] - 1]['OptionText']
            assert source['Source'] and source['Locator'] and source['FactKey'] not in exclusions
            assert source['FactKey'] in candidates
            if source['SourceCode'] == 'web':
                assert source['Locator'].startswith('https://')
            assert int(source['QuestionNumber']) == questions + 1
            level = source['EditorialDifficulty']
            points = {'easy': '1', 'medium': '2', 'hard': '3'}[level]
            assert all(r['Points'] == points and r['Difficulty'] == level for r in options)
            assert all(r['Category'] == options[0]['Category'] and 0 < len(r['Category']) <= 100 for r in options)
            levels[level] += 1
            sources[source['SourceCode']] += 1
            quiz_levels.add(level)
            total += 1
            questions += 1
        assert questions == 25 and quiz_levels == {'easy', 'medium', 'hard'}
        assert sorted(quiz_positions.values()) == [6, 6, 6, 7]
    assert total == 1000 and len(quiz_titles) == 40
    assert positions == Counter({'1': 250, '2': 250, '3': 250, '4': 250})
    assert levels == Counter({'easy': 150, 'medium': 450, 'hard': 400})
    assert set(sources) == {'songs', 'may', 'freddie', 'edge', 'life', 'web'}
    report = json.loads((HERE / 'validation.json').read_text())
    assert report['question_count'] == total and report['difficulty'] == dict(levels)
    assert report['correct_answer_positions'] == dict(positions) and report['sources'] == dict(sources)
    assert report['baseline_unique_wordings'] == len(baseline)
    assert report['candidate_count'] == len(candidates) and report['rejected_candidates'] == len(rejected)
    print(f'PASS: {total:,} questions, 40 quizzes, 4,000 option rows, four unique choices per question.')
    print(f'PASS: complete sources, all three difficulty levels in every quiz, balanced answer positions.')
    print(f'PASS: zero normalized wording matches against {len(baseline):,} existing/prior wordings.')
    print('Import CSV SHA-256:', hashlib.sha256((HERE / 'questions.csv').read_bytes()).hexdigest())


if __name__ == '__main__':
    main()
