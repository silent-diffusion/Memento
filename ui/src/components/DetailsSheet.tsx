// Details and agenda (DESIGN.md §14), transcribed from renders/AgendaImport.dc.html: a 480 px side
// sheet over the Recording session (and Review's Edit details). Every change goes through the
// DetailsSaver, which applies it at once and saves it with project.updateDetails after a pause.
// Participants can be filled from the speakers (Add from speakers), and Who spoke sets how many
// people spoke and their names for this recording.
import type { JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import type { RecordingDetails, RecordingType } from '../bridge/types';
import { CHOOSABLE_TYPES, isBuiltInType, typeName } from '../format/recording';
import { participantsFromSpeakers } from '../format/speakerOrder';
import { useServices } from '../state/context';
import type { DetailsSaver } from '../state/detailsSaver';
import { saveStatusText } from '../state/detailsSaver';
import type { UndoEntry } from '../state/undo';
import { AgendaSection } from './agenda/AgendaSection';
import { useAgendaImport, type AgendaMode } from './agenda/useAgendaImport';
import { AttachmentsSection } from './attachments/AttachmentsSection';
import { CloseIcon } from './icons';
import { SelectMenu } from './Menus';
import { NameWell } from './NameWell';
import { SideSheet } from './Overlay';
import { WhoSpokeEditor } from './WhoSpoke';

interface DetailsSheetProps {
  saver: DetailsSaver;
  onClose: () => void;
  /** M3: Review › Details › Replace opens the sheet at the drop zone. */
  agendaMode?: AgendaMode | null;
  /**
   * The names "Add from speakers" offers: Review's speakers in list order (named ones first). Without
   * it (the Record screen, before there is a transcript) the Who spoke names are offered.
   */
  speakerNames?: readonly string[];
  /** Registers "add participants from speakers" with Undo (Review). */
  onUndoable?: (entry: UndoEntry) => void;
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

export function DetailsSheet({ saver, onClose, agendaMode = null, speakerNames, onUndoable }: DetailsSheetProps): JSX.Element {
  const details = saver.details.value;
  const { store } = useServices();
  const expected = store.settings.value?.speakers.expectedSpeakers ?? 'auto';
  const settingsDefault = expected === 'auto' ? null : expected;
  // Add from speakers: the named speakers not listed yet ("Speaker n" skipped), undoable.
  const fromSpeakers = participantsFromSpeakers(details.participants, speakerNames ?? details.whoSpoke.names);
  const [lastAdd, setLastAdd] = useState<{ before: string[]; added: string[] } | null>(null);
  const addFromSpeakers = (): void => {
    const before = details.participants;
    const after = [...before, ...fromSpeakers];
    saver.update({ participants: after });
    setLastAdd({ before, added: fromSpeakers });
    onUndoable?.({
      label: 'add participants from speakers',
      undo: () => {
        saver.update({ participants: before });
        return saver.flush();
      },
      redo: () => {
        saver.update({ participants: after });
        return saver.flush();
      },
    });
  };
  const undoAddFromSpeakers = (): void => {
    if (lastAdd !== null) {
      saver.update({ participants: lastAdd.before });
      setLastAdd(null);
    }
  };
  const agenda = useAgendaImport(saver, agendaMode);
  const update = (patch: Partial<RecordingDetails>): void => {
    saver.update(patch);
  };
  const close = (): void => {
    // A parsed agenda not applied yet is applied now; if it cannot be (limits, a refusal), the
    // sheet stays open with the reason shown.
    void agenda.finish().then((ok) => {
      if (!ok) {
        // Focus goes to the first item to fix (or the reason the host gave), which also scrolls it into view.
        document.querySelector<HTMLElement>('.agenda-item--too-long .ii, .agenda-error .btn')?.focus();
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
            <div class="sheet-section-head">
              <span class="lbl" id="participants-label">
                Participants
              </span>
              {fromSpeakers.length > 0 ? (
                <button class="link-btn sheet-from-speakers" type="button" title={`Adds ${fromSpeakers.join(', ')}`} onClick={addFromSpeakers}>
                  Add from speakers
                </button>
              ) : lastAdd !== null ? (
                <button class="link-btn sheet-from-speakers" type="button" onClick={undoAddFromSpeakers}>
                  Undo add from speakers
                </button>
              ) : null}
            </div>
            <NameWell
              id="add-person"
              label="Add a participant"
              values={details.participants}
              placeholder="Add a name"
              onChange={(participants) => {
                setLastAdd(null);
                update({ participants });
              }}
            />
            {lastAdd === null ? null : (
              <p class="sheet-caption" role="status">
                Added {lastAdd.added.join(', ')} from the speakers.
              </p>
            )}
          </div>
          <div class="sheet-field sheet-field--wide">
            <span class="lbl">Who spoke</span>
            <WhoSpokeEditor
              idPrefix="sheet-who-spoke"
              value={details.whoSpoke}
              participants={details.participants}
              settingsDefault={settingsDefault}
              onChange={(whoSpoke) => {
                update({ whoSpoke });
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
