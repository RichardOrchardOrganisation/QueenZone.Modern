import { getAppConfig } from '../config';
import { saveLocalFileToPhotos } from '../media/saveToPhotos';
import { shareLocalFile } from '../media/shareLocalFile';
import { writeCachedLocalFile } from '../media/writeCachedFile';
import { resolveContentUrl } from '../ui/html/resolveContentUrl';
import { ApiError } from './client';

export type ForumAttachmentBytes = {
  finalUrl: string;
  contentType: string;
  dataUri: string;
  bytes: Uint8Array;
};

export type CachedForumAttachment = {
  fileUri: string;
  contentType: string;
  dataUri: string;
};

const OPEN_FAILED = 'This attachment cannot be opened from the app.';
const SHARE_FAILED = 'Unable to open this attachment.';

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
 * Bearer GET of `downloadUrl`. Refuses the cookie-gated `/forum/attachment/...` path.
 */
export async function fetchForumAttachment(
  downloadUrl: string,
  accessToken: string,
  signal?: AbortSignal,
): Promise<ForumAttachmentBytes> {
  if (isCookieGatedForumAttachmentPath(downloadUrl)) {
    throw ApiError.http(400, OPEN_FAILED);
  }

  const url = resolveContentUrl(downloadUrl, getAppConfig().apiBaseUrl);
  if (!url) {
    throw ApiError.http(400, OPEN_FAILED);
  }

  const response = await fetch(url, {
    method: 'GET',
    redirect: 'follow',
    headers: {
      Accept: '*/*',
      Authorization: `Bearer ${accessToken}`,
    },
    signal,
  });

  if (!response.ok) {
    throw ApiError.http(
      response.status,
      response.status === 401 ? 'Sign in to continue.' : `Request failed (${response.status}).`,
    );
  }

  const contentType = response.headers.get('content-type')?.split(';')[0]?.trim() || 'application/octet-stream';
  const bytes = new Uint8Array(await response.arrayBuffer());
  return {
    finalUrl: response.url,
    contentType,
    dataUri: `data:${contentType};base64,${bytesToBase64(bytes)}`,
    bytes,
  };
}

/**
 * Bearer GET an image (follows the legacy CDN redirect) and return a local
 * cached data URI for the in-app viewer. RN Image must not load `url`.
 */
export async function openForumAttachmentImage(
  downloadUrl: string,
  accessToken: string,
  signal?: AbortSignal,
): Promise<string> {
  const loaded = await loadForumAttachment(downloadUrl, accessToken, signal);
  return loaded.dataUri;
}

export type OpenForumAttachmentFileOptions = {
  signal?: AbortSignal;
  /** When false, skip the Files share sheet after a successful Bearer fetch. */
  present?: boolean;
};

/** Bearer GET → cache file named with `fileName`. Used by play + Files save. */
export async function cacheForumAttachment(
  downloadUrl: string,
  accessToken: string,
  fileName: string,
  signal?: AbortSignal,
): Promise<CachedForumAttachment> {
  const loaded = await loadForumAttachment(downloadUrl, accessToken, signal);
  const cacheKey = `${downloadUrl}\0${fileName}`;
  const cachedUri = fileUriCache.get(cacheKey);
  if (cachedUri) {
    return { fileUri: cachedUri, contentType: loaded.contentType, dataUri: loaded.dataUri };
  }

  const fileUri = await writeCachedLocalFile(fileName, loaded.bytes);
  fileUriCache.set(cacheKey, fileUri);
  return { fileUri, contentType: loaded.contentType, dataUri: loaded.dataUri };
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

async function loadForumAttachment(
  downloadUrl: string,
  accessToken: string,
  signal?: AbortSignal,
): Promise<ForumAttachmentBytes> {
  const cached = attachmentCache.get(downloadUrl);
  if (cached) {
    return cached;
  }

  const fetched = await fetchForumAttachment(downloadUrl, accessToken, signal);
  if (isCookieGatedForumAttachmentPath(fetched.finalUrl)) {
    throw ApiError.http(400, OPEN_FAILED);
  }

  attachmentCache.set(downloadUrl, fetched);
  return fetched;
}

const attachmentCache = new Map<string, ForumAttachmentBytes>();
const fileUriCache = new Map<string, string>();

function bytesToBase64(bytes: Uint8Array): string {
  let binary = '';
  for (let i = 0; i < bytes.length; i += 1) {
    binary += String.fromCharCode(bytes[i]!);
  }
  return globalThis.btoa(binary);
}
