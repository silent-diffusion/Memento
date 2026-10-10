// The Settings sections completed in M3 (DESIGN.md §11, renders/Settings.dc.html): General with
// startup and moving the library, AI and privacy, Export, and Storage and history. Every row keeps
// its one-sentence description; nothing here sends anything anywhere.
import type { JSX } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import type {
  AiProvider,
  AiSettingsInput,
  AiShareSettings,
  AudioExportFormat,
  DocumentExportFormat,
  ExportSelection,
  ExportSettings,
  GeneralSettings,
  LibraryUsage,
  ReclaimCodec,
  TranscriptExportFormat,
} from '../../bridge/types';
import { Segmented, Toggle } from '../../components/Controls';
import { InfoIcon } from '../../components/icons';
import { SelectMenu } from '../../components/Menus';
import { NEVER_SENT, NeverSentRow } from '../../components/NeverSentRow';
import { AUDIO_FORMAT_LABELS, TRANSCRIPT_FORMAT_LABELS } from '../../format/export';
import { formatFreeSpace, formatSize } from '../../format/storage';
import { goToLibrary, openRecording, updateSettings } from '../../state/actions';
import { useServices } from '../../state/context';
import { updateLibraryView } from '../../state/data';
import { jobsOf } from '../../state/jobs';
import { PROVIDER_NAMES } from './SettingsDialogs';
import { AboutGroup, UpdatesGroup } from './about';
import { LATER, OnOff, SettingsGroup, SettingsRow } from './SettingsParts';
import { AiProviderDefaults } from './sections-m4';

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

function InlineMessage({ message, tone = 'alert' }: { message: string | null; tone?: 'alert' | 'status' }): JSX.Element | null {
  return message === null ? null : (
    <p class={tone === 'alert' ? 'settings-inline' : 'settings-inline settings-inline--ok'} role={tone}>
      {message}
    </p>
  );
}

function ProgressLine({ label, percent }: { label: string; percent: number }): JSX.Element {
  return (
    <div class="settings-progress" role="status">
      <span class="settings-progress-label">{label}</span>
      <span class="settings-progress-track" aria-hidden="true">
        <span class="settings-progress-fill" style={{ width: `${Math.max(0, Math.min(100, percent))}%` }} />
      </span>
    </div>
  );
}

interface CheckItem {
  key: string;
  name: string;
  on: boolean;
  note?: string;
  /** Locked items stay unticked and say why ("not in this version"). */
  locked?: boolean;
}

/** `neverSent`: the names shown after the items as greyed "never sent" rows, with no checkbox. */
function Checklist({ label, items, onToggle, neverSent = [] }: { label: string; items: CheckItem[]; onToggle: (key: string) => void; neverSent?: readonly string[] }): JSX.Element {
  return (
    <div class="settings-checks" role="group" aria-label={label}>
      {items.map((item) => (
        <label key={item.key} class={item.locked === true ? 'settings-check settings-check--locked' : 'settings-check'}>
          <input
            class="chk"
            type="checkbox"
            checked={item.locked === true ? false : item.on}
            disabled={item.locked === true}
            onChange={() => {
              onToggle(item.key);
            }}
          />
          <span class="settings-check-text">
            <span>{item.name}</span>
            {item.note === undefined ? null : <span class="settings-check-note">{item.note}</span>}
          </span>
        </label>
      ))}
      {neverSent.map((name) => (
        <NeverSentRow key={name} name={name} rowClass="settings-check settings-check--locked" noteClass="settings-check-note" textClass="settings-check-text" />
      ))}
    </div>
  );
}

// ---------------------------------------------------------------------------------------------
// General
// ---------------------------------------------------------------------------------------------

function MoveBanner(): JSX.Element | null {
  const { store } = useServices();
  const move = jobsOf(store).move.value;
  if (move === null) {
    return null;
  }
  if (move.state === 'running') {
    return (
      <div class="banner settings-move-banner" role="status">
        <InfoIcon size={18} class="banner-icon" />
        <span class="banner-text">
          <span class="banner-lead">Moving the library to {move.newPath} · {Math.round(move.percent)}%.</span> Files are copied and checked
          first; the old folder is removed only after every file matches. Recording waits until it is done.
          <span class="settings-progress-track settings-progress-track--banner" aria-hidden="true">
            <span class="settings-progress-fill" style={{ width: `${Math.max(0, Math.min(100, move.percent))}%` }} />
          </span>
        </span>
      </div>
    );
  }
  return null;
}

export function GeneralSectionM3(): JSX.Element {
  const services = useServices();
  const { bridge, store } = services;
  const settings = store.settings.value;
  const move = jobsOf(store).move.value;
  const [error, setError] = useState<string | null>(null);
  const [startupError, setStartupError] = useState<string | null>(null);
  const [locationError, setLocationError] = useState<string | null>(null);
  if (settings === null) {
    return <></>;
  }
  const general = settings.general;
  const save = (patch: Parameters<typeof updateSettings>[1], onError = setError): void => {
    void updateSettings(services, patch).then(onError);
  };
  const saveGeneral = (next: Partial<GeneralSettings>): void => {
    save({ general: { ...general, ...next } }, setStartupError);
  };
  const setStartup = (startWithWindows: boolean): void => {
    setStartupError(null);
    store.settings.value = { ...settings, general: { ...general, startWithWindows } };
    bridge
      .call('app.setStartup', { startWithWindows })
      .then((result) => {
        const current = store.settings.value;
        if (current !== null) {
          store.settings.value = { ...current, general: { ...current.general, startWithWindows: result.startWithWindows } };
        }
      })
      .catch((e: unknown) => {
        const current = store.settings.value;
        if (current !== null) {
          store.settings.value = { ...current, general: { ...current.general, startWithWindows: !startWithWindows } };
        }
        setStartupError(messageOf(e));
      });
  };
  const changeLocation = async (): Promise<void> => {
    setLocationError(null);
    try {
      const picked = await bridge.call('dialog.pickFolder', {
        title: 'Choose where Memento keeps your library',
        initialPath: settings.libraryPath,
      });
      if (picked.path === null || picked.path.toLocaleLowerCase() === settings.libraryPath.toLocaleLowerCase()) {
        return;
      }
      store.dialog.value = { kind: 'libraryMove', from: settings.libraryPath, to: picked.path };
    } catch (e) {
      setLocationError(`The folder picker did not open. ${messageOf(e)}`);
    }
  };
  const moving = move?.state === 'running';
  const moveResult =
    move === null || moving ? null : move.state === 'done' ? (move.message ?? `The library is now in ${move.newPath}.`) : (move.message ?? 'The library was not moved.');
  return (
    <>
      <MoveBanner />
      <SettingsGroup label="Appearance">
        <SettingsRow label="Theme" description="Follows Windows by default.">
          <Segmented
            label="Theme"
            value={settings.theme}
            options={[
              { value: 'system', label: 'System' },
              { value: 'light', label: 'Light' },
              { value: 'dark', label: 'Dark' },
            ]}
            onChange={(theme) => {
              save({ theme });
            }}
          />
        </SettingsRow>
        <SettingsRow label="List density" description="Row height in the library and transcript." below={<InlineMessage message={error} />}>
          <Segmented
            label="List density"
            value={settings.listDensity}
            options={[
              { value: 'comfortable', label: 'Comfortable' },
              { value: 'compact', label: 'Compact' },
            ]}
            onChange={(listDensity) => {
              save({ listDensity });
            }}
          />
        </SettingsRow>
      </SettingsGroup>
      <SettingsGroup label="Startup">
        <SettingsRow
          label="Start Memento with Windows"
          description={
            !general.startWithWindowsAvailable
              ? (general.startWithWindowsNote ?? 'Start with Windows works only for an installed Memento.')
              : general.keepRunningInTray
                ? 'Starts in the tray when you sign in, so recording is one click away.'
                : 'Opens minimised when you sign in, so recording is one click away.'
          }
          {...(general.startWithWindowsAvailable ? {} : { note: 'Installed copies only' })}
        >
          <OnOff on={general.startWithWindows} />
          <Toggle
            label="Start Memento with Windows"
            checked={general.startWithWindows}
            disabled={!general.startWithWindowsAvailable && !general.startWithWindows}
            onChange={setStartup}
          />
        </SettingsRow>
        <SettingsRow
          label="Keep running in the tray when closed"
          description="Closing the window leaves Memento in the notification area with Open, Record and Quit. Recording and transcription carry on; Quit ends it."
          below={<InlineMessage message={startupError} />}
        >
          <Toggle
            label="Keep running in the tray when closed"
            checked={general.keepRunningInTray}
            onChange={(keepRunningInTray) => {
              saveGeneral({ keepRunningInTray });
            }}
          />
        </SettingsRow>
      </SettingsGroup>
      <SettingsGroup label="Library">
        <SettingsRow
          label="Library location"
          description="Recordings, transcripts and documents are stored here."
          below={
            <>
              <InlineMessage message={locationError} />
              {moveResult === null ? null : <InlineMessage message={moveResult} tone={move?.state === 'done' ? 'status' : 'alert'} />}
            </>
          }
        >
          <span class="mono settings-path" title={settings.libraryPath}>
            {settings.libraryPath}
          </span>
          <button
            class="btn ghost small-btn"
            type="button"
            aria-label="Change library location"
            disabled={moving}
            onClick={() => {
              void changeLocation();
            }}
          >
            Change
          </button>
        </SettingsRow>
        <SettingsRow label="Language" description="Interface language.">
          <SelectMenu<'en'>
            label="Language"
            value={general.language}
            options={[{ value: 'en', label: 'English' }]}
            onChange={(language) => {
              saveGeneral({ language });
            }}
          />
        </SettingsRow>
      </SettingsGroup>
      <UpdatesGroup />
      <AboutGroup />
    </>
  );
}

// ---------------------------------------------------------------------------------------------
// AI and privacy
// ---------------------------------------------------------------------------------------------

const SHARE_ITEMS: readonly { key: keyof AiShareSettings; name: string; note?: string }[] = [
  { key: 'transcript', name: 'Transcript' },
  { key: 'details', name: 'Recording details', note: 'title, date, type' },
  { key: 'participants', name: 'Participants' },
  { key: 'agenda', name: 'Agenda and imported documents' },
  { key: 'highlights', name: 'Highlights and notes' },
  { key: 'attachments', name: 'Attachments' },
];

const PROVIDERS: readonly { provider: AiProvider; description: string }[] = [
  { provider: 'anthropic', description: 'Default provider for documents.' },
  { provider: 'openai', description: 'Used only when you choose it for a document.' },
];

/** Shown instead of the key: the key itself never reaches the page. */
export const MASKED_KEY = '••••••••••••';

export function AiPrivacySection(): JSX.Element {
  const services = useServices();
  const { bridge, store } = services;
  const settings = store.settings.value;
  const [error, setError] = useState<string | null>(null);
  const [keyError, setKeyError] = useState<string | null>(null);
  if (settings === null) {
    return <></>;
  }
  const ai = settings.ai;
  const save = (next: Partial<AiSettingsInput>): void => {
    const input: AiSettingsInput = { enabled: ai.enabled, askBeforeSend: ai.askBeforeSend, keepRecord: ai.keepRecord, share: ai.share, ...next };
    void updateSettings(services, { ai: input }).then(setError);
  };
  const removeKey = (provider: AiProvider): void => {
    setKeyError(null);
    bridge
      .call('ai.clearKey', { provider })
      .then(({ hasKey }) => {
        const current = store.settings.value;
        if (current !== null) {
          store.settings.value = { ...current, ai: { ...current.ai, providers: { ...current.ai.providers, [provider]: { hasKey } } } };
        }
      })
      .catch((e: unknown) => {
        setKeyError(`The ${PROVIDER_NAMES[provider]} key was not removed. ${messageOf(e)}`);
      });
  };
  return (
    <>
      <SettingsGroup label="External AI">
        <SettingsRow
          label="Allow external AI services"
          description="Off by default. Turning it on enables document generation and other optional features; recording, transcription and export never need it."
        >
          <OnOff on={ai.enabled} />
          <Toggle
            label="Allow external AI services"
            checked={ai.enabled}
            onChange={(enabled) => {
              save({ enabled });
            }}
          />
        </SettingsRow>
        <SettingsRow label="Ask before every send" description="Shows exactly what will be sent and lets you remove items.">
          <OnOff on={ai.askBeforeSend} />
          <Toggle
            label="Ask before every send"
            checked={ai.askBeforeSend}
            onChange={(askBeforeSend) => {
              save({ askBeforeSend });
            }}
          />
        </SettingsRow>
        <SettingsRow label="Keep a record of what was sent" description="Listed in each recording’s history." below={<InlineMessage message={error} />}>
          <OnOff on={ai.keepRecord} />
          <Toggle
            label="Keep a record of what was sent"
            checked={ai.keepRecord}
            onChange={(keepRecord) => {
              save({ keepRecord });
            }}
          />
        </SettingsRow>
      </SettingsGroup>
      <SettingsGroup label="Providers">
        {PROVIDERS.map(({ provider, description }, index) => {
          const hasKey = ai.providers[provider].hasKey;
          const name = PROVIDER_NAMES[provider];
          return (
            <SettingsRow
              key={provider}
              label={name}
              description={hasKey ? description : 'Not configured.'}
              below={index === PROVIDERS.length - 1 ? <InlineMessage message={keyError} /> : undefined}
            >
              {hasKey ? (
                <span class="mono settings-key" aria-label={`${name} key stored`}>
                  {MASKED_KEY}
                </span>
              ) : (
                <span class="settings-note">No key</span>
              )}
              {hasKey ? (
                <button
                  class="btn ghost small-btn"
                  type="button"
                  aria-label={`Remove the ${name} key`}
                  onClick={() => {
                    removeKey(provider);
                  }}
                >
                  Remove
                </button>
              ) : null}
              <button
                class="btn ghost small-btn"
                type="button"
                aria-haspopup="dialog"
                aria-label={hasKey ? `Replace the ${name} key` : `Add a ${name} key`}
                onClick={() => {
                  store.dialog.value = { kind: 'aiKey', provider, replacing: hasKey };
                }}
              >
                {hasKey ? 'Replace' : 'Add'}
              </button>
            </SettingsRow>
          );
        })}
      </SettingsGroup>
      <SettingsGroup label="What may be shared">
        <SettingsRow
          label="Allowed data"
          description="Only ticked items can ever be sent. Audio and video never leave this PC."
          below={
            <Checklist
              label="Allowed data"
              items={SHARE_ITEMS.map((item) => ({ ...item, on: ai.share[item.key] }))}
              neverSent={NEVER_SENT}
              onToggle={(key) => {
                const shareKey = key as keyof AiShareSettings;
                save({ share: { ...ai.share, [shareKey]: !ai.share[shareKey] } });
              }}
            />
          }
        />
      </SettingsGroup>
      {/* M4: the default provider and the local model (sections-m4.tsx). */}
      <AiProviderDefaults />
    </>
  );
}

// ---------------------------------------------------------------------------------------------
// Export
// ---------------------------------------------------------------------------------------------

type IncludeKey = 'audioMixed' | 'tracks' | 'transcript' | 'details' | 'attachments' | 'documents';

const INCLUDE_ITEMS: readonly { key: IncludeKey | 'video'; name: string; note?: string; locked?: boolean }[] = [
  { key: 'audioMixed', name: 'Audio', note: 'mixed' },
  { key: 'tracks', name: 'Individual tracks' },
  { key: 'video', name: 'Video', note: 'not in this version', locked: true },
  { key: 'transcript', name: 'Transcript' },
  // M4: every document of the recording; the Export dialog lists them one by one.
  { key: 'documents', name: 'Documents', note: 'all of them' },
  { key: 'details', name: 'Recording details' },
  { key: 'attachments', name: 'Attachments' },
];

function toggleInclude(selection: ExportSelection, key: IncludeKey): ExportSelection {
  switch (key) {
    case 'audioMixed':
      return { ...selection, audioMixed: { ...selection.audioMixed, on: !selection.audioMixed.on } };
    case 'tracks':
      return { ...selection, tracks: { ...selection.tracks, on: !selection.tracks.on } };
    case 'transcript':
      return { ...selection, transcript: { ...selection.transcript, on: !selection.transcript.on } };
    case 'details':
      return { ...selection, details: { on: !selection.details.on } };
    case 'attachments':
      return { ...selection, attachments: { on: !selection.attachments.on } };
    case 'documents':
      return { ...selection, documents: { ...selection.documents, on: !selection.documents.on, documentIds: [] } };
  }
}

export function ExportSection(): JSX.Element {
  const services = useServices();
  const { bridge, store } = services;
  const settings = store.settings.value;
  const [error, setError] = useState<string | null>(null);
  if (settings === null) {
    return <></>;
  }
  const ex = settings.export;
  const save = (next: Partial<ExportSettings>): void => {
    void updateSettings(services, { export: { ...ex, ...next } }).then(setError);
  };
  const defaults = ex.defaults;
  const changeFolder = async (): Promise<void> => {
    setError(null);
    try {
      const picked = await bridge.call('dialog.pickFolder', {
        title: 'Choose the default folder for exports',
        ...(ex.defaultFolder === null ? {} : { initialPath: ex.defaultFolder }),
      });
      if (picked.path !== null) {
        save({ defaultFolder: picked.path });
      }
    } catch (e) {
      setError(`The folder picker did not open. ${messageOf(e)}`);
    }
  };
  const audioFormat = defaults.audioMixed.format;
  return (
    <>
      <SettingsGroup label="External copies">
        <SettingsRow label="Save copies outside Memento" description="Off means nothing is written outside the library unless you export by hand.">
          <OnOff on={ex.saveCopiesOutside} />
          <Toggle
            label="Save copies outside Memento"
            checked={ex.saveCopiesOutside}
            onChange={(saveCopiesOutside) => {
              save({ saveCopiesOutside });
            }}
          />
        </SettingsRow>
        <SettingsRow label="Default folder" description="Used when you do not pick a destination.">
          <span class="mono settings-path" title={ex.defaultFolder ?? undefined}>
            {ex.defaultFolder ?? 'Not set'}
          </span>
          <button
            class="btn ghost small-btn"
            type="button"
            aria-label="Change the default export folder"
            onClick={() => {
              void changeFolder();
            }}
          >
            Change
          </button>
        </SettingsRow>
        <SettingsRow label="Ask where to save each time" description="Otherwise the default folder is used silently.">
          <OnOff on={ex.askWhereEachTime} />
          <Toggle
            label="Ask where to save each time"
            checked={ex.askWhereEachTime}
            onChange={(askWhereEachTime) => {
              save({ askWhereEachTime });
            }}
          />
        </SettingsRow>
        <SettingsRow label="Put each export in its own folder" description="A folder named after the recording and its date, inside the destination.">
          <OnOff on={ex.createSubfolder} />
          <Toggle
            label="Put each export in its own folder"
            checked={ex.createSubfolder}
            onChange={(createSubfolder) => {
              save({ createSubfolder });
            }}
          />
        </SettingsRow>
      </SettingsGroup>
      <SettingsGroup label="What to include by default">
        <SettingsRow
          label="Components"
          description="Untick anything you rarely need. You can still change this per export."
          below={
            <Checklist
              label="Components"
              items={INCLUDE_ITEMS.map((item) => ({
                ...item,
                on: item.locked === true ? false : defaults[item.key as IncludeKey].on,
              }))}
              onToggle={(key) => {
                save({ defaults: toggleInclude(defaults, key as IncludeKey) });
              }}
            />
          }
        />
      </SettingsGroup>
      <SettingsGroup label="Formats">
        <SettingsRow label="Transcript" description="JSON keeps timestamps, speakers and confidence.">
          <Segmented<TranscriptExportFormat>
            label="Transcript format"
            value={defaults.transcript.formats[0] ?? 'json'}
            options={(Object.keys(TRANSCRIPT_FORMAT_LABELS) as TranscriptExportFormat[]).map((value) => ({ value, label: TRANSCRIPT_FORMAT_LABELS[value] }))}
            onChange={(format) => {
              save({ defaults: { ...defaults, transcript: { ...defaults.transcript, formats: [format] } } });
            }}
          />
        </SettingsRow>
        <SettingsRow label="Audio" description="FLAC is lossless; MP3 is smaller.">
          <SelectMenu<AudioExportFormat>
            label="Audio format"
            value={audioFormat}
            options={(Object.keys(AUDIO_FORMAT_LABELS) as AudioExportFormat[]).map((value) => ({ value, label: AUDIO_FORMAT_LABELS[value] }))}
            onChange={(format) => {
              const bitrateKbps = format === 'mp3' ? 192 : null;
              save({
                defaults: {
                  ...defaults,
                  audioMixed: { ...defaults.audioMixed, format, bitrateKbps },
                  tracks: { ...defaults.tracks, format, bitrateKbps },
                },
              });
            }}
          />
        </SettingsRow>
        <SettingsRow label="Documents" description="Generated documents and notes. Word and PDF follow the document's style." below={<InlineMessage message={error} />}>
          <SelectMenu<DocumentExportFormat>
            label="Documents format"
            value={defaults.documents.format}
            options={[
              { value: 'docx', label: 'Word' },
              { value: 'pdf', label: 'PDF' },
              { value: 'markdown', label: 'Markdown' },
            ]}
            onChange={(format) => {
              save({ defaults: { ...defaults, documents: { ...defaults.documents, format } } });
            }}
          />
        </SettingsRow>
      </SettingsGroup>
    </>
  );
}

// ---------------------------------------------------------------------------------------------
// Storage and history
// ---------------------------------------------------------------------------------------------

const RECLAIM_DAYS = [30, 90, 180, 365] as const;
const DEFAULT_RECLAIM_KBPS: Record<ReclaimCodec, number> = { aac: 160, mp3: 192 };

export function StorageSectionM3(): JSX.Element {
  const services = useServices();
  const { bridge, store } = services;
  const settings = store.settings.value;
  const reclaim = jobsOf(store).reclaim.value;
  const [usage, setUsage] = useState<LibraryUsage | null>(null);
  const [usageError, setUsageError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reload, setReload] = useState(0);
  const [rebuilding, setRebuilding] = useState(false);

  useEffect(() => {
    let live = true;
    bridge
      .call('library.usage')
      .then((result) => {
        if (live) {
          setUsage(result);
          setUsageError(null);
        }
      })
      .catch((e: unknown) => {
        if (live) {
          setUsageError(`Usage could not be read. ${messageOf(e)}`);
        }
      });
    return () => {
      live = false;
    };
  }, [bridge, reload]);

  useEffect(
    () =>
      bridge.on('library.changed', () => {
        setReload((n) => n + 1);
      }),
    [bridge],
  );

  if (settings === null) {
    return <></>;
  }
  const days = settings.storage.reclaimOlderThanDays;
  const running = reclaim?.state === 'running';
  const storage = settings.recording.storage;
  const codec: ReclaimCodec = storage.codec === 'flac' ? 'aac' : storage.codec;
  const bitrateKbps = storage.codec === 'flac' || storage.bitrateKbps === null ? DEFAULT_RECLAIM_KBPS[codec] : storage.bitrateKbps;

  const runReclaim = (): void => {
    setError(null);
    bridge
      .call('storage.reclaim', { recordingIds: null, downmixMono: true, codec, bitrateKbps })
      .then(({ jobId }) => {
        jobsOf(store).reclaim.value = { jobId, percent: 0, state: 'running', message: null, recordingsDone: 0, bytesFreed: 0 };
      })
      .catch((e: unknown) => {
        setError(messageOf(e));
      });
  };

  const rebuild = (): void => {
    setRebuilding(true);
    bridge
      .call('library.rebuildIndex')
      .then(({ recordings }) => {
        store.toasts.show({
          tone: 'ok',
          title: 'The library index was rebuilt',
          body: `${recordings} ${recordings === 1 ? 'recording was' : 'recordings were'} found and listed again. Nothing inside them was changed.`,
        });
      })
      .catch((e: unknown) => {
        store.toasts.show({ tone: 'warning', title: 'The library index was not rebuilt', body: `${messageOf(e)} The recordings themselves are safe.` });
      })
      .finally(() => {
        setRebuilding(false);
      });
  };

  const reviewLarge = (): void => {
    updateLibraryView(bridge, store, { type: 'sort', sort: 'size' });
    goToLibrary(services);
  };

  const reclaimLine =
    reclaim === null
      ? null
      : reclaim.state === 'running'
        ? null
        : reclaim.state === 'done'
          ? (reclaim.message ??
            `${reclaim.recordingsDone} ${reclaim.recordingsDone === 1 ? 'recording was' : 'recordings were'} made smaller, freeing ${formatSize(reclaim.bytesFreed)}. Transcripts were not touched.`)
          : (reclaim.message ?? 'Reclaiming space stopped. The recordings are as they were.');

  const count = usage?.count ?? store.library.value?.totalCount ?? 0;
  const largest = usage?.largest ?? null;
  return (
    <>
      <SettingsGroup label="Usage">
        <SettingsRow label="Library size" description={`${count} ${count === 1 ? 'recording' : 'recordings'}, all on this PC.`} below={<InlineMessage message={usageError} />}>
          <span class="settings-value">{usage === null ? '…' : formatSize(usage.totalBytes)}</span>
        </SettingsRow>
        <SettingsRow label="Free space" description="On the drive that holds the library.">
          <span class="settings-value">{usage === null ? '…' : formatFreeSpace(usage.freeBytes)}</span>
        </SettingsRow>
        <SettingsRow label="Largest recording" description={largest === null ? 'Shown once you have a recording.' : largest.title}>
          {largest === null ? null : (
            <button
              class="btn link-btn settings-open"
              type="button"
              aria-label={`Open ${largest.title}`}
              onClick={() => {
                openRecording(services, largest.recordingId);
              }}
            >
              Open
            </button>
          )}
          <span class="settings-value">{largest === null ? 'None yet' : formatSize(largest.sizeBytes)}</span>
        </SettingsRow>
      </SettingsGroup>
      <SettingsGroup label="Reclaim space">
        <SettingsRow
          label="Downmix tracks older than"
          description="Converts older recordings to smaller mono files; transcripts are never touched."
          below={
            <>
              {running ? (
                <ProgressLine
                  label={`Making recordings smaller · ${Math.round(reclaim.percent)}% · ${formatSize(reclaim.bytesFreed)} freed so far`}
                  percent={reclaim.percent}
                />
              ) : null}
              <InlineMessage message={reclaimLine} tone={reclaim?.state === 'done' ? 'status' : 'alert'} />
              <InlineMessage message={error} />
            </>
          }
        >
          <SelectMenu<string>
            label="Downmix tracks older than"
            value={days === null ? 'never' : String(days)}
            options={[{ value: 'never', label: 'Never' }, ...RECLAIM_DAYS.map((d) => ({ value: String(d), label: `${d} days` }))]}
            onChange={(value) => {
              void updateSettings(services, { storage: { reclaimOlderThanDays: value === 'never' ? null : Number(value) } }).then(setError);
            }}
          />
          <button class="btn ghost small-btn" type="button" disabled={days === null || running} onClick={runReclaim}>
            {running ? 'Running…' : 'Run now'}
          </button>
        </SettingsRow>
        <SettingsRow label="Remove video older than" description="Audio, transcript and documents are kept." note={LATER}>
          <SelectMenu label="Remove video older than" value="never" options={[{ value: 'never', label: 'Never' }]} onChange={() => undefined} disabled />
        </SettingsRow>
        <SettingsRow label="Review large recordings" description="Opens the library with the largest recordings first.">
          <button class="btn ghost small-btn" type="button" onClick={reviewLarge}>
            Review
          </button>
        </SettingsRow>
      </SettingsGroup>
      <SettingsGroup label="Index">
        <SettingsRow label="Rebuild the library index" description="Reads every recording folder again if something is missing from the list.">
          <button class="btn ghost small-btn" type="button" disabled={rebuilding} onClick={rebuild}>
            {rebuilding ? 'Rebuilding…' : 'Rebuild'}
          </button>
        </SettingsRow>
      </SettingsGroup>
    </>
  );
}
