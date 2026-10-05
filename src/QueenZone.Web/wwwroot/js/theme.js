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
  document.addEventListener("DOMContentLoaded", () => {
    const picker = document.querySelector('[data-theme-picker]');
    if (!picker) return;
    const fallback = document.querySelector('[data-theme-fallback]');
    const status = document.querySelector('[data-theme-status]');
    let saved = picker.value;
    picker.hidden = false;
    const label = document.querySelector("[data-theme-label]");
    if (label) label.hidden = false;
    if (fallback) fallback.hidden = true;
    picker.addEventListener("change", async () => {
      picker.disabled = true;
      status.textContent = "Saving appearance…";
      try {
        const session = await fetch('/appearance?handler=Token', { cache: 'no-store' });
        if (!session.ok) throw new Error('Could not prepare appearance');
        const { token } = await session.json();
        const body = new URLSearchParams({ DeviceTheme: picker.value, ReturnUrl: location.pathname + location.search });
        const response = await fetch('/appearance', {
          method: 'POST', headers: { RequestVerificationToken: token }, body,
        });
        if (!response.ok) throw new Error('Could not save appearance');
        saved = picker.value;
        location.reload();
      } catch {
        picker.value = saved;
        status.textContent = "Could not save appearance. Try again.";
        picker.disabled = false;
      }
    });
  });
})();
