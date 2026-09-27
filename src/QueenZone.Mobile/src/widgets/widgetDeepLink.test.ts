import assert from 'node:assert/strict';
import { beforeEach, describe, it } from 'node:test';
import {
  consumeInitialWidgetUrl,
  isWidgetDeepLinkUrl,
  openWidgetDestination,
  parseWidgetQuoteId,
  parseWidgetTimelineId,
  resetInitialWidgetUrlConsumption,
  widgetDeepLinkUrl,
  widgetFaceDeepLinkUrl,
  widgetQuoteDeepLinkUrl,
  widgetTimelineDeepLinkUrl,
  widgetTimelineListDeepLinkUrl,
  widgetTriviaDeepLinkUrl,
} from './widgetDeepLink.ts';

describe('widgetDeepLink', () => {
  beforeEach(() => {
    resetInitialWidgetUrlConsumption();
  });

  it('recognizes the widget URL by scheme and host', () => {
    assert.equal(isWidgetDeepLinkUrl(widgetDeepLinkUrl), true);
    assert.equal(isWidgetDeepLinkUrl('queenzone://home'), true);
    assert.equal(isWidgetDeepLinkUrl('queenzone://home/'), true);
    assert.equal(isWidgetDeepLinkUrl('queenzone://timeline'), true);
    assert.equal(isWidgetDeepLinkUrl('queenzone://timeline/'), true);
    assert.equal(isWidgetDeepLinkUrl('queenzone://timeline/12'), true);
    assert.equal(isWidgetDeepLinkUrl('queenzone://quotes/9'), true);
    assert.equal(isWidgetDeepLinkUrl('queenzone://quotes/9/'), true);
    assert.equal(isWidgetDeepLinkUrl('queenzone://trivia'), true);
    assert.equal(isWidgetDeepLinkUrl('queenzone://trivia/'), true);
  });

  it('rejects other schemes, hosts, and malformed input', () => {
    assert.equal(isWidgetDeepLinkUrl('https://queenzone.com/home'), false);
    assert.equal(isWidgetDeepLinkUrl('queenzone://forum'), false);
    assert.equal(isWidgetDeepLinkUrl('queenzone://smoke-auth?access_token=x'), false);
    assert.equal(isWidgetDeepLinkUrl('not a url'), false);
  });

  it('builds quote-face vs day-face URLs from the shown slot', () => {
    assert.equal(widgetFaceDeepLinkUrl('day', 9, 12), 'queenzone://timeline/12');
    assert.equal(widgetFaceDeepLinkUrl('day', 9), 'queenzone://timeline');
    assert.equal(widgetFaceDeepLinkUrl('day', 9, 0), 'queenzone://timeline');
    assert.equal(widgetFaceDeepLinkUrl('quote', 9, 12), 'queenzone://quotes/9');
    assert.equal(widgetQuoteDeepLinkUrl(9), 'queenzone://quotes/9');
    assert.equal(widgetTimelineDeepLinkUrl(12), 'queenzone://timeline/12');
    assert.equal(widgetTimelineListDeepLinkUrl, 'queenzone://timeline');
    assert.equal(widgetFaceDeepLinkUrl('quote', 0), 'queenzone://home');
    assert.equal(widgetFaceDeepLinkUrl('quote'), 'queenzone://home');
    assert.equal(widgetFaceDeepLinkUrl(null, 9, 12), 'queenzone://timeline/12');
    assert.equal(widgetFaceDeepLinkUrl(null, 9), 'queenzone://timeline');
    assert.equal(widgetFaceDeepLinkUrl('trivia', 9, 12), 'queenzone://trivia');
    assert.equal(widgetTriviaDeepLinkUrl, 'queenzone://trivia');
  });

  it('parses a positive integer quote id and rejects missing or invalid ids', () => {
    assert.equal(parseWidgetQuoteId('queenzone://quotes/9'), 9);
    assert.equal(parseWidgetQuoteId('queenzone://quotes/9/'), 9);
    assert.equal(parseWidgetQuoteId('queenzone://quotes/0'), null);
    assert.equal(parseWidgetQuoteId('queenzone://quotes/abc'), null);
    assert.equal(parseWidgetQuoteId('queenzone://quotes/'), null);
    assert.equal(parseWidgetQuoteId('queenzone://quotes'), null);
    assert.equal(parseWidgetQuoteId('queenzone://quotes/9///'), 9);
    assert.equal(parseWidgetQuoteId('queenzone://home'), null);
    assert.equal(parseWidgetQuoteId('queenzone://timeline/12'), null);
  });

  it('parses a positive integer timeline id and rejects missing or invalid ids', () => {
    assert.equal(parseWidgetTimelineId('queenzone://timeline/12'), 12);
    assert.equal(parseWidgetTimelineId('queenzone://timeline/12/'), 12);
    assert.equal(parseWidgetTimelineId('queenzone://timeline/0'), null);
    assert.equal(parseWidgetTimelineId('queenzone://timeline/abc'), null);
    assert.equal(parseWidgetTimelineId('queenzone://timeline/'), null);
    assert.equal(parseWidgetTimelineId('queenzone://timeline'), null);
    assert.equal(parseWidgetTimelineId('queenzone://home'), null);
    assert.equal(parseWidgetTimelineId('queenzone://quotes/9'), null);
    assert.equal(parseWidgetTimelineId('queenzone://timeline/12///'), 12);
  });

  it('finishes quickly when the path is a long slash run', () => {
    const started = performance.now();
    assert.equal(isWidgetDeepLinkUrl(`queenzone://home${'/'.repeat(40_000)}`), true);
    assert.equal(parseWidgetQuoteId(`queenzone://quotes/9${'/'.repeat(40_000)}`), 9);
    assert.ok(performance.now() - started < 100);
  });

  it('navigates through the Tabs root into HomeTab/Home', () => {
    const navigate = (name: string, params: unknown) => {
      assert.equal(name, 'Tabs');
      assert.deepEqual(params, {
        screen: 'HomeTab',
        params: { screen: 'Home', initial: false },
      });
    };
    openWidgetDestination({ navigate: navigate as never }, 'queenzone://home');
  });

  it('navigates a quote URL onto the Home stack Quote screen', () => {
    const navigate = (name: string, params: unknown) => {
      assert.equal(name, 'Tabs');
      assert.deepEqual(params, {
        screen: 'HomeTab',
        params: { screen: 'Quote', params: { id: 9 }, initial: false },
      });
    };
    openWidgetDestination({ navigate: navigate as never }, 'queenzone://quotes/9');
  });

  it('navigates a day-face URL onto Archive TimelineEvent by id', () => {
    const navigate = (name: string, params: unknown) => {
      assert.equal(name, 'Tabs');
      assert.deepEqual(params, {
        screen: 'ArchiveTab',
        params: { screen: 'TimelineEvent', params: { id: 12 }, initial: false },
      });
    };
    openWidgetDestination({ navigate: navigate as never }, 'queenzone://timeline/12');
  });

  it('navigates a deep off-page day-face id onto TimelineEvent, not list focusId', () => {
    const navigate = (name: string, params: unknown) => {
      assert.equal(name, 'Tabs');
      assert.deepEqual(params, {
        screen: 'ArchiveTab',
        params: { screen: 'TimelineEvent', params: { id: 9999 }, initial: false },
      });
    };
    openWidgetDestination({ navigate: navigate as never }, 'queenzone://timeline/9999');
  });

  it('navigates a trivia URL onto Archive Trivia', () => {
    const navigate = (name: string, params: unknown) => {
      assert.equal(name, 'Tabs');
      assert.deepEqual(params, {
        screen: 'ArchiveTab',
        params: { screen: 'Trivia', initial: false },
      });
    };
    openWidgetDestination({ navigate: navigate as never }, 'queenzone://trivia');
  });

  it('navigates a no-id day-face URL onto Timeline without focus params', () => {
    const destinations: unknown[] = [];
    const navigate = (_name: string, params: unknown) => {
      destinations.push(params);
    };
    openWidgetDestination({ navigate: navigate as never }, 'queenzone://timeline');
    openWidgetDestination({ navigate: navigate as never }, 'queenzone://timeline/0');
    openWidgetDestination({ navigate: navigate as never }, 'queenzone://timeline/abc');
    assert.deepEqual(destinations, [
      { screen: 'ArchiveTab', params: { screen: 'Timeline', initial: false } },
      { screen: 'ArchiveTab', params: { screen: 'Timeline', initial: false } },
      { screen: 'ArchiveTab', params: { screen: 'Timeline', initial: false } },
    ]);
  });

  it('falls back to Home when the quote id is missing or not an integer', () => {
    const destinations: unknown[] = [];
    const navigate = (_name: string, params: unknown) => {
      destinations.push(params);
    };
    openWidgetDestination({ navigate: navigate as never }, 'queenzone://quotes/abc');
    openWidgetDestination({ navigate: navigate as never }, 'queenzone://quotes/0');
    assert.deepEqual(destinations, [
      { screen: 'HomeTab', params: { screen: 'Home', initial: false } },
      { screen: 'HomeTab', params: { screen: 'Home', initial: false } },
    ]);
  });

  it('consumes the launch widget URL once and leaves other schemes alone', () => {
    assert.equal(consumeInitialWidgetUrl('queenzone://smoke-auth?access_token=x'), null);
    assert.equal(consumeInitialWidgetUrl('queenzone://timeline/12'), 'queenzone://timeline/12');
    assert.equal(consumeInitialWidgetUrl('queenzone://timeline/12'), null);
    assert.equal(consumeInitialWidgetUrl('queenzone://quotes/9'), null);
    assert.equal(consumeInitialWidgetUrl('queenzone://home'), null);
    resetInitialWidgetUrlConsumption();
    assert.equal(consumeInitialWidgetUrl('queenzone://trivia'), 'queenzone://trivia');
    assert.equal(consumeInitialWidgetUrl('queenzone://trivia'), null);
  });
});
