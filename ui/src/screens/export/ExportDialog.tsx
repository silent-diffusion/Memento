// Export copies (DESIGN.md §15, renders/ExportDialog.dc.html): what to include with sizes from
// export.estimate, where to write, and a live summary; export.run writes in the background and
// the status footer shows its progress. A refusal or a failed job shows the §17 inline card.
import type { JSX } from 'preact';
import { createPortal } from 'preact/compat';
import { useEffect, useMemo, useRef, useState } from 'preact/hooks';
import type {
  Attachment,
  AudioExportFormat,
  ExportComponent,
  ExportEstimate,
  ExportSelection,
  Project,
  TranscriptExportFormat,
} from '../../bridge/types';
import { Toggle } from '../../components/Controls';
import { CloseIcon, InfoIcon } from '../../components/icons';
import { MultiSelectMenu, SelectMenu } from '../../components/Menus';
import { useModal } from '../../components/Overlay';
import { joinList } from '../../format/messages';
import {
  AUDIO_FORMAT_LABELS,
  everythingOn,
  exportFolderName,
  exportPathPreview,
  fileCount,
  MP3_EXPORT_KBPS,
  summarise,
  TRANSCRIPT_FORMAT_LABELS,
} from '../../format/export';
import { formatSize } from '../../format/storage';
import { useServices } from '../../state/context';
import type { DialogRequest } from '../../state/dialogs';
import { trackExport } from '../../state/jobs';

export const ESTIMATE_DEBOUNCE_MS = 250;

const NOTHING_CHANGED = 'Nothing inside Memento was changed.';

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

/** The host's message, with the §17 promise added when it does not already say it. */
export function failureText(message: string): string {
  return /nothing inside memento was changed/i.test(message) ? message : `${message} ${NOTHING_CHANGED}`;
}

const AUDIO_OPTIONS = (Object.keys(AUDIO_FORMAT_LABELS) as AudioExportFormat[]).map((value) => ({ value, label: AUDIO_FORMAT_LABELS[value] }));
const TRANSCRIPT_OPTIONS = (Object.keys(TRANSCRIPT_FORMAT_LABELS) as TranscriptExportFormat[]).map((value) => ({
  value,
  label: TRANSCRIPT_FORMAT_LABELS[value],
}));

/** Documents stay off in M3: the row is disabled ("Documents arrive in a later version"). */
function startingSelection(selection: ExportSelection): ExportSelection {
  return {
    ...selection,
    transcript: { ...selection.transcript, formats: selection.transcript.formats.length === 0 ? ['json'] : selection.transcript.formats },
    documents: { ...selection.documents, on: false },
  };
}

function withAudioFormat(choice: ExportSelection['audioMixed'], format: AudioExportFormat): ExportSelection['audioMixed'] {
  return { ...choice, format, bitrateKbps: format === 'mp3' ? (choice.bitrateKbps ?? MP3_EXPORT_KBPS) : null };
}

interface RowProps {
  id: string;
  name: string;
  description: string;
  checked: boolean;
  /** Unavailable rows and rows not in this version cannot be ticked. */
  disabled: boolean;
  size: string;
  onToggle?: () => void;
  format: JSX.Element;
}

function ComponentRow({ id, name, description, checked, disabled, size, onToggle, format }: RowProps): JSX.Element {
  return (
    <div class={['comp', 'export-row', checked ? '' : 'off', disabled ? 'export-row--unavailable' : ''].filter((c) => c !== '').join(' ')}>
      <input
        id={id}
        class="chk"
        type="checkbox"
        checked={checked}
        disabled={disabled}
        aria-describedby={`${id}-desc`}
        // Focus starts on the first thing to decide: what to include.
        data-autofocus={id === 'exp-audio' ? true : undefined}
        onChange={() => onToggle?.()}
      />
      <label for={id} class="export-row-text">
        <span class="nm export-row-name">{name}</span>
        <span id={`${id}-desc`} class="export-row-desc">
          {description}
        </span>
      </label>
      <span class="mono export-row-size" aria-label={size === '—' ? 'No size' : `About ${size}`}>
        {size}
      </span>
      <span class="export-format">{format}</span>
    </div>
  );
}

function FixedFormat({ label, name }: { label: string; name: string }): JSX.Element {
  return (
    <span class="export-format-fixed" aria-label={`Format for ${name}: ${label}`}>
      {label}
    </span>
  );
}

export function ExportDialog({ request, close }: { request: Extract<DialogRequest, { kind: 'export' }>; close: () => void }): JSX.Element {
  const services = useServices();
  const { bridge, store } = services;
  const settings = store.settings.value;
  const exportSettings = settings?.export ?? null;
  const retry = request.retry ?? null;
  const known = store.library.value?.recordings.find((r) => r.id === request.recordingId) ?? null;

  const [project, setProject] = useState<Project | null>(null);
  const [attachments, setAttachments] = useState<Attachment[]>([]);
  const [selection, setSelection] = useState<ExportSelection | null>(() => {
    const base = retry?.selection ?? exportSettings?.defaults ?? null;
    return base === null ? null : startingSelection(base);
  });
  const [folder, setFolder] = useState<string>(retry?.destination.folder ?? exportSettings?.defaultFolder ?? '');
  const [subfolder, setSubfolder] = useState<boolean>(retry?.destination.createSubfolder ?? exportSettings?.createSubfolder ?? true);
  const [remember, setRemember] = useState(false);
  const [folderChosen, setFolderChosen] = useState(retry !== null);
  const [estimate, setEstimate] = useState<ExportEstimate | null>(null);
  const [estimateError, setEstimateError] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(retry === null ? null : failureText(retry.message));
  const [busy, setBusy] = useState(false);
  const { ref, onKeyDown } = useModal(close);

  useEffect(() => {
    let live = true;
    Promise.all([bridge.call('project.get', { recordingId: request.recordingId }), bridge.call('attachments.list', { recordingId: request.recordingId })])
      .then(([p, a]) => {
        if (live) {
          setProject(p);
          setAttachments(a.attachments);
        }
      })
      .catch((e: unknown) => {
        if (live) {
          setEstimateError(`The recording could not be read. ${messageOf(e)}`);
        }
      });
    return () => {
      live = false;
    };
  }, [bridge, request.recordingId]);

  // Settings may arrive after the dialog opened (a reload straight into Review).
  useEffect(() => {
    if (selection === null && exportSettings !== null) {
      setSelection(startingSelection(exportSettings.defaults));
      setFolder((f) => (f === '' ? (exportSettings.defaultFolder ?? '') : f));
    }
  }, [exportSettings, selection]);

  // Every row's size at once (formats matter, ticks do not), after a short pause in changes.
  const estimateKey = selection === null ? null : JSON.stringify(everythingOn(selection));
  useEffect(() => {
    if (estimateKey === null || selection === null) {
      return undefined;
    }
    let live = true;
    const timer = setTimeout(() => {
      bridge
        .call('export.estimate', { recordingId: request.recordingId, selection: everythingOn(selection) })
        .then((result) => {
          if (live) {
            setEstimate(result);
            setEstimateError(null);
          }
        })
        .catch((e: unknown) => {
          if (live) {
            setEstimateError(`Sizes could not be estimated. ${messageOf(e)}`);
          }
        });
    }, ESTIMATE_DEBOUNCE_MS);
    return () => {
      live = false;
      clearTimeout(timer);
    };
  }, [bridge, request.recordingId, estimateKey]);

  const title = project?.summary.title ?? known?.title ?? 'This recording';
  const createdAt = project?.summary.createdAt ?? known?.createdAt ?? '';
  const unavailable = useMemo(() => new Map((estimate?.unavailable ?? []).map((u) => [u.component, u.reason])), [estimate]);
  const sizeOf = (component: ExportComponent): string => {
    if (estimate === null) {
      return '…';
    }
    if (unavailable.has(component)) {
      return '—';
    }
    const items = estimate.items.filter((i) => i.component === component);
    return items.length === 0 ? '—' : formatSize(items.reduce((sum, i) => sum + i.bytes, 0));
  };
  const summary = estimate === null || selection === null ? null : summarise(estimate, selection);

  /** The selection as sent: rows that cannot be exported are off. */
  const effective = (current: ExportSelection): ExportSelection => {
    const off = (component: ExportComponent): boolean => unavailable.has(component);
    return {
      audioMixed: { ...current.audioMixed, on: current.audioMixed.on && !off('audioMixed') },
      tracks: { ...current.tracks, on: current.tracks.on && !off('tracks') },
      transcript: { ...current.transcript, on: current.transcript.on && !off('transcript') },
      documents: { ...current.documents, on: false },
      details: { on: current.details.on && !off('details') },
      attachments: { on: current.attachments.on && !off('attachments') },
    };
  };

  const pickFolder = async (): Promise<string | null> => {
    try {
      const picked = await bridge.call('dialog.pickFolder', {
        title: 'Choose where to save the copies',
        ...(folder === '' ? {} : { initialPath: folder }),
      });
      if (picked.path !== null) {
        setFolder(picked.path);
        setFolderChosen(true);
        setFailure(null);
      }
      return picked.path;
    } catch (e) {
      setFailure(failureText(`The folder picker did not open. ${messageOf(e)}`));
      return null;
    }
  };

  const run = async (destinationFolder: string | null = null): Promise<void> => {
    if (selection === null || busy) {
      return;
    }
    let target = destinationFolder ?? folder;
    // Ask where to save each time (Settings › Export): the picker confirms the folder first.
    if (destinationFolder === null && (target === '' || (exportSettings?.askWhereEachTime === true && !folderChosen))) {
      const picked = await pickFolder();
      if (picked === null) {
        return;
      }
      target = picked;
    }
    setBusy(true);
    setFailure(null);
    const chosen = effective(selection);
    const destination = { folder: target, createSubfolder: subfolder };
    try {
      const { jobId } = await bridge.call('export.run', { recordingId: request.recordingId, selection: chosen, destination, remember });
      trackExport(store, jobId, { recordingId: request.recordingId, title, selection: chosen, destination });
      if (remember) {
        bridge
          .call('settings.get')
          .then((next) => {
            store.settings.value = next;
          })
          .catch(() => undefined);
      }
      close();
    } catch (e) {
      setBusy(false);
      setFailure(failureText(messageOf(e)));
    }
  };

  // Choose another folder after a failed job: the picker opens with the dialog.
  const pickedOnOpen = useRef(false);
  useEffect(() => {
    if (retry?.pickFolder === true && !pickedOnOpen.current) {
      pickedOnOpen.current = true;
      void pickFolder();
    }
  }, []);

  const update = (next: Partial<ExportSelection>): void => {
    setSelection((current) => (current === null ? current : { ...current, ...next }));
  };

  const tracks = project?.tracks ?? [];
  const trackWords = tracks.length === 0 ? 'Each source as its own file' : `${joinList(tracks.map((t) => t.name))} as separate files`;
  const attachmentWords = attachments.length === 0 ? 'No attachments' : joinList(attachments.map((a) => a.name));
  const reasonOr = (component: ExportComponent, description: string): string => unavailable.get(component) ?? description;
  const isAvailable = (component: ExportComponent): boolean => !unavailable.has(component);
  const s = selection;

  return createPortal(
    <div class="scrim">
      <div
        ref={ref}
        class="export-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="export-title"
        aria-describedby="export-subtitle"
        tabIndex={-1}
        onKeyDown={onKeyDown}
      >
        <div class="export-head">
          <div class="export-head-text">
            <h2 id="export-title" class="export-title">
              Export copies
            </h2>
            <p id="export-subtitle" class="export-subtitle">
              {title}. Files are written outside Memento; the recording inside Memento stays the original.
            </p>
          </div>
          <button class="icon-btn export-close" type="button" aria-label="Close" onClick={close}>
            <CloseIcon size={16} />
          </button>
        </div>

        <div class="export-body">
          {failure === null ? null : (
            <div class="export-failure" role="alert">
              <span class="export-failure-icon" aria-hidden="true">
                <InfoIcon size={18} />
              </span>
              <div class="export-failure-text">
                <span class="export-failure-lead">The copies could not be written</span>
                <span>{failure}</span>
                <div class="export-failure-actions">
                  <button
                    class="btn g small-btn"
                    type="button"
                    disabled={busy}
                    onClick={() => {
                      void run(folder === '' ? null : folder);
                    }}
                  >
                    Try again
                  </button>
                  <button
                    class="btn g small-btn"
                    type="button"
                    disabled={busy}
                    onClick={() => {
                      void pickFolder();
                    }}
                  >
                    Choose another folder
                  </button>
                </div>
              </div>
            </div>
          )}

          <div class="export-group">
            <span class="lbl" id="export-include">
              What to include
            </span>
            {s === null ? (
              <p class="export-loading">Reading the export defaults…</p>
            ) : (
              <div class="export-list" role="group" aria-labelledby="export-include">
                <ComponentRow
                  id="exp-audio"
                  name="Audio (mixed)"
                  description={reasonOr('audioMixed', 'One file with every track mixed together')}
                  checked={s.audioMixed.on && isAvailable('audioMixed')}
                  disabled={!isAvailable('audioMixed')}
                  size={sizeOf('audioMixed')}
                  onToggle={() => {
                    update({ audioMixed: { ...s.audioMixed, on: !s.audioMixed.on } });
                  }}
                  format={
                    <SelectMenu<AudioExportFormat>
                      label="Format for Audio (mixed)"
                      value={s.audioMixed.format}
                      options={AUDIO_OPTIONS}
                      disabled={!s.audioMixed.on || !isAvailable('audioMixed')}
                      onChange={(format) => {
                        update({ audioMixed: withAudioFormat(s.audioMixed, format) });
                      }}
                    />
                  }
                />
                <ComponentRow
                  id="exp-tracks"
                  name="Individual tracks"
                  description={reasonOr('tracks', trackWords)}
                  checked={s.tracks.on && isAvailable('tracks')}
                  disabled={!isAvailable('tracks')}
                  size={sizeOf('tracks')}
                  onToggle={() => {
                    update({ tracks: { ...s.tracks, on: !s.tracks.on } });
                  }}
                  format={
                    <SelectMenu<AudioExportFormat>
                      label="Format for Individual tracks"
                      value={s.tracks.format}
                      options={AUDIO_OPTIONS}
                      disabled={!s.tracks.on || !isAvailable('tracks')}
                      onChange={(format) => {
                        update({ tracks: withAudioFormat(s.tracks, format) });
                      }}
                    />
                  }
                />
                <ComponentRow
                  id="exp-video"
                  name="Video"
                  description="Not in this version"
                  checked={false}
                  disabled
                  size="—"
                  format={<SelectMenu label="Format for Video" value="mp4" options={[{ value: 'mp4', label: 'MP4' }]} disabled onChange={() => undefined} />}
                />
                <ComponentRow
                  id="exp-transcript"
                  name="Transcript"
                  description={reasonOr('transcript', 'Speakers, timestamps and confidence')}
                  checked={s.transcript.on && isAvailable('transcript')}
                  disabled={!isAvailable('transcript')}
                  size={sizeOf('transcript')}
                  onToggle={() => {
                    update({ transcript: { ...s.transcript, on: !s.transcript.on } });
                  }}
                  format={
                    <MultiSelectMenu<TranscriptExportFormat>
                      label="Format for Transcript"
                      values={s.transcript.formats.length === 0 ? ['json'] : s.transcript.formats}
                      options={TRANSCRIPT_OPTIONS}
                      disabled={!s.transcript.on || !isAvailable('transcript')}
                      onChange={(formats) => {
                        update({ transcript: { ...s.transcript, formats } });
                      }}
                    />
                  }
                />
                <ComponentRow
                  id="exp-documents"
                  name="Documents"
                  description="Documents arrive in a later version"
                  checked={false}
                  disabled
                  size="—"
                  format={<SelectMenu label="Format for Documents" value="docx" options={[{ value: 'docx', label: 'Word' }]} disabled onChange={() => undefined} />}
                />
                <ComponentRow
                  id="exp-details"
                  name="Recording details"
                  description={reasonOr('details', 'Title, date, participants, agenda, tags')}
                  checked={s.details.on && isAvailable('details')}
                  disabled={!isAvailable('details')}
                  size={sizeOf('details')}
                  onToggle={() => {
                    update({ details: { on: !s.details.on } });
                  }}
                  format={<FixedFormat label="JSON" name="Recording details" />}
                />
                <ComponentRow
                  id="exp-attachments"
                  name="Attachments"
                  description={reasonOr('attachments', attachmentWords)}
                  checked={s.attachments.on && isAvailable('attachments')}
                  disabled={!isAvailable('attachments')}
                  size={sizeOf('attachments')}
                  onToggle={() => {
                    update({ attachments: { on: !s.attachments.on } });
                  }}
                  format={<FixedFormat label="Original" name="Attachments" />}
                />
              </div>
            )}
            {estimateError === null ? null : (
              <p class="export-note" role="status">
                {estimateError}
              </p>
            )}
          </div>

          <div class="export-group">
            <span class="lbl" id="export-where">
              Where
            </span>
            <div class="export-list" role="group" aria-labelledby="export-where">
              <div class="comp export-row">
                <div class="export-row-text">
                  <span class="export-row-name">Folder</span>
                  <span class="mono export-path" title={folder === '' ? undefined : exportPathPreview(folder, subfolder, title, createdAt)}>
                    {folder === '' ? 'No folder chosen yet' : exportPathPreview(folder, subfolder, title, createdAt)}
                  </span>
                </div>
                <button
                  class="btn ghost small-btn"
                  type="button"
                  aria-label="Change export folder"
                  onClick={() => {
                    void pickFolder();
                  }}
                >
                  Change
                </button>
              </div>
              <div class="comp export-row">
                <div class="export-row-text">
                  <span class="export-row-name">Put everything in a folder named after the recording</span>
                  <span class="export-row-desc">{exportFolderName(title, createdAt)}</span>
                </div>
                <Toggle label="Put everything in a folder named after the recording" checked={subfolder} onChange={setSubfolder} />
              </div>
              <div class="comp export-row">
                <div class="export-row-text">
                  <span class="export-row-name">Remember these choices</span>
                  <span class="export-row-desc">Becomes the default in Settings › Export</span>
                </div>
                <Toggle label="Remember these choices" checked={remember} onChange={setRemember} />
              </div>
            </div>
          </div>
        </div>

        <div class="export-foot">
          <span class="export-summary" aria-live="polite">
            {summary === null ? (
              'Estimating sizes…'
            ) : (
              <>
                <span class="export-summary-files">{fileCount(summary.files)}</span> · about <span class="mono">{formatSize(summary.bytes)}</span>
              </>
            )}
          </span>
          <div class="export-actions">
            <button class="btn g" type="button" onClick={close}>
              Cancel
            </button>
            <button
              class="btn p"
              type="button"
              disabled={busy || summary === null || summary.files === 0}
              onClick={() => {
                void run();
              }}
            >
              {busy ? 'Starting…' : 'Export'}
            </button>
          </div>
        </div>
      </div>
    </div>,
    document.body,
  );
}
