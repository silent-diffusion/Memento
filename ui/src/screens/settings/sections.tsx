// The Recording section (DESIGN.md §11). General and Storage and history moved to sections-m3.tsx in M3.
import type { JSX } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import type { RecordingSettings, RecordingType, StorageCodec } from '../../bridge/types';
import { Segmented, Toggle } from '../../components/Controls';
import { SelectMenu } from '../../components/Menus';
import { CHOOSABLE_TYPES, isBuiltInType, typeName } from '../../format/recording';
import { updateSettings } from '../../state/actions';
import { useServices } from '../../state/context';
import { OnOff, SettingsGroup, SettingsRow } from './SettingsParts';

function InlineMessage({ message }: { message: string | null }): JSX.Element | null {
  return message === null ? null : (
    <p class="settings-inline" role="alert">
      {message}
    </p>
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
