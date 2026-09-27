import * as BackgroundTask from 'expo-background-task';
import * as TaskManager from 'expo-task-manager';
import { Platform } from 'react-native';
import { fetchOnThisDay, fetchRandomQuote, fetchRandomTrivia } from '../api/content';
import {
  HOME_WIDGET_BACKGROUND_TASK,
  WIDGET_BACKGROUND_MIN_INTERVAL_MINUTES,
  WIDGET_QUOTE_REFRESH_JITTER_MS,
  WIDGET_REFRESH_INTERVAL_MS,
  defineHomeWidgetBackgroundTask,
  isSameLocalCalendarDay,
  nextQuoteRefreshDelayMs,
  refreshHomeWidget,
  registerHomeWidgetBackgroundRefresh,
  registerIosHomeWidgetLayout,
  runHomeWidgetBackgroundRefresh,
  syncHomeWidget,
} from './widgetSync';
import {
  readCachedWidgetProps,
  readLastWidgetRefreshAt,
  writeCachedWidgetProps,
  writeLastWidgetRefreshAt,
} from './widgetCache';
import { nextWidgetFaceSlotMs } from './widgetCopy';

const mockUpdateSnapshot = jest.fn();
const mockUpdateTimeline = jest.fn();
const mockRequestWidgetUpdate = jest.fn().mockResolvedValue(undefined);

jest.mock('./widgetCache', () => ({
  writeCachedWidgetProps: jest.fn().mockResolvedValue(undefined),
  writeLastWidgetRefreshAt: jest.fn().mockResolvedValue(undefined),
  readLastWidgetRefreshAt: jest.fn().mockResolvedValue(null),
  readCachedWidgetProps: jest.fn().mockResolvedValue({}),
}));

jest.mock('./OnThisDayWidget.ios', () => ({
  OnThisDayWidget: {
    updateSnapshot: (...args: unknown[]) => mockUpdateSnapshot(...args),
    updateTimeline: (...args: unknown[]) => mockUpdateTimeline(...args),
  },
}));

jest.mock('react-native-android-widget', () => ({
  requestWidgetUpdate: (...args: unknown[]) => mockRequestWidgetUpdate(...args),
}));

jest.mock('./OnThisDayAndroidWidget', () => ({
  OnThisDayAndroidWidget: () => null,
}));

jest.mock('../api/content', () => ({
  fetchOnThisDay: jest.fn(),
  fetchRandomQuote: jest.fn(),
  fetchRandomTrivia: jest.fn(),
}));

const writeCached = writeCachedWidgetProps as jest.MockedFunction<typeof writeCachedWidgetProps>;
const writeRefreshAt = writeLastWidgetRefreshAt as jest.MockedFunction<typeof writeLastWidgetRefreshAt>;
const readRefreshAt = readLastWidgetRefreshAt as jest.MockedFunction<typeof readLastWidgetRefreshAt>;
const readCached = readCachedWidgetProps as jest.MockedFunction<typeof readCachedWidgetProps>;
const fetchDay = fetchOnThisDay as jest.MockedFunction<typeof fetchOnThisDay>;
const fetchQuote = fetchRandomQuote as jest.MockedFunction<typeof fetchRandomQuote>;
const fetchTrivia = fetchRandomTrivia as jest.MockedFunction<typeof fetchRandomTrivia>;
const defineTask = TaskManager.defineTask as jest.MockedFunction<typeof TaskManager.defineTask>;
const isTaskRegistered = TaskManager.isTaskRegisteredAsync as jest.MockedFunction<
  typeof TaskManager.isTaskRegisteredAsync
>;
const registerTask = BackgroundTask.registerTaskAsync as jest.MockedFunction<
  typeof BackgroundTask.registerTaskAsync
>;

const content = {
  onThisDay: {
    id: 1,
    title: 'The Game',
    summary: 'Queen released The Game.',
    eventDate: '1980-06-30',
    formattedDate: '30 June 1980',
    category: 'music',
    categoryLabel: 'Release',
    sourceUrl: null,
  },
  quote: { id: 9, text: 'A kind of magic', whoSaid: 'Freddie Mercury' },
};

const widgetProps = {
  formattedDate: '30 June 1980',
  summary: 'Queen released The Game.',
  quoteText: 'A kind of magic',
  quoteWhoSaid: 'Freddie Mercury',
  quoteId: 9,
  eventId: 1,
};

describe('syncHomeWidget', () => {
  const originalOs = Platform.OS;

  afterEach(() => {
    Object.defineProperty(Platform, 'OS', { value: originalOs });
    jest.restoreAllMocks();
    mockUpdateSnapshot.mockClear();
    mockUpdateTimeline.mockClear();
    mockRequestWidgetUpdate.mockClear();
    writeCached.mockClear();
    writeRefreshAt.mockClear();
    readRefreshAt.mockReset();
    readRefreshAt.mockResolvedValue(null);
    readCached.mockReset();
    readCached.mockResolvedValue({});
    fetchDay.mockReset();
    fetchQuote.mockReset();
    fetchTrivia.mockReset();
    fetchTrivia.mockResolvedValue(null);
    defineTask.mockClear();
    isTaskRegistered.mockReset();
    isTaskRegistered.mockResolvedValue(false);
    registerTask.mockReset();
    registerTask.mockResolvedValue(undefined);
  });

  it('schedules a 4-hour iOS timeline', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'ios' });
    const now = 1_700_000_000_000;
    jest.spyOn(Date, 'now').mockReturnValue(now);
    jest.spyOn(Math, 'random').mockReturnValue(0);

    await syncHomeWidget(content);

    expect(mockUpdateTimeline).toHaveBeenCalledWith([
      { date: new Date(now), props: widgetProps },
      { date: new Date(nextWidgetFaceSlotMs(now)), props: widgetProps },
      { date: new Date(now + WIDGET_REFRESH_INTERVAL_MS), props: widgetProps },
    ]);
    expect(mockUpdateSnapshot).not.toHaveBeenCalled();
    expect(mockRequestWidgetUpdate).not.toHaveBeenCalled();
    expect(writeRefreshAt).toHaveBeenCalledWith(now);
    jest.spyOn(Date, 'now').mockRestore();
    jest.spyOn(Math, 'random').mockRestore();
  });

  it('adds jitter so the iOS quote reload is not on the hour', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'ios' });
    const now = 1_700_000_000_000;
    jest.spyOn(Date, 'now').mockReturnValue(now);
    jest.spyOn(Math, 'random').mockReturnValue(0.5);

    await syncHomeWidget(content);

    expect(mockUpdateTimeline).toHaveBeenCalledWith([
      { date: new Date(now), props: widgetProps },
      { date: new Date(nextWidgetFaceSlotMs(now)), props: widgetProps },
      { date: new Date(now + WIDGET_REFRESH_INTERVAL_MS + WIDGET_QUOTE_REFRESH_JITTER_MS / 2), props: widgetProps },
    ]);
    jest.spyOn(Date, 'now').mockRestore();
    jest.spyOn(Math, 'random').mockRestore();
  });

  it('keeps last-good trivia when Home syncs only onThisDay and quote', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'android' });
    readCached.mockResolvedValue({
      triviaText: 'Freddie wrote Crazy Little Thing Called Love in minutes.',
      triviaId: 4,
    });

    await syncHomeWidget({
      onThisDay: content.onThisDay,
      quote: content.quote,
    });

    expect(writeCached).toHaveBeenCalledWith({
      ...widgetProps,
      triviaText: 'Freddie wrote Crazy Little Thing Called Love in minutes.',
      triviaId: 4,
    });
  });

  it('keeps cached eventId when Home syncs the same day copy without an id', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'android' });
    readCached.mockResolvedValue({
      formattedDate: '30 June 1980',
      summary: 'Queen released The Game.',
      eventId: 1,
    });

    await syncHomeWidget({
      onThisDay: { ...content.onThisDay, id: 0 },
      quote: content.quote,
    });

    expect(writeCached).toHaveBeenCalledWith(widgetProps);
  });

  it('omits eventId when the on-this-day row has no usable id', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'android' });
    await syncHomeWidget({
      onThisDay: { ...content.onThisDay, id: 0 },
      quote: content.quote,
    });
    expect(writeCached).toHaveBeenCalledWith({
      formattedDate: '30 June 1980',
      summary: 'Queen released The Game.',
      quoteText: 'A kind of magic',
      quoteWhoSaid: 'Freddie Mercury',
      quoteId: 9,
    });
  });

  it('caches props and requests an Android update', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'android' });
    await syncHomeWidget(content);
    expect(writeCached).toHaveBeenCalledWith(widgetProps);
    expect(mockRequestWidgetUpdate).toHaveBeenCalledWith(
      expect.objectContaining({ widgetName: 'OnThisDayWidget' }),
    );
    const renderWidget = mockRequestWidgetUpdate.mock.calls[0]?.[0]?.renderWidget as (
      info: { width: number; height: number },
    ) => unknown;
    expect(() => renderWidget({ width: 180, height: 110 })).not.toThrow();
    expect(mockUpdateTimeline).not.toHaveBeenCalled();
    expect(writeRefreshAt).toHaveBeenCalled();
  });
});

describe('refreshHomeWidget', () => {
  const originalOs = Platform.OS;

  afterEach(() => {
    Object.defineProperty(Platform, 'OS', { value: originalOs });
    jest.restoreAllMocks();
    mockUpdateTimeline.mockClear();
    mockRequestWidgetUpdate.mockClear();
    writeCached.mockClear();
    writeRefreshAt.mockClear();
    readRefreshAt.mockReset();
    readRefreshAt.mockResolvedValue(null);
    readCached.mockReset();
    readCached.mockResolvedValue({});
    fetchDay.mockReset();
    fetchQuote.mockReset();
    fetchTrivia.mockReset();
    fetchTrivia.mockResolvedValue(null);
  });

  it('fetches on-this-day plus a new quote and pushes on iOS', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'ios' });
    fetchDay.mockResolvedValue(content.onThisDay);
    fetchQuote.mockResolvedValue(content.quote);

    await expect(refreshHomeWidget()).resolves.toBe(true);

    expect(fetchDay).toHaveBeenCalledTimes(1);
    expect(fetchQuote).toHaveBeenCalledTimes(1);
    expect(fetchTrivia).toHaveBeenCalledTimes(1);
    expect(mockUpdateTimeline).toHaveBeenCalledWith(
      expect.arrayContaining([expect.objectContaining({ props: widgetProps })]),
    );
  });

  it('includes trivia in the snapshot when the random fetch returns a fact', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'android' });
    fetchDay.mockResolvedValue(content.onThisDay);
    fetchQuote.mockResolvedValue(content.quote);
    fetchTrivia.mockResolvedValue({
      id: 4,
      text: 'Freddie wrote Crazy Little Thing Called Love in minutes.',
    });

    await expect(refreshHomeWidget()).resolves.toBe(true);

    expect(writeCached).toHaveBeenCalledWith({
      ...widgetProps,
      triviaText: 'Freddie wrote Crazy Little Thing Called Love in minutes.',
      triviaId: 4,
    });
  });

  it('does not replace the last snapshot when both fetches fail', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'android' });
    fetchDay.mockRejectedValue(new Error('offline'));
    fetchQuote.mockRejectedValue(new Error('offline'));
    fetchTrivia.mockRejectedValue(new Error('offline'));

    await expect(refreshHomeWidget()).resolves.toBe(false);

    expect(writeCached).not.toHaveBeenCalled();
    expect(writeRefreshAt).not.toHaveBeenCalled();
    expect(mockRequestWidgetUpdate).not.toHaveBeenCalled();
  });

  it('restores the cached quote id so the next snapshot can deep-link', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'android' });
    readCached.mockResolvedValue({
      quoteText: 'A kind of magic',
      quoteWhoSaid: 'Freddie Mercury',
      quoteId: 9,
    });
    fetchDay.mockResolvedValue(content.onThisDay);
    fetchQuote.mockRejectedValue(new Error('offline'));

    await expect(refreshHomeWidget()).resolves.toBe(true);

    expect(writeCached).toHaveBeenCalledWith({
      formattedDate: '30 June 1980',
      summary: 'Queen released The Game.',
      quoteText: 'A kind of magic',
      quoteWhoSaid: 'Freddie Mercury',
      quoteId: 9,
      eventId: 1,
    });
  });

  it('restores the cached event id so the next snapshot can deep-link', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'android' });
    const now = 1_700_000_000_000;
    jest.spyOn(Date, 'now').mockReturnValue(now);
    readRefreshAt.mockResolvedValue(now - 4 * 60 * 60 * 1000);
    readCached.mockResolvedValue({
      formattedDate: '30 June 1980',
      summary: 'Queen released The Game.',
      eventId: 1,
      quoteText: 'A kind of magic',
      quoteWhoSaid: 'Freddie Mercury',
      quoteId: 9,
    });
    fetchQuote.mockResolvedValue({ id: 2, text: 'New quote', whoSaid: 'Brian May' });

    await expect(refreshHomeWidget()).resolves.toBe(true);

    expect(fetchDay).not.toHaveBeenCalled();
    expect(writeCached).toHaveBeenCalledWith({
      formattedDate: '30 June 1980',
      summary: 'Queen released The Game.',
      quoteText: 'New quote',
      quoteWhoSaid: 'Brian May',
      quoteId: 2,
      eventId: 1,
    });
    jest.spyOn(Date, 'now').mockRestore();
  });

  it('keeps the last good trivia when the trivia fetch fails', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'android' });
    readCached.mockResolvedValue({
      triviaText: 'Freddie wrote Crazy Little Thing Called Love in minutes.',
      triviaId: 4,
    });
    fetchDay.mockResolvedValue(content.onThisDay);
    fetchQuote.mockResolvedValue(content.quote);
    fetchTrivia.mockRejectedValue(new Error('offline'));

    await expect(refreshHomeWidget()).resolves.toBe(true);

    expect(writeCached).toHaveBeenCalledWith({
      ...widgetProps,
      triviaText: 'Freddie wrote Crazy Little Thing Called Love in minutes.',
      triviaId: 4,
    });
  });

  it('keeps the last good quote when the quote fetch fails', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'android' });
    readCached.mockResolvedValue({
      quoteText: 'A kind of magic',
      quoteWhoSaid: 'Freddie Mercury',
    });
    fetchDay.mockResolvedValue(content.onThisDay);
    fetchQuote.mockRejectedValue(new Error('offline'));

    await expect(refreshHomeWidget()).resolves.toBe(true);

    expect(writeCached).toHaveBeenCalledWith({
      formattedDate: '30 June 1980',
      summary: 'Queen released The Game.',
      quoteText: 'A kind of magic',
      quoteWhoSaid: 'Freddie Mercury',
      eventId: 1,
    });
    expect(mockRequestWidgetUpdate).toHaveBeenCalled();
  });

  it('skips on-this-day when the last refresh was the same calendar day', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'android' });
    const now = 1_700_000_000_000;
    jest.spyOn(Date, 'now').mockReturnValue(now);
    readRefreshAt.mockResolvedValue(now - 4 * 60 * 60 * 1000);
    readCached.mockResolvedValue(widgetProps);
    fetchQuote.mockResolvedValue({ id: 2, text: 'New quote', whoSaid: 'Brian May' });

    await expect(refreshHomeWidget()).resolves.toBe(true);

    expect(fetchDay).not.toHaveBeenCalled();
    expect(fetchQuote).toHaveBeenCalledTimes(1);
    expect(writeCached).toHaveBeenCalledWith({
      formattedDate: '30 June 1980',
      summary: 'Queen released The Game.',
      quoteText: 'New quote',
      quoteWhoSaid: 'Brian May',
      quoteId: 2,
      eventId: 1,
    });
    jest.spyOn(Date, 'now').mockRestore();
  });

  it('skips a fetch when the last successful push was recent', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'android' });
    readRefreshAt.mockResolvedValue(Date.now() - 60_000);

    await expect(refreshHomeWidget()).resolves.toBe(true);

    expect(fetchDay).not.toHaveBeenCalled();
    expect(fetchQuote).not.toHaveBeenCalled();
    expect(fetchTrivia).not.toHaveBeenCalled();
    expect(writeCached).not.toHaveBeenCalled();
  });

  it('does not throttle refreshHomeWidget after a null/error sync', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'android' });
    fetchDay.mockResolvedValue(content.onThisDay);
    fetchQuote.mockResolvedValue(content.quote);

    await syncHomeWidget({ onThisDay: null, quote: null });

    expect(writeRefreshAt).not.toHaveBeenCalled();

    await expect(refreshHomeWidget()).resolves.toBe(true);

    expect(fetchDay).toHaveBeenCalledTimes(1);
    expect(fetchQuote).toHaveBeenCalledTimes(1);
  });
});

describe('runHomeWidgetBackgroundRefresh', () => {
  afterEach(() => {
    fetchDay.mockReset();
    fetchQuote.mockReset();
    fetchTrivia.mockReset();
    fetchTrivia.mockResolvedValue(null);
    readRefreshAt.mockReset();
    readRefreshAt.mockResolvedValue(null);
    readCached.mockReset();
    readCached.mockResolvedValue({});
    writeCached.mockClear();
    writeRefreshAt.mockClear();
  });

  it('reports failure when the background fetch fails', async () => {
    fetchDay.mockRejectedValue(new Error('offline'));
    fetchQuote.mockRejectedValue(new Error('offline'));
    fetchTrivia.mockRejectedValue(new Error('offline'));

    await expect(runHomeWidgetBackgroundRefresh()).resolves.toBe(BackgroundTask.BackgroundTaskResult.Failed);
    expect(writeCached).not.toHaveBeenCalled();
  });

  it('reports success after a fresh fetch', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'android' });
    fetchDay.mockResolvedValue(content.onThisDay);
    fetchQuote.mockResolvedValue(content.quote);

    await expect(runHomeWidgetBackgroundRefresh()).resolves.toBe(BackgroundTask.BackgroundTaskResult.Success);
  });
});

describe('registerHomeWidgetBackgroundRefresh', () => {
  const originalOs = Platform.OS;

  afterEach(() => {
    Object.defineProperty(Platform, 'OS', { value: originalOs });
    isTaskRegistered.mockReset();
    isTaskRegistered.mockResolvedValue(false);
    registerTask.mockReset();
    registerTask.mockResolvedValue(undefined);
  });

  it('registers the iOS background task once', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'ios' });
    isTaskRegistered.mockResolvedValue(false);

    await registerHomeWidgetBackgroundRefresh();

    expect(registerTask).toHaveBeenCalledWith(HOME_WIDGET_BACKGROUND_TASK, {
      minimumInterval: WIDGET_BACKGROUND_MIN_INTERVAL_MINUTES,
    });
  });

  it('does not register again when the task is already scheduled', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'ios' });
    isTaskRegistered.mockResolvedValue(true);

    await registerHomeWidgetBackgroundRefresh();

    expect(registerTask).not.toHaveBeenCalled();
  });

  it('does not register on Android', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'android' });

    await registerHomeWidgetBackgroundRefresh();

    expect(isTaskRegistered).not.toHaveBeenCalled();
    expect(registerTask).not.toHaveBeenCalled();
  });

  it('swallows a restricted-OS registration failure', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'ios' });
    isTaskRegistered.mockRejectedValue(new Error('restricted'));

    await expect(registerHomeWidgetBackgroundRefresh()).resolves.toBeUndefined();
    expect(registerTask).not.toHaveBeenCalled();
  });

  it('defines the background task at the JS entry', async () => {
    Object.defineProperty(Platform, 'OS', { value: 'android' });
    fetchDay.mockResolvedValue(content.onThisDay);
    fetchQuote.mockResolvedValue(content.quote);
    readRefreshAt.mockResolvedValue(null);

    defineHomeWidgetBackgroundTask();
    expect(defineTask).toHaveBeenCalledWith(HOME_WIDGET_BACKGROUND_TASK, expect.any(Function));

    const executor = defineTask.mock.calls.at(-1)?.[1] as () => Promise<BackgroundTask.BackgroundTaskResult>;
    await expect(executor()).resolves.toBe(BackgroundTask.BackgroundTaskResult.Success);
  });
});

describe('registerIosHomeWidgetLayout', () => {
  const originalOs = Platform.OS;

  afterEach(() => {
    Object.defineProperty(Platform, 'OS', { value: originalOs });
    mockUpdateSnapshot.mockClear();
    mockUpdateTimeline.mockClear();
  });

  it('stores an empty iOS snapshot at launch so the gallery is not blank', () => {
    Object.defineProperty(Platform, 'OS', { value: 'ios' });

    registerIosHomeWidgetLayout();

    expect(mockUpdateSnapshot).toHaveBeenCalledWith({});
    expect(mockUpdateTimeline).not.toHaveBeenCalled();
  });

  it('does not load the iOS widget module on Android', () => {
    Object.defineProperty(Platform, 'OS', { value: 'android' });

    registerIosHomeWidgetLayout();

    expect(mockUpdateSnapshot).not.toHaveBeenCalled();
  });
});

describe('quote cadence helpers', () => {
  it('keeps the quote interval in hours and adds up to 30 minutes of jitter', () => {
    expect(nextQuoteRefreshDelayMs(() => 0)).toBe(WIDGET_REFRESH_INTERVAL_MS);
    expect(nextQuoteRefreshDelayMs(() => 1)).toBe(WIDGET_REFRESH_INTERVAL_MS + WIDGET_QUOTE_REFRESH_JITTER_MS);
    expect(WIDGET_REFRESH_INTERVAL_MS).toBe(4 * 60 * 60 * 1000);
    expect(WIDGET_QUOTE_REFRESH_JITTER_MS).toBe(30 * 60 * 1000);
  });

  it('treats midnight as a new local calendar day', () => {
    const late = Date.parse('2026-08-27T23:00:00');
    const nextMorning = Date.parse('2026-08-28T01:00:00');
    expect(isSameLocalCalendarDay(late, late)).toBe(true);
    expect(isSameLocalCalendarDay(late, nextMorning)).toBe(false);
  });
});
