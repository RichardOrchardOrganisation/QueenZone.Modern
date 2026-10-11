const { test } = require('node:test');
const assert = require('node:assert/strict');
const s = require('../../src/QueenZone.Web/wwwroot/js/forum/youtube-scheduler.js');
const card = (distance, extra = {}) => ({ distance, state: 'idle', preload: true, evictAt: null, ...extra });

test('closest preload candidates win, never more than three', () => {
  const cards = [80, 10, 60, 20, 40].map(n => card(n));
  assert.deepEqual(s.priority(cards).map(c => c.distance), [10, 20, 40]);
  assert.equal(s.priority(Array.from({length: 30}, (_, i) => card(i))).length, 3);
  assert.equal(s.priority([card(0, { preload: false })]).length, 0);
});
test('visible playing frame is first and cannot be evicted', () => {
  const playing = card(999, { state: 'mounted', playerState: 1, visible: true, evictAt: 0 });
  assert.equal(s.priority([card(0), card(1), card(2), playing])[0], playing);
  assert.equal(s.shouldEvict(playing, 1000), false);
  assert.equal(s.shouldEvict({ ...playing, visible: false }, 1000), true);
});
test('unknown and paused states do not get playing priority', () => {
  for (const playerState of [null, -1, 0, 2, 3, 5]) {
    assert.equal(s.priority([card(900, { state: 'mounted', visible: true, playerState }), card(1)])[0].distance, 1);
  }
});
test('retention debounce cancels on reentry and restarts on next exit', () => {
  let c = card(0, { state: 'mounted' });
  c.evictAt = s.retention(c, false, 100);
  assert.equal(c.evictAt, 350);
  assert.equal(s.retention(c, false, 200), 350);
  assert.equal(s.shouldEvict(c, 349), false);
  c.evictAt = s.retention(c, true, 349);
  assert.equal(s.shouldEvict(c, 500), false);
  c.evictAt = s.retention(c, false, 501);
  assert.equal(s.shouldEvict(c, 750), false);
  assert.equal(s.shouldEvict(c, 751), true);
});
test('pending debounce keeps its slot even when nearer cards arrive', () => {
  const pending = card(999, { state: 'mounted', preload: false, evictAt: 500 });
  assert.ok(s.priority([pending, card(1), card(2), card(3)]).includes(pending));
});
test('fullscreen skips eviction, including after deadline; exit permits eviction', () => {
  const c = card(999, { state: 'mounted', fullscreen: true, evictAt: 0 });
  assert.equal(s.shouldEvict(c, 1000), false);
  assert.ok(s.priority([card(1), card(2), card(3), c]).includes(c));
  assert.equal(s.shouldEvict({ ...c, fullscreen: false }, 1000), true);
});
test('failed frees a slot and is never selected for automatic retry', () => {
  const failed = card(0, { state: 'failed' });
  assert.equal(s.occupied(failed), false);
  assert.deepEqual(s.priority([failed, card(1), card(2), card(3)]).map(c => c.distance), [1, 2, 3]);
  assert.equal(s.occupied(card(1, {state: 'mounting'})), true);
});
test('cleanup generation invalidates queued callbacks', () => {
  assert.equal(s.current(4, 4), true);
  assert.equal(s.current(4, 5), false);
});
test('bridge only parses allowlisted events and numeric states/errors', () => {
  assert.deepEqual(s.parseMessage('{"event":"onStateChange","info":1}'), {playerState: 1});
  assert.deepEqual(s.parseMessage('{"event":"infoDelivery","info":{"playerState":2}}'), {playerState: 2});
  assert.deepEqual(s.parseMessage('{"event":"onReady"}'), {ready: true});
  for (const code of [2, 5, 100, 101, 150, 153]) assert.deepEqual(s.parseMessage(JSON.stringify({event:'onError',info:code})), {error:code});
  for (const value of ['bad', 'null', '{}', '{"event":"onStateChange","info":"1"}', '{"event":"onStateChange","info":99}', '{"event":"onError","info":999}', '{"event":"command","info":1}', {event:'onReady'}]) assert.equal(s.parseMessage(value), null);
});
