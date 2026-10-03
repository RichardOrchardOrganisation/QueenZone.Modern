import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, it } from 'node:test';
import { WIDGET_FACE_SLOT_MS, widgetActiveFace, widgetEyebrow } from './widgetCopy.ts';
import { widgetFaceDeepLinkUrl } from './widgetDeepLink.ts';

const source = readFileSync(new URL('../../plugins/withIosOnThisDayNativeWidget.cjs', import.meta.url), 'utf8');
const activeFace = source.slice(source.indexOf('  private var activeFace:'), source.indexOf('  private var showDay:'));
const tapUrl = source.slice(source.indexOf('  private var tapURL:'), source.indexOf('  private func stringProp'));

describe('native Swift widget parity guards', () => {
  it('keeps day, quote, trivia ordering and the canonical four-hour UTC slot', () => {
    assert.match(activeFace, /if hasDay \{ faces\.append\("day"\) \}/);
    assert.match(activeFace, /if hasQuote \{ faces\.append\("quote"\) \}/);
    assert.match(activeFace, /if hasTrivia \{ faces\.append\("trivia"\) \}/);
    assert.ok(activeFace.indexOf('"day"') < activeFace.indexOf('"quote"'));
    assert.ok(activeFace.indexOf('"quote"') < activeFace.indexOf('"trivia"'));
    assert.match(activeFace, /guard !faces\.isEmpty else \{ return nil \}/);
    assert.match(activeFace, /floor\(entry\.date\.timeIntervalSince1970 \/ \(4 \* 3600\)\)/);
    assert.match(activeFace, /return faces\[slot % faces\.count\]/);
    assert.equal(4 * 3600 * 1000, WIDGET_FACE_SLOT_MS);
    const props = { formattedDate: 'Today', summary: 'Day', quoteText: 'Quote', quoteWhoSaid: 'Brian', triviaText: 'Fact' };
    assert.deepEqual([0, 1, 2].map((slot) => widgetActiveFace(props, slot * WIDGET_FACE_SLOT_MS)), ['day', 'quote', 'trivia']);
    for (const face of ['day', 'quote', 'trivia'] as const) assert.ok(source.includes(widgetEyebrow(face)));
  });

  it('keeps face URL shapes and lets a no-face event open its canonical timeline detail', () => {
    assert.ok(tapUrl.includes(`URL(string: "${widgetFaceDeepLinkUrl('trivia')}")`));
    assert.ok(tapUrl.includes(`URL(string: "${widgetFaceDeepLinkUrl('quote')}")`));
    assert.ok(tapUrl.includes(`URL(string: "${widgetFaceDeepLinkUrl(null)}")`));
    assert.ok(tapUrl.includes(String.raw`queenzone://quotes/\\(quoteId)`));
    assert.ok(tapUrl.includes(String.raw`queenzone://timeline/\\(eventId)`));
    assert.match(tapUrl, /if showQuote && quoteId > 0/);
    assert.match(tapUrl, /if eventId > 0/);
    assert.doesNotMatch(tapUrl, /if showDay && eventId/);
    assert.ok(tapUrl.indexOf('if showQuote {') < tapUrl.indexOf('if eventId > 0'));
    assert.equal(widgetFaceDeepLinkUrl(null, undefined, 12), 'queenzone://timeline/12');
    assert.equal(widgetFaceDeepLinkUrl('quote', 0, 12), 'queenzone://home');
  });
});
