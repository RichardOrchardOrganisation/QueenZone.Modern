/** Only the public, read-only destinations exposed by QueenZone's iOS App Intents. */
export type SiriDestination =
  | { kind: 'news' }
  | { kind: 'trivia' }
  | { kind: 'timeline'; id?: number }
  | { kind: 'search'; query: string }
  | { kind: 'album' | 'biography'; id: number };

function numericSiriDestination(kind: 'timeline' | 'album' | 'biography', url: URL): SiriDestination | null {
  if (url.search) return null;
  if (kind === 'timeline' && !url.pathname) return { kind };
  if (!/^\/\d+$/.test(url.pathname)) return null;
  const id = Number(url.pathname.slice(1));
  return Number.isSafeInteger(id) && id > 0 ? { kind, id } : null;
}

export function parseSiriDeepLink(raw: string): SiriDestination | null {
  try {
    const url = new URL(raw);
    if (url.protocol !== 'queenzone:' || url.hash) return null;
    const kind = url.hostname;
    switch (kind) {
      case 'news':
      case 'trivia':
        return !url.pathname && !url.search ? { kind } : null;
      case 'timeline':
      case 'album':
      case 'biography':
        return numericSiriDestination(kind, url);
      case 'search': {
        if (url.pathname) return null;
        const query = url.searchParams.get('q')?.trim() ?? '';
        if (query.length < 2 || query.length > 100) return null;
        return { kind: 'search', query };
      }
      default:
        return null;
    }
  } catch {
    return null;
  }
}
