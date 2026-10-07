# Memento design folder

Everything a builder needs to implement the UI. Read `DESIGN.md` first; copy from `tokens.css` and `renders/` as you go.

| Path | What it is | How to use it |
|---|---|---|
| `DESIGN.md` | The design handoff: direction, tokens, components, every screen with its behaviour, error states, and what is not designed yet. | The source of truth for look and behaviour. `../docs/PRODUCT-SPEC.md` is the source of truth for product behaviour. |
| `tokens.css` | The colour, shadow and gradient tokens for light (`.app`) and dark (`.app.dark`), the base element rules, and the neumorphism rules (hover lift, press-in, toggle snap). Extracted from the renders. | Paste into the app's global stylesheet, or translate each `--token` into your framework's resource system. Keep the names. |
| `renders/*.dc.html` | One file per designed screen: the complete markup, inline styles and a small JavaScript class with the mock data and the interactions (filters, drag and drop, toggles, tabs). Light files are the originals; `*Dark.dc.html` twins differ only in the default theme. | Open in a text editor and copy markup and inline styles for any component. The `{{holes}}`, `<sc-for>` and `<sc-if>` tags are template syntax of the design tool: `{{x}}` is a bound value, `<sc-for list as>` repeats its children, `<sc-if value>` is a conditional. The `renderVals()` method shows what data each screen expects. These files are not standalone web pages; they need the design tool's runtime to render. |
| `renders/canvas.json` | Layout of the artboards on the design canvas (positions, sizes, which are pages, which are interactive). | Reference only. |
| `renders/clay.pl`, `renders/neo.pl` | The two scripted restyling passes that produced the current look from the flat originals (Perl). | Reference for how the tokens were applied by role. Not needed to build the app. |

## Screens in `renders/`

| File | Screen | DESIGN.md |
|---|---|---|
| `Main.dc.html` | Library (home), list view | §4 |
| `LibraryEmpty.dc.html` | Library, first run | §6 |
| `LibraryGrid.dc.html` | Library, grid view | §16 |
| `Foundations.dc.html` | Colour, type, controls, spacing, radii in both themes | §2 |
| `Record.dc.html` | Recording session (State tweak: recording / ready) | §8 |
| `Review.dc.html` | Review and transcript | §9 |
| `Builder.dc.html` | Document builder with drag and drop, rows and live preview | §10 |
| `Settings.dc.html` | Settings (State tweak switches the section) | §11 |
| `DocView.dc.html` | Document viewer after Generate | §12 |
| `StyleEditor.dc.html` | Style editor with live sample page | §13 |
| `AgendaImport.dc.html` | Details sheet and agenda import (State tweak: parsed / drop) | §14 |
| `ExportDialog.dc.html` | Export dialog | §15 |
| `ErrorStates.dc.html` | Toasts, banners, dialogs, inline errors, footer variants | §17 |
| `RecordVideo.dc.html` | Recording session with screen, camera, picture-in-picture, live preview and video lane | §18.1 |
| `DisplayPicker.dc.html` | Display and window picker side sheet | §18.2 |
| `ReviewVideo.dc.html` | Review with the video player, fullscreen and audio-only states (State tweak: present / removed) | §18.3 |
| `ExportVideo.dc.html` | Export dialog with the Video row enabled | §18.4 |
| `SettingsVideo.dc.html`, `SettingsStorageVideo.dc.html` | Settings › Recording video defaults; Settings › Storage remove-video row | §18.5 |
| `ErrorStatesVideo.dc.html` | Video errors and recovery (both themes on one sheet) | §18.6 |
| `ProcessingVideo.dc.html` | Processing card with a Video stage, Library rows and cards with video (both themes) | §18.7 |
| `Features20.dc.html` | Known voices, match prompt, suggested chapters, selection mode, context menu (both themes) | §19 |

The renders in this folder are the complete record of the design; the interactive canvas they came from was private to the design session.

## Reading a render

Each file has the same shape:

1. A `<style>` block in `<helmet>` with the tokens (identical to `tokens.css`) and the screen's class rules.
2. The screen markup inside `<x-dc>`, laid out with flex and grid and styled inline, so each element carries its own sizes, radii and shadows.
3. A `<script type="text/x-dc">` with `data-props` (the tweaks: `theme`, and sometimes a `phase` or `section`) and a `Component` class whose `renderVals()` returns the data and handlers the markup binds to. Mock data lives here; it shows the exact fields a real data model needs to supply.

Class names are consistent across screens: `.btn.primary`, `.btn.ghost`, `.icon-btn`, `.chip`, `.pill.done|active|queued|failed`, `.seg` (segmented option), `.tog` (toggle), `.row`, `.card`, `.nav`, `.mod` (Builder module), `.paper` (document page), `.lbl` (uppercase label), `.mono` (timecode).
