import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, it } from 'node:test';

const source = readFileSync(new URL('./OnThisDayWidget.ios.tsx', import.meta.url), 'utf8');

const viewStart = source.indexOf('function OnThisDayWidgetView');
const viewEnd = source.indexOf('export const OnThisDayWidget');
const viewBody = source.slice(viewStart, viewEnd);

describe('OnThisDayWidget.ios referential freedom', () => {
  it('keeps the widget function free of imported helpers and module constants', () => {
    assert.notEqual(viewStart, -1);
    assert.notEqual(viewEnd, -1);

    for (const ident of [
      'widgetHasDay',
      'widgetHasQuote',
      'widgetHasTrivia',
      'widgetEyebrow',
      'widgetDayText',
      'widgetDayPrimary',
      'widgetDaySecondary',
      'widgetQuoteText',
      'widgetQuotePrimary',
      'widgetQuoteSecondary',
      'widgetTriviaPrimary',
      'widgetEmptyText',
      'widgetDeepLinkUrl',
      'WIDGET_QUOTE_MAX_PT_SMALL',
      'gold',
      'cream',
      'mutedCream',
      'cardBackground',
    ]) {
      assert.equal(viewBody.includes(ident), false, `widget function must not reference ${ident}`);
    }
  });

  it('inlines the home deep link, card colors, and empty-state copy', () => {
    assert.match(viewBody, /queenzone:\/\/home/);
    assert.match(viewBody, /queenzone:\/\/quotes\/\$\{quoteId\}/);
    assert.match(viewBody, /queenzone:\/\/timeline\/\$\{eventId\}/);
    assert.match(viewBody, /queenzone:\/\/trivia/);
    assert.match(viewBody, /queenzone:\/\/timeline/);
    assert.match(viewBody, /props\.quoteId/);
    assert.match(viewBody, /props\.eventId/);
    assert.match(viewBody, /#181614/);
    assert.match(viewBody, /#B89A4A/);
    assert.match(viewBody, /#F2F1ED/);
    assert.match(viewBody, /#B8B6B0/);
    assert.match(viewBody, /Open QueenZone to load today's story\./);
    assert.match(viewBody, /ON THIS DAY/);
    assert.match(viewBody, /QUEEN QUOTES/);
    assert.match(viewBody, /QUEEN FACTS/);
    assert.match(viewBody, /props\.triviaText/);
    assert.match(viewBody, /4 \* 60 \* 60 \* 1000/);
    assert.equal(viewBody.includes('queenzone://trivia/'), false);
  });

  it('splits primary and secondary and uses the small-ceiling scale band', () => {
    assert.equal(viewBody.includes('lineLimit(3)'), false);
    assert.match(viewBody, /lineLimit\(6\)/);
    assert.match(viewBody, /lineLimit\(2\)/);
    assert.match(viewBody, /font\(\{ size: 17 \}\)/);
    assert.match(viewBody, /font\(\{ size: 9 \}\)/);
    assert.match(viewBody, /minimumScaleFactor\(0\.65\)/);
    assert.match(viewBody, /props\.summary/);
    assert.match(viewBody, /props\.formattedDate/);
    assert.match(viewBody, /“\$\{props\.quoteText\}”/);
    assert.match(viewBody, /— \$\{props\.quoteWhoSaid\}/);
    assert.equal(viewBody.includes('${props.formattedDate}: ${props.summary}'), false);
    assert.equal(viewBody.includes('” — ${props.quoteWhoSaid}'), false);
  });
});
