// Homepage "Happening now" strip — rotates the newest-activity lines (loaded only on /).
(() => {
  const list = document.querySelector("[data-home-ticker]");
  if (!list) {
    return;
  }

  const items = Array.from(list.children);
  if (items.length < 2) {
    return;
  }

  const ACTIVE = "qz-home-live__item--active";
  let current = 0;
  let timer = 0;
  let paused = false;

  const prefersReducedMotion = () =>
    window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;

  function show(index) {
    items[current].classList.remove(ACTIVE);
    current = index;
    items[current].classList.add(ACTIVE);
  }

  function stop() {
    if (timer) {
      clearInterval(timer);
      timer = 0;
    }
  }

  function start() {
    stop();
    if (paused || prefersReducedMotion() || document.hidden) {
      return;
    }

    timer = setInterval(() => show((current + 1) % items.length), 4500);
  }

  // Hold the current line while a reader points at or tabs into it.
  list.addEventListener("mouseenter", () => { paused = true; stop(); });
  list.addEventListener("mouseleave", () => { paused = false; start(); });
  list.addEventListener("focusin", (event) => {
    paused = true;
    stop();
    const item = items.indexOf(event.target.closest("li"));
    if (item >= 0 && item !== current) {
      show(item);
    }
  });
  list.addEventListener("focusout", () => { paused = false; start(); });
  document.addEventListener("visibilitychange", start);

  start();
})();
