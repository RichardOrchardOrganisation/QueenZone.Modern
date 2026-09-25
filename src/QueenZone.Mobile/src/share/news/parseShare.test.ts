import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { findLinks, leftoverAfterUrls, normalizeShareUrl, parseShare } from './parseShare.ts';

describe('parseShare', () => {
  it('accepts a dedicated url field', () => {
    const intake = parseShare({ webUrl: 'https://www.bbc.co.uk/news/example', hasFiles: false });
    assert.deepEqual(intake, {
      kind: 'accepted',
      url: 'https://www.bbc.co.uk/news/example',
      leftoverText: '',
    });
  });

  it('accepts a https URL embedded in text and keeps leftover title ≤300', () => {
    const intake = parseShare({
      text: 'Queen announce dates https://www.bbc.co.uk/news/example tonight',
      hasFiles: false,
    });
    assert.equal(intake.kind, 'accepted');
    if (intake.kind !== 'accepted') {
      return;
    }
    assert.equal(intake.url, 'https://www.bbc.co.uk/news/example');
    assert.equal(intake.leftoverText, 'Queen announce dates tonight');
  });

  it('treats a duplicated same URL as one accepted link', () => {
    const intake = parseShare({
      webUrl: 'https://www.bbc.co.uk/news/example',
      text: 'See https://www.bbc.co.uk/news/example',
      hasFiles: false,
    });
    assert.equal(intake.kind, 'accepted');
  });

  it('asks the member to choose when two https URLs are present', () => {
    const intake = parseShare({
      text: 'https://www.bbc.co.uk/one https://www.bbc.co.uk/two',
      hasFiles: false,
    });
    assert.deepEqual(intake, {
      kind: 'choose',
      candidates: ['https://www.bbc.co.uk/one', 'https://www.bbc.co.uk/two'],
    });
  });

  it('rejects http-only shares without persisting an upgrade path', () => {
    const intake = parseShare({ text: 'http://www.bbc.co.uk/news/example', hasFiles: false });
    assert.equal(intake.kind, 'rejected');
    if (intake.kind !== 'rejected') {
      return;
    }
    assert.equal(intake.reason, 'notHttps');
  });

  it('rejects a file share even when the caption contains a URL', () => {
    const intake = parseShare({
      text: 'Photo of https://www.bbc.co.uk/news/example',
      hasFiles: true,
    });
    assert.equal(intake.kind, 'rejected');
    if (intake.kind !== 'rejected') {
      return;
    }
    assert.equal(intake.reason, 'file');
  });

  it('rejects javascript: payloads', () => {
    const intake = parseShare({ text: 'javascript:alert(1)', hasFiles: false });
    assert.equal(intake.kind, 'rejected');
    if (intake.kind !== 'rejected') {
      return;
    }
    assert.equal(intake.reason, 'unsupportedScheme');
  });

  it('rejects a custom scheme', () => {
    const intake = parseShare({ text: 'queenzone://story/9', hasFiles: false });
    assert.equal(intake.kind, 'rejected');
    if (intake.kind !== 'rejected') {
      return;
    }
    assert.equal(intake.reason, 'unsupportedScheme');
  });

  it('puts mixed http and https into choose instead of auto-picking https', () => {
    const intake = parseShare({
      text: 'http://example.com/old https://example.com/new',
      hasFiles: false,
    });
    assert.equal(intake.kind, 'choose');
    if (intake.kind !== 'choose') {
      return;
    }
    assert.deepEqual(intake.candidates, ['http://example.com/old', 'https://example.com/new']);
  });

  it('discards leftover title text longer than 300 characters', () => {
    const leftover = leftoverAfterUrls(`${'Q'.repeat(301)} https://example.com/story`, [
      { scheme: 'https', href: 'https://example.com/story' },
    ]);
    assert.equal(leftover, '');

    const short = leftoverAfterUrls('Queen announce dates https://example.com/story', [
      { scheme: 'https', href: 'https://example.com/story' },
    ]);
    assert.equal(short, 'Queen announce dates');
    assert.ok(short.length <= 300);
  });

  it('rejects shares with no URL', () => {
    const intake = parseShare({ text: 'Just a thought about the gig', hasFiles: false });
    assert.equal(intake.kind, 'rejected');
    if (intake.kind !== 'rejected') {
      return;
    }
    assert.equal(intake.reason, 'noUrl');
  });

  it('rejects empty, whitespace-only, data, file, intent, and digit-prefixed custom schemes', () => {
    assert.equal(parseShare({ text: '', hasFiles: false }).kind, 'rejected');
    assert.equal(parseShare({ text: '   \n', hasFiles: false }).kind, 'rejected');
    assert.equal(
      (parseShare({ text: 'data:text/html,hello', hasFiles: false }) as { reason: string }).reason,
      'unsupportedScheme',
    );
    assert.equal(
      (parseShare({ text: 'FILE:C:/secret', hasFiles: false }) as { reason: string }).reason,
      'unsupportedScheme',
    );
    assert.equal(
      (parseShare({ text: 'intent://scan', hasFiles: false }) as { reason: string }).reason,
      'unsupportedScheme',
    );
    assert.equal(
      (parseShare({ text: '1queenzone://story/9', hasFiles: false }) as { reason: string }).reason,
      'unsupportedScheme',
    );
    assert.equal(
      (parseShare({ text: 'ftp://example.com/a', hasFiles: false }) as { reason: string }).reason,
      'unsupportedScheme',
    );
  });
});

describe('findLinks', () => {
  it('keeps http(s) hrefs and strips trailing punctuation', () => {
    assert.deepEqual(findLinks(''), []);
    assert.deepEqual(findLinks('   '), []);
    assert.deepEqual(findLinks('https://example.com/x.'), [
      { scheme: 'https', href: 'https://example.com/x' },
    ]);
    assert.deepEqual(findLinks('See http://example.com/x).," tonight'), [
      { scheme: 'http', href: 'http://example.com/x' },
    ]);
    assert.deepEqual(findLinks('https://example.com/x'), [
      { scheme: 'https', href: 'https://example.com/x' },
    ]);
  });

  it('finishes quickly on a long trailing-punctuation run', () => {
    const started = performance.now();
    assert.deepEqual(findLinks(`https://example.com/x${'.'.repeat(40_000)}`), [
      { scheme: 'https', href: 'https://example.com/x' },
    ]);
    assert.ok(performance.now() - started < 100);
  });
});

describe('normalizeShareUrl', () => {
  it('drops default ports, hashes, and trailing slashes on non-root paths', () => {
    assert.equal(
      normalizeShareUrl('https://WWW.Example.com:443/news/story/?q=1#hash'),
      'https://www.example.com/news/story?q=1',
    );
    assert.equal(normalizeShareUrl('http://example.com:80/'), 'http://example.com/');
    assert.equal(normalizeShareUrl('https://example.com/foo///'), 'https://example.com/foo');
    assert.equal(normalizeShareUrl('  '), '');
    assert.equal(normalizeShareUrl('not a url'), 'not a url');
  });

  it('finishes quickly on a long trailing-slash pathname', () => {
    const started = performance.now();
    assert.equal(
      normalizeShareUrl(`https://example.com/foo${'/'.repeat(40_000)}`),
      'https://example.com/foo',
    );
    assert.ok(performance.now() - started < 100);
  });
});

describe('unsupported-scheme scan', () => {
  it('finishes quickly on a long letter run that used to backtrack', () => {
    const started = performance.now();
    const intake = parseShare({ text: 'a'.repeat(40_000), hasFiles: false });
    assert.equal(intake.kind, 'rejected');
    if (intake.kind === 'rejected') {
      assert.equal(intake.reason, 'noUrl');
    }
    assert.ok(performance.now() - started < 100);
  });
});
