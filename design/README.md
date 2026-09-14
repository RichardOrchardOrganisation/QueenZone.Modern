# Queenzone.org — Design System Handover

A complete implementation package for **Queenzone.org**, the relaunch of the long-running independent Queen fan community as a premium editorial archive. Hand this folder to a designer or developer (human or AI) to implement the site in a production codebase.

> **Positioning:** *The preserved archive of Queenzone.com.* The original community ran for years, then went dormant (the domain was lost). The goal now is to **publish the preserved archive** — news, articles, photography and forums — as a timeless, cinematic, editorial platform. It is **not** a recreation of the old site, and deliberately does **not** imitate the official queenonline.com. Queenzone owns a darker, "collector's box-set" identity.

---

## About these files

The files in this bundle are **design references created in HTML/React (JSX)** — high-fidelity prototypes showing the intended look, behaviour and component API. They are **not production code to copy verbatim.**

Your task is to **recreate these designs in the target codebase's environment** (React, Vue, Svelte, SwiftUI, Astro, plain HTML/CSS, a CMS theme, etc.) using its established patterns. If no codebase exists yet, choose the most appropriate stack — given the content scale (4,000+ articles, 100,000+ forum posts) a CMS or static-site generator with a component layer (Astro, Next.js, Eleventy) is a natural fit.

The one part that **is** production-ready and portable as-is: the **design tokens** in `tokens/*.css` and the root `styles.css`. Ship those directly, or translate the custom properties into your framework's theme system.

### Fidelity: **High-fidelity (hifi)**
Final colours, typography, spacing, motion and interactions. Recreate the UI pixel-accurately using the codebase's libraries and patterns; reproduce the tokens exactly.

---

## Brand foundations

**Attributes:** Timeless · Elegant · Cinematic · Editorial · British · Premium · Archival · Sophisticated · Respectful · Community-driven.
**Never:** corporate, commercial, social-media-inspired, overly nostalgic, retro-web, heavy-metal-themed, flashy or gimmicky.

**Design philosophy:** closer to a premium music documentary, a collector's edition box-set, an editorial publication or a luxury coffee-table book — than to a fan forum, social network or news portal. Prioritise storytelling, exploration and discovery. Generous white space. Type carries the hierarchy.

**Devices:** mobile-first (primary), desktop secondary but important, tablet supported.

**The signature move:** an **alternating dark / light section rhythm**. Rich-black `#111111` bands (Featured Articles, This Day in Queen History, Timeline, page mastheads, footer) carry **antique-gold** accents; warm-white bands (Explore, Photography, Discussions) carry the lighter content. This dual-tone cadence is what differentiates Queenzone from the all-bright official site.

---

## Design tokens

All tokens are CSS custom properties. The canonical source is `tokens/colors.css`, `tokens/typography.css`, `tokens/spacing.css`; `styles.css` `@import`s the set. **~90% monochrome, ~10% accent. Accent colour is used by meaning, never decoration. Gold is the rarest of all.**

`tokens/*.css` here is duplicated into `src/QueenZone.Web/wwwroot/design-system/tokens/` (the shipped copy) and into every `design_handoff_*/tokens/` folder (included so each handoff package's reference CSS reads standalone). Those copies are not hand-maintained: edit only the files in this `tokens/` folder, then run `pwsh ./scripts/Sync-DesignTokens.ps1` to propagate the change everywhere else. CI runs `pwsh ./scripts/Sync-DesignTokens.ps1 -Check` and fails if any copy has drifted.

### Colour — monochrome foundation
| Token | Hex | Use |
|---|---|---|
| `--qz-white` | `#FFFFFF` | Primary background |
| `--qz-warm-white` | `#F7F6F3` | Secondary backgrounds |
| `--qz-grey-light` | `#E8E8E8` | Borders & separators |
| `--qz-charcoal` | `#2B2B2B` | Primary text |
| `--qz-black` | `#111111` | Heroes, footer, dark bands |

Extended neutral ramp: `--qz-grey-50 #FBFBFA` · `-100 #F2F1ED` · `-200 #E8E8E8` · `-300 #D6D6D2` · `-400 #B4B4AF` · `-500 #8A8A85` · `-600 #5F5F5B` · `-700 #3D3D3B` · `-800 #2B2B2B` · `-900 #1A1A1A`.

### Colour — accents (by meaning)
| Token | Hex | Meaning / use | Hover/deep | Tint |
|---|---|---|---|---|
| `--qz-blue` | `#244A8F` | Royal Blue — links, active nav, CTA | `#1B3A72` | `#ECF0F7` |
| `--qz-purple` | `#5D3A8A` | Royal Purple — archive, history, timeline | `#492C6E` | `#F0ECF5` |
| `--qz-burgundy` | `#6B1F33` | Burgundy — featured, editorial, premium | `#551828` | `#F6ECEE` |
| `--qz-gold` | `#B89A4A` | Antique Gold — anniversaries, special badges, the gilt masthead rule (RAREST) | `#9C8038` | `#F6F1E3` |

**Semantic aliases** (reference these in product code): `--surface-page`, `--surface-raised`, `--surface-card`, `--surface-inverse`, `--surface-overlay`; `--text-primary/secondary/muted/on-dark/on-dark-muted`; `--border-default/strong/on-dark`, `--hairline`; `--link`, `--link-hover`; `--accent-archive/editorial/cta/special`; `--focus-ring`.

### Typography
- **Display — Cormorant Garamond** (`--font-display`): page titles, hero headlines, section headings, article standfirsts, drop-caps. Weight 400–600, tracking `-0.015em`, line-height 1.02–1.18.
- **Body / UI — Inter** (`--font-body`): body copy, navigation, interface. Body 17px / 1.6; long-form 18px / 1.75.
- **Titling — Cinzel** (`--font-titling`): uppercase eyebrows/kickers (tracking `0.22em`), timeline markers, anniversary titles. **Never body text.**

Fonts load from local Latin WOFF2 subsets in `tokens/fonts.css` (Cormorant Garamond, Inter, Cinzel) with `font-display: swap`.

Type scale: display `--fs-display-1..4` = 80 / 56 / 40 / 30px; body `--fs-lead` 21 · `--fs-body` 17 · `--fs-sm` 15 · `--fs-xs` 13; `--fs-eyebrow` 12px.

### Spacing — 4px base
`--space-1..10` = 4 · 8 · 12 · 16 · 24 · 32 · 48 · 64 · 96 · 128px. Section vertical rhythm ≈ 88px (`padding: 88px var(--gutter-lg)`). Layout: `--container-max 1280px`, `--container-text 680px` (reading measure), `--gutter 1.5rem`, `--gutter-lg 2.5rem`.

### Radii — restrained
`--radius-xs 2px` (inputs, tags) · `--radius-sm 3px` (buttons, cards) · `--radius-md 4px` (media) · `--radius-pill 999px` (tags/filters only). Never larger.

### Shadows — soft, editorial
`--shadow-sm 0 1px 2px rgba(17,17,17,.04)` · `--shadow-card 0 1px 3px rgba(17,17,17,.05), 0 8px 24px rgba(17,17,17,.06)` · `--shadow-lift 0 4px 12px rgba(17,17,17,.08), 0 18px 48px rgba(17,17,17,.10)`. Image scrims: `--scrim-bottom`, `--scrim-soft` (cinematic protection gradients over photography only — never decorative gradients elsewhere).

### Motion — subtle, slow, elegant
Easing `--ease-out cubic-bezier(.22,.61,.36,1)`. Durations `--dur-fast 180ms` (hover tints) · `--dur-base 320ms` (fades) · `--dur-slow 620ms` (image reveals). Preferred: fade transitions, gentle greyscale→colour image reveals, slow 1.04 scale, soft hover tints, smooth scrolling. Avoid parallax, flashy/constant motion. Respect `prefers-reduced-motion`.

---

## Components (the reusable primitives)

Full source for all eight primitives — each with its `.d.ts` prop contract and `.prompt.md` usage — is in **`COMPONENT_SOURCE.md`**. Recreate each in your framework with the same prop API.

| Component | Source (in `COMPONENT_SOURCE.md`) | Summary |
|---|---|---|
| `Button` | `components/core/Button.jsx` | Sharp 3px corners, uppercase, `0.04em` tracking. Variants `primary` (black) · `cta` (royal blue) · `secondary` (hairline) · `ghost` · `editorial` (burgundy). Sizes `sm/md/lg`. Props: `iconLeft/Right`, `fullWidth`, `disabled`, `href`. One accent button per section. |
| `IconButton` | `components/core/IconButton.jsx` | Square icon-only (32/40/48). Variants `ghost/outline/solid`, `active` (blue tint), `onDark`. Always pass a11y `label`. |
| `Input` | `components/core/Input.jsx` | Hairline border, royal-blue focus ring (`--shadow-focus`). `iconLeft`, sizes, `invalid` (burgundy border). |
| `Badge` | `components/editorial/Badge.jsx` | Cinzel content marker. Tones `neutral/archive/editorial/cta/special`; variants `soft/solid/outline`. Use `special` (gold) only for anniversaries/key highlights. |
| `Tag` | `components/editorial/Tag.jsx` | Pill taxonomy tag. `active`, `onDark`, `href`. Used in filter clusters. |
| `SectionHeader` | `components/editorial/SectionHeader.jsx` | Cinzel eyebrow + Cormorant title + hairline underline + optional trailing action. `onDark` turns the eyebrow gold. The homepage rhythm device. |
| `ArticleCard` | `components/editorial/ArticleCard.jsx` | Story card. B&W image eases to colour on hover (slow 1.04 scale). `layout vertical/horizontal`, `badge`, `onDark` (light text + gold kicker on black bands). |
| `CrestSeal` | `components/brand/CrestSeal.jsx` | The Queen crest as `seal / watermark (~6%) / ghost (~14%) / divider`. An emblem, not a clickable logo. |

Each component has a sibling `.d.ts` (prop types) and `.prompt.md` (one-line description + usage example + variants) — read those for exact APIs.

---

## Screens / views

Full click-through recreation source is in **`UI_KIT_SOURCE.md`** (desktop + mobile); rendered output is in `screenshots/`. Content data is centralised in `data.js` (`window.QZ_DATA`).

**Global chrome**
- **Header** (`Header.jsx`) — sticky masthead, **inverted dark variant** (`dark` prop): rich-black bg, white "QUEENZONE" wordmark (Cinzel, `0.18em`), nav with gold active underline, search + Sign in. Separated from content by a **gilt antique-gold hairline** (`1px rgba(184,154,74,0.55)`); soft drop-shadow fades in on scroll. 76px tall.
- **Footer** (`Footer.jsx`) — rich-black, crest watermark, four columns (brand blurb + newsletter, Archive, Community, About), gold column headings, independent-archive disclaimer.

**Pages** (routed by header nav; see `App.jsx`)
| View | File | Purpose |
|---|---|---|
| Home | `Hero.jsx` + `Sections1.jsx` + `Sections2.jsx` | Cinematic hero feature → Explore the Archive (4 cards) → **Featured Articles** (dark, tag filter) → Featured Photography (masonry) → **This Day in Queen History** (dark, gold dates) → Popular Discussions → Recently Restored → **Timeline Highlights** (dark) |
| News | `Pages1.jsx` → `NewsIndex` | Dark page-hero + year filter + chronological list rows (date · category · title · excerpt) |
| Articles | `Pages1.jsx` → `ArticlesIndex` | Lead feature (image + standfirst) + tag filter + 3-col card grid |
| Photography | `Pages2.jsx` → `PhotoGallery` | Filterable masonry (tall/wide/normal spans) with full-screen **lightbox** |
| Timeline | `Pages2.jsx` → `TimelinePage` | Warm-white vertical timeline, alternating sides, gold year markers on a centre rule |
| Forum | `Forum.jsx` → `ForumPage` | Dark community masthead (gold stats), 6-board index with latest-activity, recent-threads table (pinned markers, replies/views, Latest/Top/Unanswered tabs) |
| Article | `ArticleView.jsx` | Long-form reading view: archival B&W hero, drop-cap, 680px measure, author row, tags |
| Search | `App.jsx` → `SearchOverlay` | Full-screen blurred overlay with popular queries |

**Mobile** (`mobile.html`, `MobileScreens.jsx`) — Home, News, Photography, Article rendered in iPhone frames (mobile is the primary device).

### Layout specifics
- Editorial max width **1280px**, centred, `2.5rem` gutters; reading measure **680px**.
- Section blocks: `max-width:1280px; margin:0 auto; padding:88px 2.5rem`.
- Dark bands are full-bleed `#111111` with a faint crest watermark (~5–7% opacity) and gold eyebrows/markers.
- Cards: hairline `#E8E8E8` border on white, no border on warm-white, `--shadow-card`; hover lifts 3px (`--shadow-lift`) and image de-saturates to colour.

---

## Interactions & behaviour
- **Navigation** — header nav + Explore-the-Archive cards route between pages (SPA-style here; use real routes in production).
- **Read** — hero CTA, Featured Story cards and News rows open the article view.
- **Filter** — tag/year/category chips on Articles, News, Gallery, Forum (cosmetic in the prototype).
- **Lightbox** — clicking a gallery photo opens it full-screen on a dark blur scrim; click out or ✕ to close.
- **Search** — header search opens a full-screen overlay.
- **Header** — gilt hairline always present; translucent blur + drop-shadow engage after 12px scroll.
- **Imagery** — all photography is **black-and-white**, documentary/archival; eases gently toward colour on hover. No heavy filters or social-media grading.

---

## Iconography
**[Lucide](https://lucide.dev)** line icons (outline only, ~1.4–1.5 stroke). *This is a substitution — the brief specified no icon set; swap if a proprietary set is preferred.* Sizes 18–20px inline, 28px for section feature icons. **No emoji. No unicode-as-icon.** The crest is an emblem, not an icon.

---

## Content & voice
Knowledgeable, passionate, respectful, thoughtful, informative — authoritative but approachable, like museum wall-text or a well-edited documentary. **Avoid** clickbait, tabloid language, excessive fan worship. Largely third-person/editorial; addresses the reader only in invitations ("Explore the archive"). British dates (*13 July 1985*); decorative Roman numerals (Cinzel) for timeline markers. **No emoji anywhere.** Footer disclaimer: *"An independent fan archive. Not affiliated with Queen or its representatives."*

---

## Assets
In `assets/`:
- `crest-black.png` — crest on light surfaces
- `crest-white.png` — crest on dark surfaces / watermarks
- `crest-silver.png` — metallic crest for premium hero features
- `crest-lineart.png` — 1972-original line-art reference
- `crest-evolution.png` — crest variations chart (reference)
- `img-hero/stage/portrait/crowd/studio.jpg` — **abstract monochrome placeholders** standing in for licensed archival Queen photography. **Replace with real restored photographs in production** (the brief calls photography a major feature).

> The crest PNGs were processed from the user's original uploads into clean transparent versions. The "QUEEN" crest is the band's registered emblem — confirm usage rights for the relaunch.

---

## Mobile app

The native iOS + Android handoff (v2, 22 August 2026) lives in
[`Queenzone mobile app design/handoff/`](Queenzone%20mobile%20app%20design/handoff/).
Five tabs mirror the website nav: Home · News · Photography · Archive · Forum.
Read `STYLE_GUIDE.md` then `QUEENZONE_APP_SPEC.md` before adding screens.

## Files in this bundle
- `README.md` — this implementation brief.
- `DESIGN_GUIDE.md` — the full design-system guide (foundations, content fundamentals, visual foundations, iconography).
- `COMPONENT_SOURCE.md` — full source for every primitive (`.jsx` + `.d.ts` prop contract + `.prompt.md` usage), as reference code.
- `UI_KIT_SOURCE.md` — full source for the website recreation (desktop + mobile), as reference code.
- `styles.css` — global entry point (link this one file); `@import`s the tokens. **Production-ready; ship as-is or translate.**
- `tokens/` — `fonts.css`, `colors.css`, `typography.css`, `spacing.css`, `base.css`. **Production-ready.**
- `assets/` — crest variants + placeholder imagery.
- `screenshots/` — rendered reference: `01-home-hero.png`, `02-forum.png`, `03-mobile.png`.

### How to preview
The component and screen source lives in `COMPONENT_SOURCE.md` and `UI_KIT_SOURCE.md` as reference code, with rendered output in `screenshots/`. The live, runnable prototype (React + Babel) remains in the original Queenzone Design System project under `ui_kits/website/` (`index.html` desktop, `mobile.html` mobile) — open those to click through the real thing.

---

*Generated as a handover package. Self-sufficient — a developer who wasn't in the original conversation can implement Queenzone.org from this README alone.*
