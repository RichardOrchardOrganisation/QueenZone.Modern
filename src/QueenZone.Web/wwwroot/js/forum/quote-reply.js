// Wires the per-post "Quote" link (see _ForumPostList.cshtml) to the Quill reply editor
// (see rich-text-editor.js). Kept separate from rich-text-editor.js since that module is
// shared by News/Biography/Admin editors that have no concept of a forum post to quote.
(function () {
  "use strict";

  function escapeHtml(text) {
    return text
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;");
  }

  function buildQuoteHtml(author, plainText) {
    var body = escapeHtml(plainText.trim()).replaceAll("\n", "<br>");
    var attribution = author ? escapeHtml(author) + " wrote:" : "Quote:";
    return "<blockquote><strong>" + attribution + "</strong><br>" + body + "</blockquote><p><br></p>";
  }

  function handleQuoteClick(event) {
    var trigger = event.target.closest("[data-qz-quote-trigger]");
    if (!trigger) {
      return;
    }

    var post = trigger.closest(".qz-forum-post");
    var template = post ? post.querySelector("template[data-qz-quote-source]") : null;
    if (!template) {
      return;
    }

    event.preventDefault();

    var author = template.getAttribute("data-post-author") || "";
    var plainText = template.content.textContent || "";
    var html = buildQuoteHtml(author, plainText);

    if (!window.QueenZoneRichTextEditor || !window.QueenZoneRichTextEditor.insertQuoteBlock("Body", html)) {
      return;
    }

    var form = document.getElementById("Body")?.closest("form");
    if (form) {
      form.scrollIntoView({ behavior: "smooth", block: "start" });
    }
  }

  document.addEventListener("click", handleQuoteClick);
})();
