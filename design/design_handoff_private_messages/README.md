# Handoff: Private Messages — thread view (option 1A, "Paper & ink")

## Overview
The private-message thread screen in the Queenzone app currently renders every message the same way: white text on near-black, right-aligned, with the author name and timestamp set at almost the same visual weight as the message itself. Users cannot tell at a glance who said what.

This redesign fixes that with three simultaneous, redundant signals per message:

1. **Alignment** — your messages right, the other person's messages left.
2. **Surface inversion** — incoming messages are bright warm-white "paper" cards with charcoal text; your own messages stay dark with a hairline outline. This is the single biggest contrast win, and it uses the design system's existing dark/light rhythm rather than introducing new colour.
3. **Identity** — a burgundy monogram avatar on the incoming side, plus a small Cinzel attribution line (name · time) above each speaker's group.

Date is no longer repeated on every row; it is promoted to a centred rule-and-label divider, and the per-message meta line drops to time only within a day.

## About the design files
The files in this bundle are **design references created in HTML** — prototypes that show the intended look and behaviour. They are not production code to lift.

The task is to **recreate this design in the app's existing environment** (React Native / React / whatever the Queenzone app uses), using its established component patterns, theming layer and message data model. If no environment exists yet, choose the most appropriate framework for the project and implement it there.

Do not port the prototype's `<x-import>` mounts, the iOS device frame, or the inline-style authoring approach — those are artefacts of the prototyping tool. Port the *visual spec* below.

## Fidelity
**High fidelity.** Colours, type, spacing, radii and states are final and are all drawn from the Queenzone design system tokens (copied into `tokens/` in this bundle). Recreate pixel-for-pixel using the codebase's existing token/theme values where they map, and add the few message-specific values listed under *Design tokens*.

---

## Screen: Private message thread

**Purpose:** read a one-to-one conversation and reply to it. Mobile-first; the prototype is drawn at 402 × 874 (iPhone 16 logical size). Safe areas are the device's own — the prototype's 54px top padding and 44px bottom padding stand in for the status bar and home indicator insets.

**Layout:** a full-height flex column with three regions:

| Region | Sizing | Background |
|---|---|---|
| Header | fixed height (content), `flex-shrink: 0` | `#111111` (Rich Black) |
| Message list | `flex: 1`, vertically scrollable, anchored to the bottom (newest visible) | `#0C0C0C` |
| Composer | fixed (content), `flex-shrink: 0` | `#111111` |

**Header and composer are pinned; only the message list scrolls.** The composer sits at the bottom of the viewport at all times (above the keyboard when it is open, and above the home-indicator inset when it is not) — it never scrolls away with the thread, so a long conversation and a long reply both stay usable. The input itself grows upward from one line to a maximum of ~5 lines and then scrolls its own content, so the composer's height changes but its bottom edge does not. When the keyboard opens, the message list shrinks rather than the composer moving, and the list keeps its scroll anchored to the newest message.

The two `#111111` chrome bands against the slightly darker `#0C0C0C` list give the thread its own visual "page", separated by 1px `rgba(255,255,255,0.16)` hairlines top and bottom of the list.

### 1. Header
- Padding `6px 14px 12px`; horizontal flex, `align-items: center`, `gap: 10px`; bottom border `1px solid rgba(255,255,255,0.16)`.
- **Back affordance (left, `flex-shrink: 0`):** chevron-left, 9 × 16, `stroke-width 1.6`, plus the word "Messages" — Inter 15px, `rgba(255,255,255,0.7)`, `gap: 3px`. Tap target must be padded out to ≥44px square even though the glyphs are smaller. Navigates back to the message list.
- **Title cluster (centre, `flex: 1`, centred row, `gap: 9px`):**
  - Monogram avatar: 28px circle, background Burgundy `#6B1F33`, initials in Cinzel 11px, `letter-spacing: 0.06em`, `#FFFFFF`. Initials = first letters of the correspondent's display name (max 2). If the user has a real avatar image, use it here, rendered greyscale-to-colour per the design system's image treatment.
  - Name: Cormorant Garamond 21px / weight 500, `letter-spacing: -0.01em`, `#FFFFFF`, `white-space: nowrap` (truncate with ellipsis if it overflows).
- **Overflow (right, 20px wide, right-aligned):** `···`, `rgba(255,255,255,0.55)`, 17px. Opens a sheet containing **Archive conversation**, **Report**, **Block**. The old always-visible gold "ARCHIVE CONVERSATION" link is gone from the top of the thread — it competed with the messages; a quiet gold "ARCHIVE" label remains in the composer (see below), and the full action lives in this menu.

### 2. Message list
Padding `22px 16px 8px`; flex column, `gap: 20px`. Scrolls; on open, scroll to the newest message.

**Date divider** (rendered whenever the calendar date changes, and above the first message):
- Row, `align-items: center`, `gap: 12px`: 1px `rgba(255,255,255,0.14)` rule — label — 1px rule (both rules `flex: 1`).
- Label: Cinzel 10px, `letter-spacing: 0.22em`, uppercase, `rgba(255,255,255,0.5)`, British date style — `2 AUGUST 2026`. Use `TODAY` / `YESTERDAY` for the last two days.

**Outgoing message (you) — right aligned**
- Column, `align-items: flex-end`, `gap: 6px`.
- Attribution line: Cinzel 9.5px, `letter-spacing: 0.2em`, `rgba(255,255,255,0.62)` — `YOU · 17:10`. Within the same day show time only; the prototype's second row reads `YOU · 3 AUG, 10:50` only because it sits under an earlier date divider — in implementation that message gets its own `3 AUGUST 2026` divider and the line becomes `YOU · 10:50`.
- Bubble: `max-width: 80%`; background `#171717`; border `1px solid rgba(255,255,255,0.28)`; `border-radius: 4px` (`--radius-md`); padding `12px 14px`; text Inter 16.5px / `line-height: 1.5`, `#FFFFFF`.
- No avatar on the outgoing side.

**Incoming message (them) — left aligned**
- Row, `gap: 10px`, `align-items: flex-start`.
- Avatar column: 28px burgundy monogram circle (same spec as the header avatar, Cinzel 10px), `margin-top: 18px` so it aligns with the first line of the bubble. Shown **only on the first message of a run**; subsequent messages from the same sender render a 28px empty spacer so the bubbles stay aligned.
- Content column: `max-width: 80%`, `gap: 6px`, `align-items: flex-start`.
  - Attribution line: Cinzel 9.5px, `letter-spacing: 0.2em`, `rgba(255,255,255,0.72)` — `RICHARD ORCHARD TW · 12:38`. On subsequent messages in the same run, the name is dropped and only the time is shown.
  - Bubble: background Warm White `#F7F6F3`; **no border**; `border-radius: 4px`; padding `12px 14px`; text Inter 16.5px / 1.5, Charcoal `#2B2B2B`.
- **Moderation flag** (when a message has been reported): row, `gap: 6px`, `padding-left: 2px`; 11px flag icon (Lucide `flag`, `stroke-width: 2`) + label `REPORTED` in Cinzel 9px, `letter-spacing: 0.18em`, Antique Gold `#B89A4A`. Gold is used here deliberately and rarely — it is the only accent in the list.

**Grouping rules**
- A "run" = consecutive messages from the same author with no date divider between them.
- First message of a run: avatar (incoming only) + full attribution (`NAME · TIME`).
- Later messages in a run: no avatar (spacer instead), time only.
- `gap: 20px` between messages regardless of run; the shared surface colour and alignment already bind a run together, so no tighter intra-run spacing is needed.

### 3. Composer
Padding `12px 16px 44px` (the 44px is the home-indicator inset); flex column, `gap: 10px`; top border `1px solid rgba(255,255,255,0.16)`; background `#111111`.
- **Context row** (`justify-content: space-between`), both items Cinzel 9.5px, `letter-spacing: 0.2em`:
  - Left: `REPLYING TO RICHARD ORCHARD TW`, `rgba(255,255,255,0.45)` — confirms who receives the reply, which the original screen never stated.
  - Right: `ARCHIVE`, Antique Gold `#B89A4A`, tappable (44px tap target), archives the conversation with a confirm sheet.
- **Input:** background `rgba(255,255,255,0.06)`; border `1px solid rgba(255,255,255,0.2)`; `border-radius: 4px`; padding `12px 14px`; text Inter 16px `#FFFFFF`; placeholder "Write a reply" at `rgba(255,255,255,0.45)`. Multiline, grows from 1 line to a max of ~5 lines then scrolls internally. Focus: border → `rgba(255,255,255,0.4)`, focus ring `rgba(36,74,143,0.45)`.
- **Send button:** design-system `Button`, size `md`, full width, overridden to background Antique Gold `#B89A4A`, text Rich Black `#111111`, no border, `border-radius: 3px`, label "Send reply" (Inter, uppercase tracking as per the DS button). Disabled when the input is empty: 40% opacity, no press state. Press: translate down 1px. Height ≥44px.

---

## Interactions & behaviour
- **Open thread:** list scrolled to bottom, newest message visible; mark conversation read.
- **Back:** returns to the conversation list, preserving its scroll position.
- **Send:** optimistically append the message to the list as an outgoing bubble with a `SENDING` state (attribution line reads `YOU · SENDING`, bubble at 60% opacity), then resolve to the real timestamp on success. On failure the attribution line becomes `YOU · NOT SENT` in Burgundy `#6B1F33` with a "Retry" text action beside it.
- **Long-press a message:** action sheet — Copy, Report, Delete (own messages only).
- **Overflow menu:** Archive conversation, Report user, Block user. Archive and Block both require confirmation.
- **Scroll:** the header keeps its opaque `#111111` background (no blur needed at this size); the list scrolls under nothing.
- **Motion:** all state changes use the design-system timings — 180ms for hover/press tints, 320ms for a new message fading and rising 6px into place, easing `cubic-bezier(0.22, 0.61, 0.36, 1)`. Respect `prefers-reduced-motion` / Reduce Motion: fade only, no translate.
- **Responsive:** single column at all mobile widths; message bubbles cap at 80% of the list width. On tablet/desktop, cap the thread column at 680px (the design system's reading measure) and centre it, keeping the same specs.
- **Accessibility:** incoming bubble is charcoal on warm white (≈12:1); outgoing is white on `#171717` (≈17:1); the dimmest text in the design is the attribution line at `rgba(255,255,255,0.62)` on `#0C0C0C` (≈8:1). Do not lighten backgrounds or dim these further. Each message should expose an accessible label of the form "<Name>, <time>: <message>" so screen-reader users get the same attribution the visual design provides. Body text must scale with OS text-size settings; bubbles grow vertically.

## State management
- `conversation` — `{ id, correspondent: { id, displayName, initials, avatarUrl? }, archived, blocked }`
- `messages[]` — `{ id, authorId, body, sentAt (ISO), status: 'sending' | 'sent' | 'failed', reported: boolean, deleted: boolean }`
- Derived at render time (do not store): `isOwn` (`authorId === currentUser.id`), `isFirstOfRun`, `showDateDivider`, formatted date/time strings.
- `draft` — composer text, persisted per conversation so a half-typed reply survives navigation.
- Data: fetch messages paginated newest-first, render oldest-first; `Load earlier messages` at the top of the list when more pages exist (Cinzel 9.5px tracked label, centred, `rgba(255,255,255,0.5)`).

## Design tokens
All from the Queenzone design system (`tokens/` in this bundle) unless marked **new**. `tokens/` is a copy of `design/tokens/` (the canonical source); don't hand-edit it — run `pwsh ./scripts/Sync-DesignTokens.ps1` from the repo root to refresh it after `design/tokens/` changes.

**Colour**
- `--qz-black` `#111111` — header, composer chrome
- **new** thread list background `#0C0C0C` — one step below Rich Black so the chrome reads as chrome
- **new** outgoing bubble `#171717`, with border `rgba(255,255,255,0.28)`
- `--qz-warm-white` `#F7F6F3` — incoming bubble
- `--qz-charcoal` `#2B2B2B` — incoming bubble text
- `--qz-white` `#FFFFFF` — outgoing bubble text, header name
- `--qz-burgundy` `#6B1F33` — monogram avatar, failed-send state
- `--qz-gold` `#B89A4A` — REPORTED flag, ARCHIVE label, send button
- `--border-on-dark` `rgba(255,255,255,0.16)` — region hairlines; `rgba(255,255,255,0.14)` for date-divider rules
- Text on dark: primary `#FFFFFF`; attribution `rgba(255,255,255,0.62)` (own) / `rgba(255,255,255,0.72)` (theirs); tertiary labels `rgba(255,255,255,0.45)`–`0.5`
- `--focus-ring` `rgba(36,74,143,0.45)`

**Typography**
- Message body — Inter 16.5px / 1.5
- Composer input — Inter 16px
- Header name — Cormorant Garamond 21px / 500, `-0.01em`
- Correspondent name in header monogram, attribution lines, date dividers, flags, composer labels — Cinzel, uppercase: 11px avatar initials; 10px date divider (`0.22em`); 9.5px attribution and composer labels (`0.2em`); 9px flags (`0.18em`)
- Cinzel is never used for message bodies.

**Spacing** (4px base): list padding `22px 16px 8px`; message gap `20px`; intra-message gap `6px`; avatar gap `10px`; bubble padding `12px 14px`; composer gap `10px`; composer padding `12px 16px` + bottom inset.

**Radius:** bubbles and input `4px` (`--radius-md`); button `3px` (`--radius-sm`); avatars fully round (the only circles in the design — they read as identity, not as chrome).

**Shadows:** none. Contrast comes from surface colour, not elevation.

## Assets
- **Icons** — Lucide, outline only, ~1.5px stroke: `chevron-left` (back), `flag` (reported), `more-horizontal` (rendered as `···` in the prototype; use the Lucide glyph in production).
- **Avatars** — no image assets; monogram initials on Burgundy. Real user avatars, when present, replace the monogram.
- **Fonts** — Cormorant Garamond, Inter, Cinzel (Google Fonts in the design system; self-host in production).
- `current-screen-before.png` — screenshot of the existing screen this redesign replaces, for reference.

## Files in this bundle
- `Private Message Thread 1A.dc.html` — **the design to build.** The chosen option on its own, in a device frame.
- `Private Messages (all three options).dc.html` — the full options board (1A chosen, 1B "Correspondence" and 1C "Speaker bands" not being built; included for context on what was rejected and why). Its stylesheet paths assume the project root, so open it from there rather than from this folder.
- `ios-frame.jsx` — prototyping-only device bezel. Not part of the design; do not port.
- `tokens/colors.css`, `tokens/typography.css`, `tokens/spacing.css`, `tokens/fonts.css` — the Queenzone design tokens the design is built from.
- `current-screen-before.png` — the current screen.
- `screenshots/1A-thread-view.png` — rendered screenshot of the design as built.
