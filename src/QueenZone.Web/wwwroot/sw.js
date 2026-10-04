const CACHE_NAME = "qz-static-v2";
const STATIC_PATTERNS = [/\/css\//, /\/design-system\//, /\/js\//, /favicon/, /apple-touch-icon/];

self.addEventListener("install", () => {
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((key) => key !== CACHE_NAME).map((key) => caches.delete(key))))
  );
  self.clients.claim();
});

self.addEventListener("fetch", (event) => {
  const { request } = event;
  if (request.method !== "GET") {
    return;
  }

  const url = new URL(request.url);
  if (url.origin !== self.location.origin) {
    return;
  }

  // ES module imports do not receive Razor's content-hash query. Revalidate
  // these public dependencies online; retain the last working copy offline.
  if (/^\/js\/crossword-(core|account)\.js$/.test(url.pathname) && !url.searchParams.has("v")) {
    event.respondWith(networkFirstWithCacheFallback(new Request(request, { cache: "no-cache" })));
    return;
  }

  if (STATIC_PATTERNS.some((pattern) => pattern.test(url.pathname))) {
    event.respondWith(cacheFirst(request));
    return;
  }

  if (request.mode === "navigate") {
    if (/^\/crosswords\/[^/]+$/.test(url.pathname)) {
      event.respondWith(crosswordNetworkFirst(request));
      return;
    }
    event.respondWith(networkFirstWithCacheFallback(request));
  }
});

async function crosswordNetworkFirst(request) {
  const cache = await caches.open(CACHE_NAME);
  try {
    const response = await fetch(request);
    if (response.ok) {
      const shellUrl = new URL(request.url);
      shellUrl.search = '?handler=OfflineShell';
      // Never store the authenticated HTML, header or antiforgery token.
      try {
        const shell = await fetch(shellUrl, { credentials: 'omit', cache: 'no-store' });
        if (shell.ok && shell.headers.get('X-QueenZone-Crossword-Shell') === 'public')
          await cache.put(request, shell.clone());
      } catch { /* A cache refresh failure must not replace a successful live page. */ }
    }
    return response;
  } catch {
    const cached = await cache.match(request);
    if (cached) return cached;
    throw new Error('Needs a connection to open this crossword for the first time.');
  }
}

async function cacheFirst(request) {
  const cache = await caches.open(CACHE_NAME);
  const cached = await cache.match(request);
  if (cached) {
    return cached;
  }

  const response = await fetch(request);
  if (response.ok) {
    cache.put(request, response.clone());
  }
  return response;
}

async function networkFirstWithCacheFallback(request) {
  const cache = await caches.open(CACHE_NAME);
  try {
    const response = await fetch(request);
    if (response.ok) {
      cache.put(request, response.clone());
    }
    return response;
  } catch {
    const cached = await cache.match(request);
    if (cached) {
      return cached;
    }
    throw new Error("Network request failed and no cached response was available");
  }
}
