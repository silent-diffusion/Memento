// Details and agenda (DESIGN.md §14), transcribed from renders/AgendaImport.dc.html: a 480 px side
// sheet over the Recording session (and Review's Edit details). Every change goes through the
// DetailsSaver, which applies it at once and saves it with project.updateDetails after a pause.
import type { JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import type { AgendaItem, RecordingDetails, RecordingType } from '../bridge/types';
import { agendaSourceCaption, moveItem, newAgendaItem, splitPastedAgenda } from '../format/agenda';
import { CHOOSABLE_TYPES, isBuiltInType, typeName } from '../format/recording';
import type { DetailsSaver } from '../state/detailsSaver';
import { saveStatusText } from '../state/detailsSaver';
import { CloseIcon, DragHandleIcon, InfoIcon, UploadDocumentIcon } from './icons';
import { SelectMenu } from './Menus';
import { SideSheet } from './Overlay';

interface DetailsSheetProps {
  saver: DetailsSaver;
  onClose: () => void;
}

export function typeOptions(current: RecordingType): { value: string; label: string }[] {
  const options: { value: string; label: string }[] = CHOOSABLE_TYPES.map((type) => ({ value: type, label: typeName(type) }));
  if (!isBuiltInType(current)) {
    options.push({ value: current, label: current });
  }
  return options;
}

// ---------------------------------------------------------------------------------------------
// Name pills (participants, tags)
// ---------------------------------------------------------------------------------------------

interface PillWellProps {
  id: string;
  label: string;
  values: string[];
  placeholder: string;
  onChange: (values: string[]) => void;
}

/** A pressed-in well of name pills with an inline input: Enter or comma adds, Backspace on empty removes the last. */
function PillWell({ id, label, values, placeholder, onChange }: PillWellProps): JSX.Element {
  const [draft, setDraft] = useState('');
  const addNames = (names: string[]): void => {
    const next = [...values];
    for (const raw of names) {
      const name = raw.trim();
      if (name !== '' && !next.some((v) => v.toLocaleLowerCase() === name.toLocaleLowerCase())) {
        next.push(name);
      }
    }
    if (next.length !== values.length) {
      onChange(next);
    }
  };
  const add = (): void => {
    addNames([draft]);
    setDraft('');
  };
  return (
    <div class="sheet-well">
      {values.map((value) => (
        <span key={value} class="pill done sheet-pill">
          {value}
          <button
            class="sheet-pill-remove"
            type="button"
            aria-label={`Remove ${value}`}
            onClick={() => {
              onChange(values.filter((v) => v !== value));
            }}
          >
            <CloseIcon size={10} />
          </button>
        </span>
      ))}
      <label class="sr" for={id}>
        {label}
      </label>
      <input
        id={id}
        class="sheet-well-input"
        type="text"
        placeholder={placeholder}
        value={draft}
        autocomplete="off"
        onInput={(event) => {
          // A comma ends a name, also when several arrive at once (pasted "Sam, Priya, ").
          const parts = event.currentTarget.value.split(',');
          const rest = parts.pop() ?? '';
          if (parts.length > 0) {
            addNames(parts);
            event.currentTarget.value = rest;
          }
          setDraft(rest);
        }}
        onKeyDown={(event) => {
          if (event.key === 'Enter') {
            event.preventDefault();
            add();
          } else if (event.key === 'Backspace' && draft === '' && values.length > 0) {
            onChange(values.slice(0, -1));
          }
        }}
        onBlur={add}
      />
    </div>
  );
}

/** Tags: raised pills plus a dashed "+ Add" that turns into an input. */
export function TagEditor({ tags, onChange, noun = 'tag' }: { tags: string[]; onChange: (tags: string[]) => void; noun?: string }): JSX.Element {
  const [adding, setAdding] = useState(false);
  const [draft, setDraft] = useState('');
  const input = useRef<HTMLInputElement | null>(null);
  useEffect(() => {
    if (adding) {
      input.current?.focus();
    }
  }, [adding]);
  const commit = (): void => {
    const tag = draft.trim();
    if (tag !== '' && !tags.includes(tag)) {
      onChange([...tags, tag]);
    }
    setDraft('');
    setAdding(false);
  };
  return (
    <div class="tag-row">
      {tags.map((tag) => (
        <span key={tag} class="pill done sheet-pill">
          {tag}
          <button
            class="sheet-pill-remove"
            type="button"
            aria-label={`Remove ${noun} ${tag}`}
            onClick={() => {
              onChange(tags.filter((t) => t !== tag));
            }}
          >
            <CloseIcon size={10} />
          </button>
        </span>
      ))}
      {adding ? (
        <input
          ref={input}
          class="field tag-input"
          type="text"
          aria-label={`New ${noun}`}
          placeholder="Name it"
          value={draft}
          onInput={(event) => {
            setDraft(event.currentTarget.value);
          }}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              event.preventDefault();
              commit();
            } else if (event.key === 'Escape') {
              event.preventDefault();
              event.stopPropagation();
              setDraft('');
              setAdding(false);
            }
          }}
          onBlur={commit}
        />
      ) : (
        <button
          class="btn add-pill"
          type="button"
          onClick={() => {
            setAdding(true);
          }}
        >
          + Add
        </button>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------------------------
// Agenda
// ---------------------------------------------------------------------------------------------

interface AgendaEditorProps {
  details: RecordingDetails;
  update: (patch: Partial<RecordingDetails>) => void;
}

function AgendaEditor({ details, update }: AgendaEditorProps): JSX.Element {
  const { agenda } = details;
  const [mode, setMode] = useState<'list' | 'drop' | 'paste'>(agenda.items.length > 0 ? 'list' : 'drop');
  const [pasted, setPasted] = useState('');
  const [hot, setHot] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [dragIndex, setDragIndex] = useState<number | null>(null);
  const [overIndex, setOverIndex] = useState<number | null>(null);
  const [focusItem, setFocusItem] = useState<{ id: string; part: 'input' | 'handle' } | null>(null);
  const listRef = useRef<HTMLOListElement | null>(null);
  const pasteRef = useRef<HTMLTextAreaElement | null>(null);

  const setItems = (items: AgendaItem[], source: string | null = agenda.source): void => {
    update({ agenda: { ...agenda, source, items } });
  };

  useEffect(() => {
    if (mode === 'paste') {
      pasteRef.current?.focus();
    }
  }, [mode]);

  useEffect(() => {
    if (focusItem === null) {
      return;
    }
    const el = listRef.current?.querySelector<HTMLElement>(
      `[data-item="${CSS.escape(focusItem.id)}"] ${focusItem.part === 'input' ? 'input' : '.drag-handle'}`,
    );
    el?.focus();
    setFocusItem(null);
  }, [focusItem]);

  const takeText = (text: string): boolean => {
    const lines = splitPastedAgenda(text);
    if (lines.length === 0) {
      setNotice('No agenda items were found in that text. Put one item on each line.');
      return false;
    }
    // Pasted text replaces the agenda; it is parsed here, on this PC, and nothing is marked uncertain.
    update({ agenda: { source: null, parsedLocally: true, items: lines.map(newAgendaItem) } });
    setNotice(null);
    setPasted('');
    setMode('list');
    return true;
  };

  const showList = mode === 'list' && agenda.items.length > 0;
  const hasUncertain = agenda.items.some((i) => i.uncertain);

  return (
    <div class="sheet-section">
      <div class="sheet-section-head">
        <span class="lbl" id="agenda-label">
          Agenda
        </span>
        {showList ? (
          <span class="sheet-caption">
            {agendaSourceCaption(agenda.source, agenda.parsedLocally)} ·{' '}
            <button
              class="btn link-btn"
              type="button"
              onClick={() => {
                setMode('drop');
              }}
            >
              Replace
            </button>
          </span>
        ) : null}
      </div>

      {mode === 'paste' ? (
        <div class="paste-box">
          <label class="sr" for="agenda-paste">
            Agenda text
          </label>
          <textarea
            id="agenda-paste"
            ref={pasteRef}
            class="field paste-text"
            rows={6}
            placeholder={'1. Q2 recap\n2. Hiring plan\n3. Launch date'}
            value={pasted}
            onInput={(event) => {
              setPasted(event.currentTarget.value);
            }}
            onKeyDown={(event) => {
              if (event.key === 'Escape') {
                event.preventDefault();
                event.stopPropagation();
                setMode(agenda.items.length > 0 ? 'list' : 'drop');
              }
            }}
          />
          <span class="sheet-note">One item per line. Numbers and bullets are removed; nothing leaves this PC.</span>
          <div class="paste-actions">
            <button
              class="btn g"
              type="button"
              onClick={() => {
                setNotice(null);
                setMode(agenda.items.length > 0 ? 'list' : 'drop');
              }}
            >
              Cancel
            </button>
            <button
              class="btn p"
              type="button"
              disabled={pasted.trim() === ''}
              onClick={() => {
                takeText(pasted);
              }}
            >
              Add items
            </button>
          </div>
        </div>
      ) : !showList ? (
        <div
          class={hot ? 'drop hot agenda-drop' : 'drop agenda-drop'}
          onDragOver={(event) => {
            event.preventDefault();
            setHot(true);
          }}
          onDragLeave={() => {
            setHot(false);
          }}
          onDrop={(event) => {
            event.preventDefault();
            setHot(false);
            const transfer = event.dataTransfer;
            const text = transfer?.getData('text/plain') ?? '';
            if (transfer !== null && transfer.files.length > 0) {
              setNotice('File import arrives in a later version. Paste the agenda as text for now.');
            } else if (text !== '') {
              takeText(text);
            }
          }}
        >
          <UploadDocumentIcon size={28} class="drop-icon" />
          <span class="drop-title">Drop an agenda here</span>
          <span class="drop-sub">Word, PDF, Excel, CSV, Markdown, plain text, or a photo of a printed agenda</span>
          <div class="drop-actions">
            <button class="btn ghost drop-btn" type="button" disabled aria-describedby="agenda-file-note">
              Choose a file
            </button>
            <button
              class="btn ghost drop-btn drop-btn--quiet"
              type="button"
              onClick={() => {
                setNotice(null);
                setMode('paste');
              }}
            >
              Paste text
            </button>
          </div>
          <span class="drop-note" id="agenda-file-note">
            File import arrives in a later version
          </span>
          <span class="drop-note">Parsed on this PC. Nothing is uploaded.</span>
        </div>
      ) : (
        <>
          <ol
            ref={listRef}
            class="agenda-items"
            aria-labelledby="agenda-label"
            onDragOver={(event) => {
              if (dragIndex !== null) {
                event.preventDefault();
              }
            }}
          >
            {agenda.items.map((item, index) => (
              <li
                key={item.id}
                data-item={item.id}
                class={[
                  'item',
                  'agenda-item',
                  item.uncertain ? 'low' : '',
                  dragIndex === index ? 'agenda-item--dragging' : '',
                  overIndex === index && dragIndex !== null && dragIndex !== index ? 'agenda-item--over' : '',
                ]
                  .filter((c) => c !== '')
                  .join(' ')}
                onDragOver={(event) => {
                  if (dragIndex !== null) {
                    event.preventDefault();
                    setOverIndex(index);
                  }
                }}
                onDrop={(event) => {
                  event.preventDefault();
                  if (dragIndex !== null && dragIndex !== index) {
                    setItems(moveItem(agenda.items, dragIndex, index));
                  }
                  setDragIndex(null);
                  setOverIndex(null);
                }}
              >
                <button
                  class="drag-handle"
                  type="button"
                  draggable
                  aria-label={`Move "${item.text === '' ? `item ${index + 1}` : item.text}". Use the up and down arrow keys.`}
                  onDragStart={(event) => {
                    setDragIndex(index);
                    event.dataTransfer?.setData('text/x-memento-agenda', item.id);
                    if (event.dataTransfer !== null) {
                      event.dataTransfer.effectAllowed = 'move';
                    }
                  }}
                  onDragEnd={() => {
                    setDragIndex(null);
                    setOverIndex(null);
                  }}
                  onKeyDown={(event) => {
                    const step = event.key === 'ArrowUp' ? -1 : event.key === 'ArrowDown' ? 1 : 0;
                    if (step === 0) {
                      return;
                    }
                    event.preventDefault();
                    const to = index + step;
                    if (to < 0 || to >= agenda.items.length) {
                      return;
                    }
                    setItems(moveItem(agenda.items, index, to));
                    setFocusItem({ id: item.id, part: 'handle' });
                  }}
                >
                  <DragHandleIcon size={14} />
                </button>
                <span class="mono agenda-index" aria-hidden="true">
                  {index + 1}
                </span>
                <label class="sr" for={`agenda-${item.id}`}>
                  Agenda item {index + 1}
                </label>
                <input
                  id={`agenda-${item.id}`}
                  class="ii field"
                  type="text"
                  value={item.text}
                  placeholder="New item"
                  title={item.uncertainReason ?? undefined}
                  onInput={(event) => {
                    const text = event.currentTarget.value;
                    setItems(agenda.items.map((it) => (it.id === item.id ? { ...it, text, uncertain: false, uncertainReason: null } : it)));
                  }}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') {
                      event.preventDefault();
                      const added = newAgendaItem('');
                      const next = [...agenda.items];
                      next.splice(index + 1, 0, added);
                      setItems(next);
                      setFocusItem({ id: added.id, part: 'input' });
                    }
                  }}
                />
                <button
                  class="icon-btn agenda-remove"
                  type="button"
                  aria-label={`Remove "${item.text === '' ? `item ${index + 1}` : item.text}"`}
                  onClick={() => {
                    const next = agenda.items.filter((it) => it.id !== item.id);
                    setItems(next);
                    if (next.length === 0) {
                      setMode('drop');
                    }
                  }}
                >
                  <CloseIcon size={13} />
                </button>
              </li>
            ))}
          </ol>
          {hasUncertain ? (
            <div class="agenda-notice">
              <InfoIcon size={16} class="agenda-notice-icon" />
              <span>
                {agenda.items.filter((i) => i.uncertain).length === 1 ? 'One item is' : 'Some items are'} uncertain (dotted).{' '}
                {agenda.items.find((i) => i.uncertain)?.uncertainReason ?? 'The parser was not sure where it ends.'} Fix it here, or try the AI
                option below.
              </span>
            </div>
          ) : null}
          <button
            class="btn add-row"
            type="button"
            onClick={() => {
              const added = newAgendaItem('');
              setItems([...agenda.items, added]);
              setFocusItem({ id: added.id, part: 'input' });
            }}
          >
            + Add an item
          </button>
          <div class="ai-card">
            <div class="ai-card-text">
              <span class="ai-card-title">Not quite right? Extract with AI</span>
              <span class="ai-card-sub" id="ai-off-note">
                External AI is off. Turn it on in Settings › AI and privacy.
              </span>
            </div>
            <button class="btn ghost ai-card-btn" type="button" disabled aria-describedby="ai-off-note">
              Extract
            </button>
          </div>
        </>
      )}
      {notice === null ? null : (
        <p class="sheet-notice" role="status">
          {notice}
        </p>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------------------------
// The sheet
// ---------------------------------------------------------------------------------------------

export function DetailsSheet({ saver, onClose }: DetailsSheetProps): JSX.Element {
  const details = saver.details.value;
  const update = (patch: Partial<RecordingDetails>): void => {
    saver.update(patch);
  };
  const close = (): void => {
    void saver.flush();
    onClose();
  };
  const status = saver.status.value;
  return (
    <SideSheet
      titleId="details-sheet-title"
      title="Details and agenda"
      onClose={close}
      footer={
        <>
          <span class={status === 'error' ? 'sheet-save sheet-save--error' : 'sheet-save'} role="status" aria-live="polite">
            {saveStatusText(status, saver.error.value)}
          </span>
          <button class="btn p sheet-done" type="button" onClick={close}>
            Done
          </button>
        </>
      }
    >
      <div class="sheet-content">
        <div class="sheet-grid">
          <label class="sheet-field sheet-field--wide">
            <span class="lbl">Title</span>
            <input
              class="field sheet-input"
              type="text"
              value={details.title}
              data-autofocus
              onInput={(event) => {
                update({ title: event.currentTarget.value });
              }}
            />
          </label>
          <div class="sheet-field">
            <span class="lbl" id="sheet-type-label">
              Type
            </span>
            <SelectMenu
              label="Type"
              value={details.type}
              options={typeOptions(details.type)}
              onChange={(type) => {
                update({ type });
              }}
            />
          </div>
          <label class="sheet-field">
            <span class="lbl">Platform</span>
            <input
              class="field sheet-input"
              type="text"
              value={details.platform}
              placeholder="Zoom, Teams, in person…"
              onInput={(event) => {
                update({ platform: event.currentTarget.value });
              }}
            />
          </label>
          <div class="sheet-field sheet-field--wide">
            <span class="lbl" id="participants-label">
              Participants
            </span>
            <PillWell
              id="add-person"
              label="Add a participant"
              values={details.participants}
              placeholder="Add a name"
              onChange={(participants) => {
                update({ participants });
              }}
            />
          </div>
          <label class="sheet-field sheet-field--wide">
            <span class="lbl">Purpose</span>
            <textarea
              class="field sheet-input sheet-textarea"
              rows={2}
              value={details.purpose}
              placeholder="What this recording is for"
              onInput={(event) => {
                update({ purpose: event.currentTarget.value });
              }}
            />
          </label>
        </div>

        <div class="rec-divider" />
        <AgendaEditor details={details} update={update} />
        <div class="rec-divider" />

        <div class="sheet-section sheet-section--tags">
          <span class="lbl">Tags</span>
          <TagEditor
            tags={details.tags}
            onChange={(tags) => {
              update({ tags });
            }}
          />
        </div>
      </div>
    </SideSheet>
  );
}
