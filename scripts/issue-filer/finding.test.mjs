import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  isFilerComment,
  isValidFinding,
  keysOverlap,
  parseFields,
  parseFilerMarker,
  parseFindings,
} from './finding.mjs';

test('parseFields is order-free and ignores extra tokens', () => {
  const fields = parseFields('verdict=blocking file=src/A.cs:1 repeat=yes rule=csharp.regex-timeout level=L2');
  assert.equal(fields.level, 'L2');
  assert.equal(fields.rule, 'csharp.regex-timeout');
  assert.equal(fields.file, 'src/A.cs:1');
});

test('valid finding requires every field and a dotted rule id', () => {
  const good = {
    level: 'L2',
    rule: 'csharp.regex-timeout',
    repeat: 'yes',
    file: 'src/A.cs:42',
    verdict: 'blocking',
  };
  assert.equal(isValidFinding(good), true);
  assert.equal(isValidFinding({ ...good, rule: 'not-a-rule' }), false);
  assert.equal(isValidFinding({ ...good, level: 'L6' }), false);
  assert.equal(isValidFinding({ ...good, file: 'has space.cs' }), false);
});

test('parseFindings keeps malformed tags out of filings', () => {
  const text = [
    '<!-- qz-finding v=1 level=L2 rule=csharp.regex-timeout repeat=yes file=src/A.cs:1 verdict=blocking -->',
    '<!-- qz-finding v=1 level=L2 rule=bad repeat=yes file=src/A.cs verdict=blocking -->',
  ].join('\n');
  const { findings, malformed } = parseFindings(text, { pr: 9 });
  assert.equal(findings.length, 1);
  assert.equal(findings[0].pr, 9);
  assert.equal(malformed.length, 1);
});

test('filer marker parses keys and rejects empty or spaced keys', () => {
  const marker = parseFilerMarker('hello\n<!-- qz-filer v=1 keys=review:csharp.regex-timeout,sonar:javascript:S1 source=review -->\n');
  assert.deepEqual(marker.keys, ['review:csharp.regex-timeout', 'sonar:javascript:S1']);
  assert.equal(marker.source, 'review');
  assert.equal(parseFilerMarker('<!-- qz-filer v=1 keys= source=review -->'), null);
  assert.equal(keysOverlap(['a', 'b'], ['b', 'c']), true);
  assert.equal(keysOverlap(['a'], ['c']), false);
  assert.equal(isFilerComment('update\n<!-- qz-filer v=1 -->\n'), true);
});
