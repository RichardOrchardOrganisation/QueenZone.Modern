import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import vm from 'node:vm';
import { parsePlayerMessage, pauseScript, playerBaseUrl, playerDocument, playerError, playerNavigation } from './playerDocument.ts';
const video = { provider: 'youtube' as const, videoId: 'M7lc1UVf-VE', startSeconds: 90, watchUrl: 'https://www.youtube.com/watch?v=M7lc1UVf-VE&t=90s', anchorIndex: 0 };
describe('fixed native player document', () => {
  it('uses HTTPS store identity and fixed content without credentials or user queries', () => {
    const html = playerDocument(video, 7);
    assert.equal(playerBaseUrl, 'https://org.queenzone.mobile/');
    assert.match(html, /strict-origin-when-cross-origin/); assert.match(html, /autoplay:0/);
    assert.match(html, /start:90/); assert.match(html, /frame-src https:\/\/www.youtube.com/);
    assert.doesNotMatch(html, /playVideo\(|loadVideoById|Authorization|watchUrl|cookie|anchorIndex/);
    assert.match(pauseScript, /qzPause/);
    assert.throws(() => playerDocument({ ...video, videoId: '<script>' }, 1));
    assert.throws(() => playerDocument({ ...video, startSeconds: 86401 }, 1));
    assert.throws(() => playerDocument(video, NaN));
  });
  it('initialization, readiness and fullscreen never invoke play; only pause is exposed', () => {
    const html = playerDocument(video, 7);
    const script = html.split('<script>')[1].split('</script>')[0];
    const sent: string[] = []; const pause: string[] = [];
    let config: { events: { onReady: () => void; onStateChange: (e: { data: number }) => void; onError: (e: { data: number }) => void }; playerVars: { autoplay: number } };
    const context = vm.createContext({ window: { ReactNativeWebView: { postMessage: (m: string) => sent.push(m) } }, document: { addEventListener: () => {} }, YT: { Player: function (_id: string, options: typeof config) { config = options; return { pauseVideo: () => pause.push('pause') }; } } });
    vm.runInContext(script, context); vm.runInContext('onYouTubeIframeAPIReady()', context);
    assert.equal(config!.playerVars.autoplay, 0); config!.events.onReady(); config!.events.onStateChange({ data: 1 }); config!.events.onError({ data: 153 });
    vm.runInContext('window.qzNativeFullscreen(true);window.qzPause()', context);
    assert.deepEqual(sent.map(m => JSON.parse(m).event), ['ready', 'state', 'error', 'fullscreen']); assert.deepEqual(pause, ['pause']);
  });
  it('accepts only bounded known messages for the current incarnation', () => {
    const m = (event: string, value?: unknown, generation = 7) => JSON.stringify({ version: 1, generation, event, ...(value === undefined ? {} : { value }) });
    assert.deepEqual(parsePlayerMessage(m('ready'), 7), { event: 'ready' });
    for (const value of [-1, 0, 1, 2, 3, 5]) assert.deepEqual(parsePlayerMessage(m('state', value), 7), { event: 'state', value });
    for (const value of [2, 5, 100, 101, 150, 153]) assert.ok(parsePlayerMessage(m('error', value), 7));
    assert.deepEqual(parsePlayerMessage(m('fullscreen', true), 7), { event: 'fullscreen', value: true });
    for (const data of ['bad', 'null', 'x'.repeat(257), m('state', '1'), m('state', 6), m('error', 42), m('fullscreen', 1), m('ready', undefined, 6), m('openURL', 'https://evil.test'), JSON.stringify({ version: 1, generation: 7, event: 'ready', privileged: true })]) assert.equal(parsePlayerMessage(data, 7), null);
  });
  it('distinguishes top-level navigation, approved subframes and deliberate HTTPS links', () => {
    assert.equal(playerNavigation(playerBaseUrl, true, 'other'), 'allow');
    assert.equal(playerNavigation('about:blank', true, 'other'), 'allow');
    assert.equal(playerNavigation('https://www.youtube.com/embed/M7lc1UVf-VE?autoplay=0', false, 'other'), 'allow');
    assert.equal(playerNavigation('https://www.youtube.com/embed/M7lc1UVf-VE', true, 'other'), 'reject');
    assert.equal(playerNavigation('https://www.youtube.com/watch?v=M7lc1UVf-VE', true, 'click'), 'external');
    for (const url of ['file:///tmp/a', 'content://a', 'javascript:1', 'intent://a', 'data:text/html,a', 'http://youtube.com/', 'https://u@youtube.com/', 'https://youtube.com:444/', 'garbage']) assert.equal(playerNavigation(url, true, 'click'), 'reject');
    assert.equal(playerNavigation('https://evil.test/', false, 'other'), 'reject');
    assert.equal(playerNavigation('https://youtube.com/', undefined, 'other'), 'reject');
  });
  it('explains identification, unavailable, disabled and generic failures', () => {
    assert.match(playerError(153), /153/); assert.match(playerError(100), /private/);
    assert.match(playerError(101), /cannot play inline/); assert.match(playerError(150), /cannot play inline/);
    assert.match(playerError('timeout'), /Try again/);
  });
});
