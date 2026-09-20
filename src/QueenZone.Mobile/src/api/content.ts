import { fetchJsonWithOfflineCache } from '../cache';
import type { OfflineCacheOptions } from '../cache';
import { fetchJson, sendJson } from './client';
import type {
  AlbumDetail,
  AlbumListItem,
  ApiPagedResponse,
  ArticleDetail,
  ArticleListItem,
  BiographyChapterDetail,
  BiographyChapterListItem,
  FreddieTribute,
  HomePoll,
  LiveActivitySummary,
  NewsDetail,
  NewsListItem,
  NewsYearRange,
  PhotoCategoryListItem,
  PhotoDetail,
  PhotoListItem,
  FanPerformance,
  QuizAnswerSubmission,
  QuizDetail,
  QuizLeaderboard,
  QuizListItem,
  QuizResult,
  QuizSprintAnswerCheck,
  QuizSprintBoard,
  QuizSprintClaimResult,
  QuizSprintDailyBoard,
  QuizSprintResult,
  QuizSprintRound,
  RandomQuote,
  RandomTrivia,
  TimelineEvent,
} from './types';

export type PageQuery = {
  page?: number;
  pageSize?: number;
  signal?: AbortSignal;
};

/**
 * Opt-in stale-while-revalidate hint (issue #1477). Only callers that pass a
 * `cacheKey` route through `withOfflineCache`; everyone else keeps the
 * existing network-only `fetchJson` call.
 */
export type CacheHint = OfflineCacheOptions & {
  cacheKey: string;
};

function pageParams({ page, pageSize }: PageQuery) {
  return {
    page,
    pageSize,
  };
}

export type NewsPageQuery = PageQuery & {
  /** First year of a 10-year span (e.g. 2010 for the 2010s). Server-side filter — see issue #838. */
  decade?: number;
  /** A single year (e.g. 2008). Server-side filter for the year-rail scrubber — see issue #886. Wins over `decade` if both are set. */
  year?: number;
  /** Stale-while-revalidate hint (issue #1477) — the home screen's hero rail. */
  cacheHint?: CacheHint;
};

export function fetchNewsPage(query: NewsPageQuery = {}): Promise<ApiPagedResponse<NewsListItem>> {
  const fetchOptions = {
    query: { ...pageParams(query), decade: query.decade, year: query.year },
    signal: query.signal,
  };
  if (query.cacheHint) {
    const { cacheKey, ...cacheOptions } = query.cacheHint;
    return fetchJsonWithOfflineCache<ApiPagedResponse<NewsListItem>>('/content/news', {
      ...fetchOptions,
      cacheKey,
      ...cacheOptions,
    });
  }
  return fetchJson('/content/news', fetchOptions);
}

/** Earliest/latest published years in the archive, for the year-rail scrubber's tick marks. */
export function fetchNewsYearRange(signal?: AbortSignal): Promise<NewsYearRange> {
  return fetchJson('/content/news/years', { signal });
}

/** Network-first; caches successful responses for offline re-open. */
export function fetchNewsDetail(id: number, signal?: AbortSignal): Promise<NewsDetail> {
  return fetchJsonWithOfflineCache(`/content/news/${id}`, {
    signal,
    cacheKey: `news:${id}`,
  });
}

export function fetchArticlesPage(
  query: PageQuery = {},
): Promise<ApiPagedResponse<ArticleListItem>> {
  return fetchJson('/content/articles', { query: pageParams(query), signal: query.signal });
}

/** Network-first; caches successful responses for offline re-open. */
export function fetchArticleDetail(id: number, signal?: AbortSignal): Promise<ArticleDetail> {
  return fetchJsonWithOfflineCache(`/content/articles/${id}`, {
    signal,
    cacheKey: `articles:${id}`,
  });
}

export function fetchBiographyPage(
  query: PageQuery = {},
): Promise<ApiPagedResponse<BiographyChapterListItem>> {
  return fetchJson('/content/biography', { query: pageParams(query), signal: query.signal });
}

/** Network-first; caches successful responses for offline re-open. */
export function fetchBiographyChapter(
  id: number,
  signal?: AbortSignal,
): Promise<BiographyChapterDetail> {
  return fetchJsonWithOfflineCache(`/content/biography/${id}`, {
    signal,
    cacheKey: `biography:${id}`,
  });
}

export function fetchDiscographyPage(
  query: PageQuery = {},
): Promise<ApiPagedResponse<AlbumListItem>> {
  return fetchJson('/content/discography', { query: pageParams(query), signal: query.signal });
}

/** Network-first; caches successful responses for offline re-open. */
export function fetchAlbumDetail(id: number, signal?: AbortSignal): Promise<AlbumDetail> {
  return fetchJsonWithOfflineCache(`/content/discography/${id}`, {
    signal,
    cacheKey: `discography:${id}`,
  });
}

export function fetchTimelinePage(
  query: PageQuery = {},
): Promise<ApiPagedResponse<TimelineEvent>> {
  return fetchJson('/content/timeline', { query: pageParams(query), signal: query.signal });
}

/** A published timeline event by id. 404 when missing or unpublished. */
export function fetchTimelineEventById(id: number, signal?: AbortSignal): Promise<TimelineEvent> {
  return fetchJson(`/content/timeline/${id}`, { signal });
}

/** The single most notable history event for today's date, or null when there is none. */
export function fetchOnThisDay(signal?: AbortSignal, cacheHint?: CacheHint): Promise<TimelineEvent | null> {
  if (cacheHint) {
    const { cacheKey, ...cacheOptions } = cacheHint;
    return fetchJsonWithOfflineCache<TimelineEvent | null>('/content/on-this-day', {
      signal,
      cacheKey,
      ...cacheOptions,
    });
  }
  return fetchJson('/content/on-this-day', { signal });
}

/**
 * Count of new forum replies posted today. No presence/reading tracking exists, so this is
 * the only honest live signal for the home screen's activity strip.
 */
export function fetchLiveActivity(
  signal?: AbortSignal,
  cacheHint?: CacheHint,
): Promise<LiveActivitySummary> {
  if (cacheHint) {
    const { cacheKey, ...cacheOptions } = cacheHint;
    return fetchJsonWithOfflineCache<LiveActivitySummary>('/content/live-activity', {
      signal,
      cacheKey,
      ...cacheOptions,
    });
  }
  return fetchJson('/content/live-activity', { signal });
}

/** A single random published quote, or null when none are published. */
export function fetchRandomQuote(signal?: AbortSignal, cacheHint?: CacheHint): Promise<RandomQuote | null> {
  if (cacheHint) {
    const { cacheKey, ...cacheOptions } = cacheHint;
    return fetchJsonWithOfflineCache<RandomQuote | null>('/content/quotes/random', {
      signal,
      cacheKey,
      ...cacheOptions,
    });
  }
  return fetchJson('/content/quotes/random', { signal });
}

/** A published quote by id. 404 when missing or unpublished. */
export function fetchQuoteById(id: number, signal?: AbortSignal): Promise<RandomQuote> {
  return fetchJson(`/content/quotes/${id}`, { signal });
}

/** A single random published trivia fact, or null when none are published. */
export function fetchRandomTrivia(signal?: AbortSignal): Promise<RandomTrivia | null> {
  return fetchJson('/content/trivia/random', { signal });
}

/**
 * The current Home poll, or null when none is live. Optional Bearer marks the
 * viewer's choice, so this is deliberately never cached with a TTL (issue
 * #1477) — a stale response would hide the viewer's own just-cast vote.
 */
export function fetchHomePoll(
  signal?: AbortSignal,
  accessToken?: string | null,
): Promise<HomePoll | null> {
  return fetchJson('/content/home-poll', { signal, accessToken });
}

/** Cast one ballot. Caller must refetch `fetchHomePoll` — do not optimistic-increment. */
export function voteHomePoll(
  optionId: string,
  accessToken: string,
  signal?: AbortSignal,
): Promise<HomePoll> {
  return sendJson('/content/home-poll/votes', {
    method: 'POST',
    body: { optionId },
    accessToken,
    signal,
  });
}

export function fetchQuizzesPage(query: PageQuery = {}): Promise<ApiPagedResponse<QuizListItem>> {
  return fetchJson('/content/quizzes', { query: pageParams(query), signal: query.signal });
}

/** Options only — the correct-answer flag is never sent to the client before submit. */
export function fetchQuizDetail(id: string, signal?: AbortSignal): Promise<QuizDetail> {
  return fetchJson(`/content/quizzes/${id}`, { signal });
}

/** Scores server-side and records the attempt. Caller must be signed in. */
export function submitQuizAttempt(
  id: string,
  answers: QuizAnswerSubmission[],
  accessToken: string,
  signal?: AbortSignal,
): Promise<QuizResult> {
  return sendJson(`/content/quizzes/${id}/attempts`, {
    method: 'POST',
    body: { answers },
    accessToken,
    signal,
  });
}

/**
 * Ranked by summed attempt score. Optional Bearer includes the viewer's own rank even
 * outside the top page.
 */
export function fetchQuizLeaderboard(
  scope: 'week' | 'all',
  signal?: AbortSignal,
  accessToken?: string | null,
): Promise<QuizLeaderboard> {
  return fetchJson('/content/quizzes/leaderboard', { query: { scope }, signal, accessToken });
}

/** Starts a 60-second Quiz Sprint round. Open to anonymous callers. */
export function startQuizSprint(signal?: AbortSignal): Promise<QuizSprintRound> {
  return sendJson('/content/quizzes/sprint/start', { method: 'POST', signal });
}

/** Reveals whether one pick was right while the round is live (the ticket keeps the key server-side). */
export function checkQuizSprintAnswer(
  ticket: string,
  questionId: string,
  optionId: string,
  signal?: AbortSignal,
): Promise<QuizSprintAnswerCheck> {
  return sendJson('/content/quizzes/sprint/answer', {
    method: 'POST',
    body: { ticket, questionId, optionId },
    signal,
  });
}

/**
 * Scores the round server-side. With a Bearer token the run is recorded on today's
 * leaderboard; without one it is scored but not recorded.
 */
export function finishQuizSprint(
  ticket: string,
  answers: QuizAnswerSubmission[],
  accessToken?: string | null,
  signal?: AbortSignal,
): Promise<QuizSprintResult> {
  return sendJson('/content/quizzes/sprint/finish', {
    method: 'POST',
    body: { ticket, answers },
    accessToken,
    signal,
  });
}

/**
 * Adds a guest's finished run to the signed-in member's record. Needs the `claimToken` from that
 * run's finish response; valid for one hour and claimable once (410 once expired).
 */
export function claimQuizSprintRun(
  claimToken: string,
  accessToken: string,
  signal?: AbortSignal,
): Promise<QuizSprintClaimResult> {
  return sendJson('/content/quizzes/sprint/claim', {
    method: 'POST',
    body: { claimToken },
    accessToken,
    signal,
  });
}

/** Today's Quiz Sprint standings. Optional Bearer includes the viewer's own entry. */
export function fetchQuizSprintDaily(
  signal?: AbortSignal,
  accessToken?: string | null,
): Promise<QuizSprintDailyBoard> {
  return fetchJson('/content/quizzes/sprint/daily', { signal, accessToken });
}

/**
 * Quiz Sprint standings: best run today (`daily`), best run ever (`all`), or points summed over
 * every run (`total`). Optional Bearer includes the viewer.
 */
export function fetchQuizSprintLeaderboard(
  scope: 'daily' | 'all' | 'total',
  signal?: AbortSignal,
  accessToken?: string | null,
): Promise<QuizSprintBoard> {
  return fetchJson('/content/quizzes/sprint/leaderboard', { query: { scope }, signal, accessToken });
}

export function fetchFreddieTributePage(
  query: PageQuery = {},
): Promise<ApiPagedResponse<FreddieTribute>> {
  return fetchJson('/content/freddietribute', { query: pageParams(query), signal: query.signal });
}

export function fetchFanPerformancesPage(
  query: PageQuery = {},
): Promise<ApiPagedResponse<FanPerformance>> {
  return fetchJson('/content/fan-performances', { query: pageParams(query), signal: query.signal });
}

/** Max page size the JSON API accepts (`ApiPagination.MaxPageSize`). */
const FAN_PERFORMANCE_CATALOG_PAGE_SIZE = 100;

/**
 * Full published catalog for Play All / Shuffle Play All.
 * Walks existing paged list GETs; does not use the list screen's loaded pages.
 */
export async function fetchAllFanPerformances(signal?: AbortSignal): Promise<FanPerformance[]> {
  const catalog: FanPerformance[] = [];
  let page = 1;
  let totalPages = 1;

  do {
    const response = await fetchFanPerformancesPage({
      page,
      pageSize: FAN_PERFORMANCE_CATALOG_PAGE_SIZE,
      signal,
    });
    catalog.push(...response.items);
    totalPages = Math.max(response.totalPages, 1);
    page += 1;
  } while (page <= totalPages);

  return catalog;
}

export function fetchFanPerformanceDetail(
  id: number,
  signal?: AbortSignal,
): Promise<FanPerformance> {
  return fetchJson(`/content/fan-performances/${id}`, { signal });
}

export type PhotoPageQuery = PageQuery & {
  size?: string;
};

export function fetchPhotoCategories(
  query: PageQuery & { cacheHint?: CacheHint } = {},
): Promise<ApiPagedResponse<PhotoCategoryListItem>> {
  const fetchOptions = { query: pageParams(query), signal: query.signal };
  if (query.cacheHint) {
    const { cacheKey, ...cacheOptions } = query.cacheHint;
    return fetchJsonWithOfflineCache<ApiPagedResponse<PhotoCategoryListItem>>(
      '/content/photos/categories',
      { ...fetchOptions, cacheKey, ...cacheOptions },
    );
  }
  return fetchJson('/content/photos/categories', fetchOptions);
}

export function fetchPhotoCategory(
  slug: string,
  signal?: AbortSignal,
): Promise<PhotoCategoryListItem> {
  return fetchJson(`/content/photos/categories/${encodeURIComponent(slug)}`, { signal });
}

export function fetchPhotoCategoryItems(
  slug: string,
  query: PhotoPageQuery = {},
): Promise<ApiPagedResponse<PhotoListItem>> {
  return fetchJson(`/content/photos/categories/${encodeURIComponent(slug)}/items`, {
    query: { ...pageParams(query), size: query.size },
    signal: query.signal,
  });
}

export function fetchPhotoDetail(
  slug: string,
  picId: number,
  query: { size?: string; signal?: AbortSignal } = {},
): Promise<PhotoDetail> {
  return fetchJson(`/content/photos/categories/${encodeURIComponent(slug)}/items/${picId}`, {
    query: { size: query.size },
    signal: query.signal,
  });
}
