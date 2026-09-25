import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { formatPublishedDate, toPlainText } from './text.ts';

describe('toPlainText', () => {
  it('returns empty string for nullish input', () => {
    assert.equal(toPlainText(null), '');
    assert.equal(toPlainText(undefined), '');
    assert.equal(toPlainText(''), '');
  });

  it('strips tags and decodes common entities', () => {
    assert.equal(
      toPlainText('<p>Hello&nbsp;<strong>Queen</strong> &amp; Co.</p>'),
      'Hello Queen & Co.',
    );
  });

  it('turns breaks into newlines', () => {
    assert.equal(toPlainText('Line one<br/>Line two'), 'Line one\nLine two');
  });

  it('does not double-unescape ampersand entities', () => {
    assert.equal(toPlainText('&amp;lt;script&amp;gt;'), '&lt;script&gt;');
    assert.equal(toPlainText('A &amp;amp; B'), 'A &amp; B');
  });

  it('leaves angle-bracket entities encoded and strips nested markup', () => {
    assert.equal(toPlainText('&lt;script&gt;alert(1)&lt;/script&gt;'), '&lt;script&gt;alert(1)&lt;/script&gt;');
    assert.equal(toPlainText('<scr<script>ipt>'), 'ipt');
    // Bare angle brackets are treated as markup and removed.
    assert.equal(toPlainText('A < B and C > D'), 'A  D');
  });

  it('leaves unmatched angle brackets for the later strip, including empty and whitespace', () => {
    assert.equal(toPlainText('   '), '');
    assert.equal(toPlainText('plain text only'), 'plain text only');
    assert.equal(toPlainText('Hello <em>there'), 'Hello there');
    assert.equal(toPlainText('Hello <em>there</em>'), 'Hello there');
    assert.equal(toPlainText('<>'), '');
  });

  it('finishes quickly on a long unmatched < run that used to backtrack', () => {
    const started = performance.now();
    assert.equal(toPlainText('<'.repeat(30_000)), '');
    assert.equal(toPlainText(`<${'x'.repeat(30_000)}`), 'x'.repeat(30_000));
    assert.ok(performance.now() - started < 100);
  });
});

describe('formatPublishedDate', () => {
  it('formats a valid ISO date', () => {
    const formatted = formatPublishedDate('2020-06-15T12:00:00Z');
    assert.match(formatted, /2020/);
    assert.match(formatted, /15|Jun/i);
  });

  it('returns empty string for invalid dates', () => {
    assert.equal(formatPublishedDate('not-a-date'), '');
  });
});
