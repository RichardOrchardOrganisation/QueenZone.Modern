import { strict as assert } from 'node:assert';
import { test } from 'node:test';
import { parseSiriDeepLink } from './siriDeepLink.ts';

test('accepts only the public Siri destinations', () => {
  assert.deepEqual(parseSiriDeepLink('queenzone://news'), { kind: 'news' });
  assert.deepEqual(parseSiriDeepLink('queenzone://trivia'), { kind: 'trivia' });
  assert.deepEqual(parseSiriDeepLink('queenzone://timeline'), { kind: 'timeline' });
  assert.deepEqual(parseSiriDeepLink('queenzone://timeline/42'), { kind: 'timeline', id: 42 });
  assert.deepEqual(parseSiriDeepLink('queenzone://search?q=Bohemian%20Rhapsody'), {
    kind: 'search', query: 'Bohemian Rhapsody',
  });
  assert.deepEqual(parseSiriDeepLink('queenzone://album/12'), { kind: 'album', id: 12 });
  assert.deepEqual(parseSiriDeepLink('queenzone://biography/7'), { kind: 'biography', id: 7 });
  for (const url of ['queenzone://inbox', 'queenzone://news?x=1', 'queenzone://search?q=a',
    'queenzone://search?q=', 'https://www.queenzone.org/news', 'queenzone://search/path?q=Queen',
    'queenzone://album/-1', 'queenzone://album/0', 'queenzone://album/12/extra',
    'queenzone://timeline/0', 'queenzone://timeline/12/extra', 'queenzone://trivia/1']) {
    assert.equal(parseSiriDeepLink(url), null, url);
  }
});
