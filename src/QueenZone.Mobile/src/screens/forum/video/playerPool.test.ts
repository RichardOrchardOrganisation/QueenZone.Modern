import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { PlayerPool, evictionDelayMs } from './playerPool.ts';
function fixture() {
  const pauses: string[] = [];
  const pool = new PlayerPool(() => {}, key => pauses.push(key));
  pool.setEnabled(true);
  const add = (key: string, top: number, now = 0) => {
    const owner = pool.register(key);
    pool.measure(key, owner, { top, bottom: top + 200, width: 320, viewportTop: 0, viewportBottom: 800 }, now);
    return owner;
  };
  const move = (key: string, top: number, now = 0, width = 320) => pool.measure(key, pool.cards[key].owner, { top, bottom: top + 200, width, viewportTop: 0, viewportBottom: 800 }, now);
  const mounted = () => Object.values(pool.cards).filter(c => c.generation != null);
  return { pool, pauses, add, move, mounted };
}
describe('bounded mobile YouTube pool', () => {
  it('preloads at 300px but render-ahead and unknown geometry do not load', () => {
    const f = fixture(); f.pool.register('unmeasured'); f.add('far', 1101);
    assert.equal(f.mounted().length, 0);
    f.move('far', 1100); assert.equal(f.mounted().length, 1);
    f.add('above', -500); assert.equal(f.mounted().length, 2);
    f.add('narrow', 0); f.move('narrow', 0, 0, 199); assert.equal(f.pool.cards.narrow.generation, null);
  });
  it('retains to 600px and debounces eviction, cancelling it on reentry', () => {
    const f = fixture(); f.add('a', 0); const generation = f.pool.cards.a.generation;
    f.move('a', 1400); assert.equal(f.pool.cards.a.generation, generation);
    f.move('a', 1401, 1000); assert.equal(f.pool.cards.a.evictionAt, 1000 + evictionDelayMs);
    f.pool.tick(1249); assert.equal(f.pool.cards.a.generation, generation);
    f.move('a', 1400, 1249); assert.equal(f.pool.cards.a.evictionAt, null);
    f.move('a', 1401, 2000); f.pool.tick(2250); assert.equal(f.pool.cards.a.generation, null);
    assert.deepEqual(f.pauses, ['a']);
    f.move('a', 0, 2300); assert.notEqual(f.pool.cards.a.generation, generation);
    assert.equal(f.pool.cards.a.playing, false);
  });
  it('caps at two, prioritizes a visible playing card and pauses neighbors on user play', () => {
    const f = fixture(); f.add('a', 0); f.add('b', 400);
    f.pool.message('a', f.pool.cards.a.generation!, 'state', 1);
    f.add('c', 300); assert.equal(f.mounted().length, 2); assert.notEqual(f.pool.cards.a.generation, null);
    const other = f.mounted().find(c => c.key !== 'a')!;
    f.pool.message(other.key, other.generation!, 'state', 1);
    assert.equal(f.pool.cards.a.playing, false); assert.equal(f.pool.cards[other.key].playing, true);
    assert.ok(f.pauses.includes('a'));
  });
  it('ignores stale player/row callbacks and keeps failures sticky across recycling until Retry', () => {
    const f = fixture(); const owner = f.add('a', 0); const generation = f.pool.cards.a.generation!;
    f.pool.fail('a', generation, 'Unavailable'); assert.equal(f.pool.cards.a.generation, null);
    for (let i = 0; i < 10; i++) f.move('a', 0);
    assert.equal(f.pool.cards.a.generation, null);
    f.pool.remove('a', owner); const nextOwner = f.add('a', 0);
    assert.equal(f.pool.cards.a.failed, 'Unavailable'); assert.equal(f.pool.cards.a.generation, null);
    f.pool.retry('a', owner); assert.equal(f.pool.cards.a.failed, 'Unavailable');
    f.pool.retry('a', nextOwner); assert.notEqual(f.pool.cards.a.generation, null);
    f.pool.message('a', generation, 'state', 1); f.pool.fail('a', generation, 'stale');
    assert.equal(f.pool.cards.a.playing, false); assert.equal(f.pool.cards.a.failed, null);
    f.pool.remove('a', owner); assert.ok(f.pool.cards.a);
  });
  it('preserves native fullscreen despite viewport changes but backgrounds release it', () => {
    const f = fixture(); f.add('a', 0); const gen = f.pool.cards.a.generation!;
    f.pool.message('a', gen, 'fullscreen', true); f.move('a', 5000); f.pool.tick(10000);
    assert.equal(f.pool.cards.a.generation, gen);
    f.pool.setEnabled(false); assert.equal(f.pool.cards.a.generation, null); assert.equal(f.pool.cards.a.fullscreen, false);
    f.pool.message('a', gen, 'fullscreen', true); assert.equal(f.pool.cards.a.fullscreen, false);
    f.pool.setEnabled(true); assert.equal(f.pool.cards.a.generation, null);
    f.move('a', 0); assert.equal(f.pool.cards.a.playing, false);
  });
  it('bounds mounts through 30-card rapid scroll and never marks mounting as playing', () => {
    const f = fixture(); for (let i = 0; i < 30; i++) f.add(`card${i}`, i * 500);
    let peak = 0;
    for (let step = 0; step < 30; step++) {
      for (let i = 0; i < 30; i++) f.move(`card${i}`, i * 500 - step * 500, step * 400);
      f.pool.tick(step * 400 + 250); peak = Math.max(peak, f.mounted().length);
      assert.ok(f.mounted().length <= 2); assert.ok(f.mounted().every(c => !c.playing));
    }
    assert.equal(peak, 2);
    f.pool.setEnabled(false); assert.equal(f.mounted().length, 0);
  });
  it('batches native row measurements so a scroll frame mounts only the final nearest candidates', () => {
    const f = fixture();
    const owners = Array.from({ length: 30 }, (_, i) => f.pool.register(`batch${i}`));
    for (let i = 29; i >= 0; i--) f.pool.measure(`batch${i}`, owners[i], { top: i * 500, bottom: i * 500 + 200, width: 320, viewportTop: 0, viewportBottom: 800 }, 0, true);
    assert.equal(f.mounted().length, 0);
    f.pool.tick(0); assert.deepEqual(f.mounted().map(c => c.key), ['batch0', 'batch1']);
    const generations = f.mounted().map(c => c.generation);
    for (let i = 0; i < 30; i++) f.pool.measure(`batch${i}`, owners[i], { top: i * 500 - 10000, bottom: i * 500 - 9800, width: 320, viewportTop: 0, viewportBottom: 800 }, 1000, true);
    assert.deepEqual(f.mounted().map(c => c.generation), generations);
    f.pool.tick(1000); f.pool.tick(1250);
    assert.deepEqual(f.mounted().map(c => c.key), ['batch20', 'batch21']);
    assert.equal(f.mounted().length, 2);
  });
  it('repeated measurements keep generation stable and zero-sized fullscreen measurements are ignored', () => {
    const f = fixture(); const owner = f.add('a', 0); const gen = f.pool.cards.a.generation;
    for (let i = 0; i < 50; i++) f.move('a', 0);
    assert.equal(f.pool.cards.a.generation, gen);
    f.pool.measure('a', owner, { top: 0, bottom: 0, width: 0, viewportTop: 0, viewportBottom: 800 });
    assert.equal(f.pool.cards.a.generation, gen);
    f.pool.remove('a', owner); assert.equal(f.mounted().length, 0);
  });
});
