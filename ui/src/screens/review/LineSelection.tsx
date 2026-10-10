// Selection mode and the line context menu in Review's transcript (2.0; DESIGN.md §19's Library selection mode and
// context menu, applied to transcript lines). Ctrl+click (or Shift+click for a range, or Select in a line's menu)
// starts it; rows then carry a checkbox and a click toggles them; Ctrl+A selects every line shown (the filter's view),
// Space toggles the focused line, Shift+↑/↓ extend, Esc leaves. A raised bar above the lines says how many are
// selected and acts on them: Assign speaker (the line menu's chooser, with "Just these lines" or the merge when they
// share one speaker), Highlight, Remove highlights, Mark as chapter start, Copy. A right-click, Shift+F10 or the
// context-menu key opens the same actions for the line (for the selection, when the line is in it). Every change is
// one Undo step (reviewActions20.ts).
import type { JSX } from 'preact';
import { createPortal } from 'preact/compat';
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'preact/hooks';
import type { Highlight, Speaker, TranscriptCopyFormat, TranscriptSegment } from '../../bridge/types';
import { CloseIcon } from '../../components/icons';
import { moveFocus } from '../../components/keyboard';
import { SpeakerChooser, type PickChoice } from '../../components/SpeakerChooser';
import { formatDuration } from '../../format/duration';
import {
  extendSelection,
  keepExisting,
  NO_SELECTION,
  selectAll,
  selectionWording,
  selectRange,
  toggleLine,
  type LineSelection,
} from '../../format/lineSelection';
import './review20.css';

/** What selection mode and the context menu do; Review supplies them (each registers with Undo). */
export interface LineActions {
  assign: (lines: readonly TranscriptSegment[], speaker: Speaker) => Promise<void>;
  addSpeaker: (lines: readonly TranscriptSegment[], name: string) => Promise<void>;
  merge: (from: Speaker, into: Speaker) => Promise<void>;
  highlight: (lines: readonly TranscriptSegment[]) => Promise<number>;
  removeHighlights: (highlights: readonly Highlight[]) => Promise<number>;
  chapterAt: (line: TranscriptSegment) => Promise<void>;
  copy: (lines: readonly TranscriptSegment[], format: TranscriptCopyFormat) => void;
  play: (line: TranscriptSegment) => void;
  /** A refusal: a warning toast with the host's words. */
  onError: (title: string, message: string) => void;
  /** "Highlighted 3 lines" beside Undo. */
  announce: (text: string) => void;
}

/** How a click or key on a line asks to change the selection. */
export type SelectHow = 'toggle' | 'range';

export interface LineSelectionApi {
  selecting: boolean;
  selectedIds: ReadonlySet<string> | null;
  select: (segment: TranscriptSegment, how: SelectHow) => void;
  selectAll: () => void;
  extend: (segment: TranscriptSegment, direction: 1 | -1) => void;
  openMenu: (segment: TranscriptSegment, at: { x: number; y: number }) => void;
  leave: () => void;
  /** The bar above the lines while selecting, and the menu while open. */
  bar: JSX.Element | null;
  menu: JSX.Element | null;
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

function isTyping(target: EventTarget | null): boolean {
  return target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement || (target instanceof HTMLElement && target.isContentEditable);
}

/** Choices for a speaker picked for several lines: just these, or (all one speaker's) the merge. */
function pickChoicesFor(lines: readonly TranscriptSegment[], speakers: readonly Speaker[], actions: LineActions, done: () => void) {
  return (chosen: Speaker): readonly PickChoice[] | null => {
    const owners = new Set(lines.map((l) => l.speaker));
    const only = owners.size === 1 ? speakers.find((s) => owners.has(s.id)) : undefined;
    if (only === undefined || only.id === chosen.id) {
      return null;
    }
    const count = lines.length;
    const first = lines[0];
    return [
      {
        id: 'lines',
        label: count === 1 ? 'Just this line' : `Just these ${count} lines`,
        detail:
          count === 1 && first !== undefined
            ? `The line at ${formatDuration(first.start * 1000)} moves to ${chosen.name}.`
            : `The ${count} selected lines move to ${chosen.name}; ${only.name} keeps the others.`,
        run: () => {
          actions.assign(lines, chosen).then(done, (e: unknown) => {
            actions.onError('The speaker was not changed', messageOf(e));
          });
        },
      },
      {
        id: 'merge',
        label: `Merge ${only.name} into ${chosen.name}`,
        detail: `Every line of ${only.name} moves to ${chosen.name}, and ${only.name} goes away. Undo puts them back.`,
        run: () => {
          actions.merge(only, chosen).then(done, (e: unknown) => {
            actions.onError(`${only.name} was not merged into ${chosen.name}`, messageOf(e));
          });
        },
      },
    ];
  };
}

interface Target {
  lines: TranscriptSegment[];
  at: { x: number; y: number };
  /** The line the menu was opened on, for focus to return to. */
  origin: string;
}

/** The menu items for some lines (one line, or the selection). */
function menuItems(
  target: Target,
  selecting: boolean,
  highlights: readonly Highlight[],
  run: {
    play: () => void;
    select: () => void;
    assign: () => void;
    highlight: () => void;
    unhighlight: () => void;
    chapter: () => void;
    copy: (format: TranscriptCopyFormat) => void;
  },
): ({ id: string; label: string; run: () => void; danger?: boolean } | 'divider')[] {
  const many = target.lines.length > 1;
  const unmarked = target.lines.length - new Set(highlights.map((h) => h.segmentId)).size;
  const first = target.lines[0];
  const items: ({ id: string; label: string; run: () => void; danger?: boolean } | 'divider')[] = [];
  if (!many && first !== undefined) {
    items.push({ id: 'play', label: `Play from ${formatDuration(first.start * 1000)}`, run: run.play });
  }
  if (!selecting) {
    items.push({ id: 'select', label: 'Select', run: run.select });
  }
  items.push('divider');
  items.push({ id: 'assign', label: many ? `Assign speaker to ${target.lines.length} lines…` : 'Assign speaker…', run: run.assign });
  if (unmarked > 0) {
    items.push({ id: 'highlight', label: many ? `Highlight ${unmarked} ${unmarked === 1 ? 'line' : 'lines'}` : 'Highlight', run: run.highlight });
  }
  if (first !== undefined) {
    items.push({ id: 'chapter', label: 'Mark as chapter start', run: run.chapter });
  }
  items.push('divider');
  items.push({ id: 'copy-text', label: many ? `Copy ${target.lines.length} lines as text` : 'Copy as text', run: () => { run.copy('text'); } });
  items.push({ id: 'copy-markdown', label: many ? `Copy ${target.lines.length} lines as Markdown` : 'Copy as Markdown', run: () => { run.copy('markdown'); } });
  if (highlights.length > 0) {
    items.push('divider');
    items.push({ id: 'unhighlight', label: highlights.length === 1 ? 'Remove highlight' : `Remove ${highlights.length} highlights`, run: run.unhighlight, danger: true });
  }
  return items;
}

/** The context menu (240 px, raised, radius 16): fixed at the pointer or the line, arrow keys move, Esc closes. */
function ContextMenu({
  target,
  label,
  items,
  onClose,
}: {
  target: Target;
  label: string;
  items: ReturnType<typeof menuItems>;
  onClose: (focusLine: boolean) => void;
}): JSX.Element {
  const menu = useRef<HTMLDivElement | null>(null);
  const [place, setPlace] = useState(target.at);
  useLayoutEffect(() => {
    const el = menu.current;
    if (el === null) {
      return;
    }
    const left = Math.max(8, Math.min(target.at.x, window.innerWidth - el.offsetWidth - 8));
    const top = Math.max(8, Math.min(target.at.y, window.innerHeight - el.offsetHeight - 8));
    setPlace({ x: left, y: top });
    el.querySelector<HTMLElement>('[role="menuitem"]')?.focus();
  }, [target]);
  useEffect(() => {
    const away = (event: PointerEvent): void => {
      if (event.target instanceof Node && menu.current?.contains(event.target) !== true) {
        onClose(false);
      }
    };
    const blur = (): void => {
      onClose(false);
    };
    document.addEventListener('pointerdown', away, true);
    window.addEventListener('blur', blur);
    return () => {
      document.removeEventListener('pointerdown', away, true);
      window.removeEventListener('blur', blur);
    };
  }, [onClose]);
  return createPortal(
    <div
      ref={menu}
      class="line-menu"
      role="menu"
      aria-label={label}
      style={{ left: `${place.x}px`, top: `${place.y}px` }}
      onContextMenu={(event) => {
        event.preventDefault();
      }}
      onKeyDown={(event) => {
        if (event.key === 'Escape') {
          event.preventDefault();
          event.stopPropagation();
          onClose(true);
        } else if (event.key === 'Tab') {
          event.preventDefault();
          onClose(true);
        } else if (menu.current !== null) {
          moveFocus(event, menu.current, '[role="menuitem"]', 'vertical');
        }
      }}
    >
      {items.map((item, i) =>
        item === 'divider' ? (
          i === 0 || items[i - 1] === 'divider' ? null : <div key={`d${i}`} class="line-menu-divider" role="separator" />
        ) : (
          <button
            key={item.id}
            class={item.danger === true ? 'line-menu-item line-menu-item--danger' : 'line-menu-item'}
            type="button"
            role="menuitem"
            tabIndex={-1}
            data-item={item.id}
            onClick={() => {
              item.run();
            }}
          >
            {item.label}
          </button>
        ),
      )}
    </div>,
    document.body,
  );
}

export function useLineSelection({
  segments,
  allIds,
  highlightsBySegment,
  speakers,
  actions,
  focusLine,
}: {
  /** The lines shown, in order. */
  segments: readonly TranscriptSegment[];
  /** Every line of the transcript (lines that go away leave the selection). */
  allIds: ReadonlySet<string>;
  highlightsBySegment: ReadonlyMap<string, readonly Highlight[]>;
  speakers: readonly Speaker[];
  actions: LineActions | undefined;
  /** Moves keyboard focus to a line by id. */
  focusLine: (segmentId: string) => void;
}): LineSelectionApi {
  const [selecting, setSelecting] = useState(false);
  const [selection, setSelection] = useState<LineSelection>(NO_SELECTION);
  const [menuTarget, setMenuTarget] = useState<Target | null>(null);
  const [chooser, setChooser] = useState<{ lines: TranscriptSegment[]; at: { x: number; y: number } | null } | null>(null);
  const [busy, setBusy] = useState(false);
  const assignButton = useRef<HTMLButtonElement | null>(null);
  const chooserRoot = useRef<HTMLSpanElement | null>(null);
  const pointAnchor = useRef<HTMLSpanElement | null>(null);
  const order = useMemo(() => segments.map((s) => s.id), [segments]);
  const byId = useMemo(() => new Map(segments.map((s) => [s.id, s])), [segments]);
  const current = keepExisting(selection, allIds);
  useEffect(() => {
    if (current !== selection) {
      setSelection(current);
    }
  });
  const selected = segments.filter((s) => current.ids.has(s.id));
  const latest = useRef({ order, byId, selecting, current });
  latest.current = { order, byId, selecting, current };

  const leave = (): void => {
    setSelecting(false);
    setSelection(NO_SELECTION);
    setChooser(null);
  };

  // Esc leaves selection mode, before the filter's Esc and unless a field, a menu or a dialog takes it.
  useEffect(() => {
    if (!selecting) {
      return undefined;
    }
    const onKey = (event: KeyboardEvent): void => {
      const target = event.target;
      const inPopover = target instanceof Element && target.closest('[role="menu"],[role="dialog"],[role="listbox"],.popover,.speaker-menu') !== null;
      if (event.key === 'Escape' && !event.defaultPrevented && !isTyping(target) && !inPopover && chooser === null) {
        event.preventDefault();
        event.stopPropagation();
        leave();
      }
    };
    document.addEventListener('keydown', onKey, true);
    return () => {
      document.removeEventListener('keydown', onKey, true);
    };
  }, [selecting, chooser]);

  const act = (title: string, work: () => Promise<unknown>, after?: () => void): void => {
    setBusy(true);
    work()
      .then(() => after?.())
      .catch((e: unknown) => {
        actions?.onError(title, messageOf(e));
      })
      .finally(() => {
        setBusy(false);
      });
  };
  const highlightsOf = (lines: readonly TranscriptSegment[]): Highlight[] => lines.flatMap((l) => [...(highlightsBySegment.get(l.id) ?? [])]);
  const plural = (n: number, one: string, many: string): string => `${n} ${n === 1 ? one : many}`;

  const run = (lines: TranscriptSegment[], at: { x: number; y: number } | null) => ({
    play: () => {
      const first = lines[0];
      if (first !== undefined) {
        actions?.play(first);
      }
    },
    select: () => {
      const first = lines[0];
      if (first !== undefined) {
        setSelecting(true);
        setSelection(toggleLine(NO_SELECTION, first.id));
      }
    },
    assign: () => {
      setChooser({ lines, at });
    },
    highlight: () => {
      if (actions !== undefined) {
        act('The lines were not highlighted', () => actions.highlight(lines).then((n) => {
          actions.announce(n === 1 ? 'Highlighted 1 line' : `Highlighted ${n} lines`);
        }));
      }
    },
    unhighlight: () => {
      if (actions !== undefined) {
        act('The highlights were not removed', () => actions.removeHighlights(highlightsOf(lines)).then((n) => {
          actions.announce(`Removed ${plural(n, 'highlight', 'highlights')}`);
        }));
      }
    },
    chapter: () => {
      const first = lines[0];
      if (actions !== undefined && first !== undefined) {
        act('The chapter was not added', () => actions.chapterAt(first).then(() => {
          actions.announce(`Chapter added at ${formatDuration(first.start * 1000)}`);
        }));
      }
    },
    copy: (format: TranscriptCopyFormat) => {
      actions?.copy(lines, format);
    },
  });

  const closeMenu = (focusLineBack: boolean): void => {
    const target = menuTarget;
    setMenuTarget(null);
    if (focusLineBack && target !== null) {
      focusLine(target.origin);
    }
  };

  const seconds = selected.reduce((sum, s) => sum + Math.max(0, s.end - s.start), 0);
  const allShown = order.length > 0 && order.every((id) => current.ids.has(id));
  const selectedHighlights = highlightsOf(selected);
  const unmarked = selected.filter((s) => (highlightsBySegment.get(s.id)?.length ?? 0) === 0);
  const barRun = run(selected, null);
  const bar =
    !selecting || actions === undefined ? null : (
      <div class="sel-bar" role="toolbar" aria-label="Selected lines" aria-busy={busy}>
        <input
          class="chk"
          type="checkbox"
          checked={allShown}
          aria-label={allShown ? 'Select none' : `Select all ${order.length} lines shown`}
          onChange={() => {
            setSelection(allShown ? NO_SELECTION : selectAll(order));
          }}
        />
        <span class="sel-count" aria-live="polite">
          {selectionWording(selected.length, seconds)}
        </span>
        <span class="sel-spacer" />
        <span class="menu-root sel-assign-root" ref={chooserRoot}>
          <button
            ref={assignButton}
            class="btn g sel-btn"
            type="button"
            aria-haspopup="listbox"
            aria-expanded={chooser !== null && chooser.at === null}
            disabled={selected.length === 0 || busy}
            onClick={() => {
              setChooser(chooser === null ? { lines: selected, at: null } : null);
            }}
          >
            Assign speaker…
          </button>
        </span>
        <button class="btn g sel-btn" type="button" disabled={unmarked.length === 0 || busy} onClick={barRun.highlight}>
          Highlight
        </button>
        <button class="btn g sel-btn" type="button" disabled={selected.length === 0 || busy} onClick={barRun.chapter} title="A chapter starts at the first selected line">
          Mark as chapter start
        </button>
        <button
          class="btn g sel-btn"
          type="button"
          disabled={selected.length === 0}
          onClick={() => {
            barRun.copy('text');
          }}
        >
          Copy
        </button>
        {selectedHighlights.length > 0 ? (
          <button class="btn g sel-btn sel-btn--danger" type="button" disabled={busy} onClick={barRun.unhighlight}>
            Remove {plural(selectedHighlights.length, 'highlight', 'highlights')}
          </button>
        ) : null}
        <button class="btn sel-cancel" type="button" aria-label="Leave selection mode (Esc)" onClick={leave}>
          <CloseIcon size={12} />
          Cancel
        </button>
      </div>
    );

  const chooserLines = chooser?.lines ?? [];
  const chooserElement =
    chooser === null || actions === undefined ? null : (
      <>
        {chooser.at === null ? null : createPortal(<span ref={pointAnchor} class="line-menu-anchor" style={{ left: `${chooser.at.x}px`, top: `${chooser.at.y}px` }} />, document.body)}
        <SpeakerChooser
          anchorRef={chooser.at === null ? assignButton : pointAnchor}
          rootRef={chooser.at === null ? chooserRoot : pointAnchor}
          label={chooserLines.length === 1 ? 'Speaker for the line' : `Speaker for ${chooserLines.length} lines`}
          heading={chooserLines.length === 1 ? 'This line is said by' : 'These lines are said by'}
          speakers={speakers}
          currentId={new Set(chooserLines.map((l) => l.speaker)).size === 1 ? (chooserLines[0]?.speaker ?? null) : null}
          onPick={(speaker) => {
            act('The speaker was not changed', () => actions.assign(chooserLines, speaker));
          }}
          pickChoices={pickChoicesFor(chooserLines, speakers, actions, () => undefined)}
          onAdd={(name) => {
            act(`${name} was not added as a speaker`, () => actions.addSpeaker(chooserLines, name));
          }}
          align="start"
          onClose={(focusTrigger) => {
            const wasPoint = chooser.at !== null;
            setChooser(null);
            if (focusTrigger) {
              if (wasPoint && chooserLines[0] !== undefined) {
                focusLine(chooserLines[0].id);
              } else {
                assignButton.current?.focus();
              }
            }
          }}
        />
      </>
    );

  const menu =
    menuTarget === null || actions === undefined ? (
      chooserElement
    ) : (
      <ContextMenu
        target={menuTarget}
        label={menuTarget.lines.length === 1 ? `Line at ${formatDuration((menuTarget.lines[0]?.start ?? 0) * 1000)}` : `${menuTarget.lines.length} selected lines`}
        items={menuItems(menuTarget, selecting, highlightsOf(menuTarget.lines), (() => {
          const r = run(menuTarget.lines, menuTarget.at);
          const close = (fn: () => void) => () => {
            setMenuTarget(null);
            fn();
          };
          return {
            play: close(r.play),
            select: close(r.select),
            assign: close(r.assign),
            highlight: close(r.highlight),
            unhighlight: close(r.unhighlight),
            chapter: close(r.chapter),
            copy: (format: TranscriptCopyFormat) => {
              setMenuTarget(null);
              r.copy(format);
            },
          };
        })())}
        onClose={closeMenu}
      />
    );

  return {
    selecting,
    selectedIds: selecting ? current.ids : null,
    select: (segment, how) => {
      const state = latest.current;
      setSelecting(true);
      setSelection(how === 'range' ? selectRange(state.current, state.order, segment.id) : toggleLine(state.current, segment.id));
    },
    selectAll: () => {
      setSelecting(true);
      setSelection(selectAll(latest.current.order));
    },
    extend: (segment, direction) => {
      const state = latest.current;
      const { selection: next, focus } = extendSelection(state.current, state.order, segment.id, direction);
      setSelecting(true);
      setSelection(next);
      focusLine(focus);
    },
    openMenu: (segment, at) => {
      const state = latest.current;
      const lines = state.selecting && state.current.ids.has(segment.id) ? state.order.filter((id) => state.current.ids.has(id)).map((id) => state.byId.get(id)).filter((s): s is TranscriptSegment => s !== undefined) : [segment];
      setChooser(null);
      setMenuTarget({ lines, at, origin: segment.id });
    },
    leave,
    bar,
    menu,
  };
}
