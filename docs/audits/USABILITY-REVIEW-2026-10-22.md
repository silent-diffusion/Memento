# Usability review — 2026-10-22 (theme and navigation, for 2.3.1)

Scope: `main` 7295d61 (the 2.3.0 release). The product owner's brief: "several buttons that don't match the theme, and several usability issues for going back and forth between pages". A bug-fix and usability update: no new screens, no redesign, hub and spoke kept (DESIGN.md §1, §3).

## 1. Method

- **Driven on the published build** (`artifacts/publish/win-x64`, Release) with a private data root under `artifacts/`, `--simulate-audio` (nothing played, no device recorded) and the synthetic two-voice fixture of `tools/e2e/speech.ps1` imported through Windows' own picker. No transcription model is installed in the sandbox, so the real app shows recordings waiting for a model; Review with a transcript, the transcript filter, the Document viewer, the Style editor and the templates manager were driven on the **browser-preview host** (Vite, mock bridge, headless Edge with its own profile and `--mute-audio`).
- Every screen in **light and dark at 1024, 1280 and 1600 px**: Library (empty, list, grid, Folders), Record (ready, recording, Details sheet), Review (recorded, imported and waiting for a model, with a transcript, filter, Documents, History), Export dialog, Builder, the eight Settings sections, templates manager, Document viewer, Style editor, a row menu. Screenshots: `ui/.screenshots/review-2.3.1/` (git-ignored), `before-*` from 7295d61 and `after-*` from this branch.
- **Read the CSS and components, not only the screens**: every stylesheet under `ui/src` scanned rule by rule for raw colours, `box-shadow` values that are not tokens and radii off the §2.3 scale; every `<button>` class combination listed and checked for a complete recipe (fill, shadow, hover, press, focus); `fill="#…"` and inline colours in TSX. That scan is now a test (`ui/src/styles/tokenLint.test.ts`, §5).
- **Every path between screens** walked by hand in both hosts, then automated on the real app (`tools/e2e/m13-navigation.mjs`, 24 checks) and in vitest (`ui/src/components/SpokeNavigation.test.tsx`, `ui/src/state/navigation.test.ts`).

Severity: **high** (looks broken or fails a contrast minimum), **medium** (visibly off the design or loses the way back), **low** (consistency).

## 2. Theme

16 rows: 14 findings (3 high, 5 medium, 6 low) and 2 accepted exceptions. 13 findings are fixed; T13 is fixed in part (the base recipe) and carries recommendation R5; T15 and T16 are accepted with their reason (R6 for T15).

| # | Where | What does not follow DESIGN §2/§5 | Sev. | Status |
|---|---|---|---|---|
| T1 | Review › Details: **Move to folder…**, **Delete audio…** (2.1) | `class="btn ghost"` with no size class. Only buttons that also carried a per-screen class (spoke-ghost, small-btn, edit-details, …) had a fill and shadow, so these rendered as unstyled grey browser buttons in both themes | high | fixed: `.btn:where(.ghost)` / `.btn:where(.primary)` in `base.css` carry the full §5.1 recipe at one class, so every size class in a later stylesheet still wins; compared button by button against 2.3.0 on every browser-preview screen, only these, the processing card's Open (label now `text-2` as §5.1 says) and the fixes below changed |
| T2 | Record › **Resume recording** (2.1, while saving after Stop) | Same bare ghost, no CSS at all, and the record dot was the primary button's white dot: invisible on the light ground | high | fixed: 48 px stage ghost (radius 20, §5.1) with the dot in `accent` |
| T3 | Dark theme: **New recording** / **Start recording** dots, the **Stop** square | Fixed `#ffffff` while the labels beside them use `--on-accent` (the ground in dark): white on the light orange is about 2.5:1, below the 3:1 for non-text parts | high | fixed: `var(--on-accent)`; `StopIcon` fills with `currentColor` |
| T4 | Dark theme: primary button **hover** | tokens.css added the light theme's `rgba(255,255,255,.5)` halo in both themes: a white glow around every primary in dark | medium | fixed: new token `--neo-accent-hi` per theme (§2.5) |
| T5 | Dark theme: **destructive** button (`.d`, every Delete confirmation) | Hand-tuned light-theme shadow in both themes (`rgba(255,255,255,.7)` halo) | medium | fixed: new token `--neo-danger` per theme (§2.5) |
| T6 | Builder: **+ Add a module** | Its dashed `line-strong` outline (§10, §2.3 "add" affordances) was removed by the `button.btn` border reset, so it read as loose text | medium | fixed (`.btn.add-module`, as add-row and add-pill already did) |
| T7 | Folder tree rows and disclosure (2.1), the window picker's text button, People › "who spoke" hint | No `:focus-visible` rule: the browser's own ring instead of the 2 px `focus` ring of §7 | medium | fixed (`base.css`) |
| T8 | Export dialog and other dialogs/sheets that scroll | Native scrollbar; in dark the Export dialog showed a light classic scrollbar next to the Review panes' thin token one | medium | fixed: thin `line-strong` scrollbar inside `.scrim` |
| T9 | Chapter ×, outline +, People rename pencil, suggestion accept/dismiss, transcript search steps, inline rename field, filter options, attachment tile (1.1–2.0) | Radii 8, 9 and 10 px on 24–28 px controls; §2.3 has 12 px for tiny controls; the scrubber track is a pill | low | fixed (12 px; 999 px) |
| T10 | `.g/.p/.d` dialog buttons | Base radius 9 px (dead: tokens.css forces 14 px with `!important`) | low | fixed (14 px) |
| T11 | Review › Attachments › **Open** | Raised with `neo-xs` (the pill/segment role) instead of the button's `neo-sm` | low | fixed |
| T12 | Settings footer | A long library path wraps the 36 px footer onto two lines (seen at 1024 and 1280) | low | fixed: the path is cut with an ellipsis, full path in the tooltip |
| T13 | Ghost buttons across the app | 30 per-screen classes re-implement the ghost recipe at 26–56 px (spoke-ghost, small-btn, side-btn, drop-btn, edit-details, docs-write, proc-open, rec-add, rec-pick, ai-card-btn, …); several are below the 36 px secondary minimum of §2.3 (doc-ver-restore 26, sug-btn 28, rec-pick 28, attachment-open 28) | low | base recipe added (T1); consolidation and sizes: recommendation R5 |
| T14 | Back control in every spoke header | The back label had no width limit: a long recording, document or template name pushed the title and actions aside | low | fixed: label cut with an ellipsis (max 280 px), tooltip names the target and the shortcuts |
| T15 | `base.css` overrides `--text-3` and `--on-accent` outside tokens.css | Measured contrast corrections (h1-contrast.mjs: 0 shipped pairs below threshold) | — | accepted; recommendation R6 |
| T16 | Video wells, overlays, thumbnails and the grid card's video strip; the document paper; the scrim; the knob shadow | Raw colours and small radii | — | accepted: they are pictures of video (dark in both themes, the renders' own colours, §18), the document (§5.18), or values §5.19/§5.7 give; listed with reasons in the lint's allowlist |

Not findings (checked): the back control is shadowless at rest as in the renders and raised on hover, and so is the viewer toolbar's Insert timestamp; the display picker's chosen tile uses an `accent` ring as §18.2 says; speaker dots use the `sp1–4` tokens; labels follow §17 (sentence case, specific, no exclamation marks) on every screen walked.

## 3. Navigation

36 paths: 22 worked, 10 surprising, 4 missing. Every surprising or missing path is fixed except N27, which is fixed in part and carries recommendation R1 (§4). "Expected" is what a person expects per §3 hub and spoke: the Library is home, a spoke's Back returns to where they came from, and falls back to the hub.

| # | Path | Before (7295d61) | Status | After |
|---|---|---|---|---|
| N1 | Library → New recording → Back | Library | works | — |
| N2 | Record → Stop → Review → Back | Library (not Record) | works | — |
| N3 | Library row → Review → Back | Library with search, type chip, sort, layout, Timeline/Folders and the folder kept; the opened row focused | works | checked on the real app (m13) |
| N4 | Library scroll position after a spoke | Restored by the router | works | checked in the browser preview (list and grid) |
| N5 | Review → Create document → Builder → Back | the recording's Review | works | — |
| N6 | Builder → Generate → Document viewer → Back | Review | works | kept (not back into the Builder) |
| N7 | Review › Documents → viewer → Back | Review | works | — |
| N8 | Viewer → **timestamp chip** → Review at that moment → Back | Library: the document was lost | surprising | returns to the document; its Back then returns to Review |
| N9 | Viewer → **Regenerate** → Builder → Back | Review | surprising | returns to the document |
| N10 | Builder → **Choose a model** (failure card) / **Open AI and privacy settings** → Settings → Back | Library; the Builder's unsaved draft stayed in memory with no way back to it | surprising | returns to the Builder with the draft; back label names the template |
| N11 | Review's failure or waiting card → **Open Settings › Transcription/Speakers** → Back | Library | surprising | returns to Review |
| N12 | Details sheet (Record or Review) → agenda AI link → Settings › AI and privacy → Back | Library (from Record: away from the session, which carried on) | surprising | returns to the screen that opened the sheet |
| N13 | Library gear → Settings | always General | surprising | opens at the section shown last |
| N14 | Settings section → section | each section added a page-history entry (see N25) | surprising | the way back is unchanged by section changes |
| N15 | Builder → Edit style → Style editor → Back | the Builder, draft kept | works | — |
| N16 | Settings › Documents → Style editor → Back | Settings › Documents | works | — |
| N17 | Settings › Documents › Manage → Open template → Builder → Back | Settings › Documents | works | — |
| N18 | Review → Export → Close/Esc | Review, filter kept | works | — |
| N19 | Review › History → version → viewer `?version=` → Back to current / Back | the current version / Review | works | — |
| N20 | Deep link or reload on any route (`#/settings/storage`, `?version=`, `?t=`) → Back | the designed parent | works | — |
| N21 | Tray › Open | the window comes forward on its screen | works | — |
| N22 | Tray › **Record** | the Recording session, but set beside the router: the hash kept the old screen and the Library's scroll offset was not saved | surprising | goes through the router (not driven: the tray cannot be clicked over the DevTools protocol) |
| N23 | Review → **Resume recording** → Record → Stop → Review → Back | Library | works | — |
| N24 | Record → Back while recording → Library → **Back to recording** | rejoins the session | works | checked on the real app |
| N25 | Mouse **back button** | WebView2's page history: stepped back through every hash, Settings sections included, often to a screen only passed | surprising | presses the open spoke's back control; does nothing on the Library; forward does nothing |
| N26 | Keyboard shortcut for Back | none (Alt+← is off with the browser accelerator keys in Release) | missing | **Alt+←** and the Browser Back key press the back control on every spoke |
| N27 | Builder → Back with **unsaved template changes** | dropped without asking (the template switch asks first; the Style editor asks before leaving) | surprising | recommendation R1; Esc no longer goes back while there are unsaved changes, and the changes stay unsaved across a trip to Settings or the Style editor (they were counted as saved after it) |
| N28 | Esc on Library | nothing (menus, dialogs and selection take it) | works | — |
| N29 | Esc on Record | nothing (§8: no accidental stop) | works | kept; the back control's tooltip says Alt+← |
| N30 | Esc on Review | clears the filter, selection or fullscreen; otherwise nothing | missing | after those, goes back |
| N31 | Esc on Builder | nothing | missing | goes back when the template has no unsaved changes |
| N32 | Esc on viewer, Settings, Style editor | nothing | missing | goes back (the viewer saves first; the Style editor asks about unsaved changes as its Back does) |
| N33 | Esc order with a menu, popover, field or dialog open | each closes itself | works | menus, popovers, fields, sheets and dialogs still take Esc first; Back only when none is open |
| N34 | Back control | first Tab stop, focused when the spoke opens, names its target | works | still so; the target can now be the screen it was opened from |
| N35 | Clickable breadcrumb or title | none; the spoke title is text or a rename field | works | kept (hub and spoke is at most two levels deep; recommendation R4 explains why no breadcrumb) |
| N36 | After Generate, a way to the Builder from the viewer | Regenerate opens it | works | — |

## 4. Recommendations (judgment calls, not built)

**R1. Ask before Back drops unsaved template changes in the Builder (N27).** Today Back discards a changed structure or name without asking, while switching templates asks and the Style editor asks before leaving. Two options:

```
A. Ask, as the template switch does            B. Keep the draft, say so
┌ Leave without saving the template? ───┐     [‹ Design review]  Meeting minutes · unsaved
│ Your changes to "Meeting minutes" are │     … next time Create document opens this
│ only in this document builder.        │     recording's Builder, the draft is back,
│        [Keep editing] [Save template] │     with a line "Unsaved changes from 4:12 PM ·
│                  [Leave without saving]│     Discard".
└───────────────────────────────────────┘
```

A matches the Style editor and costs a click for people who only tweak a template for one document; B never interrupts but keeps hidden state. A is the smaller change; it needs the product owner's call because most template edits are one-off.

**R2. Show the Library's selection when coming back by keyboard only.** The row opened is focused and drawn selected (`accent-soft`) after Back. With the mouse that selection stays visible after the next click elsewhere, which some people read as "still selected". A judgment call on §4's "Selected (keyboard focus / last opened)".

**R3. Return to the same document version after a timestamp chip from an old version.** From a kept version (`?version=`), a chip opens Review and Back returns to that version (the trail keeps the full route). Whether the version banner should instead offer "Back to the current version" there is a product question.

**R4. No breadcrumb.** The deepest path is Library › Review › Builder › Style editor (or › Settings). The back control names its target, and Esc/Alt+← walk back. A breadcrumb would add a second navigation system to the header §3 keeps for the title and actions; recommended only if screens get deeper.

**R5. One ghost recipe, three sizes.** Replace the 30 per-screen ghost classes (T13) with the base recipe plus three documented sizes (28 px inside cards and rows, 36 px toolbar, 48 px stage) and lift the 26–28 px buttons that are a main action in their card (Restore a version, Accept a suggestion) to 32 px. This touches most screens' CSS, so it is a refactor for a minor release, not this bug-fix update.

**R6. Fold the contrast corrections into tokens.css.** `--text-3` (#68645E / #96928B) and `--on-accent` live in `base.css` (T16); the design owner should take them into `design/tokens.css` and §2.1 so the tokens and the app agree.

## 5. What was built and how it is tested

- **Navigation** (`ui/src/state/navigation.ts`, `state/back.ts`, `components/SpokeHeader.tsx`): both routers record each move in an in-memory trail; Settings returns to any spoke that opened it, Review to the document whose chip opened it, the Builder to the document whose Regenerate opened it; everything else keeps its designed parent and the Library clears the trail. Esc (last, after every menu, field, popover, sheet and dialog), Alt+←, Browser Back and the mouse's back button press the open spoke's back control. The Library's gear opens the last Settings section. The tray's Record goes through the router. The Builder's saved baseline travels with its draft.
- **Theme**: §2 above; DESIGN.md §2.5 gains `neo-accent-hi` and `neo-danger`, §5.1 says the base recipes are complete and size classes only resize.
- **Tests**: `navigation.test.ts` (accepted origins, Back targets along real paths, loops, the limit, history moves, the remembered section), `router.test.ts` (the trail through both routers and hashchange), `SpokeNavigation.test.tsx` (gear and section memory, Esc order with a menu, fields and dialogs, Review's filter then Back, Record ignoring Esc, Alt+←, the mouse's back button, chip → Review → document → Review, Builder → Settings → Builder with the draft and Esc held), `trayEvents.test.ts` (Record through the router), `tokenLint.test.ts` (raw colours, shadows and radii outside tokens.css with a documented allowlist; tokens.css verbatim; the base recipes; marks on the accent fill). `tools/e2e/m13-navigation.mjs` walks the paths on the real app (24 checks).
- **Not verified**: the tray's Record (N22) on the real app (the tray cannot be clicked over CDP; the unit test covers the routing); WebView2's own handling of the mouse's back button without the page's handler (CDP delivers the button to the page, where the handler takes it); Review with a real transcript, the viewer and the Style editor on the real host (no transcription model or AI in the sandbox; driven on the browser-preview host).

## Follow-up 2026-10-22 (2.4.0)

Three of the recommendations were built for 2.4.0; R2, R3 and R4 remain recommendations.

- **R1, option A (N27).** In the Builder with unsaved template changes, every way back asks **Leave without saving the template?** ("Your changes to “…” are only in this document builder.") with **Keep editing** (the default and Esc), **Save template** (the header's save path, then leaves; a failed save keeps the Builder and its toast says why) and **Leave without saving**: the back control, Esc (which goes back again in the Builder now that it asks), Alt+←, Browser Back, the mouse's back button, and a hashchange from the page's own history (a leave guard in `state/navigation.ts` that the router consults). Without changes Back goes straight back. DESIGN.md §3 and §10 and the user guide describe it. Tested by `ui/src/screens/builder/LeaveTemplate.test.tsx` (no changes, the three answers, a failed save, the history path), `components/SpokeNavigation.test.tsx` (Esc, Alt+←, Browser Back and the mouse's back button ask; Esc in the question keeps editing), `state/router.test.ts` (the guard holds and releases a hashchange) and `tools/e2e/m13-navigation.mjs` on the published build (Esc and the mouse's back button ask, Esc in the question keeps the unsaved name, Leave without saving returns to Review; 27 of 27 checks).
- **R5 (T13).** One ghost recipe in `ui/src/styles/base.css` with three size modifiers by role (`ghost-row` 28 px, `ghost-main` 32 px for a card's main action, `ghost-stage` 48 px; 36 px toolbar default), documented in DESIGN.md §2.3. The per-screen classes (spoke-ghost, small-btn, side-btn, drop-btn, edit-details, docs-write, proc-open, rec-add, rec-pick, ai-card-btn, doc-ver-restore, sug-btn, attachment-open, rec-agenda-add, vid-line-show, vid-remove, import-media, load-error-retry, rec-resume and the descendant size overrides) are removed and every component takes a modifier; the select menus take a `size`. Lifted: Restore a version 26/30 → 32, Accept and Dismiss a suggestion 28 → 32, the 34 px buttons and selects → 36 (or the dialog buttons' 32). Tested by `ui/src/styles/tokenLint.test.ts` (a rule re-declaring the ghost's fill and label colour on a button class, or a class beside `btn ghost` that sets any size or paint property, fails; the exceptions carry reasons) and `components/ghostSize.test.tsx`.
- **R6 (T15).** `--text-3` (#68645E / #96928B) and `--on-accent` (#FFFFFF / #1F1E1B) are in `design/tokens.css` and DESIGN.md §2.1 with the ratios `tools/e2e/h1-contrast.mjs` measures (design tokens and as shipped: 0 pairs below threshold); the `base.css` override is gone. `tokenLint.test.ts` pins the values and fails if either token is defined anywhere else.
