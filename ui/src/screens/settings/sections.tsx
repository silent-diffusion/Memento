// The working Settings sections for M1: General, Recording, Storage and history (DESIGN.md §11).
import type { JSX } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import type { RecordingSettings, RecordingType, StorageCodec } from '../../bridge/types';
import { Segmented, Toggle } from '../../components/Controls';
import { SelectMenu } from '../../components/Menus';
import { formatDuration, formatTotalDuration } from '../../format/duration';
import { CHOOSABLE_TYPES, isBuiltInType, typeName } from '../../format/recording';
import { formatFreeSpace, formatSize } from '../../format/storage';
import { updateSettings } from '../../state/actions';
import { useServices } from '../../state/context';
import { LATER, OnOff, SettingsGroup, SettingsRow } from './SettingsParts';

function InlineMessage({ message }: { message: string | null }): JSX.Element | null {
  return message === null ? null : (
    <p class="settings-inline" role="alert">
      {message}
    </p>
  );
}

export function GeneralSection(): JSX.Element {
  const services = useServices();
  const { bridge, store } = services;
  const settings = store.settings.value;
  const [locationError, setLocationError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  if (settings === null) {
    return <></>;
  }
  const save = (patch: Parameters<typeof updateSettings>[1]): void => {
    void updateSettings(services, patch).then(setError);
  };
  const changeLocation = async (): Promise<void> => {
    setLocationError(null);
    try {
      const picked = await bridge.call('dialog.pickFolder', {
        title: 'Choose where Memento keeps your library',
        initialPath: settings.libraryPath,
      });
      if (picked.path === null || picked.path === settings.libraryPath) {
        return;
      }
      setLocationError(await updateSettings(services, { libraryPath: picked.path }));
    } catch (e) {
      setLocationError(e instanceof Error ? e.message : 'The folder picker did not open.');
    }
  };
  return (
    <>
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
        <SettingsRow label="Start Memento with Windows" description="Opens minimised so recording is one click away." note={LATER}>
          <Toggle label="Start Memento with Windows" checked={false} disabled />
        </SettingsRow>
        <SettingsRow
          label="Keep running in the tray when closed"
          description="Lets scheduled transcription finish in the background."
          note={LATER}
        >
          <Toggle label="Keep running in the tray when closed" checked={false} disabled />
        </SettingsRow>
      </SettingsGroup>
      <SettingsGroup label="Library">
        <SettingsRow
          label="Library location"
          description="Recordings, transcripts and documents are stored here."
          below={<InlineMessage message={locationError} />}
        >
          <span class="mono settings-path" title={settings.libraryPath}>
            {settings.libraryPath}
          </span>
          <button
            class="btn ghost small-btn"
            type="button"
            aria-label="Change library location"
            onClick={() => {
              void changeLocation();
            }}
          >
            Change
          </button>
        </SettingsRow>
        <SettingsRow label="Language" description="Interface language.">
          <span class="settings-value">English</span>
        </SettingsRow>
      </SettingsGroup>
    </>
  );
}

const BITRATES: Record<Exclude<StorageCodec, 'flac'>, readonly number[]> = {
  aac: [96, 128, 160, 192, 256],
  mp3: [128, 160, 192, 256, 320],
};

const DEFAULT_BITRATE: Record<Exclude<StorageCodec, 'flac'>, number> = { aac: 160, mp3: 192 };

const CHECKPOINTS = [10, 15, 30, 60] as const;
const LOW_SPACE_GB = [2, 5, 10, 20, 50] as const;

/** The storage block after a codec change: lossless drops the bitrate; lossy keeps a valid one. */
export function withCodec(current: RecordingSettings['storage'], codec: StorageCodec): RecordingSettings['storage'] {
  if (codec === 'flac') {
    return { ...current, codec, bitrateKbps: null, downmixMono: false };
  }
  const bitrate = current.bitrateKbps !== null && BITRATES[codec].includes(current.bitrateKbps) ? current.bitrateKbps : DEFAULT_BITRATE[codec];
  return { ...current, codec, bitrateKbps: bitrate };
}

export function RecordingSection(): JSX.Element {
  const services = useServices();
  const { bridge, store } = services;
  const settings = store.settings.value;
  const sources = store.sources.value;
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    // sources.list re-enumerates each call, so the list is fresh whenever the section opens.
    bridge
      .call('sources.list')
      .then((result) => {
        store.sources.value = result.audio;
      })
      .catch((e: unknown) => {
        setError(e instanceof Error ? e.message : 'The audio sources could not be listed.');
      });
  }, [bridge, store]);

  if (settings === null) {
    return <></>;
  }
  const recording = settings.recording;
  const save = (next: Partial<RecordingSettings>): void => {
    void updateSettings(services, { recording: { ...recording, ...next } }).then(setError);
  };
  const storage = recording.storage;
  const lossy = storage.codec === 'flac' ? null : storage.codec;
  const typeOptions = [
    ...CHOOSABLE_TYPES.map((t) => ({ value: t, label: typeName(t) })),
    ...(isBuiltInType(recording.defaultType) ? [] : [{ value: recording.defaultType, label: recording.defaultType }]),
  ];

  return (
    <>
      <SettingsGroup label="Defaults">
        <SettingsRow label="Recording type" description="Decides which details and processing are offered.">
          <SelectMenu<RecordingType>
            label="Recording type"
            value={recording.defaultType}
            options={typeOptions}
            onChange={(defaultType) => {
              save({ defaultType });
            }}
          />
        </SettingsRow>
        <SettingsRow
          label="Audio sources"
          description="Pre-selected when a recording starts."
          below={
            <div class="settings-checks" role="group" aria-label="Audio sources">
              {sources === null ? (
                <span class="settings-row-desc">Looking for audio sources…</span>
              ) : (
                sources.map((source) => {
                  const on = recording.defaultSourceIds.includes(source.id);
                  return (
                    <label key={source.id} class="settings-check">
                      <input
                        class="chk"
                        type="checkbox"
                        checked={on}
                        onChange={() => {
                          save({
                            defaultSourceIds: on
                              ? recording.defaultSourceIds.filter((id) => id !== source.id)
                              : [...recording.defaultSourceIds, source.id],
                          });
                        }}
                      />
                      <span class="settings-check-text">
                        <span>{source.name}</span>
                        <span class="settings-check-note">{source.detail}</span>
                      </span>
                    </label>
                  );
                })
              )}
            </div>
          }
        />
      </SettingsGroup>
      <SettingsGroup label="Tracks and storage">
        <SettingsRow label="Keep each source as its own track" description="Needed for the best transcription and speaker identification.">
          <OnOff on />
          <Toggle label="Keep each source as its own track" checked disabled />
        </SettingsRow>
        <SettingsRow label="After recording" description="How tracks are kept once a recording is finished.">
          <Segmented<StorageCodec>
            label="After recording"
            value={storage.codec}
            options={[
              { value: 'flac', label: 'Lossless FLAC' },
              { value: 'aac', label: 'Smaller AAC' },
              { value: 'mp3', label: 'Smaller MP3' },
            ]}
            onChange={(codec) => {
              save({ storage: withCodec(storage, codec) });
            }}
          />
        </SettingsRow>
        {lossy !== null ? (
          <>
            <SettingsRow label="Bitrate" description="Higher keeps more detail and takes more space.">
              <SelectMenu<string>
                label="Bitrate"
                value={String(storage.bitrateKbps ?? DEFAULT_BITRATE[lossy])}
                options={BITRATES[lossy].map((kbps) => ({ value: String(kbps), label: `${kbps} kbps` }))}
                onChange={(value) => {
                  save({ storage: { ...storage, bitrateKbps: Number(value) } });
                }}
              />
            </SettingsRow>
            <SettingsRow label="Mix each track down to mono" description="Halves the size of stereo tracks such as system audio.">
              <OnOff on={storage.downmixMono} />
              <Toggle
                label="Mix each track down to mono"
                checked={storage.downmixMono}
                onChange={(downmixMono) => {
                  save({ storage: { ...storage, downmixMono } });
                }}
              />
            </SettingsRow>
          </>
        ) : null}
        <SettingsRow label="Save a checkpoint every" description="How much could be lost if the PC shuts down abruptly.">
          <SelectMenu<string>
            label="Save a checkpoint every"
            value={String(recording.checkpointSeconds)}
            options={CHECKPOINTS.map((s) => ({ value: String(s), label: `${s} seconds` }))}
            onChange={(value) => {
              save({ checkpointSeconds: Number(value) });
            }}
          />
        </SettingsRow>
        <SettingsRow
          label="Warn when free space is below"
          description="Recording continues until space actually runs out."
          below={<InlineMessage message={error} />}
        >
          <SelectMenu<string>
            label="Warn when free space is below"
            value={String(recording.lowSpaceGb)}
            options={LOW_SPACE_GB.map((gb) => ({ value: String(gb), label: `${gb} GB` }))}
            onChange={(value) => {
              save({ lowSpaceGb: Number(value) });
            }}
          />
        </SettingsRow>
      </SettingsGroup>
    </>
  );
}

export function StorageSection(): JSX.Element {
  const { bridge, store } = useServices();
  const library = store.library.value;
  const free = store.footer.value?.storage.freeBytes ?? null;
  const longest = library?.recordings.reduce<(typeof library.recordings)[number] | null>(
    (best, r) => (best === null || r.durationMs > best.durationMs ? r : best),
    null,
  ) ?? null;
  const [largestSize, setLargestSize] = useState<number | null>(null);

  useEffect(() => {
    if (longest === null) {
      return;
    }
    let live = true;
    bridge
      .call('project.get', { recordingId: longest.id })
      .then((project) => {
        if (live) {
          setLargestSize(project.sizeBytes);
        }
      })
      .catch(() => {
        // The row then shows the length only.
      });
    return () => {
      live = false;
    };
  }, [bridge, longest?.id]);

  const count = library?.totalCount ?? 0;
  return (
    <>
      <SettingsGroup label="Usage">
        <SettingsRow
          label="Library size"
          description={`${count} ${count === 1 ? 'recording' : 'recordings'}, all on this PC.`}
        >
          <span class="settings-value">{formatTotalDuration(library?.totalDurationMs ?? 0)}</span>
        </SettingsRow>
        <SettingsRow label="Free space" description="On the drive that holds the library.">
          <span class="settings-value">{free === null ? 'Not known yet' : formatFreeSpace(free)}</span>
        </SettingsRow>
        <SettingsRow
          label="Largest recording"
          description={longest === null ? 'Shown once you have a recording.' : `${longest.title} · ${formatDuration(longest.durationMs)}`}
        >
          <span class="settings-value">{longest === null ? 'None yet' : largestSize === null ? '…' : formatSize(largestSize)}</span>
        </SettingsRow>
      </SettingsGroup>
      <SettingsGroup label="Reclaim space">
        <SettingsRow label="Downmix tracks older than" description="Keeps a single high-quality mix and removes separate tracks." note={LATER}>
          <SelectMenu label="Downmix tracks older than" value="never" options={[{ value: 'never', label: 'Never' }]} onChange={() => undefined} disabled />
        </SettingsRow>
        <SettingsRow label="Remove video older than" description="Audio, transcript and documents are kept." note={LATER}>
          <SelectMenu label="Remove video older than" value="never" options={[{ value: 'never', label: 'Never' }]} onChange={() => undefined} disabled />
        </SettingsRow>
        <SettingsRow label="Review large recordings" description="Pick recordings to optimise or delete." note={LATER}>
          <button class="btn ghost small-btn" type="button" disabled>
            Review
          </button>
        </SettingsRow>
      </SettingsGroup>
    </>
  );
}
