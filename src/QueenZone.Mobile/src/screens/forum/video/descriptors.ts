import { parseDocument } from 'htmlparser2';
import { isTag, isText, Element, type AnyNode } from 'domhandler';
import { getOuterHTML } from 'domutils';

export type YoutubeVideo = {
  provider: 'youtube'; videoId: string; watchUrl: string;
  startSeconds: number | null; anchorIndex: number;
};
export type VideoProjection = { html: string; cards: { video: YoutubeVideo; originalHtml: string }[] };
const idPattern = /^[A-Za-z0-9_-]{11}$/;
export function watchUrl(videoId: string, startSeconds: number | null): string {
  const start = startSeconds ? `&t=${startSeconds}s` : '';
  return `https://www.youtube.com/watch?v=${videoId}${start}`;
}
export function validateDescriptors(payload: unknown): YoutubeVideo[] {
  if (!Array.isArray(payload)) return [];
  const result: YoutubeVideo[] = [];
  const pairs = new Set<string>();
  const anchors = new Set<number>();
  for (const value of payload.slice(0, 3)) {
    if (!value || typeof value !== 'object') continue;
    const { provider, videoId, watchUrl: url, startSeconds: start, anchorIndex } = value;
    if (provider !== 'youtube' || typeof videoId !== 'string' || !idPattern.test(videoId)
      || (start != null && (!Number.isInteger(start) || start < 0 || start > 86400))
      || !Number.isSafeInteger(anchorIndex) || anchorIndex < 0
      || url !== watchUrl(videoId, start ?? null)) continue;
    const pair = `${videoId}:${start ?? 0}`;
    if (pairs.has(pair) || anchors.has(anchorIndex)) continue;
    pairs.add(pair); anchors.add(anchorIndex);
    result.push({ provider, videoId, watchUrl: url, startSeconds: start || null, anchorIndex });
  }
  return result.sort((a, b) => a.anchorIndex - b.anchorIndex);
}

// This checks the mapped body's identity, not independent link discovery. Only
// additive server descriptors can produce cards. Keep in sync with #1956 policy.
function linkIdentity(href: string): { id: string; start: number | null } | null {
  if (!href || /[\\%\s\u0000-\u0008\u000e-\u001f\u007f]/.test(href) || /\/\.\.?\//.test(href)) return null;
  let url: URL;
  try { url = new URL(href); } catch { return null; }
  if (!['https:', 'http:'].includes(url.protocol) || url.username || url.password || url.port) return null;
  const short = ['youtu.be', 'www.youtu.be'].includes(url.hostname);
  if (!short && !['youtube.com', 'www.youtube.com', 'm.youtube.com'].includes(url.hostname)) return null;
  const query = new Map<string, string[]>();
  url.searchParams.forEach((v, k) => query.set(k.toLowerCase(), [...(query.get(k.toLowerCase()) ?? []), v]));
  const id = linkedVideoId(url, short, query.get('v') ?? []);
  if (!id || !idPattern.test(id)) return null;
  return { id, start: linkedStart([...(query.get('t') ?? []), ...(query.get('start') ?? [])]) };
}
function linkedVideoId(url: URL, short: boolean, vs: string[]): string | undefined {
  if (!short && url.pathname === '/watch' && vs.length === 1) return vs[0];
  if (vs.length) return undefined;
  const segments = url.pathname.split('/');
  if (short && segments.length === 2) return segments[1];
  if (!short && segments.length === 3 && ['shorts', 'embed'].includes(segments[1])) return segments[2];
  return undefined;
}
function linkedStart(times: string[]): number | null {
  if (times.length !== 1 || times[0].length > 20) return null;
  const t = times[0];
  let seconds = 0;
  if (/^[0-9]+$/.test(t)) seconds = Number(t);
  else {
    const match = /^(?:([0-9]+)h)?(?:([0-9]+)m)?(?:([0-9]+)s)?$/.exec(t);
    if (match) seconds = Number(match[1] ?? 0) * 3600 + Number(match[2] ?? 0) * 60 + Number(match[3] ?? 0);
  }
  return seconds > 0 && seconds <= 86400 ? seconds : null;
}
const formatting = new Set(['span', 'strong', 'b', 'em', 'i', 'u']);
const boundaries = new Set(['br', 'blockquote', 'pre', 'code', 'ul', 'ol', 'li', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6']);
function eligibleAnchors(nodes: AnyNode[], eligible: Set<Element>): void {
  let line: AnyNode[] = [];
  function flush() {
    const only = line[0];
    if (line.length === 1 && only && isTag(only) && only.name === 'a'
      && !getOuterHTML(only).match(/<(?:img|br)\b/i)) eligible.add(only);
    line = [];
  }
  function visit(node: AnyNode) {
    if (isText(node)) { if (node.data.trim()) line.push(node); return; }
    if (!isTag(node)) return;
    const quote = (node.attribs.class ?? '').split(/\s+/).includes('qz-bbcode-quote');
    if (['p', 'div'].includes(node.name)) {
      flush(); if (!quote) eligibleAnchors(node.children, eligible);
    } else if (boundaries.has(node.name)) flush();
    else if (formatting.has(node.name) && !quote) node.children.forEach(visit);
    else line.push(node);
  }
  nodes.forEach(visit); flush();
}
export function projectVideos(html: string, payload: unknown): VideoProjection {
  const descriptors = validateDescriptors(payload);
  if (!descriptors.length) return { html, cards: [] };
  const dom = parseDocument(html);
  const eligible = new Set<Element>();
  eligibleAnchors(dom.children, eligible);
  const anchors: Element[] = [];
  function walk(nodes: AnyNode[]) {
    for (const node of nodes) {
      if (!isTag(node)) continue;
      // Never let a posted custom tag impersonate a generated card.
      if (node.name === 'qz-forum-video') { node.name = 'span'; node.attribs = {}; }
      if (node.name === 'a') anchors.push(node);
      walk(node.children);
    }
  }
  walk(dom.children);
  const cards: VideoProjection['cards'] = [];
  for (const video of descriptors) {
    const anchor = anchors[video.anchorIndex];
    if (!anchor || !eligible.has(anchor)) continue;
    const identity = linkIdentity(anchor.attribs.href ?? '');
    if (!identity || identity.id !== video.videoId || identity.start !== video.startSeconds) continue;
    cards.push({ video, originalHtml: getOuterHTML(anchor) });
    anchor.name = 'qz-forum-video';
    anchor.attribs = { 'data-card': String(cards.length - 1) };
    anchor.children = [];
  }
  return { html: getOuterHTML(dom), cards };
}
