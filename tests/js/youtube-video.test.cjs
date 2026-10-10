const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { runInNewContext } = require('node:vm');
const scheduler = require('../../src/QueenZone.Web/wwwroot/js/forum/youtube-scheduler.js');
const script = readFileSync(require.resolve('../../src/QueenZone.Web/wwwroot/js/forum/youtube-video.js'), 'utf8');

function fixture({ count = 5, observer = true, origin = 'https://qz.example', online = true, width = 640 } = {}) {
  const timers = new Map();
  let now = 0, nextTimer = 0;
  const eventTarget = value => Object.assign(value, {
    listeners: {},
    addEventListener(name, handler, options = {}) {
      (this.listeners[name] ??= []).push({ handler, signal: options.signal });
    },
    fire(name, event = {}) {
      for (const listener of this.listeners[name] ?? []) if (!listener.signal?.aborted) listener.handler(event);
    }
  });
  const document = eventTarget({ hidden: false, fullscreenElement: null, activeElement: null, body: {} });
  const allFrames = [];
  document.createElement = () => {
    const frame = eventTarget({ messages: [], setAttribute() {}, remove() { this.parent.frames = this.parent.frames.filter(f => f !== this); } });
    frame.contentWindow = { postMessage: (message, origin) => frame.messages.push({ ...JSON.parse(message), origin }) };
    allFrames.push(frame);
    return frame;
  };
  const cards = Array.from({ length: count }, (_, i) => {
    const viewport = { frames: [], clientWidth: width, clientHeight: 360, top: i * 400,
      getBoundingClientRect() { return { top: this.top, bottom: this.top + 360, left: 0, right: width }; },
      appendChild(frame) { this.frames.push(frame); frame.parent = this; }
    };
    const link = { focus() { document.activeElement = this; } };
    const button = eventTarget({});
    const placeholder = { hidden: false }, status = {};
    const elements = { '[data-video-viewport]': viewport, '[data-video-placeholder]': placeholder,
      '[data-video-load]': button, '[data-video-status]': status, '.qz-forum-video__fallback a': link };
    const card = { isConnected: true, dataset: { videoId: `QZ${String(i).padStart(9, '0')}`, qzPlayerOrigin: origin },
      querySelector: selector => elements[selector], contains: element => element === card || Object.values(elements).includes(element) || viewport.frames.includes(element),
      viewport, button, link, placeholder, status };
    return card;
  });
  document.querySelectorAll = () => cards;
  const observers = [];
  class Observer {
    constructor(callback, options) { this.callback = callback; this.options = options; this.disconnected = false; observers.push(this); }
    observe() {}
    disconnect() { this.disconnected = true; }
    deliver(indices, isIntersecting) { this.callback(indices.map(i => ({ target: cards[i].viewport, isIntersecting }))); }
  }
  let mutation;
  const context = eventTarget({ QzYoutubeScheduler: scheduler, document, navigator: { onLine: online }, location: { origin: 'https://qz.example' },
    innerHeight: 800, innerWidth: 1280, URL, AbortController, performance: { now: () => now },
    setTimeout(fn, delay) { const id = ++nextTimer; timers.set(id, { fn, at: now + delay }); return id; },
    clearTimeout(id) { timers.delete(id); },
    MutationObserver: class { constructor(callback) { mutation = this; this.callback = callback; } observe() {} disconnect() { this.disconnected = true; } }
  });
  context.requestAnimationFrame = fn => context.setTimeout(fn, 16);
  context.cancelAnimationFrame = context.clearTimeout;
  if (observer) context.IntersectionObserver = Observer;
  context.window = context;
  runInNewContext(script, context);
  return { context, document, cards, observers, timers, allFrames, mutation,
    frames: () => cards.flatMap(c => c.viewport.frames),
    message(frame, data, origin = 'https://www.youtube-nocookie.com', source = frame.contentWindow) {
      context.fire('message', { origin, source, data: JSON.stringify(data) });
    },
    advance(ms) {
      now += ms;
      for (const [id, timer] of [...timers]) if (timer.at <= now) { timers.delete(id); timer.fn(); }
    }
  };
}

test('adapter mounts only eligible preload cards and guards duplicate observer deliveries', () => {
  const f = fixture({count:30});
  assert.equal(f.frames().length, 0);
  assert.deepEqual(f.observers.map(o => o.options.rootMargin), ['300px 0px', '600px 0px']);
  f.observers[0].deliver([0,1,2,3,4], true);
  f.observers[0].deliver([0,1,2,3,4], true);
  assert.equal(f.frames().length, 3);
  assert.ok(f.cards.every(c => c.viewport.frames.length <= 1));
  assert.equal(f.cards[29].viewport.frames.length, 0);
  const url = new URL(f.frames()[0].src);
  assert.equal(url.searchParams.get('autoplay'), '0');
  assert.equal(url.searchParams.get('origin'), 'https://qz.example');
  assert.equal(url.searchParams.get('enablejsapi'), '1');
});
test('real-source state pauses others; spoofed messages and load do not imply playback', () => {
  const f = fixture(); f.observers[0].deliver([0,1], true);
  const [a,b] = f.frames();
  a.fire('load');
  assert.deepEqual(a.messages.map(m => m.event), ['listening']);
  assert.equal(a.messages[0].channel, 'widget');
  f.advance(250);
  assert.equal(a.messages.filter(m => m.event === 'listening').length, 2);
  f.message(a, {event:'onReady'});
  f.advance(500);
  assert.equal(a.messages.filter(m => m.event === 'listening').length, 2);
  f.message(b, {event:'onStateChange',info:1}, 'https://evil.example');
  f.message(b, {event:'onStateChange',info:1}, 'https://www.youtube-nocookie.com', {});
  assert.equal(a.messages.length, 2);
  assert.ok(a.messages.every(m => m.event === 'listening'));
  f.message(b, {event:'onStateChange',info:1});
  assert.equal(a.messages.at(-1).func, 'pauseVideo');
  f.document.hidden = true; f.document.fire('visibilitychange');
  assert.equal(b.messages.at(-1).func, 'pauseVideo');
  const count = a.messages.length + b.messages.length;
  f.document.hidden = false; f.document.fire('visibilitychange');
  assert.equal(a.messages.length + b.messages.length, count);
  assert.ok(f.allFrames.flatMap(frame => frame.messages).every(m =>
    (m.event === 'listening' || m.func === 'pauseVideo') && m.channel === 'widget' && m.func !== 'playVideo'));
});
test('retention cancels on re-entry, skips fullscreen and preserves focus on eviction', () => {
  const f = fixture(); f.observers[0].deliver([0], true);
  const card = f.cards[0];
  card.viewport.top = 2000;
  f.observers[0].deliver([0], false); f.observers[1].deliver([0], false);
  f.advance(249); assert.equal(f.frames().length, 1);
  card.viewport.top = 0;
  f.observers[1].deliver([0], true); f.advance(10); assert.equal(f.frames().length, 1);
  card.viewport.top = 2000;
  f.document.fullscreenElement = card;
  f.observers[1].deliver([0], false); f.advance(300); assert.equal(f.frames().length, 1);
  f.document.activeElement = f.frames()[0];
  f.document.fullscreenElement = null; f.document.fire('fullscreenchange');
  assert.equal(f.frames().length, 0);
  assert.equal(f.document.activeElement, card.link);
  assert.equal(card.placeholder.hidden, false);
});
test('timeout survives iframe load; failure frees a slot and never retries without a click', () => {
  const f = fixture(); f.observers[0].deliver([0], true);
  f.frames()[0].fire('load'); f.advance(12000);
  assert.equal(f.frames().length, 0);
  assert.equal(f.cards[0].button.textContent, 'Retry');
  f.context.fire('resize'); assert.equal(f.frames().length, 0);
  f.cards[0].button.fire('click'); assert.equal(f.frames().length, 1);
  f.message(f.frames()[0], {event:'onError',info:153});
  assert.equal(f.frames().length, 0);
  assert.equal(f.cards[0].button.textContent, 'Retry');
});
test('offline, undersized and hidden cards never mount', () => {
  for (const options of [{online:false},{width:199}]) {
    const f = fixture(options); f.observers[0].deliver([0], true); f.cards[0].button.fire('click');
    assert.equal(f.frames().length, 0);
  }
  const f = fixture(); f.document.hidden = true; f.observers[0].deliver([0], true);
  assert.equal(f.frames().length, 0);
});
test('origin mismatch and missing observer require explicit clicks and keep one player', () => {
  for (const options of [{observer:false},{origin:'https://wrong.example'}]) {
    const f = fixture(options); assert.equal(f.frames().length, 0);
    f.cards[0].button.fire('click'); assert.equal(f.frames().length, 1);
    f.cards[1].button.fire('click'); assert.equal(f.frames().length, 1);
    assert.equal(f.cards[0].viewport.frames.length, 0);
    if (options.origin) assert.equal(new URL(f.frames()[0].src).searchParams.has('origin'), false);
    f.cards[1].button.fire('click'); assert.equal(f.frames().length, 0);
  }
});
test('pageshow persisted reinitialises listeners without resuming playback', () => {
  const auto = fixture();
  auto.observers[0].deliver([0], true);
  assert.equal(auto.frames().length, 1);
  auto.context.fire('pagehide');
  assert.equal(auto.frames().length, 0);
  auto.cards[0].button.fire('click');
  auto.observers[0].deliver([0], true);
  assert.equal(auto.frames().length, 0);
  auto.context.fire('pageshow', { persisted: false });
  auto.cards[0].button.fire('click');
  assert.equal(auto.frames().length, 0);
  auto.context.fire('pageshow', { persisted: true });
  const live = auto.observers.filter(observer => !observer.disconnected);
  assert.equal(live.length, 2);
  live[0].deliver([0, 1], true);
  assert.equal(auto.frames().length, 2);
  auto.cards[3].button.fire('click');
  assert.equal(auto.cards[3].viewport.frames.length, 1);
  assert.ok(auto.allFrames.flatMap(frame => frame.messages).every(message =>
    (message.event === 'listening' || message.func === 'pauseVideo') && message.func !== 'playVideo'));

  const fallback = fixture({ observer: false });
  fallback.cards[0].button.fire('click');
  assert.equal(fallback.frames().length, 1);
  fallback.context.fire('pagehide');
  fallback.cards[0].button.fire('click');
  assert.equal(fallback.frames().length, 0);
  fallback.context.fire('pageshow', { persisted: true });
  fallback.cards[0].button.fire('click');
  assert.equal(fallback.frames().length, 1);
  assert.ok(fallback.allFrames.flatMap(frame => frame.messages).every(message => message.func !== 'playVideo'));
});
test('pagehide and removed cards release resources and invalidate queued callbacks', () => {
  for (const removed of [false,true]) {
    const f = fixture(); f.observers[0].deliver([0], true);
    const frame = f.frames()[0];
    if (removed) { f.cards[0].isConnected = false; f.mutation.callback(); }
    else f.context.fire('pagehide');
    assert.equal(f.frames().length, 0);
    assert.equal(f.timers.size, 0);
    assert.ok(f.observers.every(o => o.disconnected));
    assert.equal(f.mutation.disconnected, true);
    f.observers[0].deliver([0,1], true); frame.fire('load'); f.cards[0].button.fire('click');
    f.context.fire('resize'); f.advance(13000);
    assert.equal(f.frames().length, 0);
  }
});
test('visible playing player survives closer candidates and bridge errors fill the freed slot', () => {
  const f = fixture();
  f.observers[0].deliver([0,1,2], true);
  const playing = f.cards[0].viewport.frames[0];
  f.message(playing, {event:'onStateChange',info:1});
  f.cards[3].viewport.top = 100; f.cards[4].viewport.top = 200;
  f.observers[0].deliver([3,4], true);
  assert.ok(f.frames().includes(playing));
  assert.equal(f.frames().length, 3);
  f.message(playing, {event:'onError',info:101});
  assert.equal(f.cards[0].button.textContent, 'Retry');
  assert.equal(f.frames().length, 3);
  assert.ok(!f.frames().includes(playing));
});
test('automatic mount never steals focus from outside the card', () => {
  const f = fixture(); const elsewhere = {};
  f.document.activeElement = elsewhere; f.observers[0].deliver([0], true);
  assert.equal(f.document.activeElement, elsewhere);
});
test('scroll before the retain observer cannot bypass the eviction debounce', () => {
  const f = fixture(); f.observers[0].deliver([0,1,2], true);
  const leaving = f.frames()[0];
  f.cards[0].viewport.top = -2000; f.cards[3].viewport.top = 100;
  f.observers[0].deliver([3], true);
  assert.ok(f.frames().includes(leaving));
  f.advance(249); assert.ok(f.frames().includes(leaving));
  f.advance(1); assert.ok(!f.frames().includes(leaving));
  assert.equal(f.frames().length, 3);
});
