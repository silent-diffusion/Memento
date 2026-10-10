// Settings › Transcription, Speakers and Documents (DESIGN.md §11, renders/Settings.dc.html), M2.
// Every control saves at once through settings.set with only the field it changes (the host merges
// the M2 blocks field by field).
import type { JSX } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import type { EngineStatusResult, HistorySettings, ModelInfo, SpeakerSettings, TranscriptionSettings, TranscriptionTiming } from '../../bridge/types';
import { Toggle } from '../../components/Controls';
import { SelectMenu } from '../../components/Menus';
import { deviceWording } from '../../format/footer';
import { TRANSCRIPTION_LANGUAGES } from '../../format/languages';
import { formatSize } from '../../format/storage';
import { updateSettings } from '../../state/actions';
import { useServices } from '../../state/context';
import { GpuMemoryLine, useCheckAgain } from './GpuMemoryLine';
import { KnownVoices } from './KnownVoices';
import { ModelCards, useModels } from './ModelCards';
import { DocumentDefaultsRows } from './sections-m4';
import { OnOff, SettingsGroup, SettingsRow } from './SettingsParts';

function InlineMessage({ message }: { message: string | null }): JSX.Element | null {
  return message === null ? null : (
    <p class="settings-inline" role="alert">
      {message}
    </p>
  );
}

/** The threshold choices for marking low-confidence words. */
export const THRESHOLDS = [0.4, 0.5, 0.6, 0.7] as const;

/** 2.0: what the live transcript costs (measured: docs/ENGINE-NOTES.md §O). */
export const LIVE_COST =
  'A rough draft on the Recording screen, a few seconds behind, from the Small model on the processor: it uses at most a quarter of the processor threads, only while it hears each 10-second window, and gives way to recording and to any full transcription; the full transcript is still made after you stop and replaces it.';

export const LIVE_GPU =
  'Lighter on the processor. It gives way at once to any transcription or document that needs the card and carries on from the processor.';
/** Auto, or 1–20 (the host's range); the menu offers the common counts and keeps any other saved one. */
const EXPECTED_SPEAKERS: readonly string[] = ['auto', '1', '2', '3', '4', '5', '6', '7', '8', '10', '12', '15', '20'];
const KEEP_DAYS = [30, 90, 365] as const;

/** "Local · GPU (NVIDIA GeForce RTX 4070)" for the Engine row, from engine.status. */
export function engineWording(status: EngineStatusResult | null): { value: string; note: string | null } {
  if (status === null) {
    return { value: 'Checking…', note: null };
  }
  const t = status.transcription;
  if (t.paused !== null) {
    return { value: `Paused · ${t.paused}`, note: null };
  }
  const device = deviceWording(t);
  if (!t.ready || device === null) {
    return { value: 'No model installed', note: 'Install a model below' };
  }
  return { value: `Local · ${device}`, note: t.freeVramBytes === null ? null : `${formatSize(t.freeVramBytes)} video memory free` };
}

/**
 * Transcription models the current settings rely on, with why Remove is off: the default (the
 * radio already says so) and the model used without a graphics card.
 */
export function transcriptionKeep(t: TranscriptionSettings): Record<string, string> {
  return {
    [t.cpuFallbackModelId]: 'Used when there is no graphics card. Choose another model for that first.',
    [t.modelId]: 'This is the default model. Choose another default first.',
  };
}

/** The speech-segmentation model is needed for every speaker pass while Identify speakers is on. */
export function segmentationKeep(sp: SpeakerSettings, models: readonly ModelInfo[]): Record<string, string> {
  if (!sp.identify) {
    return {};
  }
  return Object.fromEntries(
    models.filter((m) => m.engine === 'speakers' && m.role === 'segmentation').map((m) => [m.id, 'Finds where each voice speaks while Identify speakers is on. Turn that off first.']),
  );
}

function useEngineStatus(): [EngineStatusResult | null, (next: EngineStatusResult) => void] {
  const { bridge, store } = useServices();
  const [status, setStatus] = useState<EngineStatusResult | null>(null);
  // Re-read whenever the footer reports a change (a model installed or removed, a pause).
  const footer = store.footer.value;
  useEffect(() => {
    let live = true;
    bridge
      .call('engine.status')
      .then((next) => {
        if (live) {
          setStatus(next);
        }
      })
      .catch((e: unknown) => {
        console.warn('[settings] engine.status failed', e);
      });
    return () => {
      live = false;
    };
  }, [bridge, footer]);
  return [status, setStatus];
}

export function TranscriptionSection(): JSX.Element {
  const services = useServices();
  const { bridge, store } = services;
  const settings = store.settings.value;
  const models = useModels(bridge);
  const [engine, setEngine] = useEngineStatus();
  const [error, setError] = useState<string | null>(null);
  const { checking, check } = useCheckAgain(async (status) => {
    setEngine(status);
    await models.reload();
  });
  if (settings === null) {
    return <></>;
  }
  const t = settings.transcription;
  const save = (next: Partial<TranscriptionSettings>): void => {
    void updateSettings(services, { transcription: next }).then(setError);
  };
  const cpuModels = models.state.models.filter((m) => m.engine === 'transcription' && m.installed && m.runsOn !== 'gpu');
  const engineText = engineWording(engine);
  const languageOptions = TRANSCRIPTION_LANGUAGES.some((l) => l.value === t.language)
    ? TRANSCRIPTION_LANGUAGES
    : [...TRANSCRIPTION_LANGUAGES, { value: t.language, label: t.language }];

  return (
    <>
      <SettingsGroup label="When">
        <SettingsRow label="Transcribe automatically" description="Otherwise, transcribe from the recording page when you want it.">
          <OnOff on={t.auto} />
          <Toggle
            label="Transcribe automatically"
            checked={t.auto}
            onChange={(auto) => {
              save({ auto });
            }}
          />
        </SettingsRow>
        <SettingsRow label="Live transcript while recording" description={LIVE_COST}>
          <OnOff on={t.timing === 'during'} />
          <Toggle
            label="Live transcript while recording"
            checked={t.timing === 'during'}
            onChange={(on) => {
              const timing: TranscriptionTiming = on ? 'during' : 'after';
              save({ timing });
            }}
          />
        </SettingsRow>
        {t.timing === 'during' ? (
          <SettingsRow label="Use the graphics card for the live transcript" description={LIVE_GPU}>
            <OnOff on={t.liveOnGpu} />
            <Toggle
              label="Use the graphics card for the live transcript"
              checked={t.liveOnGpu}
              onChange={(liveOnGpu) => {
                save({ liveOnGpu });
              }}
            />
          </SettingsRow>
        ) : null}
        <SettingsRow label="Pause when the PC is busy" description="Recording is never paused, only transcription.">
          <OnOff on={t.pauseWhenBusy} />
          <Toggle
            label="Pause when the PC is busy"
            checked={t.pauseWhenBusy}
            onChange={(pauseWhenBusy) => {
              save({ pauseWhenBusy });
            }}
          />
        </SettingsRow>
      </SettingsGroup>

      <SettingsGroup label="Engine">
        <SettingsRow
          label="Engine"
          description="Falls back to the CPU when no GPU is available."
          below={
            <GpuMemoryLine
              memory={engine?.transcription.gpuMemory ?? null}
              note={engine?.transcription.note ?? null}
              checking={checking}
              onCheck={check}
            />
          }
        >
          {engineText.note === null ? null : <span class="settings-note">{engineText.note}</span>}
          <span class="settings-value" data-testid="engine-value">
            {engineText.value}
          </span>
        </SettingsRow>
        <SettingsRow
          label="Model"
          description="Larger models are more accurate and slower. Downloads are checked before they are used."
          below={
            <ModelCards
              api={models}
              engine="transcription"
              defaultId={t.modelId}
              label="Default transcription model"
              keep={transcriptionKeep(t)}
              onDefault={(modelId) => {
                save({ modelId });
              }}
            />
          }
        />
        <SettingsRow label="Without a graphics card" description="The model used when the GPU is busy or missing.">
          <SelectMenu<string>
            label="Model without a graphics card"
            value={t.cpuFallbackModelId}
            options={
              cpuModels.length === 0
                ? [{ value: t.cpuFallbackModelId, label: `${models.state.models.find((m) => m.id === t.cpuFallbackModelId)?.name ?? t.cpuFallbackModelId} · not installed` }]
                : cpuModels.map((m) => ({ value: m.id, label: `${m.name} · ${m.accuracyNote}` }))
            }
            onChange={(cpuFallbackModelId) => {
              save({ cpuFallbackModelId });
            }}
          />
        </SettingsRow>
        <SettingsRow label="Language" description="Detected per recording when set to Auto.">
          <SelectMenu<string>
            label="Language"
            value={t.language}
            options={languageOptions}
            onChange={(language) => {
              save({ language });
            }}
          />
        </SettingsRow>
        <SettingsRow label="Keep word-level timestamps and confidence" description="Used to highlight uncertain words in the transcript.">
          <OnOff on={t.keepWordTimestamps} />
          <Toggle
            label="Keep word-level timestamps and confidence"
            checked={t.keepWordTimestamps}
            onChange={(keepWordTimestamps) => {
              save({ keepWordTimestamps });
            }}
          />
        </SettingsRow>
        <SettingsRow
          label="Mark words as uncertain below"
          description="Words the engine is less sure of get a dotted underline. Applies to new transcripts."
          below={<InlineMessage message={error} />}
        >
          <SelectMenu<string>
            label="Mark words as uncertain below"
            value={String(t.lowConfidenceThreshold)}
            options={THRESHOLDS.map((v) => ({ value: String(v), label: `${Math.round(v * 100)}% confidence` }))}
            onChange={(value) => {
              save({ lowConfidenceThreshold: Number(value) });
            }}
          />
        </SettingsRow>
      </SettingsGroup>
    </>
  );
}

export function SpeakersSection(): JSX.Element {
  const services = useServices();
  const { bridge, store } = services;
  const settings = store.settings.value;
  const models = useModels(bridge);
  const [error, setError] = useState<string | null>(null);
  if (settings === null) {
    return <></>;
  }
  const sp = settings.speakers;
  const save = (next: Partial<SpeakerSettings>): void => {
    void updateSettings(services, { speakers: next }).then(setError);
  };
  return (
    <>
      <SettingsGroup label="Identification">
        <SettingsRow label="Identify speakers" description="Lines are labelled Speaker 1, 2, 3 until you rename them.">
          <OnOff on={sp.identify} />
          <Toggle
            label="Identify speakers"
            checked={sp.identify}
            onChange={(identify) => {
              save({ identify });
            }}
          />
        </SettingsRow>
        <SettingsRow label="Expected speakers" description="Leave on Auto unless results are consistently wrong. Used when one track has speech.">
          <SelectMenu<string>
            label="Expected speakers"
            value={String(sp.expectedSpeakers)}
            options={(EXPECTED_SPEAKERS.includes(String(sp.expectedSpeakers)) ? EXPECTED_SPEAKERS : [...EXPECTED_SPEAKERS, String(sp.expectedSpeakers)]).map((v) => ({
              value: v,
              label: v === 'auto' ? 'Auto' : v === '1' ? '1 person' : `${v} people`,
            }))}
            onChange={(value) => {
              save({ expectedSpeakers: value === 'auto' ? 'auto' : Number(value) });
            }}
          />
        </SettingsRow>
        <SettingsRow
          label="Remember speakers by voice"
          description="When you name a speaker in Review, Memento learns that voice and suggests the name when it hears the voice in later recordings; it never names anyone by itself. Voice profiles are signatures, not audio, stay on this PC and can be deleted below at any time."
          below={<InlineMessage message={error} />}
        >
          <OnOff on={sp.rememberVoices} />
          <Toggle
            label="Remember speakers by voice"
            checked={sp.rememberVoices}
            onChange={(rememberVoices) => {
              save({ rememberVoices });
            }}
          />
        </SettingsRow>
        {/* 2.0: the voices learned so far, each with Suggest and Forget (DESIGN.md §19). */}
        <KnownVoices />
      </SettingsGroup>
      <SettingsGroup label="Speaker models">
        <SettingsRow
          label="Speech segmentation"
          description="Finds where each voice speaks. Needed together with a voice model."
          below={
            <ModelCards
              api={models}
              engine="speakers"
              role="segmentation"
              selectable={false}
              defaultId=""
              label="Speech segmentation model"
              onDefault={() => undefined}
              keep={segmentationKeep(sp, models.state.models)}
            />
          }
        />
        <SettingsRow
          label="Voice model"
          description="Tells voices apart. Runs on this PC; voices are never uploaded."
          below={
            <ModelCards
              api={models}
              engine="speakers"
              role="embedding"
              defaultId={sp.embeddingModelId}
              label="Default voice model"
              onDefault={(embeddingModelId) => {
                save({ embeddingModelId });
              }}
            />
          }
        />
      </SettingsGroup>
    </>
  );
}

export function DocumentsSection(): JSX.Element {
  const services = useServices();
  const settings = services.store.settings.value;
  const [error, setError] = useState<string | null>(null);
  if (settings === null) {
    return <></>;
  }
  const h = settings.history;
  const save = (next: Partial<HistorySettings>): void => {
    void updateSettings(services, { history: next }).then(setError);
  };
  const dayOptions = (KEEP_DAYS as readonly number[]).includes(h.keepDays) ? KEEP_DAYS : [...KEEP_DAYS, h.keepDays];
  return (
    <>
      <SettingsGroup label="Defaults">
        {/* M4: live defaults and the templates and styles manager (sections-m4.tsx). */}
        <DocumentDefaultsRows />
      </SettingsGroup>
      <SettingsGroup label="History">
        <SettingsRow label="Keep version history" description="Previous versions of transcripts and documents can be restored.">
          <OnOff on={h.keepVersions} />
          <Toggle
            label="Keep version history"
            checked={h.keepVersions}
            onChange={(keepVersions) => {
              save({ keepVersions });
            }}
          />
        </SettingsRow>
        <SettingsRow label="Keep versions for" description="Older versions are removed to save space." below={<InlineMessage message={error} />}>
          <SelectMenu<string>
            label="Keep versions for"
            value={String(h.keepDays)}
            disabled={!h.keepVersions}
            options={dayOptions.map((d) => ({ value: String(d), label: `${d} days` }))}
            onChange={(value) => {
              save({ keepDays: Number(value) });
            }}
          />
        </SettingsRow>
      </SettingsGroup>
    </>
  );
}
