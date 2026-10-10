// Shared by the browser adapter and dependency-free node:test tests.
(function (root) {
  "use strict";
  const PRELOAD_MARGIN = "300px 0px";
  const RETAIN_MARGIN = "600px 0px";
  const CAPACITY = 3;
  const EVICTION_DELAY = 250;
  const occupied = card => card.state === "mounting" || card.state === "mounted";
  const protectedCard = card => card.fullscreen || (card.visible && card.playerState === 1);

  // Visible playback, fullscreen and pending retention deadlines cannot lose a pool slot.
  function priority(cards) {
    return cards.filter(card => card.state !== "failed" && (occupied(card) || card.preload))
      .slice().sort((a, b) => Number(protectedCard(b) || (occupied(b) && b.evictAt !== null)) -
        Number(protectedCard(a) || (occupied(a) && a.evictAt !== null)) ||
        Number(b.preload) - Number(a.preload) || a.distance - b.distance).slice(0, CAPACITY);
  }
  function retention(card, inside, now) {
    return inside ? null : card.evictAt ?? now + EVICTION_DELAY;
  }
  function shouldEvict(card, now) {
    return occupied(card) && !protectedCard(card) && card.evictAt !== null && now >= card.evictAt;
  }
  function current(expected, generation) { return expected === generation; }
  function parseMessage(data) {
    if (typeof data !== "string") return null;
    try {
      const message = JSON.parse(data);
      if (!message || typeof message !== "object") return null;
      if (message.event === "onReady") return { ready: true };
      if (message.event === "onError" && [2, 5, 100, 101, 150, 153].includes(message.info)) return { error: message.info };
      const state = message.event === "onStateChange" ? message.info :
        message.event === "infoDelivery" ? message.info?.playerState : undefined;
      return [-1, 0, 1, 2, 3, 5].includes(state) ? { playerState: state } : null;
    } catch { return null; }
  }
  const api = { PRELOAD_MARGIN, RETAIN_MARGIN, CAPACITY, EVICTION_DELAY, occupied, priority, retention, shouldEvict, current, parseMessage };
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  else root.QzYoutubeScheduler = api;
})(globalThis);
