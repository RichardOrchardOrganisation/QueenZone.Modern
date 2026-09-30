/** Only the public, read-only destinations exposed by QueenZone's iOS App Intents. */
export type SiriDestination =
  | { kind: 'news' }
  | { kind: 'trivia' }
  | { kind: 'timeline'; id?: number }
  | { kind: 'search'; query: string }
  | { kind: 'album' | 'biography'; id: number };

export function parseSiriDeepLink(raw: string): SiriDestination | null {
  try {
    const url = new URL(raw);
    if (url.protocol !== 'queenzone:' || url.hash) return null;
    if (url.hostname === 'news' && !url.pathname && !url.search) return { kind: 'news' };
    if (url.hostname === 'trivia' && !url.pathname && !url.search) return { kind: 'trivia' };
    if (url.hostname === 'timeline' && !url.search) {
      if (!url.pathname) return { kind: 'timeline' };
      if (!/^\/\d+$/.test(url.pathname)) return null;
      const id = Number(url.pathname.slice(1));
      return Number.isSafeInteger(id) && id > 0 ? { kind: 'timeline', id } : null;
    }
    if ((url.hostname === 'album' || url.hostname === 'biography') && !url.search && /^\/\d+$/.test(url.pathname)) {
      const id = Number(url.pathname.slice(1));
      return Number.isSafeInteger(id) && id > 0 ? { kind: url.hostname, id } : null;
    }
    if (url.hostname !== 'search' || url.pathname) return null;
    const query = url.searchParams.get('q')?.trim() ?? '';
    if (query.length < 2 || query.length > 100) return null;
    return { kind: 'search', query };
  } catch {
    return null;
  }
}
