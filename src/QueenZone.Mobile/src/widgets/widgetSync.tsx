import * as BackgroundTask from 'expo-background-task';
import * as TaskManager from 'expo-task-manager';
import { Platform } from 'react-native';
import { fetchOnThisDay, fetchRandomQuote, fetchRandomTrivia } from '../api/content';
import type { RandomQuote, RandomTrivia, TimelineEvent } from '../api/types';
import type { OnThisDayAndroidWidgetProps } from './OnThisDayAndroidWidget';
import {
  readCachedWidgetProps,
  readLastWidgetRefreshAt,
  writeCachedWidgetProps,
  writeLastWidgetRefreshAt,
} from './widgetCache';
import { nextWidgetFaceSlotMs } from './widgetCopy';

export const HOME_WIDGET_BACKGROUND_TASK = 'queenzone-home-widget-refresh';

/** Android `updatePeriodMillis` and the iOS quote timeline hold. Hours, not real-time. */
export const WIDGET_REFRESH_INTERVAL_MS = 4 * 60 * 60 * 1000;

/** Skip a quote fetch when the last successful push was this recent. */
export const WIDGET_REFRESH_MIN_INTERVAL_MS = 3 * 60 * 60 * 1000;

/** Spread `/quotes/random` hits so devices do not all refresh on the hour. */
export const WIDGET_QUOTE_REFRESH_JITTER_MS = 30 * 60 * 1000;

/** iOS BGTaskScheduler minimum interval, in minutes. Matches the 4-hour Android period. */
export const WIDGET_BACKGROUND_MIN_INTERVAL_MINUTES = 240;

export type WidgetContent = {
  onThisDay: TimelineEvent | null;
  quote: RandomQuote | null;
  /** Omit to keep last-good trivia (HomeScreen sync). Explicit `null` clears the face. */
  trivia?: RandomTrivia | null;
};

export function nextQuoteRefreshDelayMs(random: () => number = Math.random): number {
  const unit = Math.min(Math.max(random(), 0), 1);
  return WIDGET_REFRESH_INTERVAL_MS + Math.floor(unit * WIDGET_QUOTE_REFRESH_JITTER_MS);
}

export function isSameLocalCalendarDay(leftMs: number, rightMs: number): boolean {
  const left = new Date(leftMs);
  const right = new Date(rightMs);
  return (
    left.getFullYear() === right.getFullYear() &&
    left.getMonth() === right.getMonth() &&
    left.getDate() === right.getDate()
  );
}

function toWidgetProps(content: WidgetContent): OnThisDayAndroidWidgetProps {
  const quoteId = content.quote && content.quote.id > 0 ? content.quote.id : undefined;
  const eventId = content.onThisDay && content.onThisDay.id > 0 ? content.onThisDay.id : undefined;
  const triviaId = content.trivia && content.trivia.id > 0 ? content.trivia.id : undefined;
  return {
    formattedDate: content.onThisDay?.formattedDate,
    summary: content.onThisDay?.summary,
    quoteText: content.quote?.text,
    quoteWhoSaid: content.quote?.whoSaid,
    ...(content.trivia?.text ? { triviaText: content.trivia.text } : {}),
    ...(quoteId != null ? { quoteId } : {}),
    ...(eventId != null ? { eventId } : {}),
    ...(triviaId != null ? { triviaId } : {}),
  };
}

function quoteFromProps(props: OnThisDayAndroidWidgetProps): RandomQuote | null {
  if (!props.quoteText || !props.quoteWhoSaid) {
    return null;
  }
  const quoteId = Number(props.quoteId);
  return {
    id: Number.isInteger(quoteId) && quoteId > 0 ? quoteId : 0,
    text: props.quoteText,
    whoSaid: props.quoteWhoSaid,
  };
}

function dayFromProps(props: OnThisDayAndroidWidgetProps): TimelineEvent | null {
  if (!props.formattedDate || !props.summary) {
    return null;
  }
  const eventId = Number(props.eventId);
  return {
    id: Number.isInteger(eventId) && eventId > 0 ? eventId : 0,
    title: '',
    summary: props.summary,
    eventDate: '',
    formattedDate: props.formattedDate,
    category: '',
    categoryLabel: '',
    sourceUrl: null,
  };
}

/** Same class as trivia wipe: keep App Group eventId when day copy is present. */
function withCachedEventId(
  onThisDay: TimelineEvent | null,
  cached: OnThisDayAndroidWidgetProps,
): TimelineEvent | null {
  if (onThisDay == null || onThisDay.id > 0) {
    return onThisDay;
  }
  const cachedDay = dayFromProps(cached);
  if (
    cachedDay &&
    cachedDay.id > 0 &&
    cachedDay.formattedDate === onThisDay.formattedDate &&
    cachedDay.summary === onThisDay.summary
  ) {
    return { ...onThisDay, id: cachedDay.id };
  }
  return onThisDay;
}

function triviaFromProps(props: OnThisDayAndroidWidgetProps): RandomTrivia | null {
  if (!props.triviaText) {
    return null;
  }
  const triviaId = Number(props.triviaId);
  return {
    id: Number.isInteger(triviaId) && triviaId > 0 ? triviaId : 0,
    text: props.triviaText,
  };
}

function iosTimelineEntries(props: OnThisDayAndroidWidgetProps) {
  const now = Date.now();
  const times = new Set([now, nextWidgetFaceSlotMs(now), now + nextQuoteRefreshDelayMs()]);
  return [...times]
    .sort((left, right) => left - right)
    .map((ms) => ({ date: new Date(ms), props }));
}

function hasWidgetContent(content: WidgetContent): boolean {
  return content.onThisDay != null || content.quote != null || content.trivia != null;
}

/**
 * Pushes on-this-day + quote content to the OS widget. HomeScreen calls this after its
 * own fetches settle. Background refresh uses {@link refreshHomeWidget} instead.
 * The throttle timestamp is written only when the payload has content — a Home error
 * view (`{ onThisDay: null, quote: null }`) must not skip the next overnight fetch.
 */
export async function syncHomeWidget(content: WidgetContent): Promise<void> {
  const cached = await readCachedWidgetProps();
  const trivia = content.trivia !== undefined ? content.trivia : triviaFromProps(cached);
  const onThisDay = withCachedEventId(content.onThisDay, cached);
  const props = toWidgetProps({ ...content, onThisDay, trivia });
  await writeCachedWidgetProps(props);

  if (Platform.OS === 'ios') {
    // eslint-disable-next-line @typescript-eslint/no-require-imports -- platform-gated: this module must not load on Android, where expo-widgets has no native counterpart.
    const { OnThisDayWidget } = require('./OnThisDayWidget.ios') as typeof import('./OnThisDayWidget.ios');
    OnThisDayWidget.updateTimeline(iosTimelineEntries(props));
    if (hasWidgetContent(content)) {
      await writeLastWidgetRefreshAt(Date.now());
    }
    return;
  }

  if (Platform.OS === 'android') {
    // eslint-disable-next-line @typescript-eslint/no-require-imports -- lazy so iOS bundles never touch the Android-only widget module.
    const { requestWidgetUpdate } = require('react-native-android-widget') as typeof import('react-native-android-widget');
    // eslint-disable-next-line @typescript-eslint/no-require-imports -- platform-gated: Android widget component must not load on iOS.
    const { OnThisDayAndroidWidget } = require('./OnThisDayAndroidWidget') as typeof import('./OnThisDayAndroidWidget');
    await requestWidgetUpdate({
      widgetName: 'OnThisDayWidget',
      renderWidget: (info) => <OnThisDayAndroidWidget {...props} widgetWidth={info.width} />,
    });
    if (hasWidgetContent(content)) {
      await writeLastWidgetRefreshAt(Date.now());
    }
  }
}

async function shouldSkipBackgroundRefresh(): Promise<boolean> {
  const last = await readLastWidgetRefreshAt();
  if (last == null) {
    return false;
  }
  return Date.now() - last < WIDGET_REFRESH_MIN_INTERVAL_MS;
}

function shouldFetchOnThisDay(lastRefreshAt: number | null, now: number): boolean {
  return lastRefreshAt == null || !isSameLocalCalendarDay(lastRefreshAt, now);
}

/**
 * Fetches a new random quote and trivia (and on-this-day when the calendar day
 * changed) and pushes them to the widget. A failed quote or trivia fetch keeps
 * the last good value. Returns false when nothing new could be fetched so the
 * last snapshot stays up.
 */
export async function refreshHomeWidget(): Promise<boolean> {
  if (await shouldSkipBackgroundRefresh()) {
    return true;
  }

  const cached = await readCachedWidgetProps();
  let quote = quoteFromProps(cached);
  let onThisDay = dayFromProps(cached);
  let trivia = triviaFromProps(cached);
  const dayDue = shouldFetchOnThisDay(await readLastWidgetRefreshAt(), Date.now());

  let quoteFetched = false;
  let dayFetched = false;
  let triviaFetched = false;

  const quoteTask = fetchRandomQuote()
    .then((fresh) => {
      if (fresh) {
        quote = fresh;
      }
      quoteFetched = true;
    })
    .catch(() => {
      /* last good quote stays */
    });

  const triviaTask = fetchRandomTrivia()
    .then((fresh) => {
      if (fresh) {
        trivia = fresh;
      }
      triviaFetched = true;
    })
    .catch(() => {
      /* last good trivia stays */
    });

  const dayTask = dayDue
    ? fetchOnThisDay()
        .then((fresh) => {
          onThisDay = fresh;
          dayFetched = true;
        })
        .catch(() => {
          /* last good on-this-day stays */
        })
    : Promise.resolve();

  await Promise.all([quoteTask, triviaTask, dayTask]);

  if (!quoteFetched && !dayFetched && !triviaFetched) {
    return false;
  }

  await syncHomeWidget({ onThisDay, quote, trivia });
  return true;
}

export async function runHomeWidgetBackgroundRefresh(): Promise<BackgroundTask.BackgroundTaskResult> {
  const ok = await refreshHomeWidget();
  return ok ? BackgroundTask.BackgroundTaskResult.Success : BackgroundTask.BackgroundTaskResult.Failed;
}

/** Must run from the JS entry so a background launch can find the task. */
export function defineHomeWidgetBackgroundTask(): void {
  TaskManager.defineTask(HOME_WIDGET_BACKGROUND_TASK, () => runHomeWidgetBackgroundRefresh());
}

/**
 * Stores the iOS layout in the app group as soon as JS starts, then pushes an
 * empty snapshot so the gallery and home screen can render the empty-state copy
 * before Home finishes fetching.
 */
export function registerIosHomeWidgetLayout(): void {
  if (Platform.OS !== 'ios') {
    return;
  }

  // eslint-disable-next-line @typescript-eslint/no-require-imports -- platform-gated: this module must not load on Android.
  const { OnThisDayWidget } = require('./OnThisDayWidget.ios') as typeof import('./OnThisDayWidget.ios');
  OnThisDayWidget.updateSnapshot({});
}

/** iOS-only. Android already refreshes from the widget task's 4-hour period. */
export async function registerHomeWidgetBackgroundRefresh(): Promise<void> {
  if (Platform.OS !== 'ios') {
    return;
  }

  try {
    const registered = await TaskManager.isTaskRegisteredAsync(HOME_WIDGET_BACKGROUND_TASK);
    if (registered) {
      return;
    }
    await BackgroundTask.registerTaskAsync(HOME_WIDGET_BACKGROUND_TASK, {
      minimumInterval: WIDGET_BACKGROUND_MIN_INTERVAL_MINUTES,
    });
  } catch {
    /* Simulator / restricted OS — last snapshot stays until a run is granted. */
  }
}
