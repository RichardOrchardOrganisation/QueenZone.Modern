# Handoff: Queenzone admin forms & controls

## Overview

The Queenzone Design System specifies the public archive (editorial cards, dark/light section rhythm, crest, three typefaces) but says nothing about **form controls or admin screens**. As a result the current admin renders browser defaults: 22px native buttons in listing rows, an unstyled `Choose file` control, an inline `Subject ▭` field, and a four-column news table with no mobile behaviour.

This package specifies the missing layer: buttons in an admin context, text fields, choice controls, the file/image picker, labels and validation, form layout, admin data tables with row actions, destructive confirmation, and how all of it behaves below 720px.

The design decision behind everything here: **admin is the same design system spoken more quietly.** Identical tokens, three reductions — no display type below the page title, no accent colour except where it carries meaning, and no editorial flourish (no crest watermarks, no dark bands, no scrims).

## About the design files

`Admin Form Style Guide.dc.html` is a **design reference created in HTML** — a specimen page showing the intended appearance and behaviour of each control. It is not production code to copy. The task is to recreate these patterns in the Queenzone codebase's existing environment using its established framework, component and styling conventions.

`admin-forms.css` is a **reference implementation** of the same rules in plain CSS against the design-system tokens. If the codebase uses vanilla CSS or CSS modules it can be adopted nearly as-is; if it uses Tailwind, styled-components, or a component library, treat it as the authoritative value table and port the values.

`tokens/` contains the design system's token files verbatim. **Do not redefine these values** — the admin layer must consume the same variables the public site does. In the codebase these are already loaded; they are included here so the CSS is readable standalone. `tokens/` is a copy of `design/tokens/` (the canonical source); don't hand-edit it — run `pwsh ./scripts/Sync-DesignTokens.ps1` from the repo root to refresh it after `design/tokens/` changes.

## Fidelity

**High fidelity.** Every measurement, hex value and type spec below is final and should be matched exactly. Where the spec differs from what the admin currently renders, the spec wins — the current state is the bug being fixed.

Two components already exist in the design system and must be **used, not rebuilt**: `Button` (five variants, three sizes) and `Input`. This document extends them.

---

## Design tokens

All values below are existing design-system tokens. Reference the CSS variable, not the hex.

### Colour

| Token | Value | Use in admin |
|---|---|---|
| `--qz-white` | `#FFFFFF` | Page and field background |
| `--qz-grey-50` | `#FBFBFA` | Table row hover, dropzone background |
| `--qz-grey-100` | `#F2F1ED` | Ghost hover, disabled field, draft status pill |
| `--surface-raised` / `--qz-warm-white` | `#F7F6F3` | Table header row, spec panels |
| `--border-default` / `--hairline` | `#E8E8E8` | Row separators, card borders |
| `--border-strong` | `#D6D6D2` | Field borders, unchecked controls |
| `--qz-grey-400` | `#B4B4AF` | Placeholder text |
| `--text-muted` / `--qz-grey-500` | `#8A8A85` | Help text, meta |
| `--text-secondary` / `--qz-grey-600` | `#5F5F5B` | Secondary body, select chevron |
| `--qz-grey-700` | `#3D3D3B` | Ghost button label |
| `--text-primary` / `--qz-charcoal` | `#2B2B2B` | Body text, field text |
| `--qz-black` | `#111111` | Primary button, checked controls |
| `--qz-blue` | `#244A8F` | Focus ring, links, Publish CTA, progress |
| `--qz-blue-tint` | `#ECF0F7` | Published status pill |
| `--qz-burgundy` | `#6B1F33` | Errors, destructive confirm |
| `--qz-burgundy-tint` | `#F6ECEE` | Error banner, danger hover |
| `--focus-ring` | `rgba(36,74,143,0.45)` | 3px focus ring |
| `--surface-overlay` | `rgba(17,17,17,0.62)` | Dialog scrim |

**Gold (`#B89A4A`) never appears in admin.** It is reserved for anniversaries and special badges on the public site.

### Typography

- `--font-display` Cormorant Garamond — **page titles only** (38px / 1.12 / -0.015em, weight 500) and dialog titles (26px / 1.2).
- `--font-body` Inter — everything else.
- `--font-titling` Cinzel — fieldset legends and table column headers only (10–11px, 0.2–0.22em tracking, uppercase, weight 600). Never body text.

| Role | Spec |
|---|---|
| Field text | Inter 15px / 1.4 (16px on mobile) |
| Label | Inter 13px / 600 / 0.02em, sentence case |
| Help & error | Inter 13px / 1.5 |
| Button label | Inter 14px / 500 / 0.04em, uppercase |
| Table row title | Inter 15px / 1.5 (16px / 1.4 mobile) |
| Table column header | Cinzel 10px / 600 / 0.2em, uppercase |
| Status pill | Inter 11px / 600 / 0.06em, uppercase |

### Spacing, radius, motion

4px base scale (`--space-1` 4 … `--space-10` 128).

| Value | Use |
|---|---|
| 7px | Label → field, field → help |
| 12px (`--space-3`) | Button-to-button in a group; control → label |
| 24px (`--space-5`) | Field → field |
| 32px (`--space-6`) | Last field → action row |
| 48px (`--space-7`) | Fieldset → fieldset, with a 1px hairline |
| `--radius-xs` 2px | Fields, status pills |
| `--radius-sm` 3px | Buttons, cards, dialog |
| `--radius-md` 4px | Media frames |
| `--radius-pill` | Radios, toggles, tags only |
| `--shadow-focus` | `0 0 0 3px var(--focus-ring)` |
| `--shadow-lift` | Dialog elevation |
| `--dur-fast` 180ms / `--dur-base` 320ms, `--ease-out` `cubic-bezier(0.22,0.61,0.36,1)` | All transitions |

Respect `prefers-reduced-motion: reduce` — drop transitions, keep state changes instant.

---

## Components

### 1. Buttons

Use the design system's `Button` component. Do not create a sixth variant.

| Variant | Appearance | Admin use |
|---|---|---|
| `primary` | Black fill, white label | The single committing action — Save, Post reply |
| `cta` | Royal Blue fill | Publish only — the act that makes content public |
| `secondary` | Transparent, 1px `--border-strong` | Cancel, Unpublish, Choose file |
| `ghost` | No chrome until hover | Table row actions, toolbars |
| `editorial` | Burgundy fill | Destructive confirmation only — never in a listing row |

Sizes as shipped by the component: `sm` padding 8/16, 13px type (34px tall) · `md` padding 12/24, 14px type (43px tall) · `lg` padding 16/34, 15px type (51px tall). Label is uppercase, 0.04em tracking, weight 500; radius 3px; press state `translateY(1px)` over 180ms; disabled is `opacity: 0.45` with `not-allowed`.

**Admin defaults:** `md` for form actions. `lg` full-width for a mobile primary submit. Table rows use the ghost row-action override below (fixed 32px, padding 0 12px) rather than `sm`, so rows stay compact.

**Grouping rules**
- 12px gap, laid out with flex `gap` — never source whitespace or per-element margins.
- Order left → right: primary, secondary, ghost/link.
- Destructive sits apart — `margin-left: auto` or a 32px separation.
- Never mix a real button and a bare text link in one group; promote the link to `ghost`.
- Full width only below 720px, and only for the primary.

### 2. Text fields, textareas, selects

Class `.qz-input` — one shell for all three.

- Height 40px, padding 0 12px, Inter 15px/1.4, background white, 1px `--border-strong`, radius 2px.
- **Focus:** border `--qz-blue` plus a 3px `--focus-ring` ring. Never remove the outline without replacing it.
- **Invalid:** border `--qz-burgundy` and `aria-invalid="true"`, message below.
- **Disabled:** background `--qz-grey-100`, muted text.
- **Textarea:** minimum 3 rows, `resize: vertical` only, padding 10px 12px, line-height 1.6.
- **Select:** same shell, `appearance: none`, 5px CSS chevron 18px from the right edge.
- **Mobile:** height 48px, font-size 16px (below 16px iOS zooms the viewport on focus).

Rules:
- Labels sit **above** the field, always. The current inline `Subject ▭` layout in the new-thread form is wrong at every width.
- Placeholders are examples, never a replacement for a label.
- Width is set by content, not container: a subject line fills the column, a date is 180–200px.
- The editing column caps at 680px (`--container-text`). Beyond that add a second column rather than stretching.

### 3. Choice controls

The whole row is the hit target — control, label and description — minimum 44px tall, `cursor: pointer`, wrapped in a `<label>`.

- Box 18px, 1px `--border-strong`, radius 2px (checkbox) or pill (radio).
- Checked state fills `--qz-black` with a white 2px-stroke tick — **not blue**. Radio uses a 9px black dot.
- 12px control-to-label gap; 4px between rows. Optional 13px muted description under the label.
- Toggle: 40 × 22px track, 16px white knob, 3px inset, `--qz-black` when on, `--border-strong` when off, 320ms ease.
- Use a checkbox for independent options, radios for one-of-few, a toggle **only** where the change applies immediately without a save.

Keep the native input for accessibility (visually hidden or `appearance: none`) and drive the styled box from `:checked`.

### 4. File & image picker

The native `<input type="file">` is always visually hidden.

- Dropzone: `180px minmax(0,1fr)` grid, 20px gap, `align-items: start`, 1px **dashed** `--border-strong`, radius 3px, 16px padding, `--qz-grey-50` background.
- Preview: `width: 100%`, `aspect-ratio: 3 / 2`, `object-fit: cover`, radius 2px. Empty state is the crest-on-black 3:2 frame with the caption "No article image yet — listings will use this placeholder."
- Right column: "Drop an image here, or" + `secondary` **Choose file** and `ghost` **Choose from gallery** (12px gap) + a 13px muted constraint line: "JPEG, PNG or WebP · max 10 MB · cropped to the 3:2 news-card frame. Gallery originals are never modified."
- Drag-over state: border `--qz-blue`, background `--qz-blue-tint`.
- Upload progress: 3px `--border-default` track, `--qz-blue` fill, percentage label alongside.
- Once an image exists the dropzone becomes thumbnail + **Replace** / **Remove** ghost buttons.
- Constraints are stated before the user picks, not in an error dialog after.
- Mobile: single column, preview full width above the buttons.

### 5. Labels, help text, validation

Anatomy, top to bottom: label → field → help → error.

- Mark the **optional** field with "(optional)" in `--text-muted`; do not asterisk the required ones.
- Error: 13px `--qz-burgundy` with a 15px filled burgundy dot bearing a white `!`.
- **Timing:** validate on blur, never on keystroke; clear the error the moment the value becomes valid; re-validate everything on submit.
- **Summary:** on failed submit, a banner at the top of the form — 1px `--border-default` with a 2px `--qz-burgundy` left edge on `--qz-burgundy-tint` — headed "Two fields need attention" and listing failed fields as anchor links. Move focus to the banner.
- Tone is editorial: plain, specific, never scolding or jokey. "This slug is already used by an article from 2019." — not "Oops! Something went wrong."

### 6. Form layout

- Column max 680px for anything holding published prose.
- 24px between fields; 48px between fieldsets with a hairline rule.
- Fieldset legends are Cinzel eyebrows: **Content**, **Image**, **Publication**, **Advanced**.
- Rarely-used settings live in a collapsed **Advanced** group, closed by default.
- Action row 32px below the last field, left-aligned on desktop: Save (primary) · Cancel (secondary) · Preview (ghost), 12px gaps. This replaces the current button + bare pipe + two links arrangement.
- Page title is Cormorant 38px — the only display type on the screen.
- Warn on navigation away with unsaved changes.

### 7. Admin data table

Structure: shell → filter bar → horizontal scroll wrapper → header row + rows.

- Grid `minmax(240px, 1fr) 110px 120px 200px`, 20px gap, 16px vertical / 24px horizontal padding, minimum row height 56px.
- The table has a `min-width: 760px` inside an `overflow-x: auto` wrapper so it degrades to a scroll rather than crushing between 720px and the layout's comfortable width.
- Header row: Cinzel 10px on `--surface-raised`, 12px/24px padding.
- 1px `--border-default` between rows — **no zebra striping**. Hover tints the row `--qz-grey-50`.
- Filter bar above the header: result count on the left ("Showing 1–50 of 5,280 articles"), 32px search input and status select on the right.
- Status pill: 22px tall, 0 9px padding, radius 2px — Published `--qz-blue-tint`/`--qz-blue`, Draft `--qz-grey-100`/`--text-secondary`, Flagged `--qz-burgundy-tint`/`--qz-burgundy`.

**Row actions** — small ghost buttons, 32px tall, 4px gaps:
- The **title is the only link in the row** and opens the editor. Drop the separate "Edit / Preview" text-link pair currently in the markup.
- Maximum three visible actions; anything beyond goes into an overflow menu.
- Delete is last, separated, and only turns burgundy (`--qz-burgundy-tint` background) on hover.
- Ghost buttons must have a visible `:focus-visible` ring — they have no resting chrome.

### 8. Destructive actions

This archive exists because material was nearly lost once. Deletion is always confirmed, always names the item, and is a soft delete wherever the data model allows.

- Trigger: ghost button, burgundy on hover only.
- Dialog: 420px, `--shadow-lift`, radius 3px, 28px padding, scrim `rgba(17,17,17,0.62)`. Title Cormorant 26px. Body names the item and states what actually happens: "'Barry Mitchell, Early Queen Bassist, Has Died' will be removed from the public archive and moved to the trash for 30 days."
- Actions right-aligned, 12px gap: Cancel (`secondary`) then Delete (`editorial` burgundy). The confirm button carries the verb — never "OK".
- Focus opens on **Cancel**; Esc closes; focus is trapped while open and returns to the trigger on close.
- After: a hairline toast with an **Undo** link, 8 seconds.
- Irreversible deletions (purging trash, removing a member's post history) additionally require typing the item's name. Bulk deletion always states the count.

### 9. Mobile — below 720px

Admin is used from a phone: moderating a thread on a train, approving a photo submission.

| Rule | Value |
|---|---|
| Breakpoint | 720px |
| Touch targets | 44px minimum; 48px for primary form controls |
| Field type size | 16px — never smaller, or iOS zooms on focus |
| Page gutter | 16px; cards run edge to edge |
| Tables | Become stacked cards: status pill + date on one meta row, title below at 16px/1.4, actions in a full-width row |
| Card actions | Edit and the state action flex to equal width with a visible `--border-strong` border; Delete becomes a 44 × 44px icon-only button |
| Form actions | Sticky bottom bar, translucent white with blur, 1px top hairline; primary flexes to fill, Cancel keeps its label width |
| Destructive confirm | Full-screen sheet rather than a centred dialog |
| Two-column field rows | Stack |
| Dropzone | Single column, preview full width |

---

## State & behaviour summary

Per form: `values`, `touched` (per field), `errors` (per field), `isSubmitting`, `isDirty`, `submitError`. Validate on blur and on submit; clear a field error as soon as it becomes valid; block navigation while `isDirty`.

Per upload: `file`, `previewUrl`, `progress` (0–100), `uploadError`. Revoke object URLs on unmount.

Per listing: `page`, `pageSize` (50), `query`, `statusFilter`, `total`, `rows`, `pendingDeleteId`, `lastDeleted` (for the 8-second undo window).

Dialogs: `isOpen`, initial focus on Cancel, focus trapped, focus returned to the trigger on close, `aria-modal="true"` with `aria-labelledby` on the title.

## Accessibility

- Every field has a real `<label for>`; help and error text are wired with `aria-describedby`; invalid fields carry `aria-invalid="true"`.
- Error summary is a focusable `role="alert"` region on submit failure.
- Table is a real `<table>` semantically if the codebase allows, or `role="table"`/`role="row"`/`role="cell"` over the grid; the mobile card view should read as a list.
- Ghost buttons need `:focus-visible` rings; icon-only buttons need `aria-label`.
- Status pills must not rely on colour alone — the word is always present.
- All interactive controls reachable and operable by keyboard; visible focus everywhere.

## Assets

No new assets. The empty-image placeholder uses the existing crest asset on `--qz-black` in a 3:2 frame (`assets/crest-white.png` or the gold Q monogram already used in the admin). Icons are Lucide outline at ~1.5px stroke, 16–20px in UI — the trash glyph in the mobile card is Lucide `trash-2`.

## Files in this bundle

| File | What it is |
|---|---|
| `Admin Form Style Guide.dc.html` | The design reference — open in a browser to see every specimen. Depends on the token files below. |
| `admin-forms.css` | Reference implementation of every rule in this document, against the design-system tokens. |
| `tokens/*.css` | The design system's token files, verbatim — already present in the codebase. |

## Suggested implementation order

1. Token check — confirm the admin loads the same token files as the public site.
2. Field primitives: Input, Textarea, Select, Label, HelpText, ErrorText, Field wrapper.
3. Choice controls: Checkbox, Radio, Toggle.
4. Ghost button override + status pill.
5. Admin table shell with row actions, then the sub-720px card collapse.
6. File/image dropzone with gallery picker.
7. Confirmation dialog + undo toast.
8. Retro-fit the three known screens: Admin news listing, Article edit, New thread.
