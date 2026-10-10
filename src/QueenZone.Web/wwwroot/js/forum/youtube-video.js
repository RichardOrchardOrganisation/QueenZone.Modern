// Only server-generated forum cards reach this script; UGC cannot supply these attributes.
(() => {
  "use strict";
  const scheduler = window.QzYoutubeScheduler;
  const playerOrigin = "https://www.youtube-nocookie.com";
  const lifetime = new AbortController();
  const cards = [];
  let generation = 0;
  let stopped = false;
  let tick = null;
  let scrollTick = null;
  let preloadObserver;
  let retainObserver;
  let mutationObserver;
  const send = (card, message) => card.frame?.contentWindow?.postMessage(JSON.stringify(message), playerOrigin);
  const pause = card => send(card, { event: "command", func: "pauseVideo", args: [] });
  const focusFallback = card => {
    if (card.element.contains(document.activeElement)) card.link.focus({ preventScroll: true });
  };
  function release(card, failed = false) {
    if (!card.frame && !failed) return;
    pause(card);
    focusFallback(card);
    clearTimeout(card.timer);
    card.frameLifetime?.abort();
    card.frame?.remove();
    card.frame = null;
    card.state = failed ? "failed" : "idle";
    card.playerState = null;
    card.placeholder.hidden = false;
    card.button.textContent = failed ? "Retry" : "Load YouTube video";
    card.button.disabled = false;
    card.status.textContent = failed ? "Could not load the YouTube player. Watch on YouTube or Retry." : "";
  }
  function mount(card) {
    if (stopped || card.state !== "idle" || navigator.onLine === false || document.hidden ||
        card.viewport.clientWidth < 200 || card.viewport.clientHeight < 200) return;
    card.state = "mounting";
    const epoch = generation;
    const frame = document.createElement("iframe");
    card.frame = frame;
    card.frameLifetime = new AbortController();
    const alive = () => !stopped && scheduler.current(epoch, generation) && card.frame === frame;
    const fail = () => { if (alive()) { release(card, true); schedule(); } };
    const url = new URL(playerOrigin + "/embed/" + card.id);
    url.searchParams.set("autoplay", "0");
    url.searchParams.set("playsinline", "1");
    if (card.originMatches) {
      url.searchParams.set("enablejsapi", "1");
      url.searchParams.set("origin", card.element.dataset.qzPlayerOrigin);
    }
    if (card.start !== null) url.searchParams.set("start", String(card.start));
    frame.title = "YouTube video " + card.id;
    frame.referrerPolicy = "strict-origin-when-cross-origin";
    frame.allow = "encrypted-media; fullscreen";
    frame.allowFullscreen = true;
    frame.setAttribute("sandbox", "allow-scripts allow-same-origin");
    frame.className = "qz-forum-video__player";
    frame.addEventListener("load", () => {
      if (!alive()) return;
      // Load is not readiness or playback. Unknown players retain the failure deadline.
      if (card.originMatches) send(card, { event: "listening", id: card.id });
      else { clearTimeout(card.timer); card.state = "mounted"; }
    }, { signal: card.frameLifetime.signal });
    frame.addEventListener("error", fail, { signal: card.frameLifetime.signal });
    card.timer = setTimeout(fail, 12000);
    focusFallback(card);
    card.placeholder.hidden = true;
    card.button.textContent = card.auto ? "Player loaded" : "Unload YouTube video";
    card.button.disabled = card.auto;
    // Keep status dimensions stable on automatic mount/eviction.
    card.status.textContent = "";
    frame.src = url.href;
    card.viewport.appendChild(frame);
  }
  function refreshGeometry(card) {
    const rect = card.viewport.getBoundingClientRect();
    card.visible = rect.bottom > 0 && rect.top < innerHeight && rect.right > 0 && rect.left < innerWidth;
    card.distance = Math.abs((rect.top + rect.bottom - innerHeight) / 2);
    card.fullscreen = !!document.fullscreenElement && card.element.contains(document.fullscreenElement);
    if (card.auto) {
      // Scroll/observer callbacks may arrive in either order. Enforce the retention
      // deadline before capacity selection, even if the retain observer is queued.
      const horizontal = rect.right > 0 && rect.left < innerWidth;
      const retain = Number.parseInt(scheduler.RETAIN_MARGIN, 10);
      const preload = Number.parseInt(scheduler.PRELOAD_MARGIN, 10);
      card.evictAt = scheduler.retention(card, horizontal && rect.bottom >= -retain && rect.top <= innerHeight + retain, performance.now());
      card.preload = card.preload && horizontal && rect.bottom >= -preload && rect.top <= innerHeight + preload;
    }
  }
  function schedule() {
    if (stopped) return;
    clearTimeout(tick);
    tick = null;
    const now = performance.now();
    for (const card of cards) {
      refreshGeometry(card);
      if (scheduler.shouldEvict(card, now)) release(card);
    }
    const candidates = scheduler.priority(cards.filter(card => scheduler.occupied(card) ||
      (card.auto && card.preload && card.viewport.clientWidth >= 200 && card.viewport.clientHeight >= 200)));
    for (const card of cards.filter(scheduler.occupied)) {
      if (!candidates.includes(card)) release(card);
    }
    const pool = cards.filter(scheduler.occupied);
    for (const card of candidates) {
      if (!scheduler.occupied(card) && pool.length < scheduler.CAPACITY) {
        mount(card);
        if (scheduler.occupied(card)) pool.push(card);
      }
    }
    const deadlines = cards.filter(card => scheduler.occupied(card) && card.evictAt !== null && !card.fullscreen && !(card.visible && card.playerState === 1));
    if (deadlines.length) tick = setTimeout(schedule, Math.max(1, Math.min(...deadlines.map(card => card.evictAt)) - performance.now()));
  }
  for (const element of document.querySelectorAll(".qz-forum-post__body [data-qz-forum-video]")) {
    const id = element.dataset.videoId;
    const text = element.dataset.startSeconds;
    const start = text ? Number(text) : null;
    if (!/^[A-Za-z0-9_-]{11}$/.test(id || "") || (text && (!/^\d+$/.test(text) || !Number.isSafeInteger(start) || start > 86400))) continue;
    const card = { element, id, start, state: "idle", playerState: null, frame: null, evictAt: null, preload: false,
      viewport: element.querySelector("[data-video-viewport]"), placeholder: element.querySelector("[data-video-placeholder]"),
      button: element.querySelector("[data-video-load]"), status: element.querySelector("[data-video-status]"),
      link: element.querySelector(".qz-forum-video__fallback a"), originMatches: element.dataset.qzPlayerOrigin === location.origin };
    card.auto = card.originMatches && typeof IntersectionObserver !== "undefined";
    card.button.hidden = false;
    cards.push(card);
    card.button.addEventListener("click", () => {
      if (stopped) return;
      if (!card.auto && scheduler.occupied(card)) { release(card); return; }
      if (card.state === "failed") card.state = "idle";
      if (!card.auto) {
        // The fallback retains the previous single-player click-to-load behavior.
        for (const other of cards) if (other !== card) release(other);
      }
      if (cards.filter(scheduler.occupied).length < scheduler.CAPACITY) mount(card);
    }, { signal: lifetime.signal });
  }
  if (!cards.length) return;
  const epoch = generation;
  if (typeof IntersectionObserver !== "undefined") {
    preloadObserver = new IntersectionObserver(entries => {
      if (!scheduler.current(epoch, generation)) return;
      for (const entry of entries) cards.find(card => card.viewport === entry.target).preload = entry.isIntersecting;
      schedule();
    }, { rootMargin: scheduler.PRELOAD_MARGIN });
    retainObserver = new IntersectionObserver(entries => {
      if (!scheduler.current(epoch, generation)) return;
      for (const entry of entries) {
        const card = cards.find(item => item.viewport === entry.target);
        card.evictAt = scheduler.retention(card, entry.isIntersecting, performance.now());
      }
      schedule();
    }, { rootMargin: scheduler.RETAIN_MARGIN });
    for (const card of cards.filter(card => card.auto)) { preloadObserver.observe(card.viewport); retainObserver.observe(card.viewport); }
  }
  window.addEventListener("message", event => {
    if (event.origin !== playerOrigin) return;
    const card = cards.find(item => item.frame && item.frame.contentWindow === event.source);
    if (!card) return;
    const message = scheduler.parseMessage(event.data);
    if (!message) return;
    if (message.error) { release(card, true); schedule(); return; }
    clearTimeout(card.timer);
    card.state = "mounted";
    if (message.playerState !== undefined) {
      card.playerState = message.playerState;
      if (message.playerState === 1) {
        for (const other of cards) if (other !== card || document.hidden) pause(other);
      }
    }
  }, { signal: lifetime.signal });
  document.addEventListener("visibilitychange", () => {
    if (document.hidden) cards.forEach(pause);
  }, { signal: lifetime.signal });
  document.addEventListener("fullscreenchange", schedule, { signal: lifetime.signal });
  window.addEventListener("resize", schedule, { signal: lifetime.signal });
  window.addEventListener("scroll", () => {
    if (scrollTick === null) scrollTick = requestAnimationFrame(() => { scrollTick = null; schedule(); });
  }, { passive: true, signal: lifetime.signal });
  function cleanup() {
    if (stopped) return;
    stopped = true;
    generation++;
    cancelAnimationFrame(scrollTick);
    clearTimeout(tick);
    preloadObserver?.disconnect();
    retainObserver?.disconnect();
    mutationObserver.disconnect();
    lifetime.abort();
    cards.forEach(card => release(card));
  }
  mutationObserver = new MutationObserver(() => {
    if (cards.some(card => !card.element.isConnected)) cleanup();
  });
  mutationObserver.observe(document.body, { childList: true, subtree: true });
  window.addEventListener("pagehide", cleanup, { signal: lifetime.signal });
})();
