export type CrosswordNavigation = { navigate: (name: 'Tabs', params: { screen: 'ArchiveTab'; params: { screen: 'CrosswordPlay'; params: { slug: string }; initial: false } }) => void };
export function crosswordSlugFromUrl(value: string): string | null {
  try {
    const url = new URL(value);
    let segment: string;
    if (url.protocol === 'queenzone:' && url.hostname === 'crosswords') segment = url.pathname.slice(1);
    else if (url.protocol === 'https:' && ['queenzone.org', 'www.queenzone.org', 'dev.queenzone.org'].includes(url.hostname) && url.pathname.startsWith('/crosswords/')) segment = url.pathname.slice('/crosswords/'.length);
    else return null;
    const slug = decodeURIComponent(segment.replace(/\/$/, ''));
    return /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(slug) && slug.length <= 160 ? slug : null;
  } catch { return null; }
}
export function openCrosswordLink(navigation: CrosswordNavigation, value: string): boolean {
  const slug = crosswordSlugFromUrl(value);
  if (!slug) return false;
  navigation.navigate('Tabs', { screen: 'ArchiveTab', params: { screen: 'CrosswordPlay', params: { slug }, initial: false } });
  return true;
}
