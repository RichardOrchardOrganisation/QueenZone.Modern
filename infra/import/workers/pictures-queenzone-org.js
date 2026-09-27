const ORIGIN = 'https://queenzoneprod.blob.core.windows.net';
const EDGE_TTL_SECONDS = 60 * 60 * 24 * 30;
const BROWSER_TTL_SECONDS = 60 * 60 * 24;

function withHeaders(response) {
  const headers = new Headers(response.headers);
  if (response.status === 200) {
    headers.set('Cache-Control', 'public, max-age=' + BROWSER_TTL_SECONDS + ', s-maxage=' + EDGE_TTL_SECONDS);
  }
  headers.set('Access-Control-Allow-Origin', '*');
  headers.set('X-Content-Type-Options', 'nosniff');
  return new Response(response.body, {
    status: response.status,
    statusText: response.statusText,
    headers
  });
}

export default {
  async fetch(request, env, ctx) {
    if (request.method !== 'GET' && request.method !== 'HEAD') {
      return new Response('Method Not Allowed', {
        status: 405,
        headers: { Allow: 'GET, HEAD' }
      });
    }

    const incomingUrl = new URL(request.url);
    if (incomingUrl.pathname === '/' || incomingUrl.pathname === '') {
      return new Response('Not Found', { status: 404 });
    }

    // Fan-performance audio is member-gated and streamed by the app (#177).
    // Do not anonymously proxy /songfiles/* even if the container ACL lags.
    if (incomingUrl.pathname === '/songfiles' || incomingUrl.pathname.startsWith('/songfiles/')) {
      return new Response('Not Found', { status: 404 });
    }

    // Legacy forum attachments are member-gated and streamed by the app (#1656).
    // Do not anonymously proxy /attachments/* even if the container ACL lags.
    if (incomingUrl.pathname === '/attachments' || incomingUrl.pathname.startsWith('/attachments/')) {
      return new Response('Not Found', { status: 404 });
    }

    const originUrl = new URL(incomingUrl.pathname + incomingUrl.search, ORIGIN);
    const hasRange = request.headers.has('Range');
    const cache = caches.default;
    const cacheKey = new Request(incomingUrl.toString(), { method: 'GET' });

    if (request.method === 'GET' && !hasRange) {
      const cached = await cache.match(cacheKey);
      if (cached) return cached;
    }

    const originHeaders = new Headers();
    for (const name of ['Accept', 'Accept-Encoding', 'If-Modified-Since', 'If-None-Match', 'Range']) {
      const value = request.headers.get(name);
      if (value) originHeaders.set(name, value);
    }

    const originResponse = await fetch(originUrl.toString(), {
      method: request.method,
      headers: originHeaders,
      cf: {
        cacheEverything: request.method === 'GET' && !hasRange,
        cacheTtl: EDGE_TTL_SECONDS
      }
    });

    const response = withHeaders(originResponse);
    if (request.method === 'GET' && !hasRange && response.status === 200) {
      ctx.waitUntil(cache.put(cacheKey, response.clone()));
    }
    return response;
  }
};
