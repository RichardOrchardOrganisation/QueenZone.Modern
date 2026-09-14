import { getContentCache } from '../cache/defaultCache';
import type { ContentCache } from '../cache/contentCache';
import { fetchJsonWithOfflineCache } from '../cache/fetchCached';
import { forumTopicCacheKey, forumTopicPostsCacheKey } from '../cache/keys';
import { invalidateIncompatiblePostPages } from '../cache/pagedCache';
import { withOfflineCacheResult, type CachedResult, type OfflineCacheOptions } from '../cache/withOfflineCache';
import { reportApiFailure } from '../config/sentry';
import { fetchJson, sendJson, sendMultipart } from './client';
import type {
  ApiPagedResponse,
  ForumCategoryListItem,
  ForumIndexStats,
  ForumPoll,
  ForumPost,
  ForumPostCreated,
  ForumPostReportResponse,
  ForumPostModerationState,
  ForumRecentThread,
  ForumTopicCreated,
  ForumTopicDetail,
  ForumTopicListItem,
  ForumTopicWatch,
} from './types';
import type { PageQuery } from './content';
import { isLocalFileFailure } from './errors';
import { appendUploadFile, type UploadFilePart } from './uploadFile';

export type OfflineReadOptions = {
  cache?: ContentCache;
  /** Pull-to-refresh: write-through on success, never serve a cached snapshot. */
  networkOnly?: boolean;
};

export type { CachedResult };

export const forumPostReportCategories = [
  'Harassment or bullying',
  'Hate speech or discrimination',
  'Threats or violence',
  'Sexual or explicit content',
  'Spam or scams',
  'Copyright or ownership concern',
  'Other',
] as const;

export type ForumPostReportCategory = (typeof forumPostReportCategories)[number];

export function reportForumPost(
  accessToken: string,
  postId: number,
  category: ForumPostReportCategory,
  details: string,
): Promise<ForumPostReportResponse> {
  return sendJson<ForumPostReportResponse>(`/me/forum/posts/${postId}/report`, {
    method: 'POST', accessToken, body: { category, details: details.trim() || null },
  });
}

export function blockForumPostAuthor(accessToken: string, postId: number): Promise<void> {
  return sendJson(`/me/forum/posts/${postId}/block`, { method: 'POST', accessToken });
}

export function unblockForumPostAuthor(accessToken: string, postId: number): Promise<void> {
  return sendJson(`/me/forum/posts/${postId}/unblock`, { method: 'POST', accessToken });
}

export function fetchForumPostModerationState(
  accessToken: string,
  postIds: number[],
  authorMemberIds: string[],
): Promise<ForumPostModerationState> {
  return sendJson<ForumPostModerationState>('/me/forum/posts/moderation-state', {
    method: 'POST', accessToken, body: { postIds, authorMemberIds },
  });
}

function pageParams({ page, pageSize }: PageQuery) {
  return {
    page,
    pageSize,
  };
}

export type ForumRecentThreadsCacheHint = OfflineCacheOptions & { cacheKey: string };

/** Cross-board recent-activity feed, most-recent first. Used by the home screen. */
export function fetchForumRecentThreads(
  count: number,
  signal?: AbortSignal,
  cacheHint?: ForumRecentThreadsCacheHint,
): Promise<ForumRecentThread[]> {
  if (cacheHint) {
    const { cacheKey, ...cacheOptions } = cacheHint;
    return fetchJsonWithOfflineCache<ForumRecentThread[]>('/forum/recent-threads', {
      query: { count },
      signal,
      cacheKey,
      ...cacheOptions,
    });
  }
  return fetchJson('/forum/recent-threads', { query: { count }, signal });
}

/** Index totals. `threadCount` is the website `GetForumThreadCountAsync` value. */
export function fetchForumStats(signal?: AbortSignal): Promise<ForumIndexStats> {
  return fetchJson('/forum/stats', { signal });
}

export function fetchForumCategories(
  query: PageQuery = {},
): Promise<ApiPagedResponse<ForumCategoryListItem>> {
  return fetchJson('/forum/categories', { query: pageParams(query), signal: query.signal });
}

export function fetchForumCategory(
  id: number,
  signal?: AbortSignal,
): Promise<ForumCategoryListItem> {
  return fetchJson(`/forum/categories/${id}`, { signal });
}

export function fetchForumCategoryTopics(
  categoryId: number,
  query: PageQuery = {},
): Promise<ApiPagedResponse<ForumTopicListItem>> {
  return fetchJson(`/forum/categories/${categoryId}/topics`, {
    query: pageParams(query),
    signal: query.signal,
  });
}

export async function fetchForumTopicResult(
  id: number,
  signal?: AbortSignal,
  options: OfflineReadOptions = {},
): Promise<CachedResult<ForumTopicDetail>> {
  const cache = options.cache ?? getContentCache();
  return withOfflineCacheResult(
    cache,
    forumTopicCacheKey(id),
    () => fetchJson(`/forum/topics/${id}`, { signal }),
    { fallback: options.networkOnly !== true, invalidateOn: [404] },
  );
}

export async function fetchForumTopic(
  id: number,
  signal?: AbortSignal,
  options?: OfflineReadOptions,
): Promise<ForumTopicDetail> {
  return (await fetchForumTopicResult(id, signal, options)).data;
}

export async function fetchForumTopicPostsResult(
  topicId: number,
  query: PageQuery & OfflineReadOptions = {},
): Promise<CachedResult<ApiPagedResponse<ForumPost>>> {
  const page = query.page ?? 1;
  const cache = query.cache ?? getContentCache();
  return withOfflineCacheResult(
    cache,
    forumTopicPostsCacheKey(topicId, page),
    async () => {
      const data = await fetchJson<ApiPagedResponse<ForumPost>>(`/forum/topics/${topicId}/posts`, {
        query: pageParams(query),
        signal: query.signal,
      });
      if (page === 1) {
        await invalidateIncompatiblePostPages(cache, topicId, data);
      }
      return data;
    },
    { fallback: query.networkOnly !== true, invalidateOn: [404] },
  );
}

export async function fetchForumTopicPosts(
  topicId: number,
  query: PageQuery & OfflineReadOptions = {},
): Promise<ApiPagedResponse<ForumPost>> {
  return (await fetchForumTopicPostsResult(topicId, query)).data;
}

export type ForumTopicWrite = {
  title: string;
  body: string;
  file?: UploadFilePart;
};

export type ForumReplyWrite = {
  body: string;
  file?: UploadFilePart;
};

async function postForumWrite<T>(
  path: string,
  fields: Record<string, string>,
  file: UploadFilePart | undefined,
  accessToken: string,
  signal?: AbortSignal,
  idempotencyKey?: string,
): Promise<T> {
  if (!file) {
    return sendJson(path, {
      method: 'POST',
      body: fields,
      accessToken,
      signal,
      idempotencyKey,
    });
  }

  const form = new FormData();
  for (const [name, value] of Object.entries(fields)) {
    form.append(name, value);
  }

  try {
    await appendUploadFile(form, 'file', file, signal);
  } catch (err) {
    if (isLocalFileFailure(err)) {
      reportApiFailure({
        kind: err.kind,
        status: err.status,
        method: 'POST',
        path,
        cause: err.cause,
      });
    }
    throw err;
  }

  return sendMultipart(path, form, { accessToken, signal, idempotencyKey });
}

export function createForumTopic(
  categoryId: number,
  input: ForumTopicWrite,
  accessToken: string,
  signal?: AbortSignal,
  idempotencyKey?: string,
): Promise<ForumTopicCreated> {
  return postForumWrite(
    `/forum/categories/${categoryId}/topics`,
    { title: input.title, body: input.body },
    input.file,
    accessToken,
    signal,
    idempotencyKey,
  );
}

export function createForumReply(
  topicId: number,
  input: ForumReplyWrite,
  accessToken: string,
  signal?: AbortSignal,
  idempotencyKey?: string,
): Promise<ForumPostCreated> {
  return postForumWrite(
    `/forum/topics/${topicId}/posts`,
    { body: input.body },
    input.file,
    accessToken,
    signal,
    idempotencyKey,
  );
}

export function fetchForumTopicPoll(
  topicId: number,
  accessToken?: string | null,
  signal?: AbortSignal,
): Promise<ForumPoll> {
  return fetchJson(`/forum/topics/${topicId}/poll`, { accessToken, signal });
}

export function voteForumTopicPoll(
  topicId: number,
  optionIds: string[],
  accessToken: string,
  signal?: AbortSignal,
): Promise<ForumPoll> {
  return sendJson(`/forum/topics/${topicId}/poll/vote`, {
    method: 'POST',
    body: optionIds.length === 1 ? { optionId: optionIds[0] } : { optionIds },
    accessToken,
    signal,
  });
}

export function closeForumTopicPoll(
  topicId: number,
  accessToken: string,
  signal?: AbortSignal,
): Promise<ForumPoll> {
  return sendJson(`/forum/topics/${topicId}/poll/close`, {
    method: 'POST',
    accessToken,
    signal,
  });
}

export function fetchForumTopicWatch(
  topicId: number,
  accessToken: string,
  signal?: AbortSignal,
): Promise<ForumTopicWatch> {
  return fetchJson(`/forum/topics/${topicId}/watch`, { accessToken, signal });
}

export function watchForumTopic(
  topicId: number,
  accessToken: string,
  signal?: AbortSignal,
): Promise<ForumTopicWatch> {
  return sendJson(`/forum/topics/${topicId}/watch`, {
    method: 'POST',
    accessToken,
    signal,
  });
}

export function unwatchForumTopic(
  topicId: number,
  accessToken: string,
  signal?: AbortSignal,
): Promise<ForumTopicWatch> {
  return sendJson(`/forum/topics/${topicId}/watch`, {
    method: 'DELETE',
    accessToken,
    signal,
  });
}
