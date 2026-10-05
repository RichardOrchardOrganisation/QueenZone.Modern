import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';

const source = readFileSync(new URL('../src/QueenZone.Web/wwwroot/js/theme.js', import.meta.url), 'utf8');
function boot({ theme, saved, offline = false, osDark = false, unavailable = false } = {}) {
  const root = { dataset: { theme }, hasAttribute: () => offline };
  const store = new Map(saved ? [['qz.offline.appearance', saved]] : []);
  const meta = { removeAttribute() {}, content: '' };
  let change;
  const system = { matches: osDark, addEventListener: (_, callback) => { change = callback; } };
  const storage = {
    getItem(key) { if (unavailable) throw Error('unavailable'); return store.get(key); },
    setItem(key, value) { if (unavailable) throw Error('unavailable'); store.set(key, value); },
    removeItem(key) { if (unavailable) throw Error('unavailable'); store.delete(key); },
  };
  vm.runInNewContext(source, {
    document: { documentElement: root, querySelectorAll: () => [meta], addEventListener() {} },
    window: { matchMedia: () => system }, localStorage: storage,
  });
  return { root, store, meta, change: (dark) => { system.matches = dark; change(); } };
}

test('forced modes override the OS and update chrome immediately', () => {
  for (const theme of ['light', 'dark']) {
    const page = boot({ theme, osDark: theme === 'light' });
    assert.equal(page.meta.content, theme === 'dark' ? '#111111' : '#FFFFFF');
    page.change(theme === 'light');
    assert.equal(page.meta.content, theme === 'dark' ? '#111111' : '#FFFFFF');
    assert.equal(page.store.get('qz.offline.appearance'), theme);
  }
});
test('system mode follows OS changes and clears the previous account hint on logout', () => {
  const page = boot({ saved: 'dark' });
  assert.equal(page.store.size, 0);
  assert.equal(page.meta.content, '#FFFFFF');
  page.change(true);
  assert.equal(page.meta.content, '#111111');
});
test('an offline public shell restores only a valid choice before styles load', () => {
  const page = boot({ offline: true, saved: 'light', osDark: true });
  assert.equal(page.root.dataset.theme, 'light');
  assert.equal(page.meta.content, '#FFFFFF');
  const invalid = boot({ offline: true, saved: 'garbage', osDark: true });
  assert.equal(invalid.root.dataset.theme, undefined);
  assert.equal(invalid.meta.content, '#111111');
});
test('storage failure leaves server appearance usable', () => {
  const page = boot({ theme: 'dark', unavailable: true });
  assert.equal(page.meta.content, '#111111');
});
