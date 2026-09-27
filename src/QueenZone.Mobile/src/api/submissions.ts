/** Member `/api/v1/me/submissions/*` contract (issue #745). Matches website `/account/my-submissions`. */

import { trimTrailingChar } from '../text/trimRuns.ts';


export const submissionKinds = ['photos', 'news', 'articles', 'fan-performances'] as const;

export type SubmissionKind = (typeof submissionKinds)[number];

export type SubmissionStatusTone = 'pending' | 'review' | 'attention' | 'success' | 'danger' | 'neutral';

export type SubmissionStatus = {
  status: string;
  statusLabel: string;
  statusTone: SubmissionStatusTone;
};

export type PhotoSubmissionItem = {
  id: string;
  title: string;
  submittedAt: string;
  status: SubmissionStatus;
  notes: string | null;
  thumbnailPath: string | null;
  promotedPicId: number | null;
};

export type NewsSuggestionItem = {
  id: string;
  url: string;
  truncatedUrl: string;
  title: string | null;
  submittedAt: string;
  status: SubmissionStatus;
  notes: string | null;
  publishedNewsId: number | null;
  publishedPath: string | null;
};

export type FanPerformanceSubmissionItem = {
  id: string;
  title: string;
  coveredSong: string;
  performedBy: string;
  submittedAt: string;
  status: SubmissionStatus;
  notes: string | null;
  rejectionReason: string | null;
  promotedStageId: number | null;
  publishedPath: string | null;
};

export type ArticleSubmissionItem = {
  id: string;
  title: string;
  submittedAt: string | null;
  status: SubmissionStatus;
  notes: string | null;
  canContinueEditing: boolean;
  editPath: string | null;
  publishedPath: string | null;
};

export type PagedSubmissions<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export function submissionsApiUrl(
  apiBaseUrl: string,
  kind: SubmissionKind,
  page = 1,
  pageSize = 20,
): string {
  const origin = trimTrailingChar(apiBaseUrl, '/');
  return `${origin}/api/v1/me/submissions/${kind}?page=${page}&pageSize=${pageSize}`;
}

export function memberAuthHeaders(accessToken: string | null | undefined): Record<string, string> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (accessToken) {
    headers.Authorization = `Bearer ${accessToken}`;
  }

  return headers;
}

export function resolveMediaUrl(apiBaseUrl: string, path: string | null): string | null {
  if (!path) {
    return null;
  }

  if (path.startsWith('http://') || path.startsWith('https://')) {
    return path;
  }

  const origin = trimTrailingChar(apiBaseUrl, '/');
  return `${origin}${path.startsWith('/') ? path : `/${path}`}`;
}

export function parsePhotoSubmissions(payload: unknown): PagedSubmissions<PhotoSubmissionItem> {
  return parsePaged(payload, parsePhotoItem);
}

export function parseNewsSuggestions(payload: unknown): PagedSubmissions<NewsSuggestionItem> {
  return parsePaged(payload, parseNewsItem);
}

export function parseArticleSubmissions(payload: unknown): PagedSubmissions<ArticleSubmissionItem> {
  return parsePaged(payload, parseArticleItem);
}

export function parseFanPerformanceSubmissions(
  payload: unknown,
): PagedSubmissions<FanPerformanceSubmissionItem> {
  return parsePaged(payload, parseFanPerformanceItem);
}

function parsePaged<T>(payload: unknown, mapItem: (raw: Record<string, unknown>) => T): PagedSubmissions<T> {
  if (!payload || typeof payload !== 'object') {
    throw new Error('Submissions response was empty.');
  }

  const raw = payload as Record<string, unknown>;
  const items = Array.isArray(raw.items)
    ? raw.items.flatMap((item) => {
        if (!item || typeof item !== 'object') {
          return [];
        }

        return [mapItem(item as Record<string, unknown>)];
      })
    : [];

  return {
    items,
    page: readPositiveInt(raw.page, 1),
    pageSize: readPositiveInt(raw.pageSize, 20),
    totalCount: readNonNegativeInt(raw.totalCount, items.length),
    totalPages: readNonNegativeInt(raw.totalPages, 0),
  };
}

function parsePhotoItem(raw: Record<string, unknown>): PhotoSubmissionItem {
  return {
    id: readRequiredString(raw.id, 'Photo submission id'),
    title: readRequiredString(raw.title, 'Photo title'),
    submittedAt: readRequiredString(raw.submittedAt, 'Photo submittedAt'),
    status: parseStatus(raw.status),
    notes: readOptionalString(raw.notes),
    thumbnailPath: readOptionalString(raw.thumbnailPath),
    promotedPicId: typeof raw.promotedPicId === 'number' ? raw.promotedPicId : null,
  };
}

function parseNewsItem(raw: Record<string, unknown>): NewsSuggestionItem {
  return {
    id: readRequiredString(raw.id, 'News suggestion id'),
    url: readRequiredString(raw.url, 'News url'),
    truncatedUrl: typeof raw.truncatedUrl === 'string' ? raw.truncatedUrl : String(raw.url),
    title: readOptionalString(raw.title),
    submittedAt: readRequiredString(raw.submittedAt, 'News submittedAt'),
    status: parseStatus(raw.status),
    notes: readOptionalString(raw.notes),
    publishedNewsId: typeof raw.publishedNewsId === 'number' ? raw.publishedNewsId : null,
    publishedPath: readOptionalString(raw.publishedPath),
  };
}

function parseFanPerformanceItem(raw: Record<string, unknown>): FanPerformanceSubmissionItem {
  return {
    id: readRequiredString(raw.id, 'Fan performance submission id'),
    title: readRequiredString(raw.title, 'Fan performance title'),
    coveredSong: typeof raw.coveredSong === 'string' ? raw.coveredSong : '',
    performedBy: typeof raw.performedBy === 'string' ? raw.performedBy : '',
    submittedAt: readRequiredString(raw.submittedAt, 'Fan performance submittedAt'),
    status: parseStatus(raw.status),
    notes: readOptionalString(raw.notes),
    rejectionReason: readOptionalString(raw.rejectionReason),
    promotedStageId: typeof raw.promotedStageId === 'number' ? raw.promotedStageId : null,
    publishedPath: readOptionalString(raw.publishedPath),
  };
}

function parseArticleItem(raw: Record<string, unknown>): ArticleSubmissionItem {
  return {
    id: readRequiredString(raw.id, 'Article submission id'),
    title: readRequiredString(raw.title, 'Article title'),
    submittedAt: readOptionalString(raw.submittedAt),
    status: parseStatus(raw.status),
    notes: readOptionalString(raw.notes),
    canContinueEditing: raw.canContinueEditing === true,
    editPath: readOptionalString(raw.editPath),
    publishedPath: readOptionalString(raw.publishedPath),
  };
}

function parseStatus(value: unknown): SubmissionStatus {
  if (!value || typeof value !== 'object') {
    throw new Error('Submission status was missing.');
  }

  const raw = value as Record<string, unknown>;
  const status = readRequiredString(raw.status, 'status');
  const statusLabel = typeof raw.statusLabel === 'string' && raw.statusLabel.trim().length > 0
    ? raw.statusLabel
    : status;
  const tone = raw.statusTone;
  const statusTone: SubmissionStatusTone =
    tone === 'pending'
    || tone === 'review'
    || tone === 'attention'
    || tone === 'success'
    || tone === 'danger'
    || tone === 'neutral'
      ? tone
      : 'neutral';

  return { status, statusLabel, statusTone };
}

function readRequiredString(value: unknown, label: string): string {
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw new Error(`${label} was missing.`);
  }

  return value;
}

function readOptionalString(value: unknown): string | null {
  return typeof value === 'string' && value.trim().length > 0 ? value : null;
}

function readPositiveInt(value: unknown, fallback: number): number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0 ? Math.trunc(value) : fallback;
}

function readNonNegativeInt(value: unknown, fallback: number): number {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? Math.trunc(value) : fallback;
}
