// One transcript segment (DESIGN.md §5.12, renders/Review.dc.html): speaker with its dot over the mono
// timecode, the text with low-confidence words and search matches marked, highlight notes under it,
// in-place editing, and the speaker menu (reassign, New speaker…, Rename…).
import type { JSX } from 'preact';
import { memo } from 'preact/compat';
import { useEffect, useLayoutEffect, useRef, useState } from 'preact/hooks';
import type { Highlight, Speaker, TranscriptSegment } from '../../bridge/types';
import { CheckIcon, NotesIcon } from '../../components/icons';
import { moveFocus } from '../../components/keyboard';
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
  startEdit: (segment: TranscriptSegment) => void;
  draft: (text: string) => void;
  save: () => void;
  cancel: () => void;
  /** Reassign to an existing speaker, or create one with `newName`. */
  assign: (segment: TranscriptSegment, speakerId: string | null, newName?: string) => void;
  rename: (speaker: Speaker, name: string) => void;
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
  saving: boolean;
  highlights: readonly Highlight[];
  handlers: SegmentHandlers;
}

const AUTHORS: Record<Highlight['origin'], string> = { user: 'You', local: 'Suggested', ai: 'AI' };

function SegmentText({ segment, threshold, query, currentOccurrence }: Pick<SegmentProps, 'segment' | 'threshold' | 'query' | 'currentOccurrence'>): JSX.Element {
  const low = lowConfidenceRanges(segment.text, segment.words, threshold);
  const matches = queryRanges(segment.text, query);
  const runs = textRuns(segment.text, low, matches, currentOccurrence);
  return (
    <span class="segm-text">
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

/** The textarea that replaces the text while editing: as tall as its content. */
function SegmentEditor({ value, saving, onDraft, onSave, onCancel }: { value: string; saving: boolean; onDraft: (text: string) => void; onSave: () => void; onCancel: () => void }): JSX.Element {
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
      el.focus();
      el.setSelectionRange(el.value.length, el.value.length);
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
        event.stopPropagation();
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

type MenuMode = { kind: 'list' } | { kind: 'new' } | { kind: 'rename' };

/** The speaker name's menu: reassign to any speaker, New speaker…, Rename…. */
function SpeakerMenu({
  segment,
  speaker,
  speakers,
  onAssign,
  onRename,
}: {
  segment: TranscriptSegment;
  speaker: Speaker | null;
  speakers: readonly Speaker[];
  onAssign: (speakerId: string | null, newName?: string) => void;
  onRename: (name: string) => void;
}): JSX.Element {
  const [open, setOpen] = useState(false);
  const [mode, setMode] = useState<MenuMode>({ kind: 'list' });
  const [name, setName] = useState('');
  const root = useRef<HTMLSpanElement | null>(null);
  const button = useRef<HTMLButtonElement | null>(null);
  const pop = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (!open) {
      return undefined;
    }
    if (mode.kind === 'list') {
      (pop.current?.querySelector<HTMLElement>('[aria-checked="true"]') ?? pop.current?.querySelector<HTMLElement>('[role^="menuitem"]'))?.focus();
    } else {
      pop.current?.querySelector<HTMLInputElement>('input')?.select();
    }
    const away = (event: PointerEvent): void => {
      if (event.target instanceof Node && root.current?.contains(event.target) !== true) {
        setOpen(false);
      }
    };
    document.addEventListener('pointerdown', away);
    return () => {
      document.removeEventListener('pointerdown', away);
    };
  }, [open, mode]);

  const close = (focusButton: boolean): void => {
    setOpen(false);
    setMode({ kind: 'list' });
    if (focusButton) {
      button.current?.focus();
    }
  };

  const label = speaker?.name ?? 'Unknown speaker';
  const uncertain = isSpeakerUncertain(segment);
  const commitName = (): void => {
    const trimmed = name.trim();
    if (trimmed === '') {
      setMode({ kind: 'list' });
      return;
    }
    if (mode.kind === 'new') {
      onAssign(null, trimmed);
    } else if (speaker !== null && trimmed !== speaker.name) {
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
        aria-haspopup="menu"
        aria-expanded={open}
        title={uncertain ? 'Speaker uncertain' : undefined}
        onClick={(event) => {
          event.stopPropagation();
          setMode({ kind: 'list' });
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
        <div
          ref={pop}
          class="popover popover--menu segm-menu"
          role="menu"
          aria-label={`Speaker for the line at ${formatDuration(segment.start * 1000)}`}
          onClick={(event) => {
            event.stopPropagation();
          }}
          onDblClick={(event) => {
            event.stopPropagation();
          }}
          onKeyDown={(event) => {
            event.stopPropagation();
            if (event.key === 'Escape') {
              event.preventDefault();
              if (mode.kind === 'list') {
                close(true);
              } else {
                setMode({ kind: 'list' });
              }
              return;
            }
            if (event.key === 'Tab') {
              close(false);
              return;
            }
            if (mode.kind === 'list' && pop.current !== null) {
              moveFocus(event, pop.current, '[role^="menuitem"]', 'vertical');
            }
          }}
        >
          {mode.kind === 'list' ? (
            <>
              <span class="menu-label">This line is said by</span>
              {speakers.map((s) => (
                <button
                  key={s.id}
                  class="item menu-item"
                  type="button"
                  role="menuitemradio"
                  aria-checked={s.id === segment.speaker}
                  tabIndex={-1}
                  onClick={() => {
                    close(true);
                    if (s.id !== segment.speaker) {
                      onAssign(s.id);
                    }
                  }}
                >
                  <span class="menu-check" aria-hidden="true">
                    {s.id === segment.speaker ? <CheckIcon size={12} /> : null}
                  </span>
                  <span class="segm-dot" aria-hidden="true" style={{ background: speakerColourVar(s.color) }} />
                  {s.name}
                </button>
              ))}
              <span class="menu-sep" role="separator" />
              <button
                class="item menu-item"
                type="button"
                role="menuitem"
                tabIndex={-1}
                onClick={() => {
                  setName('');
                  setMode({ kind: 'new' });
                }}
              >
                <span class="menu-check" aria-hidden="true" />
                New speaker…
              </button>
              {speaker === null ? null : (
                <button
                  class="item menu-item"
                  type="button"
                  role="menuitem"
                  tabIndex={-1}
                  onClick={() => {
                    setName(speaker.name);
                    setMode({ kind: 'rename' });
                  }}
                >
                  <span class="menu-check" aria-hidden="true" />
                  Rename {speaker.name}…
                </button>
              )}
            </>
          ) : (
            <form
              class="segm-menu-form"
              onSubmit={(event) => {
                event.preventDefault();
                commitName();
              }}
            >
              <label class="menu-label" for={`speaker-name-${segment.id}`}>
                {mode.kind === 'new' ? 'New speaker for this line' : `Rename ${speaker?.name ?? 'speaker'} everywhere`}
              </label>
              <input
                id={`speaker-name-${segment.id}`}
                class="field segm-menu-input"
                type="text"
                autocomplete="off"
                placeholder="Name"
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
                    setMode({ kind: 'list' });
                  }}
                >
                  Back
                </button>
                <button class="btn p segm-menu-btn" type="submit" disabled={name.trim() === ''}>
                  {mode.kind === 'new' ? 'Add' : 'Rename'}
                </button>
              </div>
            </form>
          )}
        </div>
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
            onAssign={(speakerId, newName) => {
              handlers.assign(segment, speakerId, newName);
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
          <SegmentText segment={segment} threshold={threshold} query={query} currentOccurrence={currentOccurrence} />
        ) : (
          <SegmentEditor value={editing} saving={saving} onDraft={handlers.draft} onSave={handlers.save} onCancel={handlers.cancel} />
        )}
        {highlights.map((h) => (
          <span key={h.id} class="segm-note">
            <NotesIcon size={14} class="segm-note-icon" />
            <span>
              <span class="segm-note-by">{AUTHORS[h.origin]}</span> {h.note === '' ? `Highlight at ${formatDuration(h.atMs)}` : h.note}
            </span>
          </span>
        ))}
      </span>
    </div>
  );
}

/** Rows re-render only when their own props change, so the playhead moving does not redraw thousands. */
export const TranscriptSegmentRow = memo(SegmentRow);
