import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { photoCorrectionUrl } from './photoCorrectionLink.ts';

describe('photoCorrectionUrl', () => {
  it('points at the website correction form for the photo', () => {
    assert.equal(
      photoCorrectionUrl('https://www.queenzone.org', 101),
      'https://www.queenzone.org/submit/photo-correction/101',
    );
  });
  it('tolerates a trailing slash on the base url', () => {
    assert.equal(
      photoCorrectionUrl('https://dev.queenzone.org/', 7),
      'https://dev.queenzone.org/submit/photo-correction/7',
    );
  });
  it('refuses ids that are not positive integers', () => {
    assert.equal(photoCorrectionUrl('https://www.queenzone.org', 0), null);
    assert.equal(photoCorrectionUrl('https://www.queenzone.org', -3), null);
    assert.equal(photoCorrectionUrl('https://www.queenzone.org', 1.5), null);
    assert.equal(photoCorrectionUrl('https://www.queenzone.org', Number.NaN), null);
  });
});
