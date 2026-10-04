import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { crosswordSlugFromUrl, openCrosswordLink } from './deepLink.ts';
describe('crossword deep links', () => {
  it('opens canonical app and website links with a usable Archive back stack', () => {
    for (const url of ['queenzone://crosswords/meet-the-band', 'https://queenzone.org/crosswords/meet-the-band/', 'https://www.queenzone.org/crosswords/meet-the-band?ref=share', 'https://dev.queenzone.org/crosswords/meet-the-band']) {
      assert.equal(crosswordSlugFromUrl(url), 'meet-the-band');
      let target: unknown;
      assert.equal(openCrosswordLink({ navigate: (...args) => { target = args; } }, url), true);
      assert.deepEqual(target, ['Tabs', { screen: 'ArchiveTab', params: { screen: 'CrosswordPlay', params: { slug: 'meet-the-band' }, initial: false } }]);
    }
  });
  it('rejects unrelated origins, invalid escapes, extra paths and empty slugs', () => {
    for (const url of ['no url', 'https://evil.test/crosswords/meet-the-band', 'http://queenzone.org/crosswords/meet-the-band', 'https://queenzone.com/crosswords/meet-the-band',
      'queenzone://crosswords/', 'queenzone://crosswords/a/b', 'queenzone://crosswords/a%2Fb', 'queenzone://crosswords/%ZZ',
      'queenzone://crosswords/' + 'a'.repeat(161), 'queenzone://quotes/1']) {
      assert.equal(crosswordSlugFromUrl(url), null);
      assert.equal(openCrosswordLink({ navigate: () => assert.fail('Invalid link navigated') }, url), false);
    }
  });
});
