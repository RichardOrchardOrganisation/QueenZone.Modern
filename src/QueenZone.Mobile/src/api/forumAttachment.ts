import { Directory, File, Paths } from 'expo-file-system';
import { getAppConfig } from '../config';
import { saveLocalFileToPhotos } from '../media/saveToPhotos';
import { shareLocalFile } from '../media/shareLocalFile';
import { cacheFileName } from '../media/writeCachedFile';
import { resolveContentUrl } from '../ui/html/resolveContentUrl';
import { ApiError } from './client';

export type CachedForumAttachment = {
  fileUri: string;
  contentType: string;
};

/**
 * Idle budget per attempt: abort only when no bytes arrive for this long (reset on every
 * progress event), so large attachments (up to 100 MB) on slow links still finish. Two stalled
 * attempts stay inside the 20s journeys wait (#2451).
 */
export const FORUM_ATTACHMENT_IDLE_TIMEOUT_MS = 8000;
const FORUM_ATTACHMENT_ATTEMPTS = 2;

const OPEN_FAILED = 'This attachment cannot be opened from the app.';
const SHARE_FAILED = 'Unable to open this attachment.';
export const DOWNLOAD_TIMED_OUT = 'The attachment took too long to download. Tap it to try again.';

/** Cookie-gated website path. Never load this in Image / WebView / Linking. */
export function isCookieGatedForumAttachmentPath(path: string | null | undefined): boolean {
  const trimmed = path?.trim() ?? '';
  if (!trimmed) {
    return false;
  }
  return (
    /\/forum\/attachment(?:\/|$)/i.test(trimmed) &&
    !/\/api\/v1\/forum\/attachments(?:\/|$)/i.test(trimmed)
  );
}

/**
 * Bearer GET an image (follows the legacy CDN redirect) into the cache and return
 * its `file://` URI for the in-app viewer. RN Image must not load `url`.
 */
export async function openForumAttachmentImage(
  downloadUrl: string,
  accessToken: string,
  signal?: AbortSignal,
): Promise<string> {
  const fileName = cacheFileName(downloadUrl.split('?')[0] ?? '');
  const cached = await cacheForumAttachment(downloadUrl, accessToken, fileName, signal);
  return cached.fileUri;
}

export type OpenForumAttachmentFileOptions = {
  signal?: AbortSignal;
  /** When false, skip the Files share sheet after a successful Bearer fetch. */
  present?: boolean;
};

/**
 * Bearer GET → cache file named with `fileName`. Native download straight to disk
 * (no in-memory base64, #2451), with a per-attempt timeout and one retry.
 */
export async function cacheForumAttachment(
  downloadUrl: string,
  accessToken: string,
  fileName: string,
  signal?: AbortSignal,
): Promise<CachedForumAttachment> {
  const cacheKey = `${downloadUrl}\0${fileName}`;
  const cached = fileCache.get(cacheKey);
  if (cached) {
    // iOS may purge Paths.cache; never hand the viewer a dead file:// URI.
    if (new File(cached.fileUri).exists) {
      return cached;
    }
    fileCache.delete(cacheKey);
  }

  if (isCookieGatedForumAttachmentPath(downloadUrl)) {
    throw ApiError.http(400, OPEN_FAILED);
  }
  const url = resolveContentUrl(downloadUrl, getAppConfig().apiBaseUrl);
  if (!url) {
    throw ApiError.http(400, OPEN_FAILED);
  }

  const name = cacheFileName(fileName);
  let lastError: unknown;
  for (let attempt = 1; attempt <= FORUM_ATTACHMENT_ATTEMPTS; attempt += 1) {
    if (signal?.aborted) {
      throw ApiError.http(499, SHARE_FAILED);
    }
    try {
      const fileUri = await downloadOnce(url, accessToken, name, signal);
      const result = { fileUri, contentType: contentTypeFor(name) };
      fileCache.set(cacheKey, result);
      return result;
    } catch (error: unknown) {
      lastError = error;
      if (signal?.aborted || isAbortError(error)) {
        // Caller cancelled (screen closed): final, never retried.
        throw ApiError.http(499, SHARE_FAILED);
      }
      // Client errors (401, 404, cookie-gated) are final; timeouts and network errors retry once.
      if (error instanceof ApiError && error.status >= 400 && error.status < 500 && error.status !== 408) {
        throw error;
      }
    }
  }
  throw lastError instanceof ApiError ? lastError : ApiError.http(500, SHARE_FAILED);
}

async function downloadOnce(
  url: string,
  accessToken: string,
  fileName: string,
  signal: AbortSignal | undefined,
): Promise<string> {
  downloadSequence += 1;
  const directory = new Directory(Paths.cache, 'forum-attachments', String(downloadSequence));
  directory.create({ intermediates: true, idempotent: true });
  const destination = new File(directory, fileName);

  const controller = new AbortController();
  let timedOut = false;
  let timer: ReturnType<typeof setTimeout> | undefined;
  let rejectIdle: ((error: unknown) => void) | undefined;
  const idle = new Promise<never>((_, reject) => {
    rejectIdle = reject;
  });
  const armIdleTimer = () => {
    clearTimeout(timer);
    timer = setTimeout(() => {
      timedOut = true;
      controller.abort();
      rejectIdle?.(ApiError.http(408, DOWNLOAD_TIMED_OUT));
    }, FORUM_ATTACHMENT_IDLE_TIMEOUT_MS);
  };
  const onAbort = () => controller.abort();
  signal?.addEventListener('abort', onAbort);
  // Note: the native task follows redirects and does not expose the final URL, so the old
  // "redirected to a cookie-gated /forum/attachment path" check cannot run here. The API
  // alias only redirects to the CDN; a cookie-gated redirect would surface as a 401/HTML
  // download (401 is mapped below). A dedicated follow-up issue is still to be filed.
  const task = File.createDownloadTask(url, destination, {
    headers: { Accept: '*/*', Authorization: `Bearer ${accessToken}` },
    // iOS defaults to a background (nsurlsessiond) session, which can sit queued
    // indefinitely. A tap-to-open download belongs in the app's foreground session.
    sessionType: 'foreground',
    signal: controller.signal,
    onProgress: armIdleTimer,
  });
  armIdleTimer();
  try {
    const file = await Promise.race([task.downloadAsync(), idle]);
    if (!file) {
      throw ApiError.http(500, SHARE_FAILED);
    }
    return file.uri;
  } catch (error: unknown) {
    removeQuietly(directory);
    if (timedOut) {
      throw ApiError.http(408, DOWNLOAD_TIMED_OUT);
    }
    if (error instanceof ApiError) {
      throw error;
    }
    throw downloadError(error);
  } finally {
    clearTimeout(timer);
    signal?.removeEventListener('abort', onAbort);
  }
}

/** Drop a failed/aborted attempt's folder and any partial file in it. */
function removeQuietly(directory: Directory): void {
  try {
    if (directory.exists) {
      directory.delete();
    }
  } catch {
    // Best effort: the OS purges Paths.cache anyway.
  }
}

function isAbortError(error: unknown): boolean {
  return error instanceof Error && error.name === 'AbortError';
}

/**
 * expo-file-system 58 rejects non-2xx downloads with UnableToDownloadException and no
 * structured status field; the code is only in the message. Match just the exact native
 * formats (iOS task "server returned HTTP 404", iOS/Android "response has status[:] 404",
 * Android task "HTTP 404") instead of any 3-digit number.
 */
const NATIVE_HTTP_STATUS = /(?:server returned HTTP|response has status:?|^HTTP|: HTTP) (\d{3})\b/;

export function downloadStatus(error: unknown): number | undefined {
  const message = error instanceof Error ? error.message : String(error);
  const match = NATIVE_HTTP_STATUS.exec(message);
  return match ? Number(match[1]) : undefined;
}

function downloadError(error: unknown): ApiError {
  const status = downloadStatus(error);
  if (status === 401) {
    return ApiError.http(401, 'Sign in to continue.');
  }
  if (status !== undefined && status >= 400) {
    return ApiError.http(status, `Request failed (${status}).`);
  }
  return ApiError.http(500, SHARE_FAILED);
}

const CONTENT_TYPES: Record<string, string> = {
  jpg: 'image/jpeg',
  jpeg: 'image/jpeg',
  png: 'image/png',
  gif: 'image/gif',
  webp: 'image/webp',
  heic: 'image/heic',
  pdf: 'application/pdf',
  txt: 'text/plain',
  zip: 'application/zip',
  mp3: 'audio/mpeg',
  m4a: 'audio/mp4',
  wav: 'audio/wav',
  mp4: 'video/mp4',
};

function contentTypeFor(fileName: string): string {
  const ext = /\.([a-z0-9]+)$/i.exec(fileName)?.[1]?.toLowerCase() ?? '';
  return CONTENT_TYPES[ext] ?? 'application/octet-stream';
}

/** Image save: cached file → add-only Photos. Not the share sheet. */
export async function saveForumAttachmentImage(
  downloadUrl: string,
  accessToken: string,
  fileName: string,
  signal?: AbortSignal,
): Promise<void> {
  const cached = await cacheForumAttachment(downloadUrl, accessToken, fileName, signal);
  await saveLocalFileToPhotos(cached.fileUri);
}

/** Non-image (and audio Files): cache file → Sharing.shareAsync. Cancel is not an error. */
export async function openForumAttachmentFile(
  downloadUrl: string,
  accessToken: string,
  fileName: string,
  options?: OpenForumAttachmentFileOptions,
): Promise<void> {
  const present = options?.present ?? true;
  const cached = await cacheForumAttachment(downloadUrl, accessToken, fileName, options?.signal);
  if (!present) {
    return;
  }

  try {
    await shareLocalFile(cached.fileUri, cached.contentType, fileName);
  } catch (error: unknown) {
    if (error instanceof ApiError) {
      throw error;
    }
    throw ApiError.http(500, SHARE_FAILED);
  }
}

let downloadSequence = 0;
const fileCache = new Map<string, CachedForumAttachment>();

/** Test hook: forget cached downloads between tests. */
export function resetForumAttachmentCacheForTests(): void {
  fileCache.clear();
}
