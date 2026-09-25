import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import {
  toUrlSafeBase64,
  trimChar,
  trimLeadingChar,
  trimTrailingChar,
  trimTrailingSet,
} from './trimRuns.ts';

const punctuation = new Set(['.', ',', ';', ':', '!', '?', ')', ']', '}', "'", '"']);

function assertQuick(run: () => void, maxMs = 100): void {
  const started = performance.now();
  run();
  assert.ok(performance.now() - started < maxMs, 'pathological input should stay linear');
}

describe('trimTrailingChar', () => {
  it('strips a trailing run and leaves other input alone', () => {
    assert.equal(trimTrailingChar('https://qz.test///', '/'), 'https://qz.test');
    assert.equal(trimTrailingChar('https://qz.test', '/'), 'https://qz.test');
    assert.equal(trimTrailingChar('abc===', '='), 'abc');
    assert.equal(trimTrailingChar('abc', '='), 'abc');
    assert.equal(trimTrailingChar('', '/'), '');
    assert.equal(trimTrailingChar('   ', '/'), '   ');
    assert.equal(trimTrailingChar('///', '/'), '');
    assert.equal(trimTrailingChar('/', '/'), '');
  });

  it('finishes quickly on a long trailing run', () => {
    const input = `https://qz.test${'/'.repeat(40_000)}`;
    assertQuick(() => {
      assert.equal(trimTrailingChar(input, '/'), 'https://qz.test');
    });
  });
});

describe('trimLeadingChar / trimChar', () => {
  it('strips leading and both-end runs', () => {
    assert.equal(trimLeadingChar('///quotes/9', '/'), 'quotes/9');
    assert.equal(trimLeadingChar('quotes/9', '/'), 'quotes/9');
    assert.equal(trimLeadingChar('', '/'), '');
    assert.equal(trimChar('///quotes/9///', '/'), 'quotes/9');
    assert.equal(trimChar('///', '/'), '');
    assert.equal(trimChar('', '/'), '');
    assert.equal(trimChar('quotes', '/'), 'quotes');
  });

  it('finishes quickly on a long slash sandwich', () => {
    const input = `${'/'.repeat(20_000)}quotes${'/'.repeat(20_000)}`;
    assertQuick(() => {
      assert.equal(trimChar(input, '/'), 'quotes');
    });
  });
});

describe('trimTrailingSet', () => {
  it('strips trailing punctuation the share parser used to drop with /[punct]+$/', () => {
    assert.equal(trimTrailingSet('https://example.com/x.', punctuation), 'https://example.com/x');
    assert.equal(trimTrailingSet('https://example.com/x).,"', punctuation), 'https://example.com/x');
    assert.equal(trimTrailingSet('https://example.com/x', punctuation), 'https://example.com/x');
    assert.equal(trimTrailingSet('', punctuation), '');
    assert.equal(trimTrailingSet('...', punctuation), '');
    assert.equal(trimTrailingSet('   ', punctuation), '   ');
  });

  it('finishes quickly on a long punctuation run', () => {
    const input = `https://example.com/x${'.'.repeat(40_000)}`;
    assertQuick(() => {
      assert.equal(trimTrailingSet(input, punctuation), 'https://example.com/x');
    });
  });
});

describe('toUrlSafeBase64', () => {
  it('maps the URL-safe alphabet and drops padding', () => {
    assert.equal(toUrlSafeBase64('abc+def/ghi='), 'abc-def_ghi');
    assert.equal(toUrlSafeBase64('abc+def/ghi=='), 'abc-def_ghi');
    assert.equal(toUrlSafeBase64('abcd'), 'abcd');
    assert.equal(toUrlSafeBase64(''), '');
    assert.equal(toUrlSafeBase64('===='), '');
    assert.equal(toUrlSafeBase64('+/+='), '-_-');
  });

  it('finishes quickly on a long padding run', () => {
    const input = `abc${'='.repeat(40_000)}`;
    assertQuick(() => {
      assert.equal(toUrlSafeBase64(input), 'abc');
    });
  });
});
