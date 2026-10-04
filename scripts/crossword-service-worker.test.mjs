import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';

const source = readFileSync(new URL('../src/QueenZone.Web/wwwroot/sw.js', import.meta.url), 'utf8');
const origin = 'https://crossword.example';

function worker({ stored = new Map(), fetch, put } = {}) {
  const listeners = new Map();
  const cache = {
    match: async request => stored.get(typeof request === 'string' ? request : request.url)?.clone(),
    put: put ?? (async (request, response) => { stored.set(request.url, response); }),
  };
  const context = vm.createContext({
    self: { location: { origin }, addEventListener: (name, listener) => listeners.set(name, listener) },
    caches: { open: async () => cache }, URL, Request, fetch,
  });
  vm.runInContext(source, context);
  return path => {
    let response;
    listeners.get('fetch')({ request: new Request(origin + path), respondWith: value => { response = value; } });
    assert.ok(response, 'The public asset must be handled by the worker');
    return response;
  };
}

for (const path of ['/js/crossword-core.js', '/js/crossword-account.js']) {
  test(`${path} revalidates online and waits for its replacement to be stored`, async () => {
    const stored = new Map([[origin + path, new Response('obsolete')]]);
    let finishWrite;
    let requested;
    const handle = worker({ stored,
      fetch: async request => { requested = request; return new Response('export const current = true;'); },
      put: (request, response) => new Promise(resolve => { finishWrite = () => { stored.set(request.url, response); resolve(); }; }),
    });
    const response = handle(path);
    let replied = false;
    response.then(() => { replied = true; });
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(requested.cache, 'no-cache');
    assert.equal(replied, false, 'The worker must retain the write within the fetch lifetime');
    finishWrite();
    assert.equal(await (await response).text(), 'export const current = true;');
    assert.equal(await stored.get(origin + path).text(), 'export const current = true;');
  });
}

test('a cached crossword module remains usable when the network fails', async () => {
  const path = '/js/crossword-core.js';
  const handle = worker({ stored: new Map([[origin + path, new Response('export const offline = true;')]]),
    fetch: async () => { throw new TypeError('Network unavailable'); },
  });
  assert.equal(await (await handle(path)).text(), 'export const offline = true;');
});

test('versioned modules and other scripts retain cache-first behavior', async () => {
  for (const path of ['/js/crossword-core.js?v=current-hash', '/js/site.js']) {
    const handle = worker({ stored: new Map([[origin + path, new Response('cached asset')]]),
      fetch: async () => { assert.fail('Unchanged cache-first assets should not fetch'); },
    });
    assert.equal(await (await handle(path)).text(), 'cached asset');
  }
});

test('static styles are stored before responding and cache failure keeps a live response', async () => {
  let finishWrite;
  const handle = worker({ fetch: async () => new Response('body { color: black; }'),
    put: () => new Promise(resolve => { finishWrite = resolve; }),
  });
  const response = handle('/css/crossword.css');
  let replied = false;
  response.then(() => { replied = true; });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(replied, false);
  finishWrite();
  assert.equal(await (await response).text(), 'body { color: black; }');
  for (const path of ['/css/crossword.css', '/js/crossword-core.js']) {
    const quotaFailure = worker({ fetch: async () => new Response('live asset'),
      put: async () => { throw new Error('Cache quota exceeded'); },
    });
    assert.equal(await (await quotaFailure(path)).text(), 'live asset');
  }
});
