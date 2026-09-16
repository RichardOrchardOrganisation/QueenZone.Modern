# Handoff: The Friday Ten — Quiz & Leaderboard (Queenzone)

## Overview
A weekly ten-question quiz drawn from the Queenzone archive ("The Friday Ten") plus its
competitive leaderboard, and a set of evergreen topic quizzes. Ten questions, twenty seconds
each, verdict revealed immediately after each answer, points weighted by answer speed.
Standings run weekly (closing Sunday 23.59 BST) and all-time.

Three surfaces are designed:
1. **Desktop web** — 1280px editorial container (4 screens)
2. **Mobile web** — 390px, the *primary* surface per the brand brief (4 screens)
3. **Native app** — iOS, 402x874, adds streaks, badges and offline play (3 screens)

## About the Design Files
The files in this bundle are **design references created in HTML** — prototypes that show the
intended look and behaviour. They are **not production code to copy directly**. The design file
uses a bespoke template runtime (`support.js`, `<x-dc>`, `{{ }}` holes, `<sc-for>`) that exists
only to make design iteration fast; none of that should reach production.

The task is to **recreate these designs in the target codebase's existing environment**
(React/Next, Vue, SwiftUI, etc.) using its established patterns, component library and routing.
If no environment exists yet, choose the most appropriate framework and implement there. The
Queenzone design tokens (below) should be expressed as whatever the codebase already uses for
theming (CSS custom properties, Tailwind theme, SwiftUI Color extensions...).

All three platforms appear in **one** design file side-by-side on a canvas — it is a design
board, not an app shell. Ignore the board chrome (the grey `#E4E2DD` background, the
"Platform I / II / III" section headers, the numbered captions above each frame, the iPhone
bezel). Everything inside a `[data-screen-label]` element is the real screen.

## Fidelity
**High-fidelity (hifi).** Final colours, typography, spacing and interaction behaviour.
Recreate pixel-accurately using the codebase's libraries. Exact values are given below and can
also be read off the HTML.

Two caveats:
- Photography is **placeholder** monochrome imagery from the design system (`assets/img-*.jpg`).
  Real archive photographs replace them. The greyscale treatment is intentional and must stay.
- The 20-second countdown is shown as a static value in the mocks (`14` / `08`). It is a live
  timer in production — see *Interactions*.

---

## Design language (read this first)
The Queenzone design system is ~90% monochrome, with accent colour used **by meaning, never
decoration**. Type carries the hierarchy. The signature is an **alternating dark/light rhythm**:
rich-black `#111111` bands for drama, warm-white `#F7F6F3` for supporting content. The Queen
crest appears as a faint watermark (5–7% opacity) behind dark sections. Radii are restrained
(0–4px; pills only for tabs/badges). Shadows are soft and low. Motion is slow and quiet.

Quiz-specific conventions established in this design:
- **Antique Gold `#B89A4A` = correct / special.** Used for the correct answer, the countdown,
  progress bar, top-3 ranks, badges, eyebrows on dark. It is the rarest colour in the system —
  do not spread it further.
- **Burgundy `#6B1F33` = incorrect.** On dark grounds the *text* lifts to `#C98B9B` for contrast;
  the border/fill stays burgundy.
- **Royal Blue `#244A8F` = the user's own row**, active nav, primary CTA.
- No red/green. No emoji. No celebratory animation — the verdict is stated, not celebrated.

---

## Screens / Views

### WEB 01 — Quiz landing  (`[data-screen-label="Web 01 — Landing"]`)
**Purpose:** entry point; start this week's quiz, or pick an evergreen topic quiz.
**Layout:** 1280px wide, white page. Three stacked bands: nav bar → dark hero → warm-white
evergreen grid.

*Nav bar* — `padding: 18px 48px`, `border-bottom: 1px solid #E8E8E8`, space-between.
Left: crest-black at `height:30px`, `opacity:.9`, + wordmark "Queenzone" in Cinzel 13px/600,
`letter-spacing:.28em`, uppercase, `#2B2B2B`. Right: nav items in Inter 12px/500,
`letter-spacing:.08em`, uppercase, `#5F5F5B`, `gap:30px` — News · Stories · Photography ·
Timeline · **Quiz** · Forums. Active item "Quiz" is `#244A8F` with
`border-bottom:2px solid #244A8F; padding-bottom:5px`.

*Hero* — background `#111111`; the placeholder photo fills it `object-fit:cover`,
`filter:grayscale(1); opacity:.34`; over it a left-to-right scrim
`linear-gradient(90deg, rgba(17,17,17,.96) 0%, rgba(17,17,17,.82) 45%, rgba(17,17,17,.35) 100%)`.
crest-white watermark `height:300px; opacity:.06`, right `64px`, vertically centred.
Content `padding:76px 48px 68px`, `max-width:760px`, flex column `gap:22px`:
- Row: Badge (DS `Badge`, `tone="special" variant="outline"`) reading "Week 34", then Cinzel 11px/600
  `.22em` uppercase gold "The Friday Ten".
- H2: Cormorant Garamond `400 68px/1.02`, `letter-spacing:-.015em`, `#FFFFFF` —
  "Twenty-one minutes at Wembley".
- Standfirst: Inter `400 19px/1.6`, `rgba(255,255,255,.72)`, `max-width:560px` —
  "Ten questions on Live Aid, 13 July 1985 — the set, the crowd, the aftermath. Twenty seconds each."
- Meta: Cinzel 11px/600 `.2em` uppercase `rgba(255,255,255,.5)` —
  "10 questions · 20 seconds each · 4,182 played · closes Sunday 23.59".
- Buttons `gap:14px`, `padding-top:10px`: DS Button `variant="cta" size="lg"` "Begin the quiz";
  DS Button `variant="secondary" size="lg"` "View standings" overridden for dark ground
  (`color:#FFF; border-color:rgba(255,255,255,.4)`).

*Evergreen quizzes* — `padding:64px 48px 72px`, background `#F7F6F3`. DS `SectionHeader`
(`eyebrow="Test yourself" title="Evergreen quizzes"`), then a 3-col grid, `gap:28px`,
`padding-top:32px`. Each card is a link, flex column:
- Image `aspect-ratio:3/2`, `border-radius:4px`, `filter:grayscale(1)`, bg `#E8E8E8`.
- Below (`padding-top:16px`, `gap:8px`): category eyebrow Cinzel 11px/600 `.2em` uppercase
  `#5D3A8A` (purple = archive); title Cormorant `500 24px/1.2` `#2B2B2B`; description
  Inter `400 14px/1.5` `#5F5F5B`; personal-best line Cinzel 10px/600 `.2em` uppercase
  `#8A8A85`, `padding-top:4px`.
- Exact content: **Albums / Fifteen studio records** / "Sleeves, sessions and running orders.
  24 questions." / "Your best · 19/24" — **Lyrics / Words and writers** / "Who wrote what, and
  what it says. 30 questions." / "Not yet attempted" — **Live shows / On the road, 1970–1986** /
  "Venues, set lists and tour dates. 28 questions." / "Your best · 22/28".
- Hover (design-system rule, not yet in the mock): image eases greyscale→colour over 620ms,
  card lifts 3px with `--shadow-lift`.

### WEB 02 — Question & instant verdict  (interactive in the mock)
**Purpose:** answer one question, see the verdict immediately.
**Layout:** 1280px, full `#111111`. crest-white watermark `height:520px; opacity:.05`, centred at
`top:56%`, `pointer-events:none`.

*Quiz bar* — `padding:16px 48px`, `border-bottom:1px solid rgba(255,255,255,.16)`, space-between.
Left: crest-white `24px` + "The Friday Ten · Week 34" (Cinzel 11px/600 `.22em` uppercase,
`rgba(255,255,255,.6)`). Right, `gap:28px`: "Question 04 / 10" (Cinzel 11px, `rgba(255,255,255,.5)`);
the timer chip — `padding:6px 14px`, `border:1px solid #B89A4A`, `border-radius:2px`, containing
label "Time" (Cinzel 10px, `rgba(255,255,255,.55)`) and the value in Cormorant `500 20px/1`
gold, `font-variant-numeric:tabular-nums`; then "Exit quiz" (Inter 14px, `rgba(255,255,255,.45)`).

*Progress* — 2px track `rgba(255,255,255,.10)`, fill 2px gold at `width:35%` (question 4 of 10 →
`(n-0.5)/10`; animate width over 320ms `--ease-out`).

*Body* — grid `460px 1fr`, `gap:56px`, `padding:56px 48px 60px`.
- Left: image `aspect-ratio:4/5`, `border-radius:4px`, bg `#1a1a1a`,
  `filter:grayscale(1) contrast(1.05)`; caption below (`gap:14px`) Cinzel 10px/600 `.2em`
  uppercase `rgba(255,255,255,.4)` — "Wembley Stadium · 13 July 1985 · Queenzone archive".
- Right: flex column `gap:32px`, `padding-top:8px`.
  - Eyebrow Cinzel 11px/600 `.22em` uppercase gold — "Question four · Live Aid".
  - H2 Cormorant `400 46px/1.1`, `-.015em`, white, `max-width:620px`, `text-wrap:pretty` —
    "How long was Queen's set at Live Aid?".
  - **Answer options** — flex column `gap:12px`, `max-width:660px`. Each is a `<button>`:
    `position:relative; display:flex; align-items:center; gap:20px; width:100%;`
    `padding:20px 22px; background:rgba(255,255,255,.045);`
    `border:1px solid rgba(255,255,255,.16); border-radius:3px; text-align:left;`
    `transition: background 180ms cubic-bezier(.22,.61,.36,1)`; hover
    `background:rgba(255,255,255,.10)`.
    Children: letter (Cinzel 12px/600 `.18em`, `rgba(255,255,255,.45)`, `width:18px`);
    label (`flex:1`, Inter `400 19px/1.3`, white); a right-aligned verdict tag; and an absolutely
    positioned overlay ring (`inset:0`, `border-radius:3px`, `pointer-events:none`,
    `transition:all 320ms --ease-out`) that carries the answered state.
    Options: A "17 minutes" · B "21 minutes" (**correct**) · C "25 minutes" · D "30 minutes".
  - **Unanswered hint** (replaces the verdict panel) — Cinzel 10px/600 `.2em` uppercase
    `rgba(255,255,255,.35)` — "Answer to reveal the verdict · faster answers score more".
  - **Verdict panel** (after answering) — `max-width:660px`, `padding:24px 26px`,
    `border:1px solid rgba(255,255,255,.16)`, `border-left:2px solid #B89A4A`,
    `background:rgba(255,255,255,.03)`, `border-radius:3px`, flex column `gap:14px`:
    verdict word Cormorant `400 26px/1` `-.01em` — "Correct" in `#B89A4A` or "Not quite" in
    `#C98B9B`; right-aligned score Cinzel 11px/600 `.2em` uppercase `rgba(255,255,255,.5)` —
    "+180 points · 6.2s" (or "+0 points · 6.2s"); explanation Inter `400 16px/1.65`
    `rgba(255,255,255,.7)` — "Queen took the Wembley stage at 6.41pm and played for roughly
    twenty-one minutes — six songs, no encore. It is routinely named the finest live performance
    in rock."; then DS Button `variant="cta" size="md"` "Next question" beside a link
    "Read the full account" (Inter 14px, `rgba(255,255,255,.55)`, 1px underline via
    `border-bottom:1px solid rgba(255,255,255,.3); padding-bottom:2px`).

**Answered state, exactly** (applied to the overlay ring + tag of each option):

| option | border | background | tag text | tag colour |
|---|---|---|---|---|
| correct answer (always shown once answered) | `2px solid #B89A4A` | `rgba(184,154,74,.13)` | "Correct" | `#B89A4A` |
| the user's pick, when wrong | `2px solid #6B1F33` | `rgba(107,31,51,.28)` | "Your answer" | `#C98B9B` |
| all others | `1px solid transparent` | transparent | — | — |

Tag type: Cinzel 9.5px/600, `letter-spacing:.18em`, uppercase, `white-space:nowrap`.
Transition: `all 320ms cubic-bezier(.22,.61,.36,1)`.

### WEB 03 — Results & breakdown
**Purpose:** show the score, the paper question-by-question, and route on to standings.
**Layout:** 1280px white. Dark summary band → white breakdown table.

*Summary band* — `#111111`, `padding:64px 48px 56px`, crest-white `height:260px; opacity:.07`
right `56px` centred. Row, `align-items:flex-end`, space-between:
- Left (`gap:18px`): eyebrow Cinzel 11px gold "The Friday Ten · Week 34 · Complete";
  H2 Cormorant `400 56px/1.05` white "Eight from ten"; copy Inter `400 18px/1.6`
  `rgba(255,255,255,.66)`, `max-width:480px` — "Better than 91% of players this week. Two more
  and you would have taken the podium."
- Right (`gap:56px`, `padding-bottom:6px`): three stats, each label Cinzel 10px/600 `.2em`
  uppercase `rgba(255,255,255,.45)` over a value in Cormorant `400 44px/1` tabular-nums —
  **Points 1,420** (white) · **Avg. time 7.4s** (white) · **Rank 37** (gold).

*Breakdown* — `padding:56px 48px 64px`. DS `SectionHeader` (`eyebrow="Your paper"
title="Question by question"`). Rows: grid `44px 1fr 220px 90px 80px`, `gap:20px`,
`padding:18px 0`, `border-bottom:1px solid #E8E8E8`. Columns: number Cinzel 12px/600 `.14em`
`#B4B4AF`; question Inter `400 17px/1.4` `#2B2B2B`; answer given Inter `400 15px/1.4` `#5F5F5B`;
mark Cinzel 10px/600 `.18em` uppercase — "Correct" in `#9C8038` (gold-deep, for contrast on
white) or "Incorrect" in `#6B1F33`; points right-aligned Inter `400 15px/1.4` `#8A8A85`
tabular-nums.
Footer row (`padding-top:36px`, `gap:16px`): DS Button `primary/lg` "View the standings";
DS Button `secondary/lg` "Share result"; then `margin-left:auto` note Inter `400 15px/1.5`
`#8A8A85` — "Next quiz opens Friday, 4 September at 08.00 BST."

See *Sample data* for all ten paper rows.

### WEB 04 — Leaderboard  (tabs interactive in the mock)
**Purpose:** weekly and all-time standings, with the user's own position always visible.
**Layout:** 1280px white. Nav bar (identical to Web 01) → dark podium band → warm-white table.

*Podium band* — `#111111`, `padding:56px 48px 52px`, crest-white `height:340px; opacity:.05`
centred. Header row `align-items:flex-end` space-between: left eyebrow Cinzel 11px gold
"Standings" + H2 Cormorant `400 44px/1.05` white showing the tab title; right the tab pair
(`gap:10px`).
**Tab pill:** `padding:9px 20px` (desktop) / `7px 14px` (mobile), Cinzel 10.5px / 9.5px, weight
600, `letter-spacing:.18em`, uppercase, `border-radius:999px`,
`transition:all 180ms --ease-out`. Active: `color:#111111; background:#FFFFFF;
border:1px solid #FFFFFF`. Inactive: `color:rgba(255,255,255,.65); background:transparent;
border:1px solid rgba(255,255,255,.16)`. Labels "This week" / "All time".
*Podium cards* — 3-col grid `gap:24px`. Each: `padding:24px 22px`, `border-radius:4px`,
flex column `gap:18px`. Rank 1 gets `border:1px solid #B89A4A` and
`background:rgba(184,154,74,.07)`; ranks 2–3 get `border:1px solid rgba(255,255,255,.16)` and
`background:rgba(255,255,255,.03)`. Contents: rank numeral Cormorant `400 56px/1` gold
tabular-nums; then avatar + name block (`gap:14px`) — avatar 44px circle,
`background:rgba(255,255,255,.07)`, `border:1px solid rgba(255,255,255,.28)`, initials Cinzel
12px/600 `rgba(255,255,255,.85)`; name Cormorant `500 20px/1.1` white; sub-line Cinzel 10px/600
`.18em` uppercase `rgba(255,255,255,.45)`; then points Cormorant `400 30px/1` white
tabular-nums + the word "points" in Cinzel 10px uppercase `rgba(255,255,255,.45)`.

*Table* — `padding:8px 48px 20px`, background `#F7F6F3`. Header row grid
`64px 1fr 180px 130px 110px`, `gap:20px`, `padding:20px 0 12px`, all cells Cinzel 10px/600
`.2em` uppercase `#8A8A85`: Rank · Member · **Score** (weekly) / **Accuracy** (all-time) ·
Avg. time (right) · Points (right).
Data rows: same grid, `padding:16px 0`, `border-bottom:1px solid #E8E8E8`. Rank Cormorant
`400 24px/1` tabular-nums, `min-width:32px` — **gold `#B89A4A` for the top three**, `#B4B4AF`
otherwise. Avatar 38px circle, `background:#F2F1ED`, border `1px solid #E8E8E8` (or
`1px solid #B89A4A` for top three), initials Cinzel 11px/600 `.06em` `#5F5F5B`. Name Inter
`500 17px/1.2` `#2B2B2B` over sub-line Cinzel 9.5px/600 `.18em` uppercase `#8A8A85`. Score
Inter `400 15px/1.4` `#5F5F5B`. Avg. time same, right-aligned tabular. Points Inter
`500 17px/1.4` `#2B2B2B`, right, tabular.
**The user's own row** is pinned into the list and highlighted: `background:#ECF0F7`
(blue-tint), `box-shadow:inset 3px 0 0 #244A8F`, `padding-left:14px` (10px on mobile). Their
name reads "brightonrock — you". In production the row should stay visible (sticky) if they sit
outside the visible page of results.
Footer (`padding:26px 0 34px`, space-between): note Inter `400 15px/1.5` `#8A8A85` — weekly:
"Ranking 4,182 members. Standings close Sunday at 23.59 BST."; all-time: "Ranking 21,640 members
across 212 quizzes since 2019." Right: DS Button `secondary/sm` "Show full table".

---

### MOBILE WEB — 390 x 844, the primary surface
All four screens sit in a 390px frame, `border-radius:14px` (mock only — that is the phone
rounding, not app chrome), `overflow:hidden`. Gutters are **20px** throughout. Touch targets
never below 44px; answer buttons use `min-height:56px`.

**Mobile 01 — Landing.** Header `padding:14px 20px`, hairline bottom: crest-black 24px ·
wordmark Cinzel 11px `.24em` · menu glyph (replace the `≡` placeholder with a Lucide `menu`
icon, 20px, 1.5 stroke). Dark hero `padding:36px 20px 34px` with photo at
`grayscale(1) opacity:.3` under a vertical scrim
`linear-gradient(180deg, rgba(17,17,17,.7), rgba(17,17,17,.95))`; eyebrow Cinzel 10px gold
"The Friday Ten · Week 34"; H2 Cormorant `400 36px/1.06`; meta Cinzel 10px
`rgba(255,255,255,.5)` "10 questions · 20s each · 4,182 played"; full-width CTA "Begin the quiz".
Warm-white band `padding:26px 20px 20px`: eyebrow purple "Test yourself", H3 Cormorant
`400 27px/1.15`, then three list rows — grid `96px 1fr`, `gap:16px`, square `aspect-ratio:1/1`
greyscale thumb `border-radius:4px`, title Cormorant `500 20px/1.15`, meta Cinzel 9.5px/600
`.18em` uppercase `#8A8A85` ("24 questions · best 19" / "30 questions · not attempted" /
"28 questions · best 22").

**Mobile 02 — Question.** Dark. Header row `padding:14px 20px`, hairline-on-dark: "04 / 10"
(Cinzel 10px `rgba(255,255,255,.55)`) · "The Friday Ten" (Cinzel 10px `rgba(255,255,255,.4)`) ·
timer Cormorant `500 19px/1` gold tabular. 2px progress track, 35% gold fill. Content
`padding:22px 20px 20px`, `gap:18px`: image block `height:168px`, `border-radius:4px`,
greyscale; eyebrow + H2 Cormorant `400 30px/1.12`; four answer buttons — `gap:10px`,
`min-height:56px`, `padding:14px 16px`, letter Cinzel 11px `width:14px`, label Inter
`400 17px/1.3`, same answered-state rules as web.
**Verdict sheet** — instead of an inline panel, a sheet pinned to the bottom of the viewport
(`position:absolute; left:0; right:0; bottom:0`), `background:#171717`,
`border-top:2px solid #B89A4A`, `padding:22px 20px 26px`, `gap:12px`: verdict word,
right-aligned "+180 points · 6.2s" (Cinzel 10px), explanation Inter `400 15px/1.6`
`rgba(255,255,255,.7)` (short form — no "finest live performance" sentence), then a full-width
CTA "Next question". In production it should slide up 320ms `--ease-out`.

**Mobile 03 — Results.** Flex column filling 844px in three parts:
1. Fixed dark summary `padding:26px 20px 22px`, `flex-shrink:0`; crest bleeds off the right
   (`right:-40px`, `height:180px`, `opacity:.07`). Eyebrow Cinzel 10px gold "Week 34 · Complete";
   H2 Cormorant `400 32px/1.02` "Eight from ten"; then a stats row above a
   `border-top:1px solid rgba(255,255,255,.16)`, `gap:24px`, labels Cinzel 9px/600 `.18em`,
   values Cormorant `400 22px/1` — Points 1,420 · Avg. 7.4s · Rank 37 (gold).
2. **Scrollable** paper list, `flex:1; overflow:auto`, `padding:16px 20px 0`. Eyebrow purple
   "Your paper", then rows grid `28px 1fr 74px`, `gap:12px`, `padding:11px 0`, hairline bottom:
   number Cinzel 11px `#B4B4AF`, question Inter `400 14px/1.3`, mark Cinzel 9px/600 `.16em`
   uppercase right-aligned (gold-deep / burgundy). The mock shows the first four rows; the
   production list scrolls all ten.
3. Pinned footer `flex-shrink:0`, `padding:14px 20px 18px`, `border-top:1px solid #E8E8E8`,
   `gap:8px`: two full-width buttons — primary "View the standings", secondary "Share result".

**Mobile 04 — Leaderboard.** Dark head `padding:28px 20px 22px`, crest `height:200px;
opacity:.06` centred: eyebrow gold "Standings", H2 Cormorant `400 32px/1.05` = tab title, tab
pair (small pills). List `padding:4px 20px 0`: each row flex, `gap:12px`, `padding:12px 0`,
hairline bottom — rank Cormorant `400 24px/1` (`min-width:32px`, gold for top 3), 38px avatar,
then a stacked block (`flex:1`) of name Inter `500 16px/1.2` and a combined meta line Cinzel
9px/600 `.16em` uppercase "<score> · <avg time>", then points Inter `500 16px/1.2` tabular.
The user's row carries the same blue-tint + inset blue rule (`padding-left:10px`).

---

### NATIVE APP — iOS, 402 x 874
Visual parity with mobile web; the app adds **streaks, badges and offline play**. Safe areas are
respected in the mock via top padding (`52–64px`) and bottom padding (`30px`) — use real safe-area
insets in production. Tab bar: 4 items, Cinzel 9px/600 `.16em` uppercase, `padding:14px 0 30px`,
`justify-content:space-around`, top hairline — **Today · Archive · Standings · You**. Active item
is gold on dark surfaces, `#244A8F` on light ones.

**App 01 — Today.** Dark (`#111111`), `padding:64px 20px 96px`, `gap:26px`; crest bleeds right
(`right:-60px; top:120px; height:280px; opacity:.05`).
- Top row: crest-white 22px + wordmark; right a **streak pill** — `padding:5px 11px`,
  `border:1px solid #B89A4A`, `border-radius:999px`, Cinzel 9.5px/600 `.18em` uppercase gold
  "Streak 14".
- Greeting: date eyebrow Cinzel 10px `rgba(255,255,255,.45)` "Friday 28 August"; H2 Cormorant
  `400 40px/1.04` white "Good evening, brightonrock".
- **Today's quiz card** — `border:1px solid rgba(255,255,255,.16)`, `border-radius:4px`,
  `overflow:hidden`. Media `height:150px` greyscale under
  `linear-gradient(180deg, rgba(17,17,17,.15), rgba(17,17,17,.9))`, with the eyebrow
  "The Friday Ten · Week 34" (Cinzel 10px gold) absolutely placed `left:16px; bottom:14px`.
  Body `padding:18px 16px 20px`, `gap:14px`: title Cormorant `400 27px/1.12` white; meta Cinzel
  9.5px/600 `.18em` uppercase `rgba(255,255,255,.45)` — "10 questions · downloaded for offline
  play"; full-width CTA "Begin".
- **Badges** — eyebrow "Your badges", then a 4-col grid `gap:10px`. Each tile: flex column
  centred, `gap:8px`, `padding:14px 6px`, `border-radius:3px`; a Roman numeral in Cormorant
  `400 22px/1` and a caption Cinzel 8px/600 `.14em` uppercase centred. Earned+special:
  `border:1px solid #B89A4A`, numeral gold — **XIV / Fortnight**. Earned: `border:1px solid
  rgba(255,255,255,.16)`, numeral `rgba(255,255,255,.8)`, caption `rgba(255,255,255,.45)` —
  **X / Full marks**, **V / Top fifty**. Locked: `border:1px dashed rgba(255,255,255,.16)`,
  numeral and caption `rgba(255,255,255,.25)`, glyph "—", caption "Locked".

**App 02 — Question.** As mobile web, with two differences: the header's middle slot reads
"Offline · saved" (Cinzel 9.5px/600 `.18em`, `rgba(255,255,255,.35)`); the verdict sheet has
`border-radius:20px 20px 0 0`, `padding:22px 20px 40px` (home-indicator clearance), and its score
line reads "+180 points · streak 14". Different placeholder image (crowd rather than stage).

**App 03 — Standings.** Light page (`#FFFFFF`) with the same dark head and tab pills as Mobile 04,
seven rows, and the tab bar pinned via `margin-top:auto` with "Standings" active in `#244A8F`.

---

## Interactions & Behaviour

**Answering a question** (the core loop)
1. Question appears; 20s countdown starts; progress bar sits at `(n-0.5)/10`.
2. Tapping an option locks the question immediately — no confirm step, options become
   non-interactive.
3. The correct option always reveals in gold. If the pick was wrong, it *also* reveals in
   burgundy with the "Your answer" tag. Ring/fill transition `320ms cubic-bezier(.22,.61,.36,1)`.
4. The verdict panel (desktop, inline) or sheet (mobile/app, slides up 320ms) appears with
   verdict word, points earned, elapsed time and a one-paragraph explanation from the archive.
5. "Next question" advances. Timeout at 0s = no answer recorded, 0 points, verdict shown with
   "Time up" in place of the verdict word (`#C98B9B`), correct answer still revealed.

*In the mock, clicking an option toggles: clicking the same option again clears the answered
state. That is a prototype convenience — production answers are final.*

**Scoring.** Points are weighted by speed. Observed values: a correct answer at 6.2s = **+180**;
the paper rows range +130…+185; a wrong or missed answer = **0**. Implement as a base award
decaying with elapsed time (the mocks are consistent with roughly
`round(200 - 3.2 * elapsedSeconds)` for a correct answer, floored at some minimum — confirm the
exact curve with the product owner before shipping). Totals: 8/10 → 1,420 points.

**Leaderboard tabs.** "This week" / "All time" swap the board title, the third column header
(Score ↔ Accuracy), the podium, the rows, the user's own rank, and the footer note. No route
change; instant swap, 180ms style transition on the pills. Both tabs share one component; only
the dataset and two labels differ.

**Navigation.** Landing → Question (×10) → Results → Leaderboard. "Exit quiz" from the question
screen should confirm before discarding progress. Evergreen quiz cards enter the same question
loop with their own question set and no weekly deadline.

**Motion (design-system rules).** Easing `cubic-bezier(.22,.61,.36,1)`; durations 180ms (hover,
tabs), 320ms (state reveal, sheet), 620ms (image greyscale→colour). Quiet and slow — no bounce,
no confetti, no celebratory animation. Respect `prefers-reduced-motion`: drop the sheet slide and
the progress-bar tween, keep the state change instant.

**Hover/press.** Answer options lighten to `rgba(255,255,255,.10)`. Buttons depress 1px on press.
Cards lift 3px with `--shadow-lift`. Links darken to `#1B3A72`.

**Focus.** Keyboard users need a visible ring: `box-shadow: 0 0 0 3px rgba(36,74,143,.45)`
(`--shadow-focus`) — on dark grounds substitute a 2px gold outline. Answer options should also be
selectable with keys 1–4 / A–D.

**States not yet designed** (build with the same vocabulary, or ask before inventing):
loading/skeletons, empty leaderboard, network failure mid-quiz, "already played this week",
quiz closed, and the share-result output.

**Responsive.** Mobile is the primary surface — build mobile-first. The 1280px desktop layout is
the editorial max width (`--container-max`); centre it and keep the 48px gutters. Between 390px
and 1280px: single-column question layout (image above the question) up to roughly 900px, then
switch to the `460px 1fr` split; the leaderboard table collapses to the mobile row form (name +
combined meta line + points) below roughly 720px.

**Accessibility.** Answer state must not rely on colour alone — the "Correct" / "Your answer"
text tags carry it, so keep them. Announce the verdict via an `aria-live="polite"` region.
Options are `<button>`s in a group labelled by the question; the countdown should not be an
`aria-live` region (too chatty) but should be announced at 5s remaining. Gold `#B89A4A` on
`#111111` is about 5.6:1 — fine for text; gold on white is not, which is why the breakdown uses
gold-deep `#9C8038`.

## State Management
Per quiz session:
- `quizId`, `questions[]`, `currentIndex` (0–9)
- `selectedIndex: number | null` per question — `null` = unanswered (this is the flag that drives
  the whole answered/unanswered rendering)
- `elapsedMs` per question, `remainingSeconds` (countdown tick, 1s)
- `answers[]`: `{ questionId, selectedIndex, correctIndex, elapsedMs, points }`
- derived: `totalPoints`, `correctCount`, `averageTime`, `rank`, `percentile`
- `isSubmitting` / `isOffline` (app: queue submissions, sync on reconnect)

Leaderboard: `tab: 'week' | 'all'`, `rows[]`, `currentUserRow` (fetched separately so it can be
pinned regardless of page), `totalMembers`, `closesAt`.

App-only: `streakDays`, `badges[] { id, numeral, label, earned }`, `downloadedQuizzes[]`.

Data the screens need from the API: this week's quiz (questions, options, correct index,
explanation, image + caption), evergreen quiz index with the user's personal bests, submit-answer
(server-side timing and scoring — do not trust the client), results summary with rank and
percentile, and paged leaderboards for both tabs.

## Design Tokens
Load from the design system rather than re-declaring: `tokens/colors.css`,
`tokens/typography.css`, `tokens/spacing.css`, `tokens/base.css` via `styles.css`.

**Colour**

| Token | Hex | Use here |
|---|---|---|
| `--qz-white` | #FFFFFF | page background, active tab |
| `--qz-warm-white` | #F7F6F3 | leaderboard table band, evergreen band |
| `--qz-grey-100` | #F2F1ED | avatar fill |
| `--qz-grey-200` / `--hairline` | #E8E8E8 | hairlines, row dividers |
| `--qz-grey-300` | #D6D6D2 | frame borders |
| `--qz-grey-400` | #B4B4AF | rank numerals (non-podium), row numbers |
| `--qz-grey-500` | #8A8A85 | muted meta |
| `--qz-grey-600` | #5F5F5B | secondary text |
| `--qz-charcoal` | #2B2B2B | primary text |
| `--qz-black` | #111111 | dark bands, question screen |
| `--qz-blue` | #244A8F | primary CTA, active nav, user's row rule |
| `--qz-blue-tint` | #ECF0F7 | user's row background |
| `--qz-purple` | #5D3A8A | archive/category eyebrows |
| `--qz-burgundy` | #6B1F33 | incorrect |
| — | #C98B9B | incorrect text **on dark** (contrast lift) |
| `--qz-gold` | #B89A4A | correct, timer, progress, top 3, badges |
| `--qz-gold-deep` | #9C8038 | "Correct" text on white |
| `--border-on-dark` | rgba(255,255,255,.16) | dividers on dark |

Dark-surface alphas in use: `.045` (option fill), `.10` (option hover, progress track), `.03`
(verdict panel), `.25/.35/.4/.45/.5/.55/.6/.66/.7/.72/.8/.85` (text tiers), `.05–.07` (crest
watermark).

**Type** — Cormorant Garamond (display) 20/22/24/27/30/32/36/40/44/46/56/68px, weights 400–500,
`letter-spacing:-.015em` at display sizes. Inter (body/UI) 12/14/15/16/17/19px, weights 400–500.
Cinzel (titling/eyebrows only) 8/9/9.5/10/10.5/11/12/13px, weight 600, `letter-spacing`
.12–.28em, always uppercase. Numerals in scores/ranks/timers use
`font-variant-numeric: tabular-nums`.

**Spacing** — 4px base. Desktop gutter 48px, mobile 20px. Vertical rhythm inside screens
18–64px; the design board uses 110px between platforms (board only).

**Radii** — 2px (timer chip, tags), 3px (buttons, answer options, verdict panel), 4px (media,
podium cards, quiz card), 999px (tab pills, streak pill), 14px (phone frame — mock only),
20px top corners (app verdict sheet), 50% (avatars).

**Shadows** — `--shadow-card`, `--shadow-lift` on hover. The heavy shadows on the frames
(`0 24px 60px rgba(0,0,0,.10–.18)`) are **board presentation only** — do not ship them.

**Motion** — `--ease-out: cubic-bezier(.22,.61,.36,1)`; `--dur-fast 180ms`, `--dur-base 320ms`,
`--dur-slow 620ms`.

## Design-system components used
From `window.QueenzoneDesignSystem_6c12e8` (source: `components/` in the design system):
- `Button` — `variant="cta" | "primary" | "secondary"`, `size="sm" | "md" | "lg"`,
  `fullWidth`. Used for every action. On dark grounds the secondary variant is overridden
  (`color:#FFF; border-color:rgba(255,255,255,.4)`).
- `Badge` — `tone="special" variant="outline"` for "Week 34".
- `SectionHeader` — `eyebrow` + `title`, used on Web 01 and Web 03.
- `CrestSeal`, `IconButton`, `Input`, `Tag`, `ArticleCard` also exist and are the right source for
  any additional UI.

Map these onto the equivalent primitives in the target codebase rather than re-implementing the
visual rules by hand.

## Assets
All in `assets/` in this bundle, from the Queenzone design system:
- `crest-black.png` — crest for light surfaces (nav bars)
- `crest-white.png` — crest for dark surfaces; also the watermark at 5–7% opacity
- `img-stage.jpg`, `img-crowd.jpg`, `img-studio.jpg`, `img-portrait.jpg`, `img-hero.jpg` —
  **placeholder** monochrome archive imagery. Replace with real Queenzone photographs; keep the
  `filter:grayscale(1)` treatment and the greyscale→colour hover reveal.

Icons: **Lucide**, outline only, ~1.5 stroke, 18–20px inline (28px for section features). The one
icon in the mock is the mobile menu, currently a `≡` character — use Lucide `menu`. No emoji, no
unicode-as-icon anywhere.

## Sample data
All copy in the mocks is real, intentional copy — reuse it for fixtures. British numerals and
dates throughout (13 July 1985, 23.59 BST). Member names are archive-flavoured handles; the
signed-in user is `brightonrock`, member since 2002, displayed as "brightonrock — you".
Machine-readable fixtures are in `sample-data.json`: the weekly board (9 rows incl. the user at
rank 37), the all-time board (9 rows incl. the user at rank 19), and the ten paper rows with
marks and points.

## Screenshots
`screenshots/` holds a capture of every screen, cropped to the screen itself (no board chrome).
Desktop at 1x (1280px wide), mobile and app at 2x. Where a screen has an interactive state, both
are included:

| File | Screen / state |
|---|---|
| `web-01-landing.png` | Web 01 — Landing |
| `web-02-question.png` | Web 02 — Question, unanswered |
| `web-02-question-correct.png` | Web 02 — correct answer revealed (gold ring, verdict panel) |
| `web-02-question-incorrect.png` | Web 02 — wrong pick (burgundy "Your answer" + gold correct) |
| `web-03-results.png` | Web 03 — Results & breakdown |
| `web-04-leaderboard.png` | Web 04 — Leaderboard, "This week" |
| `web-04-leaderboard-alltime.png` | Web 04 — Leaderboard, "All time" |
| `mobile-01-landing.png` | Mobile 01 — Landing |
| `mobile-02-question.png` | Mobile 02 — Question, unanswered |
| `mobile-02-question-incorrect.png` | Mobile 02 — wrong pick, verdict sheet up |
| `mobile-03-results.png` | Mobile 03 — Results |
| `mobile-04-leaderboard.png` | Mobile 04 — Leaderboard, "This week" |
| `mobile-04-leaderboard-alltime.png` | Mobile 04 — Leaderboard, "All time" |
| `app-01-today.png` | App 01 — Today (streak, quiz card, badges) |
| `app-02-question.png` | App 02 — Question, unanswered |
| `app-02-question-correct.png` | App 02 — correct answer, rounded verdict sheet |
| `app-03-standings.png` | App 03 — Standings |

The screenshots are a reference for intent; the HTML and the values in this README are
authoritative for measurements.

## Files
- `Quiz & Leaderboard.dc.html` — the full design board: all 11 screens, all interactive states.
  Open it in a browser to click through (answer options and leaderboard tabs are live). The
  markup inside each `[data-screen-label]` element is the reference; the `{{ }}` holes and the
  `<script data-dc-script>` logic class at the bottom are prototype plumbing — the logic class is
  still worth reading for the exact answered-state and row-style objects.
- `support.js` — the prototype runtime. **Do not port.** Required only to open the HTML locally.
- `ios-frame.jsx` — iPhone bezel used to present the app screens. Presentation only.
- `_ds/queenzone-design-system-.../` — the Queenzone design system: token CSS, `styles.css`,
  `_ds_bundle.js` (component source), `readme.md` (the full brand guide — read this for voice,
  imagery and section-rhythm rules).
- `assets/` — crests and placeholder photography.
- `sample-data.json` — fixtures for the leaderboards and the results breakdown.
- `screenshots/` — a PNG per screen and per interactive state (see *Screenshots* above).
