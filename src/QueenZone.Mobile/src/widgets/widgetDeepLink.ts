import { trimChar } from '../text/trimRuns';
import type { WidgetFace } from './widgetCopy';

/** Opened by a quote face that has no usable id. */
export const widgetDeepLinkUrl = 'queenzone://home';

/** Day face with no usable event id — Timeline list, no expand. */
export const widgetTimelineListDeepLinkUrl = 'queenzone://timeline';

/** Trivia face — Archive Trivia screen. Screen may fetch a new random fact. */
export const widgetTriviaDeepLinkUrl = 'queenzone://trivia';

const widgetHomeHosts = new Set(['home', 'timeline']);

function widgetHost(parsed: URL): string {
  return parsed.hostname || trimChar(parsed.pathname, '/');
}

function widgetPathSegment(parsed: URL): string {
  return trimChar(parsed.pathname, '/').split('/')[0] ?? '';
}

export function widgetQuoteDeepLinkUrl(id: number): string {
  return `queenzone://quotes/${id}`;
}

export function widgetTimelineDeepLinkUrl(id: number): string {
  return `queenzone://timeline/${id}`;
}

/** Face-specific tap URL. Quote face with a real id opens that quote; day face opens the timeline event. */
export function widgetFaceDeepLinkUrl(face: WidgetFace | null, quoteId?: number, eventId?: number): string {
  if (face === 'trivia') {
    return widgetTriviaDeepLinkUrl;
  }
  if (face === 'quote') {
    if (quoteId != null && quoteId > 0) {
      return widgetQuoteDeepLinkUrl(quoteId);
    }
    return widgetDeepLinkUrl;
  }
  if (eventId != null && eventId > 0) {
    return widgetTimelineDeepLinkUrl(eventId);
  }
  return widgetTimelineListDeepLinkUrl;
}

export function parseWidgetQuoteId(url: string): number | null {
  try {
    const parsed = new URL(url);
    if (parsed.protocol !== 'queenzone:') {
      return null;
    }
    if (widgetHost(parsed) !== 'quotes') {
      return null;
    }
    const segment = widgetPathSegment(parsed);
    if (!/^\d+$/.test(segment)) {
      return null;
    }
    const id = Number(segment);
    return id > 0 ? id : null;
  } catch {
    return null;
  }
}

export function parseWidgetTimelineId(url: string): number | null {
  try {
    const parsed = new URL(url);
    if (parsed.protocol !== 'queenzone:') {
      return null;
    }
    if (widgetHost(parsed) !== 'timeline') {
      return null;
    }
    const segment = widgetPathSegment(parsed);
    if (!/^\d+$/.test(segment)) {
      return null;
    }
    const id = Number(segment);
    return id > 0 ? id : null;
  } catch {
    return null;
  }
}

function isWidgetTimelineUrl(url: string): boolean {
  try {
    const parsed = new URL(url);
    return parsed.protocol === 'queenzone:' && widgetHost(parsed) === 'timeline';
  } catch {
    return false;
  }
}

function isWidgetTriviaUrl(url: string): boolean {
  try {
    const parsed = new URL(url);
    return parsed.protocol === 'queenzone:' && widgetHost(parsed) === 'trivia';
  } catch {
    return false;
  }
}

/** True for Home, Timeline (list or by id), quote-by-id, and trivia (`queenzone://trivia`). */
export function isWidgetDeepLinkUrl(url: string): boolean {
  try {
    const parsed = new URL(url);
    if (parsed.protocol !== 'queenzone:') {
      return false;
    }
    const host = widgetHost(parsed);
    return widgetHomeHosts.has(host) || host === 'quotes' || host === 'trivia';
  } catch {
    return false;
  }
}

let consumedInitialWidgetUrl = false;

/**
 * Cold-start widget URL is handled once per process so a RootNavigator remount
 * cannot re-apply it. Later `url` events (tap while the app is running) are
 * not gated here. Smoke-auth and other schemes are never consumed.
 */
export function consumeInitialWidgetUrl(url: string | null): string | null {
  if (!url || !isWidgetDeepLinkUrl(url)) {
    return null;
  }
  if (consumedInitialWidgetUrl) {
    return null;
  }
  consumedInitialWidgetUrl = true;
  return url;
}

export function resetInitialWidgetUrlConsumption(): void {
  consumedInitialWidgetUrl = false;
}

export type WidgetHomeDestination = {
  screen: 'HomeTab';
  params: { screen: 'Home'; initial: false };
};

export type WidgetQuoteDestination = {
  screen: 'HomeTab';
  params: { screen: 'Quote'; params: { id: number }; initial: false };
};

export type WidgetTimelineDestination = {
  screen: 'ArchiveTab';
  params:
    | { screen: 'TimelineEvent'; params: { id: number }; initial: false }
    | { screen: 'Timeline'; initial: false };
};

export type WidgetTriviaDestination = {
  screen: 'ArchiveTab';
  params: { screen: 'Trivia'; initial: false };
};

export type WidgetNavigation = {
  navigate: (
    name: 'Tabs',
    params:
      | WidgetHomeDestination
      | WidgetQuoteDestination
      | WidgetTimelineDestination
      | WidgetTriviaDestination,
  ) => void;
};

export function openWidgetDestination(navigation: WidgetNavigation, url?: string): void {
  const quoteId = url ? parseWidgetQuoteId(url) : null;
  if (quoteId != null) {
    navigation.navigate('Tabs', {
      screen: 'HomeTab',
      params: { screen: 'Quote', params: { id: quoteId }, initial: false },
    });
    return;
  }

  if (url && isWidgetTimelineUrl(url)) {
    const eventId = parseWidgetTimelineId(url);
    navigation.navigate('Tabs', {
      screen: 'ArchiveTab',
      params:
        eventId != null
          ? { screen: 'TimelineEvent', params: { id: eventId }, initial: false }
          : { screen: 'Timeline', initial: false },
    });
    return;
  }

  if (url && isWidgetTriviaUrl(url)) {
    navigation.navigate('Tabs', {
      screen: 'ArchiveTab',
      params: { screen: 'Trivia', initial: false },
    });
    return;
  }

  navigation.navigate('Tabs', {
    screen: 'HomeTab',
    params: { screen: 'Home', initial: false },
  });
}
