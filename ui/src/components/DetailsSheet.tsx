// Details and agenda (DESIGN.md §14), transcribed from renders/AgendaImport.dc.html: a 480 px side
// sheet over the Recording session (and Review's Edit details). Every change goes through the
// DetailsSaver, which applies it at once and saves it with project.updateDetails after a pause.
import type { JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import type { RecordingDetails, RecordingType } from '../bridge/types';
import { CHOOSABLE_TYPES, isBuiltInType, typeName } from '../format/recording';
import type { DetailsSaver } from '../state/detailsSaver';
import { saveStatusText } from '../state/detailsSaver';
import { AgendaSection } from './agenda/AgendaSection';
import { useAgendaImport, type AgendaMode } from './agenda/useAgendaImport';
import { AttachmentsSection } from './attachments/AttachmentsSection';
import { CloseIcon } from './icons';
import { SelectMenu } from './Menus';
import { SideSheet } from './Overlay';

interface DetailsSheetProps {
  saver: DetailsSaver;
  onClose: () => void;
  /** M3: Review › Details › Replace opens the sheet at the drop zone. */
  agendaMode?: AgendaMode | null;
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
export function TagEditor({
  tags,
  onChange,
  noun = 'tag',
  locked = [],
}: {
  tags: string[];
  onChange: (tags: string[]) => void;
  noun?: string;
  /** Shown first as plain pills without a remove button (topics Memento found locally). */
  locked?: readonly string[];
}): JSX.Element {
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
    if (tag !== '' && !tags.includes(tag) && !locked.includes(tag)) {
      onChange([...tags, tag]);
    }
    setDraft('');
    setAdding(false);
  };
  return (
    <div class="tag-row">
      {locked.map((tag) => (
        <span key={`locked-${tag}`} class="pill done">
          {tag}
        </span>
      ))}
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
          aria-label={`Add a ${noun}`}
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
// The sheet
// ---------------------------------------------------------------------------------------------

export function DetailsSheet({ saver, onClose, agendaMode = null }: DetailsSheetProps): JSX.Element {
  const details = saver.details.value;
  const agenda = useAgendaImport(saver, agendaMode);
  const update = (patch: Partial<RecordingDetails>): void => {
    saver.update(patch);
  };
  const close = (): void => {
    // A parsed agenda not applied yet is applied now; if it cannot be (limits, a refusal), the
    // sheet stays open with the reason shown.
    void agenda.finish().then((ok) => {
      if (!ok) {
        document.querySelector<HTMLElement>('.agenda-limit, .agenda-error')?.scrollIntoView({ block: 'nearest' });
        return;
      }
      void saver.flush();
      onClose();
    });
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
        <AgendaSection controller={agenda} details={details} update={update} />
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
        <AttachmentsSection recordingId={saver.recordingId.value} variant="sheet" />
      </div>
    </SideSheet>
  );
}
