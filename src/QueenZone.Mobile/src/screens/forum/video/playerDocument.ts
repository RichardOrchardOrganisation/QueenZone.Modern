import type { YoutubeVideo } from './descriptors';

// Store/OS registered identity, also used by app.json on both platforms.
export const playerBaseUrl = 'https://org.queenzone.mobile/';
export const pauseScript = 'window.qzPause && window.qzPause(); true;';
export const playerTimeoutMs = 12000;
export type PlayerMessage =
  | { event: 'ready' }
  | { event: 'state'; value: number }
  | { event: 'error'; value: number }
  | { event: 'fullscreen'; value: boolean };
export function parsePlayerMessage(data: string, generation: number): PlayerMessage | null {
  if (data.length > 256) return null;
  let m: unknown;
  try { m = JSON.parse(data); } catch { return null; }
  if (!m || typeof m !== 'object') return null;
  const v = m as Record<string, unknown>;
  if (v.version !== 1 || v.generation !== generation) return null;
  if (v.event === 'ready' && Object.keys(v).length === 3) return { event: 'ready' };
  if (Object.keys(v).length !== 4) return null;
  if (v.event === 'state' && [-1, 0, 1, 2, 3, 5].includes(v.value as number)) return { event: 'state', value: v.value as number };
  if (v.event === 'error' && [2, 5, 100, 101, 150, 153].includes(v.value as number)) return { event: 'error', value: v.value as number };
  if (v.event === 'fullscreen' && typeof v.value === 'boolean') return { event: 'fullscreen', value: v.value };
  return null;
}
export function playerDocument(video: YoutubeVideo, generation: number): string {
  // Callers only pass validated descriptors. Assert at this boundary as well.
  if (!/^[A-Za-z0-9_-]{11}$/.test(video.videoId) || !Number.isSafeInteger(generation)
    || (video.startSeconds != null && (!Number.isInteger(video.startSeconds) || video.startSeconds < 0 || video.startSeconds > 86400))) throw new Error('Invalid player input');
  return `<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1">
<meta name="referrer" content="strict-origin-when-cross-origin">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline' https://www.youtube.com https://s.ytimg.com; frame-src https://www.youtube.com; style-src 'unsafe-inline'; connect-src https://www.youtube.com; base-uri 'none'; form-action 'none'">
<style>html,body{margin:0;width:100%;height:100%;background:#111}iframe,#player{width:100%;height:100%;border:0}</style></head>
<body><div id="player"></div><script>
var player;function send(event,value){var m={version:1,generation:${generation},event:event};if(value!==undefined)m.value=value;window.ReactNativeWebView.postMessage(JSON.stringify(m));}
window.qzNativeFullscreen=function(value){send('fullscreen',value);};
window.qzPause=function(){if(player&&player.pauseVideo)player.pauseVideo();};
function onYouTubeIframeAPIReady(){player=new YT.Player('player',{width:'100%',height:'100%',videoId:'${video.videoId}',playerVars:{autoplay:0,playsinline:1,controls:1,fs:1,origin:'https://org.queenzone.mobile',start:${video.startSeconds ?? 0}},events:{onReady:function(){send('ready');},onStateChange:function(e){send('state',e.data);},onError:function(e){send('error',e.data);}}});}
document.addEventListener('fullscreenchange',function(){send('fullscreen',!!document.fullscreenElement);});
document.addEventListener('webkitfullscreenchange',function(){send('fullscreen',!!document.webkitFullscreenElement);});
</script><script src="https://www.youtube.com/iframe_api"></script></body></html>`;
}
export type NavigationDecision = 'allow' | 'external' | 'reject';
export function playerNavigation(url: string, isTopFrame: boolean | undefined, navigationType: string): NavigationDecision {
  if (url === 'about:blank' || url === playerBaseUrl) return 'allow';
  let u: URL;
  try { u = new URL(url); } catch { return 'reject'; }
  if (u.protocol !== 'https:' || u.username || u.password || u.port) return 'reject';
  // iOS reports child-frame navigations; Android normally reports main-frame
  // only and may omit isTopFrame. Do not allow a top-level embed to replace the wrapper.
  if (isTopFrame === false && u.hostname === 'www.youtube.com' && /^\/embed\/[A-Za-z0-9_-]{11}$/.test(u.pathname)) return 'allow';
  if (navigationType === 'click') return 'external';
  return 'reject';
}
export function playerError(code: number | string): string {
  if (code === 153) return 'YouTube could not identify this app (153). Try again or watch on YouTube.';
  if (code === 100) return 'This video is private or unavailable. You can try watching on YouTube.';
  if (code === 101 || code === 150) return 'This video cannot play inline. Watch on YouTube instead.';
  return 'YouTube could not load this video. Try again or watch on YouTube.';
}
