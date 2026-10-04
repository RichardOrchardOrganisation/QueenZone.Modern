import { fetchJson } from './client';
import type { ApiPagedResponse, CrosswordDetail, CrosswordListItem } from './types';
import type { PageQuery } from './content';

export function fetchCrosswordsPage(query: PageQuery = {}): Promise<ApiPagedResponse<CrosswordListItem>> {
  return fetchJson('/crosswords', {
    query: { page: query.page, pageSize: query.pageSize },
    signal: query.signal,
  });
}

export function fetchCrosswordDetail(id: string, signal?: AbortSignal): Promise<CrosswordDetail> {
  return fetchJson(`/crosswords/${encodeURIComponent(id)}`, { signal });
}
