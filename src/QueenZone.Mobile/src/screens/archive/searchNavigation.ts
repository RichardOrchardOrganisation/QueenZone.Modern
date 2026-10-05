import type { SearchResult } from '../../api/types';
import { trimTrailingChar } from '../../text/trimRuns.ts';

export function websiteUrl(apiBaseUrl: string, path: string): string | null {
  if (!path) {
    return null;
  }
  if (path.startsWith('http://') || path.startsWith('https://')) {
    return path;
  }
  const origin = trimTrailingChar(apiBaseUrl, '/');
  const relativePath = path.startsWith('/') ? path : '/' + path;
  return `${origin}${relativePath}`;
}

export type SearchTabTarget =
  | { kind: 'tab'; tab: 'NewsTab'; screen: 'Story'; params: { id: number } }
  | { kind: 'tab'; tab: 'ForumTab'; screen: 'Thread'; params: { id: number } }
  | { kind: 'tab'; tab: 'ArchiveTab'; screen: 'BiographyChapter'; params: { id: number } }
  | { kind: 'tab'; tab: 'ArchiveTab'; screen: 'Album'; params: { id: number } }
  | { kind: 'tab'; tab: 'ArchiveTab'; screen: 'Song'; params: { slug: string } }
  | { kind: 'tab'; tab: 'ArchiveTab'; screen: 'Timeline'; params?: { focusId: number } }
  | { kind: 'tab'; tab: 'ArchiveTab'; screen: 'FanPerformanceDetail'; params: { id: number } };

export type SearchOpenTarget = SearchTabTarget | { kind: 'web'; url: string } | { kind: 'unsupported' };

function songSlugFromSourceKey(sourceKey: string): string | null {
  const prefix = 'song:';
  if (!sourceKey.toLowerCase().startsWith(prefix)) {
    return null;
  }
  const slug = sourceKey.slice(prefix.length).trim();
  return slug.length > 0 ? slug : null;
}

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

type IdSearchRoute<T = SearchTabTarget> = T extends { params: { id: number } }
  ? Omit<T, 'kind' | 'params'>
  : never;

const idSearchReaders: ReadonlyMap<string, IdSearchRoute> = new Map([
  ['news', { tab: 'NewsTab', screen: 'Story' }],
  ['forum', { tab: 'ForumTab', screen: 'Thread' }],
  ['biography', { tab: 'ArchiveTab', screen: 'BiographyChapter' }],
  ['discography', { tab: 'ArchiveTab', screen: 'Album' }],
  ['fan-performance', { tab: 'ArchiveTab', screen: 'FanPerformanceDetail' }],
]);

/** Maps a live search hit to a native reader, or the website URL when no reader exists. */
export function targetForSearchResult(item: SearchResult, apiBaseUrl: string): SearchOpenTarget {
  const contentType = item.contentType.trim().toLowerCase();
  const id = positiveId(item.id);

  const reader = idSearchReaders.get(contentType);
  if (reader) {
    return tabOrWeb(item, apiBaseUrl, id ? { kind: 'tab', ...reader, params: { id } } : null);
  }

  switch (contentType) {
    case 'song': {
      const slug = songSlugFromSourceKey(item.sourceKey);
      return tabOrWeb(
        item,
        apiBaseUrl,
        slug ? { kind: 'tab', tab: 'ArchiveTab', screen: 'Song', params: { slug } } : null,
      );
    }

    case 'timeline': {
      return {
        kind: 'tab',
        tab: 'ArchiveTab',
        screen: 'Timeline',
        params: id ? { focusId: id } : undefined,
      };
    }

    default:
      return tabOrWeb(item, apiBaseUrl, null);
  }
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
