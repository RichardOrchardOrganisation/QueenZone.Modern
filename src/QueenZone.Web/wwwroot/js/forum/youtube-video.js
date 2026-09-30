// Only server-generated forum cards reach this script; the UGC sanitizer removes these attributes.
(() => {
  "use strict";
  const cards = Array.from(document.querySelectorAll(".qz-forum-post__body [data-qz-forum-video]"));
  let active = null;

  const release = () => {
    if (!active) return;
    window.clearTimeout(active.timer);
    active.frame.remove();
    active.placeholder.hidden = false;
    active.button.textContent = "Load YouTube video";
    active.status.textContent = "Player unloaded.";
    active = null;
  };

  for (const card of cards) {
    const id = card.dataset.videoId;
    const startText = card.dataset.startSeconds;
    const start = startText ? Number(startText) : null;
    if (!id || id.length !== 11 || !/^[A-Za-z0-9_-]{11}$/.test(id) ||
        (startText && (startText.trim() !== startText || !/^[0-9]+$/.test(startText) || !Number.isSafeInteger(start) || start < 0 || start > 86400))) continue;

    const button = card.querySelector("[data-video-load]");
    const viewport = card.querySelector("[data-video-viewport]");
    const placeholder = card.querySelector("[data-video-placeholder]");
    const status = card.querySelector("[data-video-status]");
    if (!button || !viewport || !placeholder || !status) continue;
    button.hidden = false;

    button.addEventListener("click", () => {
      if (active?.card === card) {
        release();
        return;
      }
      // Never mount a player below YouTube's minimum size. The external link remains usable.
      if (viewport.clientWidth < 200 || viewport.clientHeight < 200) {
        status.textContent = "More space is needed for the player. Watch on YouTube instead.";
        return;
      }
      release();
      const source = new URL("https://www.youtube-nocookie.com/embed/" + id);
      source.searchParams.set("autoplay", "0");
      source.searchParams.set("playsinline", "1");
      if (start !== null) source.searchParams.set("start", String(start));
      const frame = document.createElement("iframe");
      frame.title = "YouTube video " + id;
      frame.referrerPolicy = "strict-origin-when-cross-origin";
      frame.allow = "encrypted-media; fullscreen";
      frame.allowFullscreen = true;
      frame.setAttribute("sandbox", "allow-scripts allow-same-origin");
      frame.className = "qz-forum-video__player";
      status.textContent = "Loading YouTube player…";
      // Cross-origin load is not evidence of successful playback (including YouTube errors 100/101/153).
      const fallback = "If playback is unavailable, offline or blocked, use Watch on YouTube.";
      const timer = window.setTimeout(() => {
        if (active?.frame === frame) status.textContent = "YouTube is taking longer to load. " + fallback;
      }, 12000);
      active = { card, frame, placeholder, button, status, timer };
      frame.addEventListener("load", () => {
        if (active?.frame !== frame) return;
        window.clearTimeout(timer);
        status.textContent = "Use the YouTube player controls to play. " + fallback;
      });
      frame.addEventListener("error", () => {
        if (active?.frame !== frame) return;
        window.clearTimeout(timer);
        status.textContent = "Could not load the YouTube player. " + fallback;
      });
      placeholder.hidden = true;
      button.textContent = "Unload YouTube video";
      frame.src = source.href;
      viewport.appendChild(frame);
      // Keep the activating button in the DOM and focused; Tab reaches the fallback without trapping focus.
    });
  }
  window.addEventListener("pagehide", release);
})();
