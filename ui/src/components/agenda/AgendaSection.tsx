// The Details sheet's Agenda section (DESIGN.md §14, renders/AgendaImport.dc.html): the drop zone,
// Paste text, the parsed preview with uncertain items and their reasons, the recording's agenda
// list, and the AI fallback card (disabled until M4).
import type { JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import type { RecordingDetails } from '../../bridge/types';
import { agendaSourceCaption, moveItem, PASTED_SOURCE } from '../../format/agenda';
import { openExternal, openSettings } from '../../state/actions';
import { useServices } from '../../state/context';
import { CloseIcon, DragHandleIcon, InfoIcon, UploadDocumentIcon } from '../icons';
import {
  blankItem,
  checkLimits,
  fromAgendaItems,
  toAgendaItems,
  tooLongMessage,
  tooManyMessage,
  uncertainNotice,
  withText,
  type EditableItem,
} from './agendaItems';
import type { AgendaImportController } from './useAgendaImport';

// ---------------------------------------------------------------------------------------------
// The item list: handle, mono index, inline input, remove; drag or arrow keys reorder
// ---------------------------------------------------------------------------------------------

interface AgendaListProps {
  items: EditableItem[];
  onChange: (items: EditableItem[]) => void;
  labelledBy: string;
  /** The last item was removed. */
  onEmpty?: () => void;
}

export function AgendaList({ items, onChange, labelledBy, onEmpty }: AgendaListProps): JSX.Element {
  const [dragIndex, setDragIndex] = useState<number | null>(null);
  const [overIndex, setOverIndex] = useState<number | null>(null);
  const [focusItem, setFocusItem] = useState<{ key: string; part: 'input' | 'handle' } | null>(null);
  const listRef = useRef<HTMLOListElement | null>(null);
  const limits = checkLimits(items);
  const notice = uncertainNotice(items);

  useEffect(() => {
    if (focusItem === null) {
      return;
    }
    const el = listRef.current?.querySelector<HTMLElement>(
      `[data-item="${CSS.escape(focusItem.key)}"] ${focusItem.part === 'input' ? 'input' : '.drag-handle'}`,
    );
    el?.focus();
    setFocusItem(null);
  }, [focusItem]);

  const nameOf = (item: EditableItem, index: number): string => (item.text === '' ? `item ${index + 1}` : item.text);

  return (
    <>
      <ol
        ref={listRef}
        class="agenda-items"
        aria-labelledby={labelledBy}
        onDragOver={(event) => {
          if (dragIndex !== null) {
            event.preventDefault();
          }
        }}
      >
        {items.map((item, index) => {
          const tooLong = limits.tooLong.has(item.key);
          const describedBy = [item.uncertain ? `why-${item.key}` : '', tooLong ? `long-${item.key}` : ''].filter((id) => id !== '').join(' ');
          return (
            <li
              key={item.key}
              data-item={item.key}
              class={[
                'item',
                'agenda-item',
                item.uncertain ? 'low' : '',
                item.level > 0 ? 'agenda-item--nested' : '',
                tooLong ? 'agenda-item--too-long' : '',
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
                if (dragIndex === null) {
                  return;
                }
                event.preventDefault();
                event.stopPropagation();
                if (dragIndex !== index) {
                  onChange(moveItem(items, dragIndex, index));
                }
                setDragIndex(null);
                setOverIndex(null);
              }}
            >
              <div class="agenda-row">
                <button
                  class="drag-handle"
                  type="button"
                  draggable
                  aria-label={`Move "${nameOf(item, index)}". Use the up and down arrow keys.`}
                  onDragStart={(event) => {
                    setDragIndex(index);
                    event.dataTransfer?.setData('text/x-memento-agenda', item.key);
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
                    if (to < 0 || to >= items.length) {
                      return;
                    }
                    onChange(moveItem(items, index, to));
                    setFocusItem({ key: item.key, part: 'handle' });
                  }}
                >
                  <DragHandleIcon size={14} />
                </button>
                <span class="mono agenda-index" aria-hidden="true">
                  {index + 1}
                </span>
                <label class="sr" for={`agenda-${item.key}`}>
                  Agenda item {index + 1}
                  {item.uncertain ? ', uncertain' : ''}
                </label>
                <input
                  id={`agenda-${item.key}`}
                  class="ii field"
                  type="text"
                  value={item.text}
                  placeholder="New item"
                  title={item.uncertainReason ?? undefined}
                  aria-invalid={tooLong ? true : undefined}
                  aria-describedby={describedBy === '' ? undefined : describedBy}
                  onInput={(event) => {
                    const text = event.currentTarget.value;
                    onChange(items.map((it) => (it.key === item.key ? withText(it, text) : it)));
                  }}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') {
                      event.preventDefault();
                      const added = blankItem();
                      const next = [...items];
                      next.splice(index + 1, 0, added);
                      onChange(next);
                      setFocusItem({ key: added.key, part: 'input' });
                    }
                  }}
                />
                <button
                  class="icon-btn agenda-remove"
                  type="button"
                  aria-label={`Remove "${nameOf(item, index)}"`}
                  onClick={() => {
                    const next = items.filter((it) => it.key !== item.key);
                    onChange(next);
                    if (next.length === 0) {
                      onEmpty?.();
                    } else {
                      const neighbour = next[Math.min(index, next.length - 1)];
                      if (neighbour !== undefined) {
                        setFocusItem({ key: neighbour.key, part: 'input' });
                      }
                    }
                  }}
                >
                  <CloseIcon size={13} />
                </button>
              </div>
              {item.uncertain ? (
                <span id={`why-${item.key}`} class="sr">
                  {item.uncertainReason ?? 'The parser was not sure about this item.'}
                </span>
              ) : null}
              {tooLong ? (
                <span id={`long-${item.key}`} class="agenda-limit" role="alert">
                  {tooLongMessage(index, item.text.trim().length)}
                </span>
              ) : null}
            </li>
          );
        })}
      </ol>
      {notice === null ? null : (
        <div class="agenda-notice">
          <InfoIcon size={16} class="agenda-notice-icon" />
          <span class="agenda-notice-text">
            <span>{notice.lead}</span>
            {notice.reasons.length === 0 ? null : (
              <ul class="agenda-reasons">
                {notice.reasons.map((reason) => (
                  <li key={reason}>{reason}</li>
                ))}
              </ul>
            )}
          </span>
        </div>
      )}
      {limits.overCount > 0 ? (
        <p class="agenda-limit agenda-limit--count" role="alert">
          {tooManyMessage(items.filter((i) => i.text.trim() !== '').length)}
        </p>
      ) : null}
      <button
        class="btn add-row"
        type="button"
        onClick={() => {
          const added = blankItem();
          onChange([...items, added]);
          setFocusItem({ key: added.key, part: 'input' });
        }}
      >
        + Add an item
      </button>
    </>
  );
}

// ---------------------------------------------------------------------------------------------
// Pieces of the section
// ---------------------------------------------------------------------------------------------

function DropZone({ controller }: { controller: AgendaImportController }): JSX.Element {
  const [hot, setHot] = useState(false);
  const reading = controller.reading;
  return (
    <div
      class={hot ? 'drop hot agenda-drop' : 'drop agenda-drop'}
      aria-busy={reading !== null}
      onDragEnter={(event) => {
        event.preventDefault();
        setHot(true);
      }}
      onDragOver={(event) => {
        event.preventDefault();
        if (event.dataTransfer !== null) {
          event.dataTransfer.dropEffect = 'copy';
        }
        setHot(true);
      }}
      onDragLeave={(event) => {
        const next = event.relatedTarget;
        if (!(next instanceof Node) || !event.currentTarget.contains(next)) {
          setHot(false);
        }
      }}
      onDrop={(event) => {
        event.preventDefault();
        setHot(false);
        const transfer = event.dataTransfer;
        if (transfer === null) {
          return;
        }
        const files = [...transfer.files];
        const text = transfer.getData('text/plain');
        if (files.length > 0) {
          void controller.drop(files);
        } else if (text.trim() !== '') {
          void controller.parseText(text);
        }
      }}
    >
      <UploadDocumentIcon size={28} class="drop-icon" />
      <span class="drop-title">Drop an agenda here</span>
      <span class="drop-sub">Word, PDF, Excel, CSV, Markdown, plain text, or a photo of a printed agenda</span>
      <div class="drop-actions">
        <button
          class="btn ghost drop-btn"
          type="button"
          disabled={reading !== null}
          onClick={() => {
            void controller.chooseFile();
          }}
        >
          Choose a file
        </button>
        <button
          class="btn ghost drop-btn drop-btn--quiet"
          type="button"
          disabled={reading !== null}
          onClick={() => {
            controller.setMode('paste');
          }}
        >
          Paste text
        </button>
      </div>
      <span class="drop-note" role="status">
        {reading === null ? 'Parsed on this PC. Nothing is uploaded.' : `Reading ${reading} on this PC…`}
      </span>
    </div>
  );
}

function PasteBox({ controller, onCancel }: { controller: AgendaImportController; onCancel: () => void }): JSX.Element {
  const [text, setText] = useState('');
  const ref = useRef<HTMLTextAreaElement | null>(null);
  useEffect(() => {
    ref.current?.focus();
  }, []);
  return (
    <div class="paste-box">
      <label class="sr" for="agenda-paste">
        Agenda text
      </label>
      <textarea
        id="agenda-paste"
        ref={ref}
        class="field paste-text"
        rows={6}
        placeholder={'1. Q2 recap\n2. Hiring plan\n3. Launch date'}
        value={text}
        onInput={(event) => {
          setText(event.currentTarget.value);
        }}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            event.preventDefault();
            event.stopPropagation();
            onCancel();
          }
        }}
      />
      <span class="sheet-note">One item per line. Numbers and bullets are removed; nothing leaves this PC.</span>
      <div class="paste-actions">
        <button class="btn g" type="button" onClick={onCancel}>
          Cancel
        </button>
        <button
          class="btn p"
          type="button"
          disabled={text.trim() === '' || controller.reading !== null}
          onClick={() => {
            void controller.parseText(text);
          }}
        >
          Add items
        </button>
      </div>
    </div>
  );
}

function AiFallbackCard(): JSX.Element {
  const services = useServices();
  const enabled = services.store.settings.value?.ai.enabled === true;
  return (
    <div class="ai-card">
      <div class="ai-card-text">
        <span class="ai-card-title">Not quite right? Extract with AI</span>
        {enabled ? (
          <span class="ai-card-sub" id="ai-off-note">
            Sends only the agenda file to Claude. You will be asked first. Available in a later version.
          </span>
        ) : (
          <span class="ai-card-sub" id="ai-off-note">
            External AI is off. Turn it on in{' '}
            <button
              class="btn link-btn ai-card-link"
              type="button"
              onClick={() => {
                openSettings(services, 'ai-privacy');
              }}
            >
              Settings › AI and privacy
            </button>
            .
          </span>
        )}
      </div>
      <button class="btn ghost ai-card-btn" type="button" disabled aria-describedby="ai-off-note">
        Extract
      </button>
    </div>
  );
}

function ErrorCard({ controller }: { controller: AgendaImportController }): JSX.Element | null {
  const services = useServices();
  const error = controller.error;
  if (error === null) {
    return null;
  }
  return (
    <div class="agenda-error" role="alert">
      <span class="agenda-error-text">{error.message}</span>
      <div class="agenda-error-actions">
        {error.settingsUrl === null ? null : (
          <button
            class="btn g small-btn"
            type="button"
            onClick={() => {
              openExternal(services, error.settingsUrl ?? '', 'Windows language settings');
            }}
          >
            Open Windows settings
          </button>
        )}
        <button class="btn toast-quiet" type="button" onClick={controller.clearError}>
          Dismiss
        </button>
      </div>
    </div>
  );
}

function captionOf(source: string, kind: 'preview' | 'applied', ocrEngine: string | null, parsedLocally = true): string {
  if (ocrEngine !== null) {
    return `From ${source} · read with ${ocrEngine} on this PC`;
  }
  if (kind === 'preview' && source === PASTED_SOURCE) {
    return 'Pasted text · parsed on this PC';
  }
  return agendaSourceCaption(source, parsedLocally);
}

// ---------------------------------------------------------------------------------------------
// The section
// ---------------------------------------------------------------------------------------------

interface AgendaSectionProps {
  controller: AgendaImportController;
  details: RecordingDetails;
  update: (patch: Partial<RecordingDetails>) => void;
}

export function AgendaSection({ controller, details, update }: AgendaSectionProps): JSX.Element {
  const { agenda } = details;
  const { mode, pending } = controller;
  const applied = fromAgendaItems(agenda.items);
  const showApplied = mode === 'list' && agenda.items.length > 0;
  const [warningsOpen, setWarningsOpen] = useState(false);

  const setApplied = (items: EditableItem[]): void => {
    update({ agenda: { ...agenda, items: toAgendaItems(items) } });
  };

  const caption =
    mode === 'preview' && pending !== null
      ? captionOf(pending.preview.source, 'preview', pending.preview.ocrEngine)
      : showApplied
        ? captionOf(agenda.source ?? PASTED_SOURCE, 'applied', null, agenda.parsedLocally)
        : null;

  const warnings = pending?.preview.warnings ?? [];
  const shownWarnings = warningsOpen ? warnings : warnings.slice(0, 2);

  return (
    <div class="sheet-section">
      <div class="sheet-section-head">
        <span class="lbl" id="agenda-label">
          Agenda
        </span>
        {caption !== null ? (
          <span class="sheet-caption">
            {caption} ·{' '}
            <button class="btn link-btn" type="button" onClick={controller.replace}>
              Replace
            </button>
          </span>
        ) : mode === 'drop' && agenda.items.length > 0 ? (
          <span class="sheet-caption">
            <button
              class="btn link-btn"
              type="button"
              onClick={() => {
                controller.setMode('list');
              }}
            >
              Keep the current agenda
            </button>
          </span>
        ) : null}
      </div>

      {mode === 'paste' ? (
        <PasteBox
          controller={controller}
          onCancel={() => {
            controller.setMode(agenda.items.length > 0 ? 'list' : 'drop');
          }}
        />
      ) : mode === 'preview' && pending !== null ? (
        <>
          {pending.preview.title === null ? null : <span class="agenda-title">{pending.preview.title}</span>}
          {warnings.length === 0 ? null : (
            <ul class="agenda-warnings" aria-label="Notes from the parser">
              {shownWarnings.map((w) => (
                <li key={`${w.code}-${w.message}`}>{w.message}</li>
              ))}
              {warnings.length > 2 ? (
                <li>
                  <button
                    class="btn link-btn"
                    type="button"
                    aria-expanded={warningsOpen}
                    onClick={() => {
                      setWarningsOpen(!warningsOpen);
                    }}
                  >
                    {warningsOpen ? 'Show fewer' : `${warnings.length - 2} more`}
                  </button>
                </li>
              ) : null}
            </ul>
          )}
          <AgendaList items={pending.items} onChange={controller.setPendingItems} labelledBy="agenda-label" />
          <div class="agenda-apply">
            <span class="agenda-apply-note">Not saved yet. Done applies it too.</span>
            <div class="agenda-apply-actions">
              <button class="btn g small-btn" type="button" onClick={controller.discard}>
                Discard
              </button>
              <button
                class="btn p small-btn"
                type="button"
                disabled={!controller.limitsOk}
                onClick={() => {
                  void controller.apply();
                }}
              >
                Apply agenda
              </button>
            </div>
          </div>
          <AiFallbackCard />
        </>
      ) : showApplied ? (
        <>
          <AgendaList
            items={applied}
            onChange={setApplied}
            labelledBy="agenda-label"
            onEmpty={() => {
              controller.setMode('drop');
            }}
          />
          <AiFallbackCard />
        </>
      ) : (
        <DropZone controller={controller} />
      )}
      <ErrorCard controller={controller} />
    </div>
  );
}
