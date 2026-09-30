# Queenzone Design System

**Queenzone — The Queenzone.com Archive** — a premium fan archive preserving and publishing the content of the original Queenzone.com community.

This design system powers the relaunch of **Queenzone.org**. The original **Queenzone.com** ran for many years as one of the longest-running independent Queen fan communities, then went dormant (the domain was lost and the site sat untouched for years). The goal now is to **publish the preserved archive** — its news, stories, photography and forums — as a modern, timeless, editorial platform that respects Queen's legacy and the community's own history. The aim is **not** to recreate the old site, nor to imitate the official queenonline.com; Queenzone owns a darker, more cinematic "collector's box-set" identity.

> **Key statement:** *"A beautifully curated, modern archive celebrating Queen's legacy through news, photography, stories and community history."* Visitors should feel they are exploring a premium publication and historical archive — not browsing a traditional fan site.

The content archive is the product's greatest asset: **4,000+ news articles, 100+ long-form features, tens of thousands of photographs, 100,000+ forum posts.**

### Design philosophy
Closer to a **premium music documentary**, a **collector's edition box set**, an **editorial publication**, a **luxury coffee-table book** — than to a fan forum, a social network or a news portal. Prioritise **storytelling, exploration and discovery**.

### Brand attributes
Timeless · Elegant · Cinematic · Editorial · British · Premium · Archival · Sophisticated · Respectful · Community-driven.

**Never:** corporate, commercial, social-media-inspired, overly nostalgic, retro-web, heavy-metal-themed, flashy or gimmicky.

### Audience & devices
Returning original members (emotional connection) and casual fans arriving from search. **Design mobile-first** — mobile is primary, desktop secondary but important, tablet supported.

### Sources provided
- `uploads/crest.jpg` — white Queen crest (processed → `assets/crest-white.png` / `crest-black.png`)
- `uploads/Queen Band Images.jpg` — silver/metallic 3D crest (→ `assets/crest-silver.png`)
- `uploads/Queen Band Logo Tattoo.jpg` — line-art crests (→ `assets/crest-lineart.png`)
- `uploads/crests.png` — crest evolution chart (→ `assets/crest-evolution.png`)
- Full written brand & website design brief (pasted by the user).

No codebase or Figma was provided — the written brief is the source of truth.

---

## Content fundamentals

**Voice:** knowledgeable, passionate, respectful, thoughtful, informative. Speaks with **authority but stays approachable** — the tone of a well-edited music documentary or a museum wall text, never a fan blog.

**Avoid:** clickbait, tabloid language, excessive fan worship, breathless hype.

**Person & address:** largely third-person and editorial ("the band", "Queen", "the archive"). Addresses the reader directly only in invitations to explore ("Explore the archive", "Browse the gallery"). Never first-person-singular gushing.

**Casing:**
- **Headlines & titles** — sentence case or title case in Cormorant Garamond, e.g. *"The Day Queen Stole Live Aid"*, *"Inside the Making of Bohemian Rhapsody"*.
- **Eyebrows / kickers / labels** — UPPERCASE with wide tracking, set in Cinzel, e.g. *EXPLORE THE ARCHIVE*, *ON THIS DAY*, *FROM THE VAULTS*.
- **Meta lines** — uppercase, small, tracked: *13 JULY 1985 · 8 MIN READ*.

**Numerals & dates:** British style — *13 July 1985*, *MCMLXXV* for decorative timeline markers. Scale is stated plainly and proudly ("4,000+ articles", "100,000+ forum posts").

**Emoji:** never. No emoji anywhere in the interface or copy.

**Section naming follows the homepage structure:** Hero Feature · Explore the Archive · Featured Stories · Featured Photography · This Day in Queen History · Popular Discussions · Recently Restored · Timeline Highlights.

**Example copy (on-brand):**
- Eyebrow: *THE QUEENZONE.COM ARCHIVE*
- Standfirst: *"Twenty-one minutes on a July afternoon in 1985 that rewrote the rules of the stadium show."*
- Disclaimer (footer): *"An independent fan archive. Not affiliated with Queen or its representatives."*

---

## Visual foundations

**Overall:** predominantly monochrome (~90%), with accent colour reserved for ~10% and used **by meaning, never decoration**. The crest is the recurring visual anchor. Type carries most of the hierarchy. Generous white space; content breathes.

**Colour** (see `tokens/colors.css`)
- Foundation: Pure White `#FFFFFF` (primary bg) · Warm White `#F7F6F3` (secondary bg) · Light Grey `#E8E8E8` (borders) · Charcoal `#2B2B2B` (text) · Rich Black `#111111` (heroes, footer, drama).
- Accents by role: **Royal Blue** `#244A8F` (links, active nav, CTA) · **Royal Purple** `#5D3A8A` (archive, history, timeline) · **Burgundy** `#6B1F33` (featured/editorial/premium) · **Antique Gold** `#B89A4A` (anniversaries, special badges — rarest; never dominant).

**Typography** (see `tokens/typography.css`)
- **Cormorant Garamond** — display: page titles, hero headlines, section headings, article standfirsts and drop-caps. Elegant, regal, literary. Weight 400–600, tight tracking (`-0.015em`).
- **Inter** — body, navigation, UI. Modern, clean, highly readable. 17px body / 1.6 line-height; long-form at 18px / 1.75.
- **Cinzel** — titling accent only: eyebrows, timeline markers, anniversary content. Uppercase, wide tracking (`0.22em`). **Never body text.**

**Backgrounds & imagery:** large hero imagery, one strong image over competing elements. **Black-and-white, documentary/archival** photography — no heavy filters, no modern social grading. The site uses an **alternating dark/light section rhythm** as its signature (distinct from the bright official queenonline.com): rich-black `#111111` bands carry Featured Stories, This Day in Queen History, the Timeline and footer, with gold accents; light warm-white bands carry Explore, Photography and Discussions. The crest appears as a faint watermark (~5–7% opacity) behind dark sections. Imagery eases gently from greyscale toward colour on hover in cards. No gradients as decoration — only the cinematic scrim over photography (`--scrim-bottom` / `--scrim-soft`).

**Spacing & layout** (see `tokens/spacing.css`): 4px base scale; section vertical rhythm ~88px. Magazine-inspired structured grids (1280px max editorial width; 680px reading measure) — **not** social-media card walls. Generous gutters.

**Corners & cards:** restrained radii only — 0–4px (`--radius-xs` 2 · `--radius-sm` 3 · `--radius-md` 4); pills only for tags/filters. Cards are quiet: hairline `#E8E8E8` border on white, no border on warm-white sections, soft editorial shadow (`--shadow-card`) used sparingly and a gentle lift on hover (`--shadow-lift`).

**Borders:** 1px hairlines in Light Grey separate sections and list rows; on dark, `rgba(255,255,255,0.16)`.

**Shadows:** soft and low — never heavy drop shadows. `sm` for subtle raise, `card` for elevated media, `lift` on hover.

**Motion:** subtle, slow, elegant. Fade transitions, gentle image reveals (greyscale→colour, slow scale 1.04), soft hover tints, smooth scrolling. Easing `cubic-bezier(0.22,0.61,0.36,1)`; durations 180/320/620ms. **Avoid** excessive parallax, flashy animation, constant motion. Respect `prefers-reduced-motion`.

**Hover / press states:** links darken (`--link-hover`); buttons depress 1px on press; cards lift 3px with a soft shadow; images de-saturate to colour. Quiet, never bouncy.

**Transparency & blur:** used sparingly — the sticky header gains a translucent blurred background on scroll; the search overlay uses a dark blur scrim. Otherwise surfaces are opaque.

---

## Iconography

No bespoke icon set was provided in the brief. The system uses **[Lucide](https://lucide.dev)** (CDN) — a thin, elegant, open-source line set whose **~1.5px stroke** matches the refined, editorial tone. *This is a substitution — flag to the user if a proprietary icon set is preferred.*

- **Style:** outline / line icons only, consistent ~1.4–1.5 stroke weight, no filled or duotone glyphs.
- **Usage:** sparing and functional — search, bookmark, share, menu, chevrons, section markers (newspaper, camera, book-open, messages-square). Icons support, never decorate.
- **Sizing:** 18–20px inline in UI; 28px for section feature icons.
- **No emoji. No unicode-as-icon.** Decorative Roman numerals (Cinzel) may mark timeline years.
- **The crest is not an icon** — it is the brand emblem/seal (see `components/brand/CrestSeal`). Use the colour variant that suits the surface: `crest-black` on light, `crest-white` on dark, `crest-silver` as a premium hero feature, `crest-lineart` for the 1972-original reference.

---

## Index / manifest

**Root**
- `styles.css` — global entry point (consumers link this); `@import`s only.
- `tokens/` — `fonts.css` (Google Fonts), `colors.css`, `typography.css`, `spacing.css`, `base.css` (element + editorial primitives).
- `readme.md` — this guide. · `SKILL.md` — Agent-Skill wrapper.
- `assets/` — crest variants (`crest-black/white/silver/lineart.png`), `crest-evolution.png`, monochrome placeholder imagery (`img-*.jpg`).

**Components** (`components/`) — `window.QueenzoneDesignSystem_6c12e8`
- `core/` — `Button`, `IconButton`, `Input`
- `editorial/` — `Badge`, `Tag`, `SectionHeader`, `ArticleCard`
- `brand/` — `CrestSeal`

**Foundation cards** (`guidelines/`) — specimen cards for the Design System tab (Colors, Type, Spacing, Brand).

**UI kit** (`ui_kits/website/`) — the Queenzone site: multi-page desktop click-through (`index.html` — home, news, stories, photography gallery + lightbox, timeline, article view) and a mobile showcase (`mobile.html` — four iPhone screens). Distinct dark/light section rhythm. See its `README.md`.

> Fonts ship via a Google Fonts `@import` (Cormorant Garamond, Inter, Cinzel) rather than self-hosted binaries — the compiler reports 0 self-hosted fonts, which is expected.
