import { fetchJson, sendJson } from './client';
import type {
  ApiPagedResponse, CrosswordDetail, CrosswordListItem, CrosswordSelection, CrosswordCheckResult,
  CrosswordRevealResult, CrosswordProgress, CrosswordProgressWrite, CrosswordCompletionResult,
} from './types';
import type { PageQuery } from './content';

export function fetchCrosswordsPage(query: PageQuery & { accessToken?: string | null; difficulty?: 'easy' | 'medium' | 'hard'; size?: 'small' | 'large' } = {}): Promise<ApiPagedResponse<CrosswordListItem>> {
  return fetchJson('/crosswords', {
    query: { page: query.page, pageSize: query.pageSize, difficulty: query.difficulty, size: query.size },
    signal: query.signal,
    accessToken: query.accessToken,
  });
}

type PlayOptions = { signal?: AbortSignal; accessToken?: string | null };
function path(id: string, action: string): string { return `/crosswords/${encodeURIComponent(id)}/${action}`; }

export function checkCrossword(id: string, playVersion: string, letters: string, selection: CrosswordSelection,
  autoCheck = false, options: PlayOptions = {}): Promise<CrosswordCheckResult> {
  return sendJson(path(id, 'check'), { ...options, body: { letters, selection, autoCheck, playVersion } });
}

export function revealCrossword(id: string, playVersion: string, selection: CrosswordSelection, options: PlayOptions = {}): Promise<CrosswordRevealResult> {
  return sendJson(path(id, 'reveal'), { ...options, body: { selection, playVersion } });
}

export function fetchCrosswordProgress(id: string, accessToken: string, signal?: AbortSignal): Promise<CrosswordProgress | undefined> {
  return fetchJson(path(id, 'progress'), { accessToken, signal });
}

export function saveCrosswordProgress(id: string, progress: CrosswordProgressWrite, accessToken: string,
  signal?: AbortSignal): Promise<CrosswordProgress> {
  return sendJson(path(id, 'progress'), { method: 'PUT', body: progress, accessToken, signal });
}

export function completeCrossword(id: string, progress: CrosswordProgressWrite, accessToken: string,
  signal?: AbortSignal): Promise<CrosswordCompletionResult> {
  return sendJson(path(id, 'complete'), { body: progress, accessToken, signal });
}

export function fetchCrosswordDetail(id: string, signal?: AbortSignal): Promise<CrosswordDetail> {
  return fetchJson(`/crosswords/${encodeURIComponent(id)}`, { signal });
}

export function fetchCrosswordBySlug(slug: string, signal?: AbortSignal): Promise<CrosswordDetail> {
  return fetchJson(`/crosswords/by-slug/${encodeURIComponent(slug)}`, { signal });
}
