import { trimTrailingChar, trimTrailingSet } from '../../text/trimRuns';

/** Native bits we are willing to look at. Files are a reject, not a field. */
export type ShareRaw = {
  text?: string | null;
  webUrl?: string | null;
  /** If true, the share is a file/image payload. Parser returns `rejected: file`. */
  hasFiles: boolean;
};

export type ShareRejectReason = 'file' | 'noUrl' | 'notHttps' | 'unsupportedScheme';

/**
 * http is visible so we can say "use https" instead of "no URL".
 * It cannot become a draft.
 */
export type FoundLink =
  | { scheme: 'https'; href: string }
  | { scheme: 'http'; href: string };

/**
 * Illegal to construct: auto-picked-one-of-many, a file share, javascript:,
 * custom schemes as the chosen URL.
 */
export type ShareIntake =
  | { kind: 'accepted'; url: string; leftoverText: string }
  | { kind: 'choose'; candidates: [string, string, ...string[]] }
  | { kind: 'rejected'; reason: ShareRejectReason; detail: string };

const httpUrlPattern = /https?:\/\/[^\s<>"'`]+/gi;
const trailingPunctuationChars = new Set(['.', ',', ';', ':', '!', '?', ')', ']', '}', "'", '"']);
const namedUnsupportedSchemes = ['javascript:', 'data:', 'file:', 'intent:'] as const;

const fileRejectDetail = 'Share a link, not a file or photo.';
const notHttpsDetail = 'News suggestions need an https:// link.';
const unsupportedDetail = 'That link type cannot be suggested.';
const noUrlDetail = 'We could not find a web link in that share.';

const leftoverTitleMax = 300;

export function parseShare(raw: ShareRaw): ShareIntake {
  if (raw.hasFiles) {
    return { kind: 'rejected', reason: 'file', detail: fileRejectDetail };
  }

  const combined = [raw.webUrl, raw.text].filter((part) => Boolean(part?.trim())).join('\n');
  const links = findLinks(combined);
  const unique = uniqPreserveOrder(links.map((link) => link.href));

  if (unique.length >= 2) {
    return { kind: 'choose', candidates: [unique[0]!, unique[1]!, ...unique.slice(2)] };
  }

  const only = links[0];
  if (only?.scheme === 'https') {
    return {
      kind: 'accepted',
      url: only.href,
      leftoverText: leftoverAfterUrls(combined, links),
    };
  }

  if (only?.scheme === 'http') {
    return { kind: 'rejected', reason: 'notHttps', detail: notHttpsDetail };
  }

  if (containsUnsupportedScheme(combined)) {
    return { kind: 'rejected', reason: 'unsupportedScheme', detail: unsupportedDetail };
  }

  return { kind: 'rejected', reason: 'noUrl', detail: noUrlDetail };
}

/** Pull http(s) URLs from webUrl and text. Ignore javascript:, data:, file:, queenzone:. */
export function findLinks(text: string): FoundLink[] {
  if (!text) {
    return [];
  }

  const found: FoundLink[] = [];
  for (const raw of text.match(httpUrlPattern) ?? []) {
    const href = trimTrailingSet(raw, trailingPunctuationChars);
    if (href.startsWith('https://')) {
      found.push({ scheme: 'https', href });
    } else if (href.startsWith('http://')) {
      found.push({ scheme: 'http', href });
    }
  }
  return found;
}

export function leftoverAfterUrls(text: string, links: FoundLink[]): string {
  let leftover = text;
  for (const link of links) {
    leftover = leftover.split(link.href).join(' ');
  }
  leftover = leftover.replace(/\s+/g, ' ').trim();
  if (!leftover || leftover.length > leftoverTitleMax) {
    return '';
  }
  return leftover;
}

export function normalizeShareUrl(url: string): string {
  const trimmed = url.trim();
  try {
    const parsed = new URL(trimmed);
    parsed.hash = '';
    parsed.hostname = parsed.hostname.toLowerCase();
    if ((parsed.protocol === 'https:' && parsed.port === '443') || (parsed.protocol === 'http:' && parsed.port === '80')) {
      parsed.port = '';
    }
    if (parsed.pathname.length > 1) {
      parsed.pathname = trimTrailingChar(parsed.pathname, '/');
    }
    return parsed.href;
  } catch {
    return trimmed.toLowerCase();
  }
}

export function hostOf(url: string): string {
  try {
    return new URL(url).host;
  } catch {
    return '';
  }
}

function uniqPreserveOrder(hrefs: string[]): string[] {
  const seen = new Set<string>();
  const unique: string[] = [];
  for (const href of hrefs) {
    const key = normalizeShareUrl(href);
    if (seen.has(key)) {
      continue;
    }
    seen.add(key);
    unique.push(href);
  }
  return unique;
}

function containsUnsupportedScheme(text: string): boolean {
  const lower = text.toLowerCase();
  for (const scheme of namedUnsupportedSchemes) {
    if (lower.includes(scheme)) {
      return true;
    }
  }

  let from = 0;
  while (from < lower.length) {
    const sep = lower.indexOf('://', from);
    if (sep === -1) {
      return false;
    }
    const scheme = readSchemeBefore(lower, sep);
    if (scheme && scheme !== 'http' && scheme !== 'https') {
      return true;
    }
    from = sep + 3;
  }
  return false;
}

/** `[a-z][a-z0-9+.-]*` immediately before `://`, same as the old lookahead regex. */
function readSchemeBefore(lower: string, colonIndex: number): string | null {
  let start = colonIndex;
  while (start > 0 && isSchemeContinue(lower.charCodeAt(start - 1))) {
    start -= 1;
  }
  while (start < colonIndex) {
    const code = lower.charCodeAt(start);
    if (code >= 97 && code <= 122) {
      return lower.slice(start, colonIndex);
    }
    start += 1;
  }
  return null;
}

function isSchemeContinue(code: number): boolean {
  return (
    (code >= 97 && code <= 122) ||
    (code >= 48 && code <= 57) ||
    code === 43 ||
    code === 46 ||
    code === 45
  );
}
