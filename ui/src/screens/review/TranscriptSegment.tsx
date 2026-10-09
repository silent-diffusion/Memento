// One transcript segment (DESIGN.md §5.12, renders/Review.dc.html): speaker with its dot over the mono
// timecode, the text with low-confidence words and search matches marked, highlight notes under it,
// editing in place (click the text: the caret lands where you clicked), and the speaker menu (find or
// add a speaker, reassign, Rename…).
import type { JSX } from 'preact';
import { memo } from 'preact/compat';
import { useEffect, useLayoutEffect, useRef, useState } from 'preact/hooks';
import type { Highlight, Speaker, TranscriptSegment } from '../../bridge/types';
import { PopoverLayer, useFloatingPopovers } from '../../components/Floating';
import { InlineInput } from '../../components/InlineInput';
import { NotesIcon } from '../../components/icons';
import { SpeakerChooser } from '../../components/SpeakerChooser';
import { undoKeyOf } from '../../state/undo';
import { caretOffsetAt, isSelectingIn } from '../../format/caret';
import { formatDuration } from '../../format/duration';
import {
  isSpeakerUncertain,
  lowConfidenceRanges,
  queryRanges,
  segmentTimecode,
  speakerColourVar,
  textRuns,
} from '../../format/transcript';

export interface SegmentHandlers {
  seek: (segment: TranscriptSegment) => void;
  /** Edit the text in place; `caret` is where in the text the click landed (else the end). */
  startEdit: (segment: TranscriptSegment, caret?: number) => void;
  draft: (text: string) => void;
  save: () => void;
  cancel: () => void;
  /** Move the line to another speaker ("Just this line"). */
  assign: (segment: TranscriptSegment, speaker: Speaker) => void;
  /** Create a speaker with this name and move the line to them. */
  addSpeaker: (segment: TranscriptSegment, name: string) => void;
  /** Every line of one speaker moves to another, which keeps its name ("Merge {old} into {new}"). */
  merge: (from: Speaker, into: Speaker) => void;
  rename: (speaker: Speaker, name: string) => void;
  renameHighlight: (highlight: Highlight, note: string) => void;
  /** Arrow keys between segments: the list moves focus. */
  moveFocus: (index: number, direction: 1 | -1) => void;
}

interface SegmentProps {
  segment: TranscriptSegment;
  index: number;
  count: number;
  speaker: Speaker | null;
  speakers: readonly Speaker[];
  current: boolean;
  threshold: number;
  query: string;
  /** Which occurrence of the query in this segment the search is on, or -1. */
  currentOccurrence: number;
  /** The draft text while this segment is being edited, else null. */
  editing: string | null;
  /** Where the caret starts in the editor (characters into the text), or null for the end. */
  caret: number | null;
  saving: boolean;
  highlights: readonly Highlight[];
  handlers: SegmentHandlers;
}

const AUTHORS: Record<Highlight['origin'], string> = { user: 'You', local: 'Suggested', ai: 'AI' };

function SegmentText({
  segment,
  threshold,
  query,
  currentOccurrence,
  onEdit,
}: Pick<SegmentProps, 'segment' | 'threshold' | 'query' | 'currentOccurrence'> & { onEdit: (caret: number | undefined) => void }): JSX.Element {
  const low = lowConfidenceRanges(segment.text, segment.words, threshold);
  const matches = queryRanges(segment.text, query);
  const runs = textRuns(segment.text, low, matches, currentOccurrence);
  return (
    <span
      class="segm-text segm-text--editable"
      title="Click to correct"
      onClick={(event) => {
        // A click on the words edits them; dragging over them selects as usual.
        event.stopPropagation();
        const el = event.currentTarget;
        if (isSelectingIn(el)) {
          return;
        }
        onEdit(caretOffsetAt(el, event.clientX, event.clientY) ?? undefined);
      }}
    >
      {runs.map((run, i) => {
        if (!run.low && !run.match) {
          return run.text;
        }
        const cls = [run.low ? 'lowc' : '', run.match ? 'hl' : '', run.current ? 'hl-current' : ''].filter((c) => c !== '').join(' ');
        return (
          <span key={i} class={cls} title={run.low ? 'Low confidence' : undefined}>
            {run.text}
            {run.low ? <span class="sr"> (low confidence)</span> : null}
          </span>
        );
      })}
    </span>
  );
}

/** The textarea that replaces the text while editing: as tall as its content, the caret where the click was. */
function SegmentEditor({
  value,
  caret,
  saving,
  onDraft,
  onSave,
  onCancel,
}: {
  value: string;
  caret: number | null;
  saving: boolean;
  onDraft: (text: string) => void;
  onSave: () => void;
  onCancel: () => void;
}): JSX.Element {
  const ref = useRef<HTMLTextAreaElement | null>(null);
  useLayoutEffect(() => {
    const el = ref.current;
    if (el !== null) {
      el.style.height = 'auto';
      el.style.height = `${el.scrollHeight}px`;
    }
  }, [value]);
  useEffect(() => {
    const el = ref.current;
    if (el !== null) {
      el.focus({ preventScroll: true });
      const at = Math.max(0, Math.min(caret ?? el.value.length, el.value.length));
      el.setSelectionRange(at, at);
    }
  }, []);
  return (
    <textarea
      ref={ref}
      class="field segm-editor"
      aria-label="Edit this line. Enter saves, Esc cancels."
      value={value}
      rows={1}
      disabled={saving}
      onClick={(event) => {
        event.stopPropagation();
      }}
      onDblClick={(event) => {
        event.stopPropagation();
      }}
      onInput={(event) => {
        onDraft(event.currentTarget.value);
      }}
      onKeyDown={(event) => {
        // Ctrl+Z / Ctrl+Y go on to the app's undo, which leaves them to the field while it has changes.
        if (undoKeyOf(event) === null) {
          event.stopPropagation();
        }
        if (event.key === 'Enter' && !event.shiftKey) {
          event.preventDefault();
          onSave();
        } else if (event.key === 'Escape') {
          event.preventDefault();
          onCancel();
        }
      }}
      onBlur={onSave}
    />
  );
}

/** A highlight note under the line: its words edit in place, like the outline's highlight names. */
function SegmentNote({ highlight, onRename }: { highlight: Highlight; onRename: (note: string) => void }): JSX.Element {
  const [editing, setEditing] = useState(false);
  const button = useRef<HTMLButtonElement | null>(null);
  const shown = highlight.note === '' ? `Highlight at ${formatDuration(highlight.atMs)}` : highlight.note;
  const refocus = (): void => {
    requestAnimationFrame(() => {
      button.current?.focus();
    });
  };
  return (
    <span
      class="segm-note"
      onClick={(event) => {
        event.stopPropagation();
      }}
      onDblClick={(event) => {
        event.stopPropagation();
      }}
    >
      <NotesIcon size={14} class="segm-note-icon" />
      <span>
        <span class="segm-note-by">{AUTHORS[highlight.origin]}</span>{' '}
        {editing ? (
          <InlineInput
            label={`Name of the highlight at ${formatDuration(highlight.atMs)}. Enter saves, Esc cancels.`}
            initial={highlight.note}
            placeholder="Highlight name"
            class="segm-note-input"
            onCommit={(next) => {
              setEditing(false);
              if (next !== highlight.note) {
                onRename(next);
              }
              refocus();
            }}
            onCancel={() => {
              setEditing(false);
              refocus();
            }}
          />
        ) : (
          <button
            ref={button}
            class="segm-note-edit"
            type="button"
            title="Click to rename"
            aria-label={highlight.note === '' ? `Name the highlight at ${formatDuration(highlight.atMs)}` : `Rename ${highlight.note}`}
            onClick={() => {
              setEditing(true);
            }}
          >
            <span class="segm-note-words">{shown}</span>
          </button>
        )}
      </span>
    </span>
  );
}

/**
 * The speaker name's menu (SpeakerChooser): find or add a speaker, move the line to one, or Rename… the
 * current speaker everywhere.
 */
function SpeakerMenu({
  segment,
  speaker,
  speakers,
  onAssign,
  onMerge,
  onAdd,
  onRename,
}: {
  segment: TranscriptSegment;
  speaker: Speaker | null;
  speakers: readonly Speaker[];
  onAssign: (speaker: Speaker) => void;
  onMerge: (from: Speaker, into: Speaker) => void;
  onAdd: (name: string) => void;
  onRename: (name: string) => void;
}): JSX.Element {
  const [open, setOpen] = useState(false);
  const [renaming, setRenaming] = useState(false);
  const [name, setName] = useState('');
  const root = useRef<HTMLSpanElement | null>(null);
  const button = useRef<HTMLButtonElement | null>(null);
  const pop = useRef<HTMLDivElement | null>(null);
  const floating = useFloatingPopovers();

  // The rename form: its field takes focus, and a click elsewhere closes it.
  useEffect(() => {
    if (!renaming) {
      return undefined;
    }
    pop.current?.querySelector<HTMLInputElement>('input')?.select();
    const away = (event: PointerEvent): void => {
      if (event.target instanceof Node && root.current?.contains(event.target) !== true && pop.current?.contains(event.target) !== true) {
        setRenaming(false);
      }
    };
    document.addEventListener('pointerdown', away);
    return () => {
      document.removeEventListener('pointerdown', away);
    };
  }, [renaming]);

  const close = (focusButton: boolean): void => {
    setOpen(false);
    setRenaming(false);
    if (focusButton) {
      button.current?.focus();
    }
  };

  const label = speaker?.name ?? 'Unknown speaker';
  const uncertain = isSpeakerUncertain(segment);
  const commitRename = (): void => {
    const trimmed = name.trim();
    if (trimmed !== '' && speaker !== null && trimmed !== speaker.name) {
      onRename(trimmed);
    }
    close(true);
  };

  return (
    <span class="menu-root segm-speaker-root" ref={root}>
      <button
        ref={button}
        class="segm-speaker"
        type="button"
        aria-haspopup="listbox"
        aria-expanded={open || renaming}
        title={uncertain ? 'Speaker uncertain' : undefined}
        onClick={(event) => {
          event.stopPropagation();
          setRenaming(false);
          setOpen(!open);
        }}
        onDblClick={(event) => {
          event.stopPropagation();
        }}
        onKeyDown={(event) => {
          event.stopPropagation();
          if (event.key === 'ArrowDown') {
            event.preventDefault();
            setOpen(true);
          }
        }}
      >
        <span
          class={uncertain ? 'segm-dot segm-dot--uncertain' : 'segm-dot'}
          aria-hidden="true"
          style={{ background: speaker === null ? 'var(--line-strong)' : speakerColourVar(speaker.color) }}
        />
        <span class="segm-speaker-name">{label}</span>
        {uncertain ? <span class="sr"> (speaker uncertain)</span> : null}
      </button>
      {open ? (
        <SpeakerChooser
          anchorRef={button}
          rootRef={root}
          label={`Speaker for the line at ${formatDuration(segment.start * 1000)}`}
          heading="This line is said by"
          speakers={speakers}
          currentId={segment.speaker}
          onPick={(chosen) => {
            if (chosen.id !== segment.speaker) {
              onAssign(chosen);
            }
          }}
          pickChoices={(chosen) =>
            // A line with a speaker: move just it, or every line of that speaker (Undo puts them back).
            speaker === null
              ? null
              : [
                  {
                    id: 'line',
                    label: 'Just this line',
                    detail: `The line at ${formatDuration(segment.start * 1000)} moves to ${chosen.name}.`,
                    run: () => {
                      onAssign(chosen);
                    },
                  },
                  {
                    id: 'merge',
                    label: `Merge ${speaker.name} into ${chosen.name}`,
                    detail: `Every line of ${speaker.name} moves to ${chosen.name}, and ${speaker.name} goes away. Undo puts them back.`,
                    run: () => {
                      onMerge(speaker, chosen);
                    },
                  },
                ]
          }
          onAdd={onAdd}
          actions={
            speaker === null
              ? []
              : [
                  {
                    id: 'rename',
                    label: `Rename ${speaker.name}…`,
                    run: () => {
                      setName(speaker.name);
                      setOpen(false);
                      setRenaming(true);
                    },
                  },
                ]
          }
          onClose={close}
        />
      ) : null}
      {renaming ? (
        <PopoverLayer anchorRef={button} popRef={pop} align="start" gap={6}>
          <div
            ref={pop}
            class="popover popover--menu segm-menu"
            role="dialog"
            aria-label={`Rename ${speaker?.name ?? 'speaker'}`}
            onClick={(event) => {
              event.stopPropagation();
            }}
            onDblClick={(event) => {
              event.stopPropagation();
            }}
            onKeyDown={(event) => {
              if (undoKeyOf(event) === null) {
                event.stopPropagation();
              }
              if (event.key === 'Escape') {
                event.preventDefault();
                close(true);
              } else if (event.key === 'Tab') {
                // Floating on <body>: back to the speaker first, so Tab moves on from there.
                close(floating);
              }
            }}
          >
            <form
              class="segm-menu-form"
              onSubmit={(event) => {
                event.preventDefault();
                commitRename();
              }}
            >
              <label class="menu-label" for={`speaker-name-${segment.id}`}>
                Rename {speaker?.name ?? 'speaker'} everywhere
              </label>
              <input
                id={`speaker-name-${segment.id}`}
                class="field segm-menu-input"
                type="text"
                autocomplete="off"
                placeholder="Name"
                maxLength={100}
                value={name}
                onInput={(event) => {
                  setName(event.currentTarget.value);
                }}
              />
              <div class="segm-menu-actions">
                <button
                  class="btn g segm-menu-btn"
                  type="button"
                  onClick={() => {
                    setRenaming(false);
                    setOpen(true);
                  }}
                >
                  Back
                </button>
                <button class="btn p segm-menu-btn" type="submit" disabled={name.trim() === ''}>
                  Rename
                </button>
              </div>
            </form>
          </div>
        </PopoverLayer>
      ) : null}
    </span>
  );
}

function SegmentRow({
  segment,
  index,
  count,
  speaker,
  speakers,
  current,
  threshold,
  query,
  currentOccurrence,
  editing,
  caret,
  saving,
  highlights,
  handlers,
}: SegmentProps): JSX.Element {
  const lastEnter = useRef(0);
  const classes = ['segm', current ? 'now' : '', editing !== null ? 'segm--editing' : ''].filter((c) => c !== '').join(' ');
  return (
    <div
      class={classes}
      role="listitem"
      tabIndex={editing === null ? 0 : -1}
      aria-posinset={index + 1}
      aria-setsize={count}
      aria-current={current ? 'true' : undefined}
      data-index={index}
      data-segment-id={segment.id}
      onClick={() => {
        if (editing === null) {
          handlers.seek(segment);
        }
      }}
      onDblClick={(event) => {
        if (editing === null) {
          event.preventDefault();
          handlers.startEdit(segment);
        }
      }}
      onKeyDown={(event) => {
        if (event.target !== event.currentTarget) {
          return;
        }
        if (event.key === 'F2') {
          event.preventDefault();
          handlers.startEdit(segment);
        } else if (event.key === 'Enter') {
          event.preventDefault();
          // Enter plays the line; Enter again straight away edits it.
          const now = Date.now();
          if (now - lastEnter.current < 600) {
            lastEnter.current = 0;
            handlers.startEdit(segment);
          } else {
            lastEnter.current = now;
            handlers.seek(segment);
          }
        } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
          event.preventDefault();
          handlers.moveFocus(index, event.key === 'ArrowDown' ? 1 : -1);
        }
      }}
    >
      <span class="segm-side">
        {/* Speakers not identified (a dictation, or the stage has not run): the timecode alone. */}
        {speaker === null && speakers.length === 0 ? null : (
          <SpeakerMenu
            segment={segment}
            speaker={speaker}
            speakers={speakers}
            onAssign={(chosen) => {
              handlers.assign(segment, chosen);
            }}
            onMerge={(from, into) => {
              handlers.merge(from, into);
            }}
            onAdd={(name) => {
              handlers.addSpeaker(segment, name);
            }}
            onRename={(name) => {
              if (speaker !== null) {
                handlers.rename(speaker, name);
              }
            }}
          />
        )}
        <span class="mono segm-at">{segmentTimecode(segment.start)}</span>
        {segment.edited === null ? null : (
          <span class="segm-edited" title={`Original: ${segment.edited.original}`}>
            edited<span class="sr">. The original said: {segment.edited.original}</span>
          </span>
        )}
      </span>
      <span class="segm-body">
        {editing === null ? (
          <SegmentText
            segment={segment}
            threshold={threshold}
            query={query}
            currentOccurrence={currentOccurrence}
            onEdit={(at) => {
              handlers.startEdit(segment, at);
            }}
          />
        ) : (
          <SegmentEditor value={editing} caret={caret} saving={saving} onDraft={handlers.draft} onSave={handlers.save} onCancel={handlers.cancel} />
        )}
        {highlights.map((h) => (
          <SegmentNote
            key={h.id}
            highlight={h}
            onRename={(note) => {
              handlers.renameHighlight(h, note);
            }}
          />
        ))}
      </span>
    </div>
  );
}

/** Rows re-render only when their own props change, so the playhead moving does not redraw thousands. */
export const TranscriptSegmentRow = memo(SegmentRow);
