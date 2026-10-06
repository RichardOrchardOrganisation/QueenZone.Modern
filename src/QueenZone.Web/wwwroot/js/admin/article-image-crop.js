(function () {
  "use strict";

  // The native file input is always visually hidden (design spec); wire the
  // "Choose file" button to it independently of the cropper below so picking
  // a file still works even if Cropper.js fails to load.
  document.querySelectorAll("[data-article-image-choose]").forEach(function (button) {
    var root = button.closest("[data-article-image-crop]");
    var input = root && root.querySelector("[data-article-image-input]");
    if (input) {
      button.addEventListener("click", function () {
        input.click();
      });
    }
  });

  if (typeof Cropper !== "function") {
    return;
  }

  var allowedExtensions = { ".jpg": true, ".jpeg": true, ".png": true, ".webp": true };

  document.querySelectorAll("[data-article-image-crop]").forEach(bindCropper);

  function bindCropper(root) {
    var form = root.closest("form");
    var input = root.querySelector("[data-article-image-input]");
    var preview = root.querySelector("[data-article-image-preview]");
    var error = root.querySelector("[data-article-image-error]");
    var dialog = root.querySelector("[data-article-image-dialog]");
    var stageImg = root.querySelector("[data-article-image-stage-img]");
    var zoomInput = root.querySelector("[data-article-image-zoom]");
    var applyButton = root.querySelector("[data-article-image-apply]");
    var cancelButton = root.querySelector("[data-article-image-cancel]");
    var cropX = root.querySelector("[data-crop-x]");
    var cropY = root.querySelector("[data-crop-y]");
    var cropWidth = root.querySelector("[data-crop-width]");
    var cropHeight = root.querySelector("[data-crop-height]");
    var blobKeyInput = form.querySelector("[data-article-image-blob-key]");
    var galleryIdInput = form.querySelector("[data-article-image-gallery-id]");
    if (!form || !input || !dialog || !stageImg || !zoomInput) {
      return;
    }

    var aspectWidth = Number(root.dataset.aspectWidth) || 3;
    var aspectHeight = Number(root.dataset.aspectHeight) || 2;
    var minCropWidth = Number(root.dataset.minCropWidth) || 400;
    var minCropHeight = Number(root.dataset.minCropHeight) || 267;
    var maxBytes = Number(root.dataset.maxBytes) || 10 * 1024 * 1024;
    var imageLabel = root.dataset.imageLabel || "Article image";
    var objectUrl = "";
    var cropper = null;
    var cropApplied = false;
    var baseRatio = 1;
    var syncingZoom = false;
    var pendingGalleryPick = null;
    var gallerySnapshot = null;

    input.addEventListener("change", function () {
      clearError();
      resetCropFields();
      cropApplied = false;
      pendingGalleryPick = null;
      gallerySnapshot = null;
      destroyCropper();
      var file = input.files && input.files[0];
      if (!file) {
        return;
      }

      var rejection = validateFile(file, maxBytes, imageLabel);
      if (rejection) {
        showError(rejection);
        input.value = "";
        return;
      }

      revokeObjectUrl();
      objectUrl = asBlobObjectUrl(URL.createObjectURL(file));
      if (!objectUrl) {
        return;
      }

      stageImg.onload = function () {
        if (typeof dialog.showModal === "function") {
          dialog.showModal();
        }

        startCropper();
      };
      assignBlobImageSrc(stageImg, objectUrl);
    });

    zoomInput.addEventListener("input", function () {
      if (!cropper || syncingZoom) {
        return;
      }

      cropper.zoomTo(Number(zoomInput.value) || baseRatio);
      fillStageCropBox();
    });

    applyButton && applyButton.addEventListener("click", function () {
      var crop = currentCrop();
      if (!crop) {
        showError("Could not read the crop. Try another image.");
        return;
      }

      if (crop.width < minCropWidth || crop.height < minCropHeight) {
        showError(
          "The selected crop is too small. Use at least " +
            minCropWidth +
            "\u00d7" +
            minCropHeight +
            " pixels.");
        return;
      }

      cropX.value = String(crop.x);
      cropY.value = String(crop.y);
      cropWidth.value = String(crop.width);
      cropHeight.value = String(crop.height);
      cropApplied = true;
      if (pendingGalleryPick) {
        if (galleryIdInput) {
          galleryIdInput.value = pendingGalleryPick.picId;
        }

        if (blobKeyInput) {
          blobKeyInput.value = "gallery:" + pendingGalleryPick.picId;
        }

        gallerySnapshot = null;
      }
      if (preview) {
        assignBlobImageSrc(preview, objectUrl);
        preview.alt = imageLabel;
        preview.style.objectPosition =
          ((crop.x + crop.width / 2) / Math.max(stageImg.naturalWidth, 1)) * 100 + "% " +
          ((crop.y + crop.height / 2) / Math.max(stageImg.naturalHeight, 1)) * 100 + "%";
      }

      var caption = root.querySelector(".admin-article-image figcaption");
      if (caption) {
        caption.remove();
      }

      destroyCropper();
      zoomInput.disabled = true;
      dialog.close();
    });

    cancelButton && cancelButton.addEventListener("click", cancelSelection);
    dialog.addEventListener("cancel", function (event) {
      event.preventDefault();
      cancelSelection();
    });

    root.addEventListener("queenzone:article-gallery-crop", function (event) {
      var detail = event.detail || {};
      var originalUrl = asGalleryOriginalUrl(detail.originalUrl || "");
      var picId = detail.picId || "";
      if (!originalUrl || !picId) {
        return;
      }

      clearError();
      gallerySnapshot = {
        blobKey: blobKeyInput ? blobKeyInput.value : "",
        galleryId: galleryIdInput ? galleryIdInput.value : "",
        cropX: cropX.value,
        cropY: cropY.value,
        cropWidth: cropWidth.value,
        cropHeight: cropHeight.value,
        cropApplied: cropApplied
      };
      pendingGalleryPick = { picId: picId, title: detail.title || "Article image" };
      resetCropFields();
      cropApplied = false;
      destroyCropper();
      input.value = "";

      fetch(originalUrl, { credentials: "same-origin", headers: { "X-Requested-With": "fetch" } })
        .then(function (response) {
          if (!response.ok) {
            throw new Error("Could not load that gallery photo for cropping.");
          }

          return response.blob();
        })
        .then(function (blob) {
          revokeObjectUrl();
          objectUrl = asBlobObjectUrl(URL.createObjectURL(blob));
          if (!objectUrl) {
            throw new Error("Could not load that gallery photo for cropping.");
          }

          stageImg.onload = function () {
            if (typeof dialog.showModal === "function") {
              dialog.showModal();
            }

            startCropper();
          };
          assignBlobImageSrc(stageImg, objectUrl);
        })
        .catch(function () {
          pendingGalleryPick = null;
          restoreGallerySnapshot();
          showError("Could not load that gallery photo for cropping.");
        });
    });

    form.addEventListener("submit", function (event) {
      if (input.files && input.files[0] && !cropApplied) {
        event.preventDefault();
        if (typeof dialog.showModal === "function") {
          dialog.showModal();
        }

        if (!cropper) {
          startCropper();
        }

        return;
      }

      if (pendingGalleryPick && !cropApplied) {
        event.preventDefault();
        if (typeof dialog.showModal === "function") {
          dialog.showModal();
        }

        if (!cropper) {
          startCropper();
        }
      }
    }, true);

    function startCropper() {
      destroyCropper();
      zoomInput.disabled = false;
      // Lock #1439: pan the photo under a fixed 3:2 card. viewMode 3 covers
      // the stage so the outline is the viewport, not a free-floating box.
      cropper = new Cropper(stageImg, {
        aspectRatio: aspectWidth / aspectHeight,
        viewMode: 3,
        autoCropArea: 1,
        dragMode: "move",
        cropBoxMovable: false,
        cropBoxResizable: false,
        toggleDragModeOnDblclick: false,
        modal: false,
        background: false,
        guides: false,
        center: false,
        highlight: false,
        movable: true,
        rotatable: false,
        scalable: false,
        zoomable: true,
        zoomOnWheel: true,
        ready: function () {
          cropper.setDragMode("move");
          fillStageCropBox();
          var imageData = cropper.getImageData();
          baseRatio = imageData.width / Math.max(imageData.naturalWidth, 1);
          zoomInput.min = String(baseRatio);
          zoomInput.max = String(maxAllowedRatio(baseRatio));
          zoomInput.step = "any";
          zoomInput.value = String(baseRatio);
        },
        zoom: function (event) {
          if (cropWouldBeTooSmall(event.detail.ratio, event.detail.oldRatio)) {
            event.preventDefault();
            return;
          }

          syncingZoom = true;
          zoomInput.value = String(event.detail.ratio);
          syncingZoom = false;
          fillStageCropBox();
        }
      });
    }

    function fillStageCropBox() {
      if (!cropper) {
        return;
      }

      var container = cropper.getContainerData();
      if (!container || container.width < 1 || container.height < 1) {
        return;
      }

      cropper.setCropBoxData({
        left: 0,
        top: 0,
        width: container.width,
        height: container.height
      });
    }

    function maxAllowedRatio(startRatio) {
      var data = cropper.getData();
      if (data.width < 1 || data.height < 1) {
        return startRatio;
      }

      var byWidth = startRatio * (data.width / minCropWidth);
      var byHeight = startRatio * (data.height / minCropHeight);
      return Math.max(startRatio, Math.min(byWidth, byHeight));
    }

    function cropWouldBeTooSmall(newRatio, oldRatio) {
      if (!cropper || !oldRatio || newRatio <= oldRatio) {
        return false;
      }

      var data = cropper.getData();
      var scale = oldRatio / newRatio;
      return data.width * scale < minCropWidth || data.height * scale < minCropHeight;
    }

    function currentCrop() {
      if (!cropper) {
        return null;
      }

      var data = cropper.getData(true);
      if (!data || data.width < 1 || data.height < 1) {
        return null;
      }

      var naturalWidth = Math.max(stageImg.naturalWidth, 1);
      var naturalHeight = Math.max(stageImg.naturalHeight, 1);
      var x = clampNumber(data.x, 0, Math.max(0, naturalWidth - 1));
      var y = clampNumber(data.y, 0, Math.max(0, naturalHeight - 1));
      var width = clampNumber(data.width, 1, naturalWidth - x);
      var height = clampNumber(data.height, 1, naturalHeight - y);
      return { x: x, y: y, width: width, height: height };
    }

    function destroyCropper() {
      if (cropper) {
        cropper.destroy();
        cropper = null;
      }
    }

    function cancelSelection() {
      destroyCropper();
      zoomInput.disabled = true;
      input.value = "";
      if (pendingGalleryPick) {
        pendingGalleryPick = null;
        restoreGallerySnapshot();
      }
      else {
        resetCropFields();
        cropApplied = false;
      }

      revokeObjectUrl();
      dialog.close();
    }

    function restoreGallerySnapshot() {
      if (!gallerySnapshot) {
        resetCropFields();
        cropApplied = false;
        return;
      }

      if (blobKeyInput) {
        blobKeyInput.value = gallerySnapshot.blobKey;
      }

      if (galleryIdInput) {
        galleryIdInput.value = gallerySnapshot.galleryId;
      }

      cropX.value = gallerySnapshot.cropX;
      cropY.value = gallerySnapshot.cropY;
      cropWidth.value = gallerySnapshot.cropWidth;
      cropHeight.value = gallerySnapshot.cropHeight;
      cropApplied = gallerySnapshot.cropApplied;
      gallerySnapshot = null;
    }

    function resetCropFields() {
      cropX.value = "";
      cropY.value = "";
      cropWidth.value = "";
      cropHeight.value = "";
    }

    function showError(message) {
      if (!error) {
        return;
      }

      error.textContent = message;
      error.hidden = false;
    }

    function clearError() {
      if (!error) {
        return;
      }

      error.textContent = "";
      error.hidden = true;
    }

    function revokeObjectUrl() {
      if (objectUrl) {
        URL.revokeObjectURL(objectUrl);
        objectUrl = "";
      }
    }
  }

  // createObjectURL is a modeled taint step from input.files (js/xss-through-dom).
  // Only browser blob: URLs may reach img.src; encodeURI is the XSS sanitizer CodeQL recognizes.
  function asBlobObjectUrl(url) {
    return typeof url === "string" && url.startsWith("blob:") ? url : "";
  }

  function asGalleryOriginalUrl(url) {
    return typeof url === "string" && url.startsWith("/admin/news/gallery-original/") ? url : "";
  }

  function assignBlobImageSrc(image, url) {
    var safeUrl = asBlobObjectUrl(url);
    if (!image || !safeUrl) {
      return false;
    }

    image.src = encodeURI(safeUrl);
    return true;
  }

  function validateFile(file, maxBytes, imageLabel) {
    var name = (file.name || "").toLowerCase();
    var dot = name.lastIndexOf(".");
    var ext = dot >= 0 ? name.slice(dot) : "";
    var type = (file.type || "").toLowerCase();
    var typeOk = type === "image/jpeg" || type === "image/png" || type === "image/webp"
      || type === "image/jpg";
    if (!typeOk && !allowedExtensions[ext]) {
      return imageLabel + " must be a JPEG, PNG, or WebP file.";
    }

    if (file.size > maxBytes) {
      return imageLabel + " must be " + maxBytes + " bytes or smaller.";
    }

    return "";
  }

  function clampNumber(value, min, max) {
    return Math.min(max, Math.max(min, value));
  }
})();
