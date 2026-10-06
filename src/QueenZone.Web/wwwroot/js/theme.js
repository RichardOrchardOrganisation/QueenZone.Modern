(() => {
  const root = document.documentElement;
  const storageKey = "qz.offline.appearance";
  const valid = (value) => value === "light" || value === "dark";
  try {
    if (root.dataset.themeOffline !== undefined) {
      const saved = localStorage.getItem(storageKey);
      if (valid(saved)) root.dataset.theme = saved;
    } else {
      // Every online page replaces this public hint, including after logout/account switching.
      const choice = root.dataset.theme;
      if (valid(choice)) localStorage.setItem(storageKey, choice);
      else localStorage.removeItem(storageKey);
    }
  } catch { /* Storage is optional; the server choice or OS still applies. */ }

  const system = window.matchMedia("(prefers-color-scheme: dark)");
  function updateChrome() {
    const dark = root.dataset.theme === "dark" || (!valid(root.dataset.theme) && system.matches);
    for (const meta of document.querySelectorAll('meta[name="theme-color"]')) {
      meta.removeAttribute("media");
      meta.content = dark ? "#111111" : "#FFFFFF";
    }
  }
  updateChrome();
  system.addEventListener("change", updateChrome);
})();
