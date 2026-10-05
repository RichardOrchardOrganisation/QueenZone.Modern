(function () {
  "use strict";

  document.querySelectorAll("[data-gallery-picker]").forEach(bindPicker);

  function bindPicker(root) {
    var host = root.closest("[data-gallery-picker-host]") || root;
    var openButton = root.querySelector("[data-gallery-picker-open]");
    var dialog = host.querySelector("[data-gallery-picker-dialog]");
    var panel = host.querySelector("[data-gallery-picker-panel]");
    var closeButton = host.querySelector("[data-gallery-picker-close]");
    var fileInput = root.querySelector("[data-article-image-input]");
    if (!openButton || !dialog || !panel) {
      return;
    }

    var pickerUrl = openButton.dataset.galleryPickerUrl || "/admin/news/gallery-picker";

    openButton.addEventListener("click", function () {
      if (typeof dialog.showModal === "function") {
        dialog.showModal();
      }

      if (!panel.dataset.loaded) {
        loadPanel(panel, pickerUrl);
      }
    });

    closeButton && closeButton.addEventListener("click", function () {
      dialog.close();
    });

    dialog.addEventListener("cancel", function (event) {
      event.preventDefault();
      dialog.close();
    });

    panel.addEventListener("submit", function (event) {
      var form = event.target.closest("[data-gallery-picker-filter]");
      if (!form) {
        return;
      }

      event.preventDefault();
      loadPanel(panel, form.action + "?" + new URLSearchParams(new FormData(form)).toString());
    });

    panel.addEventListener("click", function (event) {
      var pageLink = event.target.closest("[data-gallery-picker-page]");
      if (pageLink) {
        event.preventDefault();
        loadPanel(panel, pageLink.getAttribute("href"));
        return;
      }

      var pick = event.target.closest("[data-gallery-pick]");
      if (!pick) {
        return;
      }

      var picId = pick.dataset.picId;
      var originalUrl = pick.dataset.originalUrl || "";
      var title = pick.dataset.title || "Article image";
      if (!picId || !asGalleryOriginalUrl(originalUrl)) {
        return;
      }

      if (fileInput) {
        fileInput.value = "";
      }

      dialog.close();
      root.dispatchEvent(new CustomEvent("queenzone:article-gallery-crop", {
        bubbles: true,
        detail: {
          picId: picId,
          originalUrl: originalUrl,
          title: title
        }
      }));
    });
  }

  function loadPanel(panel, url) {
    panel.dataset.loaded = "1";
    panel.setAttribute("aria-busy", "true");
    fetch(url, { headers: { "X-Requested-With": "fetch" } })
      .then(function (response) {
        if (!response.ok) {
          throw new Error("Could not load gallery photos.");
        }

        return response.text();
      })
      .then(function (html) {
        var parsed = document.createElement("div");
        parsed.innerHTML = html;
        var next = parsed.querySelector("[data-gallery-picker-panel]");
        panel.innerHTML = next ? next.innerHTML : html;
        panel.setAttribute("aria-busy", "false");
      })
      .catch(function () {
        panel.innerHTML = "<p class=\"admin-help\">Could not load gallery photos.</p>";
        panel.setAttribute("aria-busy", "false");
      });
  }

  // data-original-url is a modeled taint step (js/xss-through-dom).
  // Only the same-origin PIC original route may be fetched for the crop dialog.
  function asGalleryOriginalUrl(url) {
    return typeof url === "string" && url.startsWith("/admin/news/gallery-original/") ? url : "";
  }
})();
