# Memento — Design Handoff

Design reference for whoever implements the Memento UI. It records the decisions made in the design session on 2026-10-06, the tokens, the components, and the exact behaviour of the screens designed so far. Treat it as the source of truth for look and feel; `docs/PRODUCT-SPEC.md` remains the source of truth for product behaviour.

Live design canvas (interactive mockups, light and dark, foundations sheet):
(The original interactive design canvas was private to the design session; the renders in this folder are the complete record.)

Scope so far: the **Library** (home) in list, grid and first-run states, a foundations sheet, the four spokes (**Recording session**, **Review and transcript**, **Document builder**, **Settings**), the **Document viewer**, the **Style editor**, the **Details sheet with agenda import**, the **Export dialog**, and a sheet of **error and recovery states**. Every screen exists in light and dark on the canvas, and the artboards link to each other so the canvas plays as a clickable prototype (Library → Record → Review → Builder → Document viewer, Library → Settings, Builder → Style editor, Review → Export, back links everywhere).

**Design language: neumorphism** (decided 2026-10-06, after a flat pass and a claymorphism pass). Same palette family, but the UI is one matte material: cards, buttons and chips are *extruded from the ground* in the ground's own colour, shaped only by a paired light shadow (top-left) and dark shadow (bottom-right). Inputs, wells and selected states are *pressed into* it. Controls are tactile: every button and chip visibly depresses on press, toggles snap, primary buttons have a convex fill. No hairline borders anywhere; dividers are faint grooves. §2.5 has the exact shadow tokens and §2.4 the press behaviour; the component rules in §5 assume them.

| Screen | Section | Canvas artboards |
|---|---|---|
| Library | §4, §6, §16 | Library · light / dark / first run / grid view |
| Recording session | §8 | Recording session · light / dark |
| Review and transcript | §9 | Review and transcript · light / dark |
| Document builder | §10 | Document builder · light / dark |
| Settings | §11 | Settings · light / dark |
| Document viewer | §12 | Document viewer · light / dark |
| Style editor | §13 | Style editor · light / dark |
| Details sheet and agenda import | §14 | Details and agenda import · light / dark |
| Export dialog | §15 | Export dialog · light / dark |
| Error and recovery states | §17 | Error and recovery states (light and dark on one sheet) |
| Video: recording, picker, review, export, settings, errors, processing | §18 | Recording session · video, Display and window picker, Review · video player, Export dialog · video enabled, Settings › Recording · video defaults, Settings › Storage · remove video, Error and recovery states · video, Processing card, rows and cards · video |
| 2.0 components | §19 | 2.0 components (light and dark on one sheet) |

---

## 1. Direction

Three decisions, made by the product owner, that every screen must respect:

| Decision | Choice | What it means in practice |
|---|---|---|
| Tone | **Calm studio** | Warm neutral greys, generous whitespace, a single warm accent. Feels like a quiet writing tool, not a DAW and not a dashboard. |
| Theme | **Follow Windows system setting** | Light and dark are first-class. Read `prefers-color-scheme` / the WinUI theme and switch live. Never ship a screen that was only checked in one theme. |
| Navigation | **Hub and spoke** | The Library is home. Record, Review, Document builder and Settings open as full-screen modes with a back control. There is no persistent sidebar and no tab strip. |
| Design language | **Neumorphism** | One matte ground. Raised elements are the same colour as the ground and read as extruded through paired light/dark shadows; inputs and selected states are pressed in. Buttons and switches physically depress and snap. No hairlines, no glass, no gloss. |

Guiding line from the README: *everything important should be only a click or two away.* The Library proves it: New recording is one click, opening a recording is one click, settings is one click.

### Things to avoid

- No gradients, glows, glassmorphism or left-border "accent stripe" cards.
- No emoji as icons. No Inter, Roboto or Arial.
- No fake OS chrome. The window title bar and min/max/close controls belong to Windows; our UI starts below them.
- No red/green-only status. Status is carried by shape and text as well as colour (see pills).
- Don't bury the fact that data is local. Say it plainly where it matters (summary line, footer, empty state).

---

## 2. Tokens

Implement these as theme variables (CSS custom properties, WinUI resources, or equivalent). Names below are the canonical names; use them in code.

### 2.1 Colour

| Token | Light | Dark | Use |
|---|---|---|---|
| `bg` | `#EFEDE8` | `#1F1E1B` | The one matte ground everything is extruded from |
| `surface` | `#EFEDE8` | `#1F1E1B` | **Same as `bg`** by design: cards, header, buttons and chips are the ground, shaped by shadow only |
| `surface-2` | `#E4E1DA` | `#1A1917` | Floor of pressed-in wells: toggle tracks, progress tracks, icon tiles, grid-view waveform strip |
| `line` | `rgba(29,28,26,.07)` | `rgba(255,255,255,.06)` | Grooves: row dividers, pane dividers, footer rule. Never a border around a control |
| `line-strong` | `#C9C5BC` | `#434039` | Emphasised borders (ghost button, dashed queued pill, empty-state ring) |
| `text` | `#1D1C1A` | `#EDEAE4` | Primary text, selected-chip fill |
| `text-2` | `#5E5B55` | `#A8A49C` | Secondary text, meta lines, icons, durations |
| `text-3` | `#7D7971` | `#86827A` | Tertiary text: group labels, placeholders, footer, chevrons |
| `accent` | `#C2410C` | `#F08A5C` | Primary button, record dot, active progress, pulse |
| `accent-soft` | `#FBE9E0` | `#3A2218` | Active-pill fill, selected-row fill |
| `accent-text` | `#9A3412` | `#F4A37E` | Text on `accent-soft`, "Processing now" label |
| `ok` | `#2F6B4F` | `#7FC7A3` | Done progress bar, "ready" status dot |
| `ok-soft` | `#E3EFE8` | `#1E2F27` | Reserved for success fills |
| `focus` | `#2563EB` | `#60A5FA` | Keyboard focus ring |
| `danger` | `#B42318` | `#F28B82` | Failed stages, destructive confirm button, lost-source status |
| `danger-soft` | `#F6E0DD` | `#3A1F1C` | Reserved for failure fills |

`danger` is used only where something has actually gone wrong or will be destroyed. Warnings that are still safe (low disk, paused processing) use `accent` / `accent-soft` / `accent-text`.

Speaker colours (dots next to speaker names in Review; never used for text):

| Token | Light | Dark |
|---|---|---|
| `sp1` | `#3B6FB6` | `#7FA7E0` |
| `sp2` | `#2E8B7A` | `#6FC4B2` |
| `sp3` | `#7A5AB8` | `#B39BE6` |
| `sp4` | `#A3741A` | `#D4A94A` |

Assign in order of first appearance; cycle after four. Speaker names themselves are always `text`, so the colour is a hint, not the only cue.

Convex fills (used only on primary and destructive buttons and the toggle knob; everything else is flat):

| Token | Light | Dark |
|---|---|---|
| `accent-grad` | `linear-gradient(145deg, #D04E18, #B03A0A)` | `linear-gradient(145deg, #F79A70, #E07848)` |
| `danger-grad` | `linear-gradient(145deg, #C43024, #9E1C12)` | `linear-gradient(145deg, #F6A09A, #D9655C)` |
| `knob` | `linear-gradient(145deg, #FFFFFF, #E4E1DA)` | `linear-gradient(145deg, #34322E, #242320)` |

Notes:
- The ground moved from `#F5F4F0` to `#EFEDE8` (light) and from `#161513` to `#1F1E1B` (dark) so that the white and black shadows both have room to show. Text, accent, ok and danger colours are unchanged, and every text/background pair still meets 4.5:1.
- The accent is deliberately the **record colour**. There is one warm accent in the whole app and it means "recording / primary action".
- White text sits on `accent` for the primary button in both themes (`#FFFFFF`).
- All text/background pairs above meet 4.5:1. If you introduce a new pair, check it.

### 2.2 Typography

Fonts: **Manrope** (UI) and **JetBrains Mono** (timecodes and hex values). Fallback stack: `Manrope, 'Segoe UI Variable', system-ui, sans-serif` and `'JetBrains Mono', Consolas, monospace`. Bundle the fonts with the app; do not load them from the network at runtime.

| Style | Size / weight | Extras | Used for |
|---|---|---|---|
| Display | 32 / 800 | letter-spacing −0.02em, line-height 1.1 | Page title ("Library", "Your library is empty") |
| Title | 18 / 700 | letter-spacing −0.01em | Card titles (processing card) |
| Row title | 15 / 600 | single line, ellipsis | Recording title in a list row |
| Body | 15 / 400 | line-height 1.45 | Default text |
| Body emphasis | 15 / 700 | | Feature card headings |
| Meta | 13 / 400, `text-2` | | "Meeting · 5 people · 10:00 AM" |
| Control | 13–14 / 600–700 | | Button and chip labels |
| Label | 12 / 700 | uppercase, letter-spacing 0.06em, `text-3` | Date-group headers, "Processing now" |
| Pill | 12 / 600 | | Status pills |
| Timecode | 13 / 400 mono, `text-2` | right-aligned | Durations `1:02:14`, `6:41` |
| Footer | 12 / 400, `text-3` | | Status footer |

Duration format: `h:mm:ss` when ≥ 1 hour, otherwise `m:ss`. Totals read as `8 h 38 min`.

### 2.3 Spacing and shape

- Grid: 4 px. Common steps: 4, 8, 12, 16, 24, 32, 48.
- Page padding: 40 px top, 48 px sides, 56 px bottom. Content column `max-width: 1200px`, centred.
- Vertical rhythm between major blocks on a page: 28 px. Inside a group (label → list): 10 px.
- Radii: **12** tiny (28 px icon buttons, segmented options), **14** small controls (32–36 px buttons, chips' parent wells), **16** standard controls (40 px buttons, inputs, icon tiles, tab strips), **20** module cards, **24** cards and list containers, **28** dialogs, **999** chips and pills.
- Borders: none on cards, buttons, inputs or wells. Shape comes from the paired shadows (§2.5). `line` grooves remain only as dividers: between list rows, between the three Review panes, under the status footer. Dashed `line-strong` outlines mark "add" affordances and queued pills.
- Hit targets: 40 px for primary controls, 36 px minimum for secondary, 32 px for chips (with 8 px gaps). Rows are ≥ 68 px tall.

### 2.4 Motion

- **Hover** on a raised control: the shadow pair grows one step (`neo-sm` → `neo-sm-hi`), so the element appears to lift toward the cursor. No colour change. Hover on a list row, transcript segment or chapter: a shallow press (`neo-in-xs`).
- **Press** (`:active`) on any raised control: shadows invert to `neo-in` (primary: `neo-accent-in`), the element moves 1 px down and scales to 98.5%. The press transition is 40–80 ms so it feels instant; release eases back over 150 ms. This is what makes buttons "click": the extrusion visibly sinks under the finger.
- **Toggle**: the knob slides with an overshoot curve, `cubic-bezier(.34, 1.56, .64, 1)` over 220 ms, so it snaps into place. While the pointer is down the knob stretches to 20 px wide and shifts toward the destination; on release it lands. The track colour fades between `surface-2` (off) and `text` (on) over 180 ms.
- **Segmented control**: the raised option glides; the pressed option sinks for the duration of the press.
- Keyboard activation (Enter, Space) plays the same press animation so the feedback is not pointer-only.
- Respect `prefers-reduced-motion`: keep the shadow inversion on press (it is state, not decoration) but drop the overshoot, the lift and the scale.
- Processing indicator: 8 px dot pulsing opacity 1 → 0.3 → 1 over 1.6 s, ease-in-out, infinite. Respect reduced-motion: show a static dot.
- Spoke transitions (Library → Record / Review / Settings): quick cross-fade or slide, ≤ 200 ms. Nothing bouncy.

### 2.5 Neumorphic surfaces

Everything is one material. A raised element has the **same colour as the ground** and is shaped by two shadows: a light one offset up-left and a dark one offset down-right. A pressed element uses the same pair inset. Implement these as shared tokens and apply by role; never hand-tune a shadow on one element.

| Token | Light | Dark | Applied to |
|---|---|---|---|
| `neo-card` | `-8px -8px 18px rgba(255,255,255,.9), 10px 10px 22px rgba(29,28,26,.14)` | `-8px -8px 18px rgba(255,255,255,.035), 10px 10px 22px rgba(0,0,0,.6)` | Cards, list containers, dialogs, the side sheet, toasts, the document paper |
| `neo-sm` | `-4px -4px 10px rgba(255,255,255,.9), 5px 5px 12px rgba(29,28,26,.14)` | `-4px -4px 10px rgba(255,255,255,.04), 5px 5px 12px rgba(0,0,0,.6)` | Ghost and icon buttons, chips, module cards, version cards, palette items |
| `neo-sm-hi` | `-6px -6px 14px rgba(255,255,255,.95), 7px 7px 16px rgba(29,28,26,.17)` | `-6px -6px 14px rgba(255,255,255,.05), 7px 7px 16px rgba(0,0,0,.65)` | Hover state of anything carrying `neo-sm` |
| `neo-xs` | `-2px -2px 5px rgba(255,255,255,.9), 3px 3px 6px rgba(29,28,26,.12)` | `-2px -2px 5px rgba(255,255,255,.04), 3px 3px 6px rgba(0,0,0,.55)` | Status pills, the raised option of a segmented control, colour swatches |
| `neo-in` | `inset 4px 4px 9px rgba(29,28,26,.13), inset -4px -4px 9px rgba(255,255,255,.9)` | `inset 4px 4px 9px rgba(0,0,0,.6), inset -4px -4px 9px rgba(255,255,255,.04)` | Text inputs, search fields, segmented wells, tab strips, icon tiles, drop zones, the selected row / segment / chapter, and the **pressed** state of every raised control |
| `neo-in-xs` | `inset 2px 2px 4px rgba(29,28,26,.14), inset -2px -2px 4px rgba(255,255,255,.9)` | `inset 2px 2px 4px rgba(0,0,0,.6), inset -2px -2px 4px rgba(255,255,255,.04)` | Toggle tracks, selected chips / nav rows / icon toggles, hover on list rows |
| `neo-accent` | `-4px -4px 10px rgba(255,255,255,.7), 6px 6px 14px rgba(194,65,12,.35)` | `-4px -4px 10px rgba(255,255,255,.04), 6px 6px 14px rgba(240,138,92,.3)` | Primary buttons (with `accent-grad` fill); destructive uses the danger variant |
| `neo-accent-in` | `inset 4px 4px 8px rgba(0,0,0,.25), inset -3px -3px 8px rgba(255,255,255,.25)` | `inset 4px 4px 8px rgba(0,0,0,.4), inset -3px -3px 8px rgba(255,255,255,.15)` | Pressed state of primary and destructive buttons |
| `neo-bar` | `0 8px 18px rgba(29,28,26,.08)` | `0 8px 18px rgba(0,0,0,.5)` | The header bar |

Rules of thumb:
- **Raised = can be pressed. Pressed-in = holds a value, is selected, or is being pressed right now.** A chip is raised until chosen, then sunk. A segmented well is sunk and its chosen option is raised inside it. A toggle track is sunk and its knob is a convex (`knob` gradient) raised disc.
- **Cards never change colour.** There is no white card on a grey page; a card is a region of the ground pushed up. The only non-ground fills are `surface-2` (the floor of wells), `accent-soft` / `accent-text` for active processing and warnings, `danger` for failures, and the convex gradients on primary buttons and knobs.
- **Emphasis rings**: the current module, version or swatch adds a 2 px `text` ring to its raised shadow. Drop targets while dragging are sunk wells; the hovered one adds a 2 px `accent` ring. Focus is the 2 px `focus` ring, drawn in addition to the shadow (inputs show it with `neo-in` still visible underneath).
- Light source is top-left everywhere, in both themes. Never flip it, never animate it. Offsets scale with element size (card 8–10 px, button 4–5 px, pill 2–3 px) and never exceed the element's corner radius.
- Keep shadow pairs symmetrical in offset and blur; only the colours differ. Keep elements at least 8 px apart so adjacent shadows do not merge into a smear.
- Depth stacking: a raised control may sit on a raised card on the ground. Do not go deeper. Never put a raised control inside a sunk well except a segmented control's chosen option and a toggle's knob.
- The document paper (Builder preview, Document viewer, Style editor) is the one deliberate exception to "same colour": it is white in both themes, radius 12, with `neo-card`. It is a document lying on the desk, not UI.

---

## 3. Layout shell (shared by every Library state)

```
┌────────────────────────────────────────────────────────────────┐
│ ● Memento        [🔍 Search recordings, transcripts, people]   [● New recording] [⚙] │  60 px, surface, bottom line
├────────────────────────────────────────────────────────────────┤
│                                                                │
│   content column, max 1200, centred, bg                        │
│                                                                │
├────────────────────────────────────────────────────────────────┤
│ ● Local transcription ready · GPU        Everything is stored on this PC · 212 GB free │  36 px, top line
└────────────────────────────────────────────────────────────────┘
```

**Header** (`surface`, 60 px, padding 0 24 px, flex, gap 24, wraps at narrow widths):
- Wordmark: 22 px ring (2 px `text` border) with a 10 px `accent` dot, then "Memento" 17/700.
- Search: centred, `max-width 520`, 40 px tall, radius 10, `bg` fill, `line` border, 18 px search icon at left, placeholder in `text-3`. Searches titles, transcript text and people.
- Right: primary **New recording** button (40 px, radius 10, `accent` fill, white 10 px dot + label 14/700) and a 40 px icon-only **Settings** button (needs `aria-label`/AutomationProperties.Name).

**Footer** (36 px, `text-3` 12 px): left = processing engine status with a 7 px `ok` dot ("Local transcription ready · GPU"); right = storage statement ("Everything is stored on this PC · 212 GB free"). Both are live values. When disk is low, the right side switches to `accent-text` and reads e.g. "Low disk space · 4 GB free".

Minimum supported window width is 1024. Header groups wrap below ~900.

### Spoke header (Record, Review, Builder, Settings)

Same 60 px `surface` bar, but the left side is a **back control** instead of the wordmark:

```
[‹ Library]  |  Title (16/700) + optional meta line (12, text-2)      [spoke actions…]
```

- Back control: ghost button, 36 px, radius 9, 16 px chevron-left + the name of the screen it returns to ("Library", or "Design review: library screen" when the Builder was opened from a recording). Always the first element and always keyboard-reachable first.
- A 1 px × 24 px `line` divider separates it from the title.
- Title area is either static text (Review, Settings) or a borderless 36 px text input (Record, Builder) so the recording or template can be renamed in place. The input shows a border only on hover/focus.
- Actions sit at the right: ghost buttons first, the one primary action last. Only Record and Builder have a primary action in the header (none and "Generate" respectively; Record's primary control is the Stop button on the stage).
- Padding is 0 16 px on the right and 0 12 px on the left so the back chevron aligns with the content below.

Spokes keep the 36 px status footer where it carries live information (Record, Settings). Review and Builder drop it to give the panes the full height.

---

## 4. Screen: Library (populated)

Canvas artboards: *Library · light*, *Library · dark*.

Content column, top to bottom, 28 px gaps:

1. **Heading row.** Left: "Library" (Display) over a summary line (Meta, `text-2`): `{n} recordings · {total} · all on this PC`. The count and total reflect the **current filter and search**, not the whole library. Right: a ghost sort button ("Newest first ▾", 36 px, radius 9, `line` border) and two 36 px icon toggles for list / grid view (list is the default and shown as "on" with `surface-2` fill).

2. **Processing card** (only when something is running and no filter/search is active). `surface`, `line` border, radius 14, padding 20×24, horizontal flex that wraps.
   - Left block: label "PROCESSING NOW" in `accent-text` with the pulsing accent dot; recording title (Title); meta line (Meta).
   - Middle: one column per pipeline stage, each with a 13 px label (600) and status (`text-3`) on one line and a 4 px progress track (`surface-2`) beneath. Fill colour by state: done → `ok` at 100%; active → `accent` at its percentage; queued → no fill. Stages shown: Stored, Transcribing, Speakers, Minutes. Only show stages that are enabled in Settings for that recording.
   - Right: ghost **Open** button (36 px).
   - If several recordings are processing, show the most recent and add "+2 more" after the title; do not stack cards.

3. **Type filter chips.** `All · Meetings · Interviews · Lectures · Presentations · Dictation · Research` (pluralised labels; custom types appear after Research). 32 px, radius 999, `surface` fill, `line` border, 13/600 `text-2`. Selected chip inverts: `text` fill, `bg` text. Single-select. Hover raises border to `line-strong`.

4. **Grouped list.** One section per date bucket, in order: Today, Yesterday, Earlier this week, then one bucket per month ("September"). Each section = Label (uppercase 12/700 `text-3`, 2 px left inset) + a list container (`surface`, `line` border, radius 14, overflow hidden). Empty buckets are omitted.

   **Row** (a single button/list item, min-height 68, padding 14 / 18 / 14 / 20, 18 px column gap). Grid columns: `40px | 1fr | auto | 76px | 20px`.
   - Type icon: 40 px tile, radius 10, `surface-2` fill, 20 px stroke icon in `text-2`.
   - Title (Row title, ellipsised) over meta (Meta): `{Type} · {people} · {when}`. If the recording has video, a 14 px camera icon precedes the meta text (give it an accessible name "Includes video"). `{people}` reads "5 people", "2 people", "1 speaker" or "Just me". `{when}` is a time for Today/Yesterday ("10:00 AM"), weekday + time within the week ("Thu, 11:00 AM"), and a date otherwise ("Sep 29").
   - Status pills, right-aligned, 6 px gap (see §5.4). If no processing has run, show plain `text-3` 12 px text "Audio only" instead of pills.
   - Duration in Timecode style, right-aligned.
   - 18 px chevron in `text-3`.
   - Hover: `surface-2` fill. Selected (keyboard focus / last opened): `accent-soft` fill. Rows are separated by 1 px `line`. Click or Enter opens the Review spoke for that recording.

5. **No-match state.** When filter + search yield nothing: a dashed `line-strong` box, radius 14, 48 px padding, centred `text-2` text "No recordings match. Try another type or clear the search." Replaces the groups; the processing card is hidden whenever a filter or search is active.

### Type icons

24×24 viewBox, `stroke="currentColor"`, `stroke-width="1.75"`, round caps and joins, no fill. Path data as used in the mockups:

| Type | Path |
|---|---|
| Meeting | `M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8 M22 21v-2a4 4 0 0 0-3-3.87 M16 3.13a4 4 0 0 1 0 7.75` |
| Interview | `M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2 M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8` |
| Lecture | `M2 4h6a2 2 0 0 1 2 2v14a2 2 0 0 0-2-2H2z M22 4h-6a2 2 0 0 0-2 2v14a2 2 0 0 1 2-2h6z` |
| Presentation | `M3 4h18v12H3z M8 21h8 M12 16v5` |
| Dictation | `M12 2a3 3 0 0 0-3 3v6a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3z M19 11a7 7 0 0 1-14 0 M12 18v4 M8 22h8` |
| Research | `M9 3h6v4H9z M9 5H6a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-3 M8 12h8 M8 16h5` |

Custom recording types get the Research glyph until the user picks one. Other glyphs in use (search, settings gear, chevron right/down, list, grid, camera, check) follow the same stroke style; any Lucide/Feather-style set at 1.75 stroke matches.

---

## 5. Components

All components follow §2.5 and §2.4: raised elements carry `neo-sm` / `neo-xs` and lift to `neo-sm-hi` on hover, containers `neo-card`, inputs and wells `neo-in`, and every raised control inverts to `neo-in` while pressed. Where an older note below mentions a 1 px border or a `surface-2` hover fill on a card, button, chip or input, read it as "no border; shadow does the work".

### 5.1 Buttons
- **Primary**: 40 px, radius 16, `accent-grad` convex fill, `neo-accent` shadow, white text 14/700, padding 0 18 / 0 14, leading 10 px white dot when the action starts a recording. Large variant (empty state, start recording): 48–56 px, radius 20 or pill, 15–16/700, 12–14 px dot. Hover adds a wider light shadow; press inverts to `neo-accent-in` and sinks 1 px.
- **Ghost**: ground colour, `neo-sm`, no border, `text-2` label 13–14/600, radius 14 (36 px) or 16–20 (40–48 px). Hover `neo-sm-hi`; press `neo-in`.
- **Icon**: 36 or 40 px square, radius 14/16, ground colour with `neo-sm` when it sits in a toolbar, or shadowless when it sits inside a card header (chapter "+", rename pencil, module up/down/remove), where it gains `neo-sm-hi` on hover and sinks on press. "On" state is pressed in (`neo-in-xs`, `text` glyph). Always has an accessible name.
- **Destructive**: as primary but `danger-grad` fill and the danger shadow. Only inside a confirmation dialog, never in a toolbar.

### 5.2 Chip (filter)
32 px, radius 999, ground colour, `neo-sm`, 13/600 `text-2`. Hover `neo-sm-hi`. Selected: sunk (`neo-in-xs`, `text` colour) and stays sunk on hover. Pressing a raised chip sinks it before it becomes selected, so the click reads as a physical latch. Chips are real buttons with `aria-pressed`.

### 5.3 Search field and inputs
40 px, radius 16, ground colour, `neo-in`, 42 px left padding for the icon, 14 px text. Focus adds the 2 px `focus` ring on top of the inset shadow. Has a visually hidden label. Disabled (empty library): 70% opacity, placeholder explains why. All other text inputs, textareas and password fields follow the same recipe at 34–36 px (radius 14). Title inputs in spoke headers are shadowless until hovered or focused.

### 5.4 Status pills
24 px tall, radius 999, padding 0 9, 12/600, 5 px gap to an optional 11 px icon.

| Kind | Look | Meaning |
|---|---|---|
| `done` | `surface-2` fill, `text-2` text, leading check icon | Stage finished: "Transcript", "Speakers", "Chapters", "Minutes" |
| `active` | `accent-soft` fill, `accent-text` text, no icon | Stage running: "Transcribing 64%" |
| `queued` | transparent, 1 px dashed `line-strong`, `text-3` text | Stage waiting: "Speakers" |
| (none) | plain `text-3` text "Audio only" | No processing enabled or run |

Failed stages are not designed yet. Proposed: `queued` shape with solid `accent-text` border and label "Transcript failed · retry"; keep the recording itself unaffected.

### 5.5 Progress bar
4 px track in `surface-2`, radius 2, fill `accent` (active) or `ok` (done). Label line above at 13 px: name left (600), status right (`text-3`).

### 5.6 Cards
`surface`, 1 px `line`, radius 14, padding 20 or 20×24. Never shadows. Feature cards (empty state) carry a 36 px `surface-2` icon tile, a 15/700 heading and 13 px `text-2` body, 8 px gaps.

### 5.7 Toggle (switch)
40 × 22 px sunk track (`neo-in-xs`), radius 999. Off: `surface-2` track. On: `text` track. The knob is a 16 px convex disc (`knob` gradient, shadow `2px 3px 6px rgba(29,28,26,.28)` plus a 1 px top-left highlight; dark `rgba(0,0,0,.6)`) that slides from 3 px to 21 px with the overshoot curve in §2.4 and stretches to 20 px wide while held. It is a `<button role="switch" aria-checked>` with an accessible name; the visible label sits to its left (settings rows) or right (inline options). The toggle is deliberately **not** accent-coloured: the accent means recording.

### 5.8 Segmented control
A sunk pill-group (`neo-in`, `surface-2` floor), radius 14, 3 px padding, 2 px gaps. Each option is a 30–32 px button, radius 12, 13/600 `text-2`. The selected option is raised (`neo-xs`, ground colour, `text` colour); pressing any option sinks it for the duration of the press. Used for mutually exclusive 2–4 way choices (System/Light/Dark, During/After recording, Short/Medium/Long, Details/Documents/History). Real buttons with `aria-pressed`, or `role="tab"` when it switches panes.

### 5.9 Checkbox and radio
Native inputs, 16 px, `accent-color` set to `text`. The label is a real `<label>` wrapping input and text; an optional 12 px `text-3` note sits at the right ("never sent", "agenda.docx"). Disabled items use `text-3` for the label and stay unticked.

### 5.10 Level meter (Record)
6 px track, radius 3, `surface-2`; fill `ok` at the current RMS level. One per audio source, under the source name, indented to align with the text (52 px). Disabled sources show an empty track at 55% opacity.

### 5.11 Waveform and track lanes
Bars 2 px apart, radius 1, flex-grow so they fill the width. Record lanes: 36 px tall `surface-2` tracks, bars in `ok` for live sources and `text-3` for muted ones. Review player: 56 px tall, bars `line-strong`, played portion `text`. Bars are purely visual; the real control is the time scrubber under them (keyboard: left/right 5 s, shift 30 s).

### 5.12 Transcript segment (Review)
Grid `132px | 1fr`, 16 px gap, 12 × 16 px padding, radius 10. Left: speaker (13/700 with 8 px speaker dot) over timecode (mono 11, `text-3`). Right: text 15 / line-height 1.55. Current segment: `surface-2` fill. Hover: `surface-2`. Low-confidence words: 1.5 px dotted underline in `text-3`, 3 px offset. Highlighted phrase: `accent-soft` background, radius 3, 2 px horizontal padding. Notes attach under the text as a `bg` box with 1 px `line`, 8 × 12 px padding, note icon, author in bold.

### 5.13 Chapter and outline rows
Full-width buttons, 8 × 10 px padding, radius 8, mono timecode (11, `text-3`, 44 px column) + 13/600 title. Active chapter: `surface-2`.

### 5.14 Module card (Builder)
`surface`, `clay-sm`, radius 20, padding 12 / 12 / 12 / 10. Header row: six-dot **drag handle** (`text-3`, `cursor: grab`, the only draggable part of the card), two-digit index (mono 11, counts top to bottom then left to right), name 14/600, instruction preview 12 `text-2` (ellipsised), then three 26 px icon buttons: up, down, remove. Selected card adds a 2 px `text` ring and expands to show the instruction textarea, a Length segmented control and a "Link to transcript" toggle, indented 32 px. A card being dragged stays in place at 40% opacity until it is dropped. Cards in the same row share the row's width equally (two or three columns).

### 5.15 Palette item (Builder)
34 px row, radius 12, 13/600, a faint six-dot handle at the left, name, and a 14 px plus glyph (`text-3`) at the right. `cursor: grab`. Hover `surface-2`. Click appends the module as a new row at the end and selects it; drag places it (see §10).

### 5.17 Drop zones (Builder)
- **Row gap**: a 10 px strip between rows that grows to 40 px while anything is being dragged, pressed in (`clay-in`) with the label "Drop here for a new row" in 12/600 `text-3`. The hovered strip adds a 2 px `accent` ring and `accent-soft` text. A final strip after the last row reads "Drop here to add at the end".
- **Beside**: a 28 px wide pressed-in slot with a plus glyph at the right end of a row, shown only while dragging and only if the row has fewer than three modules and does not already contain the dragged one. Dropping there appends the module to that row.

### 5.18 Document paper
White (`#FFFFFF`) page with `#1D1C1A` ink in both themes, radius 12, `clay-card`. Used at three sizes: Builder preview (~400 px wide, skeleton content), Style editor preview (620–640 px, sample text), Document viewer (up to 820 px, real content). The paper's own typography comes from the chosen document **style**, not from the app tokens.

### 5.19 Toast, banner, dialog
- **Toast**: 380 px, `surface`, `clay-card`, radius 20, padding 14 × 16, an 8 px status dot (`accent` for warnings, `danger` for failures), 14/700 title, 13 `text-2` body, up to two actions. Bottom-right, 6 s, pauses on hover, stacks upward.
- **Banner**: full width of the content column, `accent-soft` fill, `accent-text` text, radius 16, 12 × 16 padding, info icon, bold lead sentence, one outlined action, stays until resolved. Used for conditions that are still safe (low disk, paused processing).
- **Dialog**: 560–680 px, `surface`, `clay-card`, radius 28, 22 × 24 padding, 18–20/800 title, 14 `text-2` body, actions right-aligned (ghost then primary or destructive). Scrim `rgba(29,28,26,.28)` light / `rgba(0,0,0,.5)` dark, covering everything below the header.
- **Side sheet**: 480 px, anchored right below the header, `surface`, left `line` divider, header with title and close, scrolling body, footer with the primary action. Same scrim.

### 5.16 Settings row
Inside a card list, 14 × 20 px padding, min-height 64, rows divided by 1 px `line`. Left: label 15/600 over description 13 `text-2`. Right: the control (toggle with an "On/Off" word in `text-3` before it, segmented control, select-style ghost button with chevron, mono path + "Change", password field + Add/Replace, or a plain value). A checklist row wraps its checkboxes onto a full-width 3-column grid beneath the label.

---

## 6. Screen: Library (first run / empty)

Canvas artboard: *Library · first run*. Same shell; search is disabled with placeholder "Search will work once you have a recording".

Centred column, `max-width 760`, 40 px gaps:
1. A 72 px ring (3 px `line-strong`, `surface` fill) with a 28 px `accent` dot. Then "Your library is empty" (Display) and a `text-2` 16 px line: *Recordings, transcripts and documents live here, on this PC. Nothing leaves it unless you choose to export.*
2. Large primary **Start your first recording** and ghost **Import audio or video**.
3. Three feature cards in a 3-column grid (16 px gap): **Record what you choose** (mic icon), **Transcribe on this PC** (lines icon), **Turn it into documents** (document icon). Copy is in the mockup; keep it factual and short. The third card states that AI is optional and off by default.

Once the first recording exists this state is never shown again (the no-match state in §4 covers filtered-empty).

---

## 7. Behaviour the UI must support

- **Theme switch** without restart. Token swap only; layout does not change between themes.
- **Live processing**: the processing card and row pills update as stages progress; a row's pill moves `queued → active → done` in place.
- **Resource awareness** surfaces quietly in the footer (engine ready / busy, GPU or CPU, free disk). Never a modal unless recording is at risk.
- **Keyboard**: Tab reaches every control in reading order; rows, chips and buttons show the 2 px `focus` ring with 2 px offset; Enter opens a row; arrow keys move within the chip group.
- **Back from a spoke** returns to the Library with scroll position and filters preserved.

---

## 8. Screen: Recording session

Canvas artboards: *Recording session · light / dark*. Opened from "New recording". The screen does one job: make recording easy and obviously safe. Nothing here depends on transcription or AI.

**Header**: back to Library · title input (default "Untitled {type}", prefilled from the calendar or the last session when possible) · type chip ("Meeting ▾") · right: ghost **Details and agenda** (opens a side sheet for participants, agenda import, purpose, tags).

**Body**: content column `max-width 1344`, three cards in a wrapping flex row, 24 px gaps, padding 32 / 48 / 40.

1. **Sources card** (left, 280–320 px). Label "AUDIO SOURCES" with a small ghost **Add**. One block per source: toggle, name 15/600, sub-line 12 `text-2` (device or scope: "Shure MV7 · USB", "Everything this PC plays", "Only this app"), and a level meter (§5.10). Disabled sources fade to 55%. A divider, then "VIDEO" with Screen and Camera toggles (off by default, sub-line names the display or says "Not recording video"). Footer line: *Each source is saved as its own synchronized track.* Changing a source during recording starts or stops that track; the others are untouched.

2. **Stage card** (centre, flexible). Centred column:
   - Status label: pulsing accent dot + "RECORDING" (`accent-text`, label style). "PAUSED" with a static dot when paused. Before starting, the plain label "READY TO RECORD".
   - Timer: mono 64 px, weight 500, `hh:mm:ss`. `text` while recording, `text-3` when ready (shows 00:00:00).
   - Sub-line 13 `text-2`: "Started 10:00 AM · 3 audio tracks · audio only" (or "… · screen + audio").
   - Controls, 16 px apart: **Pause/Resume** (56 px round ghost, `line-strong` border), **Stop** (72 px round, `accent` fill, white rounded square), **Mark highlight** (56 px pill ghost with flag icon). Stop ends the session and opens Review for that recording. In the ready state the row holds one control: a 56 px accent pill **Start recording** with a 14 px white dot, plus a `text-3` line underneath: *Starts the moment you press. Everything is saved to this PC as it records; transcription runs locally afterwards.*
   - Below the stage: a **Tracks card** with one lane per active source (§5.11) and a mono ruler (00:00 · ¼ · ½ · ¾ · now). Appears only while recording.
   - A **Highlights card** lists markers as `mono time + optional note input` rows. Mark highlight appends a row at the current time and focuses its note field. Hidden until the first marker.

3. **Right column** (300–360 px), two cards:
   - **Live transcript**: label plus a `done` pill naming the engine ("Local · GPU"). Rough segments as `mono "00:11:02 · Speaker 1"` over 14 px text; the newest ends with an ellipsis in `text-3`. Footer: *Rough draft. Speaker names and corrections happen in Review.* When live transcription is off, the card explains where to turn it on. When ready, it explains what will appear.
   - **Agenda**: source line ("From agenda.docx"), ordered list of items. Covered items: `ok` check and struck-through `text-3`; the current item: accent ring and bold; upcoming: `line-strong` ring. Marking "covered" is manual (click) and may be suggested by local keyword matching; it never requires AI.

**Footer**: left `ok` dot + "Saving continuously · last checkpoint 8 s ago" (ready: "Ready · recordings save to this PC as they happen"); right "212 GB free · about 190 hours at this quality". If a source fails mid-recording, the footer dot turns `accent` and the message names the source; the other tracks keep going.

**Keyboard**: Space = pause/resume, Ctrl+M = mark highlight, Esc does nothing (no accidental stop). Stop asks for no confirmation; it is not destructive because everything is already saved.

---

## 9. Screen: Review and transcript

Canvas artboards: *Review and transcript · light / dark*. Opened from a Library row or by stopping a recording. Three panes fill the window below the header; from 1024 px the screen is fixed to the window height and the outline, the transcript (under the pinned player strip) and the details each scroll on their own with thin scrollbars, and menus inside a pane float above their trigger when there is no room below (1.1.0); at narrow widths they stack (outline, then player and transcript, then details) and the page scrolls.

**Header**: back to Library · title 16/700 over a meta line (type · when · duration · people · audio/video) · right: the **Undo** status and button (below), ghost **Export**, primary **Create document** (opens the Builder), 36 px **More** (⋯: rename, change type, reprocess, delete).

**Undo** (after 1.1.0; also in the Builder and the viewer headers): a ghost button (§5.1, 36 px) with a counter-clockwise arrow and the word "Undo", shown only while there is a step to undo; its tooltip and accessible name name the step ("Undo merge speakers"). Before it, a 12 px `text-3` live region says "Undone: merge speakers", "Redone: …" or "Saved" for 4 s (a refusal reads "Not undone: …" in `danger` and also gets a warning toast with the host's §17 message; the step stays). No toast for a success. Ctrl+Z undoes and Ctrl+Y / Ctrl+Shift+Z redo anywhere on the screen, except in a text field that holds changes since it was focused (its own undo goes first), and never while a dialog is open. The stack belongs to the open recording (Review), template draft (Builder, kept across a trip to the Style editor) or document (viewer), holds 50 steps and starts empty when another one opens. Implementation: `ui/src/state/undo.ts`.

**Left pane: Outline** (`bg`, 260 px, 24 × 16 px padding, right `line` border).
- **Chapters**: list of chapter rows (§5.13) with a 24 px "+" icon button to add one at the playhead. The row's timecode is a button that seeks (and the active chapter is marked); the title is a button that turns into an inline field (§5.3, 28 px) to rename it, Enter or leaving saves, Esc cancels, an empty field keeps the old name; a 24 px shadowless × icon button at the end (shown on hover and focus) removes it without asking (Undo brings it back).
- **Highlights · n**: same row style and behaviour, timecode + the highlight's note ("Highlight" in `text-3` when it has none).
- **Topics**: `done` pills, wrapping. Topics are generated locally or added by hand.
- **People**: speaker dot, name 13/600, share of talk time 11 `text-3`, 24 px pencil icon button to rename. Renaming updates every segment immediately.

**Centre pane: Player and transcript** (`surface`, flexible).
- **Player strip** (padding 20 / 32 / 16, bottom `line` border): waveform 56 px (§5.11); then controls: back 10 s, **Play/Pause** (44 px round, `text` fill, `bg` glyph), forward 10 s, mono timecode "18:42 / 1:10:02", speed ghost "1.0×", a ghost **Skip silences** chip (1.1.0: `aria-pressed`; jumps over gaps longer than 1.5 s between transcript lines to 0.2 s before the next line, dims the skipped waveform bars, and shows a mono "Skipped 4 s" caption in the strip's top padding that fades over 1.6 s; disabled with the title "Available once transcribed" until there is a transcript; remembered per user), a transcript search field (260 px, shrinking to 200 px when the row is tight), and ghost **Highlight** (flags the current segment). The strip stays pinned while the transcript scrolls.
- **Transcript** (padding 20 / 24, `max-width 920`): a label row showing the current chapter name and the hint *Click the time to play a line · click the words to correct them*. Then segments (§5.12). A click on the timecode or beside the words seeks to the segment. A click on the words turns them into a textarea in place (`field` recipe, 15 / 1.55 like the text, sized to its content, its padding taken back by negative margins so the words do not move) with the caret where the click landed; a drag selection does not edit. Enter or leaving the field saves through `transcript.editSegment` (the original wording is kept; "Saved" in the header's status), Shift+Enter is a new line, Esc cancels. Double-click, F2 and Enter twice still edit. While a line is being edited, following the playhead never scrolls it away. A highlight note under a line is a button: a click turns it into the inline field to rename the highlight. Edits are saved with the recording and, when version history is on, versioned.
- **Speaker menu** (the speaker name on a line; also the People list's merge menu): a floating popover (§5.19 radius 20, `neo-card`, 260 px wide) whose height is capped at 420 px and at the room above or below the trigger (whichever is larger), so it never leaves the window. At the top a 34 px search field (placeholder *Find or add a speaker*, focused when the menu opens) filters as you type, ignoring case and accents: names that start with the text first, then names with a word that does, then names that contain it. Below, the uppercase label ("This line is said by", "Merge {name} into") and a listbox that scrolls with a thin scrollbar: speaker dot + name, the current one checked. The arrow keys (and Page Up / Page Down) move the active row (a shallow `neo-in-xs` press, `aria-activedescendant` on the field), Enter picks, Esc closes and returns focus to the name, Tab closes. When the typed name is not exactly a speaker's, a last row under a groove reads **Add "{name}" as a new speaker** (plus glyph): it creates the speaker (`transcript.setSegmentSpeaker` with `newSpeakerName`) and moves the line to them. With nothing typed, **Rename {name}…** follows the speakers and opens the rename form in the same place.

**Right pane: Details** (`bg`, 320 px, left `line` border, 20 px padding). A three-way segmented control (Details | Documents | History) switches the content:
- **Details**: a two-column fact grid (Type, Recorded, Duration in mono, Platform, Tracks, Purpose), Participants as pills, Agenda as a numbered list with its source ("agenda.docx · parsed locally"), Tags with a dashed "+ Add" pill, and a full-width ghost **Edit details**.
- **Documents**: one card per document (name 14/700, style · generated when · provider, versions line). Generated and hand-written documents sit together. A primary **Create document** and the note *Documents are saved inside this recording and can be exported on their own.*
- **History**: the processing timeline as a dotted list: Recorded, Stored (tracks, size), Transcribed locally (engine, time, segments), Speakers identified, Minutes generated (provider and exactly what was sent), Transcript edited. `ok` dots for completed stages, `text-3` for informational entries, `accent` for a failed stage with a Retry link.

---

## 10. Screen: Document builder

Canvas artboards: *Document builder · light / dark*. Opened from Review's **Create document** or from a saved template. This is the README's visual composer: structure on the left and centre, inputs and output on the right, and no free-form "prompt box" as the primary control.

**Header**: back to the recording it was opened from · template name input (16/700) · `done` pill "Template · n modules" (live count) · **Template** select (1.1.0: built-ins first, then your saved templates; switching with unsaved changes asks first) · right: ghost **Save template**, ghost **Save as new template**, primary **Generate {document}** with a trailing arrow. Generate is the only moment anything is sent anywhere; it opens the Document viewer (§12).

**Body**: `max-width 1360`, three columns, 24 px gaps: palette (≈236 px), structure (flexible, ≥ 440 px), preview/inputs (≈400 px).

1. **Modules palette** (card): a search field, then groups with uppercase labels: **Structure** (Title, Executive summary, Participants, Agenda, Discussion summary, Decisions, Action items, Open questions, Next meeting), **Detail** (Topic, Quote, Highlight, Chapter, Timeline, Follow-up email, Notes), **Custom** (Custom text, Custom AI section). Items per §5.15; an item already on the structure is greyed (`text-3` at 55% opacity) with an "in use" marker and the accessible name "Add {module}, in use", and stays draggable and clickable for a second copy (1.1.0). Footer hint *Drag onto the structure. Drop beside a module to put them side by side. Click to add at the end.*

2. **Structure**: label "STRUCTURE · n MODULES IN r ROWS" and the hint *Top to bottom, left to right is the order in the document*. The structure is a **list of rows**; each row holds one to three modules laid out as equal columns. Module cards per §5.14, drop zones per §5.17.
   - **Drag from the palette** or **drag a card by its handle**. While dragging, every row gap becomes a "new row" target and every eligible row shows a "beside" slot at its right edge. Dropping in a gap creates a one-module row there; dropping beside adds the module to that row (max three). Moving a module out of a shared row collapses the row if it becomes empty.
   - **Keyboard and mouse fallbacks**: the up/down buttons move a lone module's row; on a module inside a shared row they pull it out into its own row above or below. × removes without confirmation (Undo or Ctrl+Z brings it back). **+ Add a module** (dashed, 44 px) appends a new row.
   - **Undo** (header, §9): one stack with the rest of the app. Adding, moving and removing modules, every module setting (instructions, length, text size, heading, your text, link to transcript), the template name, inputs, provider, style, output and switching templates are steps ("Undo remove Agenda", "Undo change Executive summary length"); typing in one field is one step until a 1.5 s pause. Save template is not a step and is never undone.
   - One card is selected and expanded at a time: instruction textarea, Length (Short / Medium / Long), "Link to transcript" toggle (points carry timestamps back to the transcript).
   - Side-by-side rows become columns in the generated document (two-column grid in Word and PDF; stacked in Markdown).

3. **Preview / Inputs and output** (right column, a two-way segmented control switches them; Preview is the default):
   - **Preview**: caption "How the minutes will be laid out · {Style} style · {Paper}" with an **Edit style** link (→ §13), then a live document paper (§5.18, ~400 px wide) that re-renders on every structure change. It shows the real title and meta line, then every row as a grid with the same column count as in the structure. Each module renders its heading in the chosen style plus a **skeleton** of the content type it will produce: paragraph bars, bulleted bars, a three-column table (Action items), chips (Participants), a label/value pair (Meeting purpose, Next meeting), an indented quote block (Quote, Highlight), a timeline (Timeline, Chapter). Style switches change fonts, heading treatment and rules immediately: Corporate = navy small-caps headings and a rule under the title; Minimal = plain bold headings and hairlines between sections; Academic = serif with numbered headings and a centred title. Footer line: *Grey bars stand for text the AI will write. Headings, order and columns are exactly what you will get.*
   - **Inputs and output** (card, four blocks divided by `line`):
     - **What the AI receives**: checklist (§5.9): Transcript (with speakers), Participants and details, Agenda (source name), Highlights and notes (count), Imported documents; then Audio and Video as locked rows without a checkbox (1.1.0: `text-3`, a lock glyph, the note "never sent", `role="group"` with `aria-disabled="true"`, not focusable; the same rows in Settings › What may be shared). The ticked set is the exact payload.
     - **Provider**: radio cards (`text` ring when selected): Claude (Anthropic · key saved), ChatGPT (OpenAI · no key). *Keys live in Settings › AI and privacy.* If external AI is disabled in Settings this block is replaced by an explanation and a link; Generate is disabled.
     - **Style**: segmented Corporate | Minimal | Academic plus **Edit styles** (→ §13). *Same structure, different look. Styles never change what is written.*
     - **Output**: "Saved inside this recording · always" with an `ok` check, then toggles "Also export as Word (.docx)" and "Also export as Markdown".
   - Below either tab, a small notice card: *Nothing leaves this PC until you press Generate.* with the bold text button **Preview exactly what will be sent** (opens the composed payload read-only).

4. **Generating** (the progress card in the preview's place): label "Generating" with the pulsing 8 px dot, the percent, what is being written, the progress bar, then the actions: ghost **Cancel** and, beside it, ghost **Show live output**; the "Running on this PC" note wraps to its own line under them. The §17 failure card also offers **Show live output** (ghost, after Try again and Switch to …) while the failed exchange is still in memory.

5. **Live output sheet** (opened by **Show live output**). A full-height sheet anchored right below the header like the side sheet (§5.19, same scrim, `neo-card`, left `line` divider) but `min(1120px, 100vw)` wide. It is modal and closes with ×, **Done** or Esc; it can be closed and opened again while generation continues, and it survives the Builder opening the viewer, so it can be read to the end.
   - **Header**: "Live output" (18/800) over a 13 px `text-2` line: the provider ("Qwen3.5 4B on this PC", "Claude") · the state ("Writing" with the pulsing dot, "Sending and receiving", then "Finished · read only", "Cancelled · read only", "Stopped by a failure · read only").
   - **Left, the passes** (≈ 300 px, scrolling, a `line` groove to the right): uppercase group labels in pipeline order, **Segment**, **Map** (with the count), **Reduce**, **Verify**, **Grounding**. Each pass is a 13/600 title ("Decisions and action items · segment 1 of 2", "Check Decisions and action items · 6 questions") over a 12 px `text-2` line ("Writing · 120 tokens", "Waiting for Claude", "312 tokens · 4.1 s", "In code · 3 ms", "Stopped"), with an 8 px dot: `accent` pulsing while running, `ok` when done, a dashed `line-strong` ring when stopped. Rows are flat on the ground, sink (`neo-in-xs`) on hover and stay sunk (`neo-in`) when selected; arrow keys, Home and End move between them.
   - **Right, the selected pass**: its title (15/700) and counters in mono 12 `text-2` (tokens, tokens/s for the local model, time, the request's tokens); then **Request · read by the local model** / **Request · sent to Claude** over a sunk well (`neo-in`, radius 16, mono 12.5, scrolling, about a third of the height) holding exactly the text sent; then **Reply** over a second sunk well taking the rest, mono for the model's text, the body face for a step done in code. A streaming reply ends in a 7 px `accent` caret (still under reduced motion). Cloud replies say "Replies from {provider} arrive whole" beside the label.
   - **Following**: the sheet shows the newest pass and keeps its reply scrolled to the end, with "Following the newest output" in `text-3` at the top right. Scrolling the reply up, or choosing a pass, pauses it and shows a ghost **Follow live output**; scrolling back to the end of the newest pass also resumes. Scrolling is instant, never animated.
   - **Footer**: the privacy line: *Read by the local model on this PC; nothing leaves it. Shown only here and not saved.* or *Only what was sent to Claude and what it answered. Nothing more is sent; shown only here and not saved.*, and primary **Done**.
   - Below 760 px the passes sit above the pass in one column.

---

## 11. Screen: Settings

Canvas artboards: *Settings · light / dark* (the AI and privacy section is shown; the artboard's State tweak switches sections). Opened from the gear in the Library header.

**Header**: back to Library · "Settings". **Footer**: *Changes save as you make them.* · version and library path.

**Body**: `max-width 1080`, a 240 px nav column and a 520–760 px content column, 40 px apart.

**Nav**: 38 px rows (§ nav style: radius 9, 14/600 `text-2`, 18 px icon; active row `surface-2` + `text`). Sections in order: General, Recording, Transcription, Speakers, AI and privacy, Documents, Export, Storage and history.

**Content**: section title 28/800 with a one-line `text-2` blurb, then groups: uppercase label + a card list of settings rows (§5.16). The designed contents:

| Section | Groups and rows |
|---|---|
| General | Appearance: Theme (System/Light/Dark), List density. Startup: Start with Windows, Keep running in tray. Library: Library location (path + Change), Language. |
| Recording | Defaults: Recording mode (Audio only / Audio + video), Audio sources, Recording type. Tracks and storage: Keep each source as its own track (on), After recording optimisation (Lossless default), Checkpoint interval (30 s), Low-space warning threshold (10 GB). |
| Transcription | When: Transcribe automatically (on), Timing (During / After recording), Pause when PC is busy (on). Engine: Engine (Local · GPU, CPU fallback), Model (size and installed state), Language (Auto), Keep word-level timestamps and confidence (on). Blurb: *Runs on this PC. Nothing is uploaded.* |
| Speakers | Identify speakers (on), Expected speakers (Auto), Remember renamed speakers (on). |
| AI and privacy | External AI: Allow external AI services (**off by default**), Ask before every send (on), Keep a record of what was sent (on). Providers: Claude key (masked, Replace), ChatGPT key (empty, Add). What may be shared: checklist with Transcript, Recording details, Participants, Agenda and imported documents, Highlights and notes ticked; Attachments unticked; Audio and Video locked off "never". |
| Documents | Defaults: Default template, Default style, Manage templates and styles. History: Keep version history (on), Keep versions for (90 days). |
| Export | External copies: Save copies outside Memento (off), Default folder (path), Ask where to save each time (on). Include by default: checklist (Audio mixed, Individual tracks, Video, Transcript, Documents, Recording details, Attachments). Formats: Transcript (JSON/Markdown/Text), Audio (FLAC), Documents (Markdown + Word). |
| Storage and history | Usage: Library size, Free space, Largest recording (plain values). Reclaim space: Downmix tracks older than, Remove video older than, Review large recordings. |

Rules: every row has a description that says what the setting does in one sentence; defaults match the README (local first, AI off, nothing written outside the library). API key fields are password inputs and never echo the key. The checklists are the single source of truth for what the Builder may send.

**Graphics card line** (after 1.1.0; not on the canvas): under the Transcription › Engine row and under the local model's "Documents are written with …" line, a full-width row with the host's sentence at 13 px `text-2` ("The graphics card has 4.0 GB of 6 GB free. Windows desktop (dwm.exe) is using 0.4 GB.") and a **Check again** small button (§5.1, `neo-sm`) at its right; while checking the button reads "Checking…" and is disabled. When the model in effect cannot use the card the sentence is the §17 one (the amount, who holds the memory, what runs meanwhile, the fix) in `accent-text`, the colour of the other warning states. Under 640 px the button drops below the text. The Builder's Local provider card shows the same §17 sentence on its own line under the name (12 px `accent-text`).

---

## 12. Screen: Document viewer

Canvas artboards: *Document viewer · light / dark*. Shown after Generate, and when a document is opened from Review's Documents tab.

**Header**: back to the recording · document name input (16/700) · `done` pill "{Style} · {Provider} · {when}" · "Saved" in `text-3` (autosave indicator) · right: the **Undo** status and button (§9), ghost **Regenerate** (→ Builder with this template loaded), ghost **Export** (→ §15), **More** (⋯: duplicate, make template, delete). Undo takes back a run of typing in the paper (until a 1.5 s pause) or a toolbar command, redrawing and saving the paper as it was, and a rename; while the paper holds changes since it was focused, Ctrl+Z is the browser's own first.

**Body**: `max-width 1200`, document column (≤ 820 px) plus a 320 px side column.

- **Formatting toolbar** above the paper: a small clay card holding Bold, Italic, a divider, H2 / ¶, a divider, bulleted list, numbered list, table, a divider, and **Insert timestamp**. The current block type is pressed in. The toolbar is for light edits; it is not a word processor.
- **Paper** (§5.18, padding 56 / 64 / 72) rendered in the document's style. Title 28/800, meta line 12 `#6B6861`, sections with the style's headings. Side-by-side modules render as a two-column grid with 28 px gap. Content is editable in place; edits save as you type.
- **Timestamp chips**: inline `mono 10` pills (`#EDEBE6` fill, `#5E5B55` text, hover `accent-soft`/`accent-text`) after sentences the AI tied to the transcript. Clicking one opens Review at that moment. They export as footnote-style links in Word and PDF and as `[18:42]` in Markdown.
- **Side column**, offset to align with the paper top:
  - **How this was made** card: Template, Style, Provider, Generated (time and duration); "Sent to the provider" pills (the payload actually used) and the line *Audio and video were not sent.*; ghost **Change structure and regenerate**.
  - **Versions**: label with "History on · 90 days"; the current version card (`text` ring) with "Edited by you · when · n changes", earlier versions with a **Restore** ghost button. Hidden when version history is off, replaced by a line saying so.
  - Footnote: *Timestamps link back to the transcript. Edits save as you type and stay inside this recording.*
  - **Regenerating**: while this document is being regenerated, the Builder's progress card (§10.4, with **Cancel** and **Show live output**) sits at the top of the side column. **After a generation**: below How this was made, a ghost **Show live output** with the 12 px `text-3` note *The exchange with the local model that wrote this version. It is not saved and goes when you leave this document.* It opens the Live output sheet (§10.5), read-only, and is gone once the viewer is left.

---

## 13. Screen: Style editor

Canvas artboards: *Style editor · light / dark*. Opened from "Edit style" in the Builder or from Settings › Documents.

**Header**: back to the template it came from · style name input · `done` pill "Style · used by n templates" · right: ghost **Duplicate**, ghost **Reset**, primary **Save style**.

**Body**: `max-width 1280`; a 380 px settings column and a flexible preview column.

**Settings** are four clay cards of §5.16-style rows (44 px, label 14/600, control at the right):
- **Type**: Headings (Sans | Serif), Body (Sans | Serif), Base size (Small | Normal | Large = 11 / 12 / 13.5 px on screen), Heading case (Normal | Small caps), Numbered headings (toggle).
- **Colour**: Headings and rules (four 28 px swatches: Navy `#1F3A5F`, Ink `#1D1C1A`, Forest `#2F6B4F`, Burgundy `#7A2E2E`; the selected one has a 2 px `text` ring; each has a paired soft tint for table headers), Body text (fixed Ink, shown for reference), Table header fill (toggle).
- **Structure**: Rule under the title (toggle), Lines between sections (toggle), Spacing (Tight | Normal | Airy = 10 / 18 / 28 px).
- **Page**: Paper (Letter | A4), Page numbers (toggle), Running header (toggle, recording title and date on every page).
Footer line: *A style changes how a document looks, never what it says. Exports to Word and PDF follow it; Markdown ignores it.*

**Preview**: caption "Preview · sample minutes · {Paper}" and the line *Updates as you change settings*; then a paper (§5.18) at 640 px (Letter) or 620 px (A4) showing fixed sample minutes (title, meta, Executive summary, Decisions, Action items table) that re-render on every change: typefaces, base size, heading colour and case, numbering, rules, spacing, table header fill, running header and page number. The preview is the contract: what it shows is what Word and PDF produce.

The three built-in styles are presets of these settings: **Corporate** = Sans/Sans, Normal, Small caps, Navy, header fill on, title rule on, section lines off, Normal spacing. **Minimal** = Sans/Sans, Normal, Normal case, Ink, header fill off, title rule off, section lines on, Airy. **Academic** = Serif/Serif, Normal, Normal case, Ink, numbered headings on, header fill off, title rule off, section lines off, Normal.

---

## 14. Details sheet and agenda import

Canvas artboards: *Details and agenda import · light / dark*. A 480 px side sheet (§5.19) over the Recording session, opened from the header's **Details and agenda** button, before or during recording. The recording screen stays visible behind the scrim and keeps running.

**Sheet header**: "Details and agenda" (18/800) and a close button. **Footer**: *Everything here can be changed later.* and a primary **Done**.

**Fields** (two-column grid, 12 px gaps): Title (full width), Type (select), Platform, Participants (full width: a pressed-in well holding name pills and an inline "Add a name" input), Purpose (two-line textarea, full width). Then a divider.

**Agenda** section:
- **Empty / replace state**: a pressed-in **drop zone** (radius 24, padding 28 × 20): document-upload icon, "Drop an agenda here" 15/700, the supported sources in 13 `text-2` (*Word, PDF, Excel, CSV, Markdown, plain text, or a photo of a printed agenda*), ghost **Choose a file** and **Paste text**, and the line *Parsed on this PC. Nothing is uploaded.* While a file hovers, the zone gains a 2 px `accent` ring.
- **Parsed state**: caption "From `agenda.docx` · parsed on this PC · Replace". An ordered list of items, each a 14 px row with a faint drag handle, mono index, an inline text input that shows its field only on hover/focus, and a remove ×. Items the local parser is unsure about get a dotted `accent` underline, and an `accent-soft` notice explains why (*A heading and its bullet may have been merged*) with the two ways out: fix it here, or the AI option. **+ Add an item** (dashed) follows the list.
- **AI fallback** card (`bg`, `line`): *Not quite right? Extract with AI* · *Sends only the agenda file to Claude. You will be asked first.* · ghost **Extract**. When external AI is off in Settings the button is disabled and the caption says where to turn it on. Local parsing is always attempted first and never requires a key.
- Agenda items carry into the Recording session's Agenda card (§8) and the Review Details tab (§9).

**Tags** section closes the sheet: pills plus a dashed "+ Add".

---

## 15. Export dialog

Canvas artboards: *Export dialog · light / dark*. A 680 px dialog (§5.19) over Review, opened from **Export** in Review or the Document viewer.

**Header**: "Export copies" (20/800) and the subtitle *{Recording title}. Files are written outside Memento; the recording inside Memento stays the original.* Close button at the right.

**What to include**: a clay list of component rows (52 px min, divided by `line`). Each row: checkbox, name 14/600, one-line description 12 `text-3`, estimated size in mono (right-aligned, 64 px), and a format select (`FLAC ▾`, `MP4 ▾`, `JSON ▾`, `Word ▾`, `Original`) which is disabled while the row is unticked. Rows: Audio (mixed), Individual tracks (lists the track names), Video, Transcript (*Speakers, timestamps and confidence*), Documents (with a sub-row of per-document checkboxes and sizes, shown only while ticked), Recording details, Attachments (names the files). Unticked rows dim their name to `text-2`. Defaults come from Settings › Export.

**Where**: Folder (mono path + **Change**), "Put everything in a folder named after the recording" (toggle, on; the path preview updates), "Remember these choices" (toggle, off; becomes the Settings default).

**Footer** (`bg` strip): live summary "**n files** · about {size}" computed from the ticked rows and formats, then ghost **Cancel** and primary **Export**. Export runs in the background with progress in the status footer; failure shows the inline error in §17 and never touches the project.

Format options per component: Audio FLAC / WAV / MP3; Video MP4 / original; Transcript JSON / Markdown / Text / SRT; Documents Word / PDF / Markdown; Details JSON; Attachments original files.

---

## 16. Screen: Library, grid view

Canvas artboards: *Library · grid view · light / dark*. Same shell, heading row and chips as §4, with the grid toggle pressed in and the list toggle as a link back. The processing card is not shown in grid view; the processing recording's card carries its `active` pill instead.

Groups keep their uppercase date labels. Each group is a grid of cards: `repeat(auto-fill, minmax(264px, 1fr))`, 16 px gaps. **Card** (§5.6 with `clay-card`, radius 24, overflow hidden, min-height 220):
- **Media strip** (118 px): for video recordings a dark `#2A2824` block with a schematic screen, a camera inset, a white play button and the mono label "Screen + camera"; for audio recordings a `surface-2` well with a 48-bar `ok` waveform. Purely decorative, `aria-hidden`.
- **Body** (14 × 16 padding): 32 px type icon tile, title 15/600 clamped to two lines, meta 12 `text-2`, duration in mono at the right; at the bottom the status pills (or the "Audio only" caption).
- The whole card is one link to Review. Hover raises nothing; it only shows the pointer and a slightly stronger ring on focus.
- Below ~1100 px the grid drops to two columns; below 800 to one.

---

## 17. Error and recovery states

Canvas artboard: *Error and recovery states* (light and dark side by side). Components, not a screen. The governing rule from the README: **a failing stage never interrupts recording, and nothing destructive happens without the user asking for it.**

| Situation | Component | Copy pattern |
|---|---|---|
| A source stops mid-recording | Toast (`accent` dot) | "{Source} stopped at {time}." + "{Other sources} are still recording." Actions: Reconnect, Dismiss. The footer also switches to the lost-source line. |
| Low disk space | Banner (`accent-soft`) | "Low disk space · {n} GB free." + what continues and what pauses. Action: Free up space. Footer right side turns `accent-text`. |
| Transcription (or any stage) fails | Processing card with `danger` label and dot | Stage name + "failed", the reason in plain words, what was kept ("the partial transcript was kept"). Actions: a specific fix first (Retry on CPU, Use the Medium model), then Details. |
| Failed stage in the Library | `failed` pill: 1 px `danger` outline, `danger` text, no shadow | "Transcript failed · Retry" |
| Interrupted recording recovered at launch | Dialog | "Recovered an interrupted recording" + what was saved, how many tracks are intact, what may be missing. Actions: Later, Open recording. Never asks whether to keep it. |
| Delete a recording | Dialog with destructive button | Name the recording, say exactly what is removed and its size, state that exports are untouched and it cannot be undone. Cancel, Delete (`danger`). |
| Export failed | Inline card with `danger` icon | What could not be written and why; "Nothing inside Memento was changed." Actions: Try again, Choose another folder. |
| AI provider failed | Inline card with `danger` icon | Provider + what happened (rate limited, no network, invalid key); "Nothing was sent twice and no document was changed." Actions: Try again, Switch to {other provider}. |
| Another program holds the graphics card | Graphics card line (§11), `accent-text` | "The graphics card has {free} of {size} free. {Program} is using {amount}. {Model} needs {need} on the card, so it runs on the processor until that memory is free." + "To use the card, close {program or the app that started it} or wait until it lets go of the memory, then check again." Action: Check again. |
| Windows blocks a device | Inline card | "Memento needs access to your microphone." + where to allow it; "the other sources keep working." Actions: Open Windows settings, Continue without microphone. |
| Drive fills during recording | Stage status with `danger` label | "Stopped · drive full" + the exact time, "Everything up to that point is saved and will transcribe once there is room." Actions: Open recording, Free up space. |
| Status footer variants | Footer | `ok` dot = normal; `accent` dot = paused or warning; `danger` dot = a source lost; the right side carries the storage warning in `accent-text`. |

Copy rules: name the thing (source, stage, drive, provider), give the time or amount, say what is safe, then offer the most specific fix first. No exclamation marks, no "Oops".

---

## 18. Video

Canvas artboards: *Recording session · video*, *Display and window picker*, *Review · video player*, *Export dialog · video enabled*, *Settings › Recording · video defaults*, *Settings › Storage · remove video*, *Error and recovery states · video*, *Processing card, rows and cards · video*, each light and dark (the two component sheets show both themes on one board). Renders: `RecordVideo`, `DisplayPicker`, `ReviewVideo`, `ExportVideo`, `SettingsVideo`, `SettingsStorageVideo`, `ErrorStatesVideo`, `ProcessingVideo`.

Governing rules: video is one more synchronized track, never the recording itself. It can fail, pause or be removed on its own while audio continues; it is never transcribed, analysed or sent anywhere; and every video figure (resolution, frame rate, size, time remaining) is shown in plain numbers.

### 18.1 Recording session with video

The Sources card gains a live **VIDEO** section under the audio sources, headed by an `active` pill "Recording video" while any video source is on. Each video source is a row of toggle · name 15/600 · sub-line 12 `text-2` · a 72 × 42 px live **thumbnail** (pressed-in well, radius 12) · and, on a second line indented 52 px, a small ghost **selector** chip that opens the picker (§18.2):

| Source | Sub-line | Selector |
|---|---|---|
| Screen | "Display 1 · 2560 × 1440 · 30 fps" | "Display 1 ▾" |
| Window | "Capture one window instead of a whole display" | "Pick a window ▾" |
| Camera | "Logitech Brio · 1080p" | device name ▾ |

Below them, **Screen + camera** (toggle) places the camera as a picture-in-picture inset. When on, an **inset corner** picker appears: a 2 × 2 grid of 22 × 16 px buttons (raised; the chosen one pressed in and `text`-filled) and the note *Both are also kept as separate tracks.* Disabled sources fade to 55% as audio sources do. The card footer reads *Each source is saved as its own synchronized track. Video never leaves this PC.*

The **stage** keeps the timer (56 px here to make room) and the sub-line becomes "Started 4:00 PM · screen + camera + 3 audio tracks" (the parts update live as sources toggle). Under the sub-line sits the **live preview**: a 16:9 pressed-in well up to 560 px wide showing the captured screen with the camera inset in the chosen corner, a mono caption at the lower left ("Display 1 · 2560 × 1440 · 30 fps") and "LIVE" at the lower right. Controls are unchanged; Stop opens Review with the video player.

The **Tracks card** gains a **video lane** first: the same 36 px well, filled with alternating frame blocks instead of bars, labelled with a camera icon and "Screen + camera" (or "Screen", "Camera", "Video (off)"). Audio lanes follow.

The right column keeps the live transcript (footnote: *Video is not analysed; only the audio tracks are transcribed.*) and adds an **Encoder** card: a `done` pill ("Hardware · GPU" or "Software · CPU") and a small mono fact grid: Resolution, Frame rate with dropped-frame count, Bitrate, Video so far. Footnote: *Change resolution and quality in Settings › Recording. Changes apply to the next recording.*

Footer: left "Saving continuously · last checkpoint 6 s ago · video and audio in sync"; right the storage estimate **at video rates**: "212 GB free · about 9 hours at this quality (screen 30 fps + camera)".

### 18.2 Display and window picker

A 600 px side sheet (§5.19) over the Recording session, opened from a Screen or Window selector. Header "Choose what to capture" with close. Body:

- A segmented control **Entire screen | This window** (the Screen selector opens on the first, the Window selector on the second).
- A two-column grid of **tiles** (raised, radius 16, 10 px padding): a 16:9 schematic thumbnail, name 14/600, sub-line 12 `text-2` with resolution and a hint ("primary · this is where Memento is", "right of Display 1", "Zoom · 1600 × 900"). The chosen tile is pressed in with a 2 px `accent` ring. Memento's own window is listed but disabled, with the sub-line *This app is excluded from capture*.
- An inline **note** (pressed-in well, info icon) that says exactly what will be captured. Screen: *Captures everything shown on the chosen display, including other windows and notifications that appear on it. Your other displays are not captured. The mouse pointer is included.* Window: *Captures only this window, even when other windows cover it. If it is minimised, the video pauses and resumes when it comes back; audio keeps recording.*

Footer: *You can change this while recording; the track continues.* · ghost **Cancel** · primary **Use {name}**.

### 18.3 Review with video

The centre pane gains a **video area** above the waveform: a 16:9 pressed-in well up to 640 px wide, centred, showing the screen recording with the camera inset. A mono caption at the top left gives the chapter and timecode. An overlay row along the bottom (translucent dark pills, 32 px, radius 12) holds: **Camera inset** (toggle button; when on, a 2 × 2 corner picker follows it so the inset can be moved during playback), a spacer, **Audio only**, and **Fullscreen**.

- One playhead drives video, waveform, transcript and chapters. Clicking a transcript line seeks the video; the play, skip and speed controls are the ones already in the player strip, so there is no second set.
- **Fullscreen** hides both side panes and the transcript, lets the video fill the centre pane, and swaps the button to **Exit fullscreen**. Esc also exits.
- **Audio only** hides the video area and shows a pressed-in line *Video hidden. Playback and the transcript are unchanged.* with a **Show video** button. When the recording's video has been removed (Settings › Storage or the row menu) the same line reads *Video was removed on {date} to free {size}. Audio tracks, transcript and documents are intact.* and there is no Show video button. The artboard's State tweak switches between present and removed.
- The outline pane adds a **Video** block (Sources, Resolution, Frame rate, Size) and a ghost **Remove video, keep audio…** that opens a confirmation dialog (§18.6). The details pane lists the video tracks and encoder facts and states that video is never transcribed or sent.
- The header meta line starts with the camera icon and ends with "screen + camera · 1.9 GB video".

### 18.4 Export dialog with video

The Video row is ticked: name **Video**, description "Screen and camera, 2560 × 1440 · 30 fps, with the mixed audio", size, and the format select **MP4 ▾** (options: MP4, Original container). While ticked it shows a sub-row with a segmented **One file, as recorded | Two files: screen and camera**, and a note: one file *burns the camera inset in where it was while recording*; two files *keep their own resolution; audio goes with the screen file*. *MP4 re-encodes; "Original container" copies the recorded frames untouched.* The footer's file count and size follow the choice (two files add the camera track's size).

### 18.5 Settings for video

**Recording › Video defaults** (a new group after Tracks and storage): Default video source (select: Screen + camera), Resolution (select: Match display (2560 × 1440), 1080p, 720p), Frame rate (segmented 24 | 30 | 60; 30 default), Quality (segmented Smaller | Balanced | Best; the description states the bitrate, "Balanced is about 12 Mb/s at 1440p"), Default camera (select), Picture-in-picture position (select: four corners), Hardware encoder (toggle, on; *Falls back to software automatically if it fails, with a notice*), Include the mouse pointer (toggle, on). Recording mode defaults to Audio + video on this artboard to show the group in context; the product default remains Audio only.

**Storage and history › Remove video older than** is live: select (Never, 30, 90, 180 days, 1 year) and a description that states the effect right now: *Would free 11.3 GB now, from 3 recordings. Audio tracks, transcript and documents are kept; the Library shows "Video removed".* Removal is a stage like any other and appears in the recording's History.

### 18.6 Video errors and recovery

Same components and copy rules as §17; the extra rule is **video fails first and alone**.

| Situation | Component | Copy |
|---|---|---|
| Camera in use by another app | Inline card | "Logitech Brio is in use by Zoom." Recording continues with screen and audio; the camera track starts when free. Actions: Retry camera, Continue without camera, Pick another camera. |
| Screen capture blocked by Windows privacy settings | Permission card | Where to allow it; audio unaffected. Actions: Open Windows settings, Record audio only. |
| Display disconnected mid-recording | Toast (`accent` dot) | "Display 2 disconnected at 00:23:40." Video paused, audio and camera continue. Actions: Switch to Display 1, Keep waiting. |
| Captured window closed | Toast (`accent` dot) | "'Zoom Meeting' was closed at 00:58:12." The video track ends there; everything before is saved. Actions: Capture Display 1 instead, Audio only from here. |
| Hardware encoder failed | Banner | "Hardware encoder failed at 00:12:03." Switched to software; no frames lost; CPU use higher. Action: Lower quality. |
| Low disk with video | Banner | "Low disk space · 4 GB free · about 25 minutes of video at this quality." Video stops first, audio last. Actions: Stop video now, Free up space. |
| Video encoding failed in processing | Processing card, `danger` | Where it stopped; raw frames and audio safe; transcript unaffected. Actions: Retry with software encoder, Keep raw frames, Details. |
| Remove video (user action) | Dialog (§5.19) | Names the recording and the size freed, states that audio, transcript and documents are kept, and that it cannot be undone. Cancel, Remove video (`danger`). |
| Footer variants | Footer | "video and audio in sync"; "Video paused · Display 2 disconnected · audio recording"; "Software encoder · 3 dropped frames". |

### 18.7 Processing and Library states with video

- The processing card gains a **Video** stage between Stored and Transcribing. It runs in parallel with transcription and never blocks it. Its status text names the sub-step: "Encoding 42% · GPU", "Trimming", "Muxing camera inset".
- Pill vocabulary: `active` "Video · Encoding 42%" / "Video · Trimming", `done` "Video", `queued` "Video", `failed` "Video failed · Retry". A recording whose video was removed shows the crossed-camera icon in its meta line and the caption "Video removed · audio kept" in place of a pill.
- Grid cards: the video strip carries the `active` pill at its top left while encoding and a mono caption ("Screen + camera · 1440p"). A removed-video card shows a pressed-in `surface-2` strip with the crossed-camera icon and "Video removed · audio kept".
- List rows reuse the camera icon in the meta line (§4) and the pills above.

---

## 19. 2.0 components

Canvas artboard: *2.0 components* (light and dark on one sheet). Render: `Features20`.

- **Known voices** (Settings › Speakers): a card listing each learned voice with its speaker dot, name, "Confirmed in n recordings · last {date}", a per-voice toggle (suggest this voice) and a ghost **Forget**. Footnote: *A voice is learned only when you confirm a name in Review. Forgetting it removes the signature immediately.* Signatures are stored on the PC, never audio.
- **Match prompt** (Review › People): under an unnamed speaker, a pressed-in line "Sounds like **Priya Natarajan** · 91% match · 4 past recordings" with a small primary **Use name** and a ghost **Not her**. Accepting renames every segment as §9 describes; declining hides the suggestion for this recording.
- **Suggested chapters** (Review › Outline): a "Suggested chapters · n" block with **Accept all**. Each row has the mono time, the title with a dotted underline (meaning unconfirmed), and two 26 px icon buttons: accept (`ok` check) and dismiss (×). Accepted chapters lose the dots and move into the Chapters list. Suggestions come from local topic shifts.
- **Selection mode** (Library): a bulk action bar (raised, radius 16) with a select-all checkbox, "n selected · duration · size", and actions Export…, Change type, Remove video, Delete… (`danger` text), Cancel. Rows gain a leading checkbox and selected rows are pressed in. Enter via a row menu's Select, Ctrl+click, or a Select button that appears on hover over a group label; Esc leaves.
- **Context menu** (rows and cards): 240 px, raised, radius 16, 6 px padding, 34 px items. Open · Rename… · Change type ▸ · Select · divider · Create document… · Export… · Reprocess ▸ · Remove video, keep audio… · divider · Show in folder · Delete… (`danger`). Opens on right-click, Shift+F10, or the ⋯ button.

---

## 20. Not yet designed (follow this document when building)

- Reprocess, rename and change-type dialogs behind the context menu (simple dialogs; use §5.19).
- The "Preview exactly what will be sent" payload view (read-only sheet listing each input with its size).
- Templates and styles manager (list of templates with their module counts; opened from Settings › Documents).
- Onboarding of transcription models (download size, progress) in Settings › Transcription.
- Small-window layout below 1024 px, and the stacked phone-width fallback of the three-pane Review.
- Trimming video in Review (start and end handles on the waveform) and a camera-only recording layout.

Add each as a new artboard on the existing canvas rather than a new file set, so tokens and components stay shared. Derive the dark variant from the light file; only the theme default differs. Apply the shadow tokens from §2.5 by role rather than inventing new shadows, and give every new control the press behaviour in §2.4.
