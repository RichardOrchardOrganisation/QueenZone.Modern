"""Independently validate the deliverable, its ledger, and existing-bank wording."""
import argparse
import csv
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
    assert len(rows) == 6000 and list(rows[0]) == HEADER
    baseline = {normalize(r['QuestionText']) for path in (ROOT / 'docs/backlog').rglob('questions*.csv')
                if path.parent != HERE for r in read(path) if r.get('IsCorrect') == 'true'}
    baseline.update(normalize(r['QuestionText']) for r in read(HERE / 'production_baseline_delta.csv'))
    if args.live_baseline:
        baseline.update(normalize(r['QuestionText']) for r in json.loads(args.live_baseline.read_text()))
    ledger = read(HERE / 'sources.csv')
    assert len(ledger) == 1500
    indexed = {(r['QuizTitle'], r['QuestionText']): r for r in ledger}
    assert len(indexed) == 1500 and len({r['FactKey'] for r in ledger}) == 1500
    quiz_titles, question_texts, positions, levels = set(), set(), Counter(), Counter()
    exclusions = {r['FactKey'] for r in read(HERE / 'excluded_facts.csv')}
    total = 0
    for title, grouped_quiz in groupby(rows, lambda r: r['QuizTitle']):
        assert title and len(title) <= 200 and title not in quiz_titles
        quiz_titles.add(title)
        quiz = list(grouped_quiz)
        assert quiz[0]['QuizDescription'] and len(quiz[0]['QuizDescription']) <= 1000
        assert all(not r['QuizDescription'] for r in quiz[1:])
        questions = 0
        quiz_levels = set()
        for text, grouped_question in groupby(quiz, lambda r: r['QuestionText']):
            options = list(grouped_question)
            assert len(options) == 4 and len(text) <= 500
            key = normalize(text)
            assert key and key not in question_texts and key not in baseline, text
            question_texts.add(key)
            assert len({normalize(r['OptionText']) for r in options}) == 4
            assert all(0 < len(r['OptionText']) <= 200 and r['IsCorrect'] in ('true', 'false') for r in options)
            correct = [i for i, r in enumerate(options, 1) if r['IsCorrect'] == 'true']
            assert len(correct) == 1
            positions[str(correct[0])] += 1
            source = indexed[(title, text)]
            assert source['CorrectAnswer'] == options[correct[0] - 1]['OptionText']
            assert source['Source'] and source['FactKey'] not in exclusions
            editorial = source['EditorialDifficulty']
            expected_points = {'easy': '1', 'medium': '2', 'hard': '3', 'very hard': '4'}[editorial]
            expected_level = 'hard' if editorial == 'very hard' else editorial
            assert all(r['Points'] == expected_points and r['Difficulty'] == expected_level for r in options)
            assert all(r['Category'] == options[0]['Category'] and len(r['Category']) <= 100 for r in options)
            levels[editorial] += 1
            quiz_levels.add(editorial)
            total += 1
            questions += 1
        assert questions == 25 and quiz_levels == {'easy', 'medium', 'hard', 'very hard'}
    assert total == 1500 and len(quiz_titles) == 60
    assert positions == Counter({'1': 375, '2': 375, '3': 375, '4': 375})
    assert set(levels) == {'easy', 'medium', 'hard', 'very hard'}
    report = json.loads((HERE / 'validation.json').read_text())
    assert report['question_count'] == total and report['difficulty'] == dict(levels)
    assert report['correct_answer_positions'] == dict(positions)
    print('PASS: 1,500 questions; 60 quizzes; 6,000 option rows; complete source ledger;')
    print(f'zero normalized wording matches against {len(baseline):,} existing-bank wordings; balanced answer positions.')


if __name__ == '__main__':
    main()
