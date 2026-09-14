# Handoff: Queenzone Timeline

## Overview
An editorial, scroll-through timeline of Queen's history (1970 → today) for the Queenzone fan archive. It presents events grouped by decade in a single vertical scroll, with a sticky left rail for jump-navigation (by decade, and by year within the active decade) and category filters. Each event is a **compact, scannable row** that **expands in place on click** to reveal a full standfirst, a second paragraph of detail, an archival photo, and a "Read the story" link.

The design is built to **scale to hundreds of events** without becoming a wall of cards — compactness is the default state, richness is on demand.

## About the Design Files
The files in this bundle are **design references created in HTML/React (via in-browser Babel)** — a prototype showing the intended look and behaviour. **They are not production code to copy directly.** The task is to **recreate this design in the target codebase's existing environment** (React, Vue, Svelte, etc.) using its established patterns, component library, routing and data-fetching. If no front-end environment exists yet, choose the most appropriate framework for the project and implement it there.

The prototype loads React + ReactDOM + Babel from CDN and transpiles JSX in the browser — do **not** replicate that approach in production. Use the codebase's real build pipeline.

## Fidelity
**High-fidelity (hifi).** Colours, typography, spacing, motion and interactions are final and precise. Recreate the UI to match, using the design tokens listed below (they already exist as the Queenzone design-system CSS — reuse those variables rather than hardcoding hex values where the system is available).

## Design System / Tokens
This design consumes the **Queenzone design system**. All values below are CSS custom properties defined in `styles.css` (which `@import`s `tokens/*.css`). **In production, reference these tokens** — do not reinvent them. `tokens/` here is a copy of `design/tokens/` (the canonical source); don't hand-edit it — run `pwsh ./scripts/Sync-DesignTokens.ps1` from the repo root to refresh it after `design/tokens/` changes.

### Colours
Monochrome foundation (~90%) + sparing accents (~10%); gold is the rarest.
- `--qz-white` `#FFFFFF` · `--qz-warm-white` `#F7F6F3` (page/raised surfaces)
- `--qz-charcoal` `#2B2B2B` (primary text) · `--qz-black` `#111111` (hero/footer)
- Neutral ramp: `--qz-grey-100 #F2F1ED`, `-200 #E8E8E8`, `-300 #D6D6D2`, `-400 #B4B4AF`, `-500 #8A8A85`, `-600 #5F5F5B`
- Accents (category colours): Music = Royal Blue `--qz-blue #244A8F`; Live = Royal Purple `--qz-purple #5D3A8A`; Milestone = Antique Gold `--qz-gold #B89A4A` / deep `--qz-gold-deep #9C8038`; Loss = Burgundy `--qz-burgundy #6B1F33`
- Semantic aliases used here: `--text-primary`, `--text-secondary` (#5F5F5B), `--text-muted` (#8A8A85), `--hairline` (= grey-200), `--border-strong` (= grey-300), `--link` (= blue).

### Typography
- `--font-display`: **Cormorant Garamond** (serif) — headlines, event titles
- `--font-body`: **Inter** — UI, body copy, ledes
- `--font-titling`: **Cinzel** (serif caps) — eyebrows, category tags, year labels, "Jump to" label
- Weights: `--fw-regular 400`, `--fw-medium 500`, `--fw-semibold 600`
- Loaded via local Latin WOFF2 subsets (`tokens/fonts.css`).

### Spacing / radii / motion
- 4px spacing base (`--space-*`). Layout max-width **1180px**, gutters `--gutter-lg 2.5rem`.
- Radii are small and restrained: `--radius-xs 2px` (tags/inputs), media frames 2px. Never pill-shaped except the (removed) exploration switcher.
- Motion easing `--ease-out cubic-bezier(.22,.61,.36,1)`; durations 180 / 320 / 620ms.

---

## Screen: The Timeline (single page)

### Layout
Full-height page on `--qz-warm-white`. Three stacked regions:

1. **Hero** — full-width, `--qz-black` background, padding `clamp(70px,9vw,120px)` top / `--gutter-lg` sides. Faint crest watermark (`assets/crest-white.png`, width 260px, opacity 0.06) pinned centre-right. Inner content max-width 1180px, centred. Contains: gold Cinzel eyebrow ("Five decades · 1970 – today", letter-spacing 0.22em), an `h1` in Cormorant `clamp(48px,7vw,88px)`/1, letter-spacing -0.015em, white, and a lead paragraph (Inter 20px/1.55, `rgba(255,255,255,0.78)`, max-width 620px).

2. **Sticky filter bar** — `position: sticky; top:0; z-index:20`. Background `rgba(247,246,243,0.92)` with `backdrop-filter: saturate(180%) blur(10px)`, 1px bottom hairline. Inner max-width 1180px, padding `14px --gutter-lg`, space-between: filter chips on the left, live event count ("N events") on the right (Inter 12px medium, `--text-muted`).

3. **Body grid** — max-width 1180px, `display:grid; grid-template-columns: 182px 1fr; gap: clamp(24px,4vw,72px)`. Padding `clamp(44px,5vw,72px) --gutter-lg 140px`. Left column = decade rail; right column = decade sections.

### Component: Filter chips (`FilterChips`)
Row of chips: **All events** + one per category (Music, Live, Milestone, Loss). Each chip: Inter 12px, letter-spacing 0.04em, padding `9px 15px`, `border-radius: 2px`, 1px border. Category chips show a 7px colour dot before the label. Idle = `--text-secondary` text + `--border-strong` border, transparent bg. Active = darker/coloured text + coloured or charcoal border, weight 600. Transition `all 200ms ease`. Clicking sets the active filter (single-select).

### Component: Decade rail (`DecadeRail`)
`position: sticky; top: 96px; align-self: start; max-height: calc(100vh - 120px); overflow-y:auto`. Column layout.
- Header label "Jump to" — Cinzel 10px, letter-spacing 0.2em, uppercase, `--qz-grey-500`, padding-left 16px.
- **Decade buttons** — Cinzel 17px, letter-spacing 0.06em, padding `7px 0 7px 16px`. Active decade = `--qz-gold-deep` text with a 2px × 20px gold vertical bar on the left edge (animates height 220ms); inactive = `--text-secondary`, no bar. Each shows a small count (Inter 11px, `--qz-grey-400`) after the label.
- **Year sub-markers** — revealed only under the **active** decade via a `max-height` transition (`320ms cubic-bezier(.22,.61,.36,1)`; collapsed = 0, expanded = years×30+8). Rendered inside a container with a left 1px hairline border, padding-left 16px. Each year button: Inter 12px, padding `5px 0 5px 14px`, `--qz-grey-500`; active year = `--qz-gold-deep` weight 600 with an 8px × 2px gold horizontal tick on the left (animates width 180ms).
- Clicking a decade smooth-scrolls to that section (offset -84px); clicking a year smooth-scrolls to the first row of that year (offset -96px).

### Component: Decade section header
Per decade: a flex row, baseline-aligned, gap 16px, margin-bottom 20px. `h2` = decade label in Cormorant `--fw-medium` 38px/1, `--qz-charcoal`. A flexible 1px `--border-strong` rule fills the space, then a count (Inter 12px, `--text-muted`) on the right. Section margin-bottom 52px. Each section carries `data-decade="1970s"` for scroll tracking.

### Component: Event row (`EventRowA`) — the core element
A vertical **spine + node** timeline on the left, with a compact clickable header and an expandable detail panel.

**Spine & node:** container has `padding-left: 34px; position: relative`. A 1px `--hairline` vertical line at `left:5px` runs top→bottom (top starts at 22px for the first row so it doesn't cap above the first node). A **node dot** at `left:0; top:20px`, 11×11px, `border-radius:50%`, filled with the **category colour**, ringed with `box-shadow: 0 0 0 4px --qz-warm-white` to punch through the spine.

**Compact header (button, full width):** `display:grid; grid-template-columns: 78px 1fr auto; align-items: baseline; gap: clamp(14px,2.2vw,34px)`; padding `13px 0`; a 1px `--hairline` top border on every row except the first.
- Col 1 — **year**: Cormorant `--fw-medium` 21px/1, `--qz-charcoal`.
- Col 2 — **title block** (min-width:0 so it can shrink). A single line-height-1.25 block containing: the **category tag** (`CatTag`, inline-flex, vertical-align 0.12em, margin-right 14px) followed inline by the **title** in Cormorant `--fw-semibold` 20px/1.25, `--text-primary`. The tag+title share one wrapping block so long titles wrap cleanly and the row grows to fit. **When collapsed only**, a one-line **lede** appears below: Inter 15px/1.5, `--text-muted`, `max-width:620px`, truncated with `text-overflow: ellipsis; white-space: nowrap`.
- Col 3 — **chevron**: 15×15 SVG, `--qz-grey-500`, rotates 180° when open (transition 300ms ease).

**Expandable detail:** uses a `grid-template-rows: 0fr → 1fr` transition (`380ms cubic-bezier(.22,.61,.36,1)`) with an `overflow:hidden` inner wrapper — the smooth height-auto expand technique. Inside: a grid matching the header (`78px 1fr`, same gap), with col 1 empty so detail aligns under the title. Detail column is `grid-template-columns: 1fr 190px` when the event has an image, else single column max-width 640px:
  - Full **standfirst** paragraph: Inter 16px/1.62, `--text-secondary`, margin-bottom 12px.
  - Second **detail** paragraph: Inter 15px/1.6, `--text-secondary`, separated by a 1px `--hairline` top border with 12px padding-top.
  - **"Read the story"** link: Inter 12px `--fw-semibold`, uppercase, letter-spacing 0.08em, `--link`, with a 13px arrow SVG; margin-top 16px. (In the prototype it's a no-op `href="#"` — wire to the real article route.)
  - **Archival photo** (`EventPhoto`, when `ev.img` set): aspect-ratio 1/1, 1px `--hairline` border, radius 2px, image `object-fit: cover` with `filter: grayscale(1) contrast(1.02)`. If no image, an on-brand diagonal-hatch placeholder with a camera glyph and "Archive photo" caption is shown.

Each row carries `data-year="1985"` so the rail's year tracking can observe it.

### Component: Category tag (`CatTag`)
Inline-flex, gap 7px. A 7px category-colour dot + the label in Cinzel 10px, `--fw-semibold`, letter-spacing 0.18em, uppercase. Colour = the category's `deep` shade on light surfaces (`onDark` variant available: white text, faint ring on the dot).

---

## Interactions & Behaviour
- **Expand/collapse row:** click anywhere on the compact header toggles the detail panel (local `open` state per row). Chevron rotates; height animates via the 0fr→1fr grid trick. Collapsed rows show the truncated lede; open rows hide it (the full standfirst appears in the panel instead).
- **Filtering:** selecting a category chip filters `QZ_TIMELINE` to that `cat`; sections with no matching events are dropped, counts and the total update, and the scroll observers re-bind. "All events" clears the filter.
- **Scroll tracking (two IntersectionObservers):**
  - *Decade* observer watches each `[data-decade]` section with `rootMargin: '-25% 0px -70% 0px'`; the intersecting section becomes the active decade (highlights rail + reveals its year sub-markers).
  - *Year* observer watches every `[data-year]` row with `rootMargin: '-30% 0px -65% 0px'`; the intersecting row sets the active year tick.
  - Both disconnect/re-observe when the filter or group set changes.
- **Jump navigation:** `window.scrollTo({ behavior: 'smooth' })` to the section (offset 84px) or first row of a year (offset 96px), to clear the sticky filter bar.
- **Motion:** all timings use `--ease-out`; respect `prefers-reduced-motion` (the shared `Reveal` helper already gates entrance animations — base state is visible, animation only added when motion is allowed).

## State Management
Component-local state is enough (no global store needed for this view):
- `filter` — active category key (`'all'` | `'music'` | `'live'` | `'milestone'` | `'loss'`).
- `activeDecade` — currently-in-view decade string (drives rail highlight + year expansion).
- `activeYear` — currently-in-view year string (drives year tick).
- `open` — per-row boolean for the expanded detail.
- Derived: `events` (filtered list), `groups` (events bucketed by decade with the sorted unique years present), `counts` (per-decade totals) — memoized on `filter`.

**Data:** in production, fetch the events from the CMS/API rather than a static array. Each event needs: `year` (string), `cat` (category key), `title`, `text` (standfirst/lede), `more` (secondary detail paragraph), `img` (URL or null). Decade bucketing: `year.slice(0,3) + '0s'`.

## Data model (from `tl-data.js`)
```
QZ_CATS: { music, live, milestone, loss } → { label, color, tint, deep }
QZ_TIMELINE: [{ year, cat, title, text, more, img }]   // 19 sample events, 1970–2024
QZ_DECADES: ['1970s','1980s','1990s','2000s','2010s','2020s']
qzDecadeOf(year): year.slice(0,3)+'0s'
```
⚠️ **Content caveats flagged in the data file:** verify all facts/dates before publishing; the **2024 catalogue-deal entry** is unverified. The questionnaire's "Community" category was folded into "Milestone" to keep the palette to four accents — split it back out if the product needs it.

## Assets
Referenced from `assets/` (relative `../../assets/` in the prototype):
- `crest-white.png` — Queen crest watermark in the hero.
- `img-hero.jpg` (Live Aid, 1985), `img-crowd.jpg` (Magic Tour, 1986), `img-portrait.jpg` (1991) — archival photos, displayed greyscaled. All other events use the built-in "Archive photo" placeholder until real imagery is supplied.

Provide production-resolution, rights-cleared imagery for each event; the greyscale + contrast treatment is applied in CSS, so supply full-colour originals.

## Files in this bundle
- `Queenzone Timeline.html` — entry point (CDN React + Babel; loads the scripts below).
- `tl-data.js` — category definitions, event data, decade helpers.
- `tl-shared.jsx` — shared helpers: `CatTag`, `FilterChips`, `Reveal`, `EventPhoto`.
- `tl-variant-a.jsx` — the timeline itself: `DecadeRail`, `EventRowA`, `TimelineDecades`.
- `tl-app.jsx` — mounts `<TimelineDecades />`.
- `tokens/` + `styles.css` — the Queenzone design tokens this design consumes (reference copy).

> Note: an earlier "Filmstrip" (horizontal) variant was explored and dropped; only the Decades direction is included here.
