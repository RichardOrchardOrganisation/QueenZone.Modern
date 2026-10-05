(() => {
  const masthead = document.querySelector("[data-masthead]");
  if (!masthead) {
    return;
  }

  const groups = Array.from(masthead.querySelectorAll("[data-nav-group]"));
  const menu = masthead.querySelector("[data-mobile-menu]");
  const menuOpen = masthead.querySelector("[data-menu-open]");
  const menuClose = Array.from(masthead.querySelectorAll("[data-menu-close]"));
  let openGroup = null;
  let closeTimer = 0;
  let lastFocus = null;

  if (menu && menu.parentElement !== document.body) {
    document.body.appendChild(menu);
  }

  const setScrolled = () => {
    masthead.classList.toggle("is-scrolled", window.scrollY > 12);
  };

  const closeGroup = () => {
    if (!openGroup) {
      return;
    }

    openGroup.trigger.setAttribute("aria-expanded", "false");
    openGroup.panel.hidden = true;
    openGroup = null;
  };

  const openGroupPanel = (group) => {
    window.clearTimeout(closeTimer);
    if (openGroup && openGroup !== group) {
      closeGroup();
    }

    group.trigger.setAttribute("aria-expanded", "true");
    group.panel.hidden = false;
    openGroup = group;
  };

  const scheduleClose = () => {
    window.clearTimeout(closeTimer);
    closeTimer = window.setTimeout(closeGroup, 130);
  };

  const focusPanelItem = (group, index) => {
    const items = Array.from(group.panel.querySelectorAll("a"));
    if (items.length === 0) {
      return;
    }

    items[(index + items.length) % items.length].focus();
  };

  groups.forEach((groupEl) => {
    const group = {
      el: groupEl,
      trigger: groupEl.querySelector("[data-nav-trigger]"),
      panel: groupEl.querySelector("[data-nav-panel]")
    };

    groupEl.addEventListener("pointerenter", (event) => {
      if (event.pointerType === "mouse") {
        openGroupPanel(group);
      }
    });

    groupEl.addEventListener("pointerleave", (event) => {
      if (event.pointerType === "mouse") {
        scheduleClose();
      }
    });

    group.trigger.addEventListener("click", () => {
      if (openGroup === group) {
        closeGroup();
      } else {
        openGroupPanel(group);
      }
    });

    group.trigger.addEventListener("keydown", (event) => {
      if (event.key === "ArrowDown" || event.key === "ArrowUp") {
        event.preventDefault();
        openGroupPanel(group);
        focusPanelItem(group, event.key === "ArrowDown" ? 0 : -1);
      }
    });

    group.panel.addEventListener("keydown", (event) => {
      const items = Array.from(group.panel.querySelectorAll("a"));
      const currentIndex = items.indexOf(document.activeElement);

      if (event.key === "ArrowDown" || event.key === "ArrowUp") {
        event.preventDefault();
        focusPanelItem(group, currentIndex + (event.key === "ArrowDown" ? 1 : -1));
      }

      if (event.key === "Escape") {
        event.preventDefault();
        closeGroup();
        group.trigger.focus();
      }

      if (event.key === "Tab") {
        closeGroup();
      }
    });
  });

  document.addEventListener("click", (event) => {
    if (!masthead.contains(event.target)) {
      closeGroup();
    }
  });

  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && openGroup) {
      const trigger = openGroup.trigger;
      closeGroup();
      trigger.focus();
    }
  });

  const focusableSelector = "a[href], button:not([disabled])";

  const closeMenu = () => {
    if (!menu || menu.hidden) {
      return;
    }

    menu.hidden = true;
    document.body.classList.remove("qz-menu-lock");
    menuOpen?.setAttribute("aria-expanded", "false");
    lastFocus?.focus();
  };

  const openMenu = () => {
    if (!menu || !menuOpen) {
      return;
    }

    lastFocus = document.activeElement;
    menu.hidden = false;
    document.body.classList.add("qz-menu-lock");
    menuOpen.setAttribute("aria-expanded", "true");
    menu.querySelector(focusableSelector)?.focus();
  };

  menuOpen?.addEventListener("click", openMenu);
  menuClose.forEach((control) => control.addEventListener("click", closeMenu));

  menu?.addEventListener("keydown", (event) => {
    if (event.key === "Escape") {
      event.preventDefault();
      closeMenu();
      return;
    }

    if (event.key !== "Tab") {
      return;
    }

    const focusable = Array.from(menu.querySelectorAll(focusableSelector));
    if (focusable.length === 0) {
      return;
    }

    const first = focusable[0];
    const last = focusable[focusable.length - 1];

    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  });

  window.addEventListener("scroll", setScrolled, { passive: true });
  setScrolled();
})();

// Progressive submit feedback for forms that can take a noticeable time
// (e.g. avatar upload with server-side image processing).
(() => {
  const forms = document.querySelectorAll("form[data-busy-submit]");
  if (forms.length === 0) {
    return;
  }

  forms.forEach((form) => {
    form.addEventListener("submit", (event) => {
      if (event.defaultPrevented) {
        return;
      }

      if (form.classList.contains("is-submitting")) {
        // Already submitted once; block double-posts.
        event.preventDefault();
        return;
      }

      // Honour HTML constraint validation even when the form has novalidate
      // (novalidate only disables the browser's automatic pre-submit check).
      if (typeof form.reportValidity === "function" && !form.reportValidity()) {
        event.preventDefault();
        return;
      }

      form.classList.add("is-submitting");

      const busyLabel = form.dataset.busyLabel || "Working…";
      const status = form.querySelector("[data-busy-status]");
      let timerId = null;
      if (status) {
        status.hidden = false;
        status.textContent = busyLabel;
        if (form.dataset.busyTimer === "true") {
          const startedAt = Date.now();
          const updateTimer = () => {
            const elapsed = Math.max(0, Math.floor((Date.now() - startedAt) / 1000));
            status.textContent = `${busyLabel} (${elapsed}s)`;
          };
          updateTimer();
          timerId = window.setInterval(updateTimer, 1000);
        }
      }

      const submitters = form.querySelectorAll('button[type="submit"], input[type="submit"]');
      submitters.forEach((control) => {
        if (control instanceof HTMLButtonElement) {
          control.classList.add("is-busy");
          control.setAttribute("aria-busy", "true");
          control.textContent = busyLabel;
          // Disable after the current event so the browser still completes this submit.
          window.setTimeout(() => {
            control.disabled = true;
          }, 0);
        } else if (control instanceof HTMLInputElement) {
          control.classList.add("is-busy");
          control.setAttribute("aria-busy", "true");
          control.value = busyLabel;
          window.setTimeout(() => {
            control.disabled = true;
          }, 0);
        }
      });

      window.addEventListener("pageshow", () => {
        if (timerId !== null) {
          window.clearInterval(timerId);
        }
      }, { once: true });
    });
  });
})();

// Apply per-element inline styles that come from server-rendered data (percentages,
// computed bar heights, avatar colours) via the CSSOM instead of a style="" attribute,
// so these stay CSP-compliant without needing style-src 'unsafe-inline'.
(() => {
  document.querySelectorAll("[data-bar-height]").forEach((el) => {
    el.style.setProperty("height", el.dataset.barHeight);
  });

  document.querySelectorAll("[data-bar-width]").forEach((el) => {
    el.style.setProperty("width", el.dataset.barWidth);
  });

  document.querySelectorAll("[data-avatar-bg]").forEach((el) => {
    el.style.setProperty("--qz-avatar-bg", el.dataset.avatarBg);
  });
})();

// Poll component (design handoff: designs/poll-component/). Single-choice polls
// cast the vote as soon as an option is picked — no separate submit step — so
// auto-submit the form on radio change. Multi-choice keeps its own "Cast your
// vote" button, enabled once at least one checkbox is checked; both degrade
// gracefully without JS (the radio/checkbox form still posts normally).
(() => {
  document.querySelectorAll(".qz-poll__options").forEach((list) => {
    const form = list.closest("form");
    if (!form) {
      return;
    }

    const radios = Array.from(list.querySelectorAll('input[type="radio"]'));
    radios.forEach((radio) => {
      radio.addEventListener("change", () => form.requestSubmit());
    });

    const checkboxes = Array.from(list.querySelectorAll('input[type="checkbox"]'));
    const voteButton = form.querySelector(".qz-poll__vote-button");
    if (checkboxes.length > 0 && voteButton) {
      const syncDisabled = () => {
        voteButton.disabled = !checkboxes.some((box) => box.checked);
      };
      checkboxes.forEach((box) => box.addEventListener("change", syncDisabled));
      syncDisabled();
    }
  });
})();

// Progressive enhancement: use the OS share sheet on touch browsers that
// expose navigator.share. Static platform links stay the no-JS / desktop
// fallback — desktop Chrome also has share(), but the explicit X / Facebook
// / WhatsApp / email links are the better option there.
(() => {
  if (typeof navigator.share !== "function") {
    return;
  }

  if (!window.matchMedia("(pointer: coarse)").matches) {
    return;
  }

  const roots = document.querySelectorAll("[data-share]");
  if (roots.length === 0) {
    return;
  }

  roots.forEach((root) => {
    const title = root.dataset.shareTitle || document.title;
    const url = root.dataset.shareUrl || window.location.href;
    const payload = { title, url };

    if (typeof navigator.canShare === "function" && !navigator.canShare(payload)) {
      return;
    }

    const nativeButton = root.querySelector("[data-share-native]");
    const fallback = root.querySelector("[data-share-fallback]");
    if (!(nativeButton instanceof HTMLButtonElement) || !fallback) {
      return;
    }

    nativeButton.hidden = false;
    fallback.hidden = true;

    nativeButton.addEventListener("click", () => {
      navigator.share(payload).catch((error) => {
        if (error && error.name === "AbortError") {
          return;
        }

        fallback.hidden = false;
      });
    });
  });
})();

// Photo lightbox: swipe / arrows / keyboard. Touch handling lives here rather
// than an inline script so iOS Safari cannot drop document.currentScript, and
// so horizontal pans can preventDefault (non-passive) instead of becoming a
// scroll, image-drag, or history swipe.
(() => {
  const root = document.querySelector("[data-photo-lightbox]");
  if (!(root instanceof HTMLElement)) {
    return;
  }

  const swipeThreshold = 56;
  const swipeMaxOffAxis = 72;
  const href = (name) => {
    const value = root.getAttribute(name);
    return value && value.trim() ? value : "";
  };
  const go = (url) => {
    if (url) {
      window.location.href = url;
    }
  };

  document.addEventListener("keydown", (event) => {
    if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey) {
      return;
    }

    if (event.key === "Escape") {
      go(href("data-back-href"));
    } else if (event.key === "ArrowLeft") {
      go(href("data-prev-href"));
    } else if (event.key === "ArrowRight") {
      go(href("data-next-href"));
    }
  });

  const surface = root.querySelector(".qz-lightbox__body") || root;
  let startX = 0;
  let startY = 0;
  let tracking = false;

  const touchPoint = (event) => {
    if (!event.changedTouches || event.changedTouches.length !== 1) {
      return null;
    }

    return event.changedTouches[0];
  };

  surface.addEventListener("touchstart", (event) => {
    const touch = touchPoint(event);
    if (!touch) {
      tracking = false;
      return;
    }

    tracking = true;
    startX = touch.clientX;
    startY = touch.clientY;
  }, { passive: true });

  surface.addEventListener("touchmove", (event) => {
    if (!tracking) {
      return;
    }

    const touch = touchPoint(event);
    if (!touch) {
      return;
    }

    const dx = touch.clientX - startX;
    const dy = touch.clientY - startY;
    if (Math.abs(dx) > 10 && Math.abs(dx) > Math.abs(dy)) {
      event.preventDefault();
    }
  }, { passive: false });

  const finish = (event) => {
    if (!tracking) {
      return;
    }

    tracking = false;
    const touch = touchPoint(event);
    if (!touch) {
      return;
    }

    const dx = touch.clientX - startX;
    const dy = touch.clientY - startY;
    if (Math.abs(dx) < swipeThreshold || Math.abs(dy) > swipeMaxOffAxis || Math.abs(dx) <= Math.abs(dy)) {
      return;
    }

    go(dx > 0 ? href("data-prev-href") : href("data-next-href"));
  };

  surface.addEventListener("touchend", finish, { passive: true });
  surface.addEventListener("touchcancel", () => {
    tracking = false;
  }, { passive: true });
})();

(() => {
  const list = document.querySelector("[data-qz-stage-list]");
  if (!list) {
    return;
  }

  const rows = Array.from(list.querySelectorAll("[data-qz-stage-play]")).map((button) => {
    const row = button.closest(".qz-stage-row");
    const idValue = Number.parseInt(button.dataset.qzStageId || (row && row.dataset.qzStageId) || "", 10);
    return {
      button,
      row,
      audio: row ? row.querySelector("audio") : null,
      title: button.dataset.title || "recording",
      id: Number.isInteger(idValue) ? idValue : null
    };
  }).filter((player) => player.audio);

  if (rows.length === 0) {
    return;
  }

  // catalogNode.textContent is a modeled taint step (js/xss-through-dom).
  // Only cookie-gated same-origin audio paths may reach audio.src; encodeURI is
  // the XSS sanitizer CodeQL recognizes.
  const asFanPerformanceAudioPath = (path) => {
    if (typeof path !== "string") {
      return "";
    }

    return /^\/fan-performances\/\d+\/audio(?:\/[A-Za-z0-9._-]+)?$/.test(path) ? path : "";
  };

  const catalogNode = document.querySelector("[data-qz-stage-catalog]");
  const catalog = (() => {
    if (!catalogNode) {
      return [];
    }

    try {
      const parsed = JSON.parse(catalogNode.textContent || "[]");
      if (!Array.isArray(parsed)) {
        return [];
      }

      return parsed.filter((entry) =>
        entry &&
        Number.isInteger(entry.id) &&
        asFanPerformanceAudioPath(entry.audioPlayPath));
    } catch {
      return [];
    }
  })();

  const playAllButton = document.querySelector("[data-qz-stage-play-all]");
  const shuffleAllButton = document.querySelector("[data-qz-stage-shuffle-all]");
  const nowPlaying = document.querySelector("[data-qz-stage-now-playing]");
  const sharedAudio = document.createElement("audio");
  sharedAudio.preload = "none";
  sharedAudio.hidden = true;
  list.appendChild(sharedAudio);

  let queue = null;
  let queueGeneration = 0;

  const playIcon = (player) => player.button.querySelector(".qz-stage-play__icon--play");
  const pauseIcon = (player) => player.button.querySelector(".qz-stage-play__icon--pause");

  const findRow = (id) => rows.find((player) => player.id === id);

  const catalogEntry = (id) => catalog.find((entry) => entry.id === id);

  const setPlaying = (player, playing) => {
    player.button.setAttribute("aria-pressed", playing ? "true" : "false");
    player.button.setAttribute("aria-label", (playing ? "Pause " : "Play ") + player.title);
    player.row.classList.toggle("is-playing", playing);
    const play = playIcon(player);
    const pause = pauseIcon(player);
    if (play) {
      play.hidden = playing;
    }
    if (pause) {
      pause.hidden = !playing;
    }
  };

  const clearRowPlaying = () => {
    rows.forEach((player) => setPlaying(player, false));
  };

  const updateNowPlaying = (entry) => {
    if (!nowPlaying) {
      return;
    }

    if (!entry) {
      nowPlaying.hidden = true;
      nowPlaying.textContent = "";
      return;
    }

    nowPlaying.hidden = false;
    nowPlaying.textContent = "Now playing: " + (entry.title || "recording");
  };

  const pauseShared = () => {
    if (!sharedAudio.paused) {
      sharedAudio.pause();
    }
  };

  const pauseOthers = (active) => {
    rows.forEach((player) => {
      if (player !== active && !player.audio.paused) {
        player.audio.pause();
      }
    });
    if (active) {
      pauseShared();
    }
  };

  const cancelQueue = () => {
    queueGeneration += 1;
    queue = null;
  };

  const shuffleIds = (ids) => {
    const copy = ids.slice();
    for (let index = copy.length - 1; index > 0; index -= 1) {
      const swap = Math.floor(Math.random() * (index + 1));
      const current = copy[index];
      copy[index] = copy[swap];
      copy[swap] = current;
    }
    return copy;
  };

  const playAttempt = (media, onFailure) => {
    const attempt = media.play();
    if (attempt && typeof attempt.catch === "function") {
      attempt.catch(() => {
        if (typeof onFailure === "function") {
          onFailure();
        }
      });
    }
  };

  const playNextInQueue = () => {
    if (!queue) {
      updateNowPlaying(null);
      return;
    }

    queue.index += 1;
    if (queue.index >= queue.ids.length) {
      cancelQueue();
      updateNowPlaying(null);
      return;
    }

    playQueueIndex(queue.index);
  };

  const playQueueIndex = (index) => {
    if (!queue || index < 0 || index >= queue.ids.length) {
      return;
    }

    const generation = queueGeneration;
    const id = queue.ids[index];
    const entry = catalogEntry(id);
    const rowPlayer = findRow(id);
    queue.index = index;

    const fail = () => {
      if (generation !== queueGeneration) {
        return;
      }
      cancelQueue();
      updateNowPlaying(null);
      clearRowPlaying();
    };

    if (rowPlayer) {
      pauseOthers(rowPlayer);
      updateNowPlaying(entry || { title: rowPlayer.title });
      rowPlayer.audio.currentTime = 0;
      playAttempt(rowPlayer.audio, fail);
      return;
    }

    if (!entry) {
      fail();
      return;
    }

    rows.forEach((player) => {
      if (!player.audio.paused) {
        player.audio.pause();
      }
    });
    const audioPath = asFanPerformanceAudioPath(entry.audioPlayPath);
    if (!audioPath) {
      fail();
      return;
    }

    clearRowPlaying();
    sharedAudio.src = encodeURI(audioPath);
    updateNowPlaying(entry);
    playAttempt(sharedAudio, fail);
  };

  const startQueue = (ids) => {
    cancelQueue();
    if (!ids.length) {
      updateNowPlaying(null);
      return;
    }

    queue = { ids, index: 0 };
    playQueueIndex(0);
  };

  rows.forEach((player) => {
    player.button.addEventListener("click", () => {
      cancelQueue();
      if (player.audio.paused) {
        pauseOthers(player);
        playAttempt(player.audio, () => {
          setPlaying(player, false);
          updateNowPlaying(null);
        });
      } else {
        player.audio.pause();
        updateNowPlaying(null);
      }
    });

    player.audio.addEventListener("play", () => {
      if (queue && queue.ids[queue.index] !== player.id) {
        cancelQueue();
      }
      pauseOthers(player);
      setPlaying(player, true);
      if (!queue) {
        updateNowPlaying({ title: player.title });
      }
    });

    player.audio.addEventListener("pause", () => {
      setPlaying(player, false);
    });

    player.audio.addEventListener("ended", () => {
      setPlaying(player, false);
      playNextInQueue();
    });
  });

  sharedAudio.addEventListener("ended", () => {
    playNextInQueue();
  });

  if (playAllButton) {
    playAllButton.addEventListener("click", () => {
      startQueue(catalog.map((entry) => entry.id));
    });
  }

  if (shuffleAllButton) {
    shuffleAllButton.addEventListener("click", () => {
      startQueue(shuffleIds(catalog.map((entry) => entry.id)));
    });
  }

  window.addEventListener("pagehide", () => {
    cancelQueue();
    pauseOthers(null);
    pauseShared();
    sharedAudio.removeAttribute("src");
    updateNowPlaying(null);
    clearRowPlaying();
  });

  list.classList.add("is-enhanced");
})();

// Admin Form Style Guide: styled destructive-confirmation dialog.
// Opens on the trigger button, focuses Cancel first, and returns focus
// to the trigger on close (native <dialog> handles Esc + focus trap).
(() => {
  const openButtons = document.querySelectorAll("[data-confirm-dialog-open]");
  if (openButtons.length === 0) {
    return;
  }

  openButtons.forEach((trigger) => {
    const dialogId = trigger.dataset.confirmDialogOpen;
    const dialog = dialogId ? document.getElementById(dialogId) : null;
    if (!(dialog instanceof HTMLDialogElement)) {
      return;
    }

    trigger.addEventListener("click", () => {
      dialog.showModal();
      const cancelButton = dialog.querySelector("[data-confirm-dialog-cancel]");
      if (cancelButton instanceof HTMLElement) {
        cancelButton.focus();
      }
    });

    dialog.querySelectorAll("[data-confirm-dialog-cancel]").forEach((cancelButton) => {
      cancelButton.addEventListener("click", () => {
        dialog.close();
      });
    });

    dialog.addEventListener("close", () => {
      trigger.focus();
    });
  });
})();
