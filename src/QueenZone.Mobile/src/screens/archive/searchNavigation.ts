import type { SearchResult } from '../../api/types';
import { trimTrailingChar } from '../../text/trimRuns';

export function websiteUrl(apiBaseUrl: string, path: string): string | null {
  if (!path) {
    return null;
  }
  if (path.startsWith('http://') || path.startsWith('https://')) {
    return path;
  }
  const origin = trimTrailingChar(apiBaseUrl, '/');
  return `${origin}${path.startsWith('/') ? path : `/${path}`}`;
}

export type SearchTabTarget =
  | { kind: 'tab'; tab: 'NewsTab'; screen: 'Story'; params: { id: number } }
  | { kind: 'tab'; tab: 'ForumTab'; screen: 'Thread'; params: { id: number } }
  | { kind: 'tab'; tab: 'ArchiveTab'; screen: 'BiographyChapter'; params: { id: number } }
  | { kind: 'tab'; tab: 'ArchiveTab'; screen: 'Album'; params: { id: number } }
  | { kind: 'tab'; tab: 'ArchiveTab'; screen: 'Timeline'; params?: { focusId: number } }
  | { kind: 'tab'; tab: 'ArchiveTab'; screen: 'FanPerformanceDetail'; params: { id: number } };

export type SearchOpenTarget = SearchTabTarget | { kind: 'web'; url: string } | { kind: 'unsupported' };

function positiveId(value: number | null | undefined): number | null {
  if (value == null || !Number.isInteger(value) || value <= 0) {
    return null;
  }
  return value;
}

function tabOrWeb(item: SearchResult, apiBaseUrl: string, tab: SearchTabTarget | null): SearchOpenTarget {
  if (tab) {
    return tab;
  }
  const url = websiteUrl(apiBaseUrl, item.url);
  return url ? { kind: 'web', url } : { kind: 'unsupported' };
}

/** Maps a live search hit to a native reader, or the website URL when no reader exists. */
export function targetForSearchResult(item: SearchResult, apiBaseUrl: string): SearchOpenTarget {
  const contentType = item.contentType.trim().toLowerCase();
  const id = positiveId(item.id);

  if (contentType === 'news') {
    return tabOrWeb(
      item,
      apiBaseUrl,
      id ? { kind: 'tab', tab: 'NewsTab', screen: 'Story', params: { id } } : null,
    );
  }

  if (contentType === 'forum') {
    return tabOrWeb(
      item,
      apiBaseUrl,
      id ? { kind: 'tab', tab: 'ForumTab', screen: 'Thread', params: { id } } : null,
    );
  }

  if (contentType === 'biography') {
    return tabOrWeb(
      item,
      apiBaseUrl,
      id ? { kind: 'tab', tab: 'ArchiveTab', screen: 'BiographyChapter', params: { id } } : null,
    );
  }

  if (contentType === 'discography') {
    return tabOrWeb(
      item,
      apiBaseUrl,
      id ? { kind: 'tab', tab: 'ArchiveTab', screen: 'Album', params: { id } } : null,
    );
  }

  if (contentType === 'timeline') {
    return {
      kind: 'tab',
      tab: 'ArchiveTab',
      screen: 'Timeline',
      params: id ? { focusId: id } : undefined,
    };
  }

  if (contentType === 'fan-performance') {
    return tabOrWeb(
      item,
      apiBaseUrl,
      id ? { kind: 'tab', tab: 'ArchiveTab', screen: 'FanPerformanceDetail', params: { id } } : null,
    );
  }

  return tabOrWeb(item, apiBaseUrl, null);
}

type TabNavigate = (
  tab: SearchTabTarget['tab'],
  params: { screen: string; params?: object; initial?: boolean },
) => void;

/** Applies a mapped search target: tab navigation or in-app browser. */
export function applySearchTarget(
  target: SearchOpenTarget,
  navigate: TabNavigate,
  openUrl: (url: string) => void,
): void {
  if (target.kind === 'unsupported') {
    return;
  }
  if (target.kind === 'web') {
    openUrl(target.url);
    return;
  }
  navigate(
    target.tab,
    target.params
      ? { screen: target.screen, params: target.params, initial: false }
      : { screen: target.screen, initial: false },
  );
}
