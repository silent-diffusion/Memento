// The browser preview's model manager: the host's catalog ids (BRIDGE.md M2 clarification 8) with
// sizes and notes copied from the host catalog, one simulated download at a time with
// models.progress, cancel and remove. Nothing is downloaded.
import { formatSize } from '../format/storage';
import { MockHostError } from './mockSession';
import type { EngineStatusDetail, EventName, EventPayload, ModelInfo } from './types';

const MB = 1024 ** 2;
const GB = 1024 ** 3;

/** How a simulated download ends: normally, refused for space, or dropped part-way (`?models=nospace|fail`). */
export type ModelFailureMode = 'none' | 'noSpace' | 'network';

/** The ids every side uses (catalog.json on the host). */
export const MODEL_IDS = {
  turbo: 'whisper-large-v3-turbo',
  medium: 'whisper-medium',
  small: 'whisper-small',
  base: 'whisper-base',
  segmentation: 'pyannote-segmentation-3-0',
  titanet: 'nemo-titanet-small',
  eres2net: '3dspeaker-eres2net-base',
  tesseract: 'tesseract-eng',
  // M4: the Local provider's language models (ROADMAP M4a).
  qwen: 'qwen3.5-4b-q4',
  ministral: 'ministral-3-3b-q4',
} as const;

/**
 * The catalog as the preview starts: Large v3 Turbo, Small and both speaker models installed, or
 * nothing installed (`?models=none`, the first run).
 */
export function sampleModels(installed: 'sample' | 'none' = 'sample', alsoInstalled: readonly string[] = []): ModelInfo[] {
  const has = (id: string): boolean =>
    alsoInstalled.includes(id) ||
    (installed === 'sample' && ([MODEL_IDS.turbo, MODEL_IDS.small, MODEL_IDS.segmentation, MODEL_IDS.titanet] as string[]).includes(id));
  const model = (m: Omit<ModelInfo, 'installing' | 'installed'>): ModelInfo => ({ ...m, installed: has(m.id), installing: null });
  return [
    model({
      id: MODEL_IDS.turbo,
      engine: 'transcription',
      name: 'Large v3 Turbo',
      description: 'Whisper large-v3-turbo. The most accurate model; fast on a graphics card.',
      sizeBytes: 1_624_555_275,
      license: 'MIT',
      recommended: true,
      runsOn: 'gpu',
      minVramBytes: Math.round(2.5 * GB),
      accuracyNote: 'Most accurate',
      role: null,
    }),
    model({
      id: MODEL_IDS.medium,
      engine: 'transcription',
      name: 'Medium',
      description: 'Whisper medium. Accurate, slower than Large v3 Turbo on a graphics card.',
      sizeBytes: 1_533_763_059,
      license: 'MIT',
      recommended: false,
      runsOn: 'gpu',
      minVramBytes: 2 * GB,
      accuracyNote: 'Very accurate',
      role: null,
    }),
    model({
      id: MODEL_IDS.small,
      engine: 'transcription',
      name: 'Small',
      description: 'Whisper small. Good accuracy and the best choice without a graphics card.',
      sizeBytes: 487_601_967,
      license: 'MIT',
      recommended: false,
      runsOn: 'either',
      minVramBytes: 1 * GB,
      accuracyNote: 'Fast on CPU',
      role: null,
    }),
    model({
      id: MODEL_IDS.base,
      engine: 'transcription',
      name: 'Base',
      description: 'Whisper base. Smallest download; rough drafts on slow computers.',
      sizeBytes: 147_951_465,
      license: 'MIT',
      recommended: false,
      runsOn: 'either',
      minVramBytes: 512 * MB,
      accuracyNote: 'Fastest, least accurate',
      role: null,
    }),
    model({
      id: MODEL_IDS.segmentation,
      engine: 'speakers',
      name: 'Speech segmentation (pyannote 3.0)',
      description: 'Finds where each voice speaks. Needed for speaker identification together with a voice model.',
      sizeBytes: 5_992_913,
      license: 'MIT',
      recommended: true,
      runsOn: 'cpu',
      minVramBytes: null,
      accuracyNote: 'Required',
      role: 'segmentation',
    }),
    model({
      id: MODEL_IDS.titanet,
      engine: 'speakers',
      name: 'Voice model (NeMo TitaNet small, English)',
      description: 'Tells voices apart. Separated two readers exactly and kept one reader whole in testing.',
      sizeBytes: 40_257_283,
      license: 'CC-BY-4.0',
      recommended: true,
      runsOn: 'cpu',
      minVramBytes: null,
      accuracyNote: 'Most accurate',
      role: 'embedding',
    }),
    model({
      id: MODEL_IDS.eres2net,
      engine: 'speakers',
      name: 'Voice model (3D-Speaker ERes2Net base)',
      description: 'An alternative voice model trained on Mandarin speech; try it when TitaNet merges voices.',
      sizeBytes: 39_593_761,
      license: 'Apache-2.0',
      recommended: false,
      runsOn: 'cpu',
      minVramBytes: null,
      accuracyNote: 'Alternative',
      role: 'embedding',
    }),
    model({
      id: MODEL_IDS.tesseract,
      engine: 'ocr',
      name: 'Tesseract English',
      description: 'English language data for the Tesseract OCR engine (agenda photos). Used from version 0.4.',
      sizeBytes: 4_113_088,
      license: 'Apache-2.0',
      recommended: true,
      runsOn: 'cpu',
      minVramBytes: null,
      accuracyNote: 'Per-word confidence',
      role: null,
    }),
    model({
      id: MODEL_IDS.qwen,
      engine: 'llm',
      name: 'Qwen3.5 4B',
      description: 'Writes documents on this PC. Nothing leaves it. Best with a graphics card with 4 GB or more.',
      sizeBytes: 2_715_000_000,
      license: 'Apache-2.0',
      recommended: true,
      runsOn: 'either',
      minVramBytes: 4 * GB,
      accuracyNote: 'Most capable',
      role: null,
    }),
    model({
      id: MODEL_IDS.ministral,
      engine: 'llm',
      name: 'Ministral 3 3B',
      description: 'A smaller local model for computers with less graphics memory; slower on the processor alone.',
      sizeBytes: 2_020_000_000,
      license: 'Apache-2.0',
      recommended: false,
      runsOn: 'either',
      minVramBytes: 3 * GB,
      accuracyNote: 'Lighter',
      role: null,
    }),
  ];
}

export interface ModelManagerEnvironment {
  emit<E extends EventName>(event: E, payload: EventPayload<E>): void;
  /** Free bytes on the library drive. */
  freeBytes(): number;
  /** Model ids a running stage is using right now. */
  inUse(): readonly string[];
  failure: ModelFailureMode;
  /** `?models=none`: start with nothing installed, like a first run. */
  installed?: 'sample' | 'none';
  /** M4: models installed in addition (`?ai=local` installs the local language model). */
  alsoInstalled?: readonly string[];
  /** Called after a model finished installing (stages waiting for a model start by themselves). */
  onInstalled?: (modelId: string) => void;
  /** Milliseconds between progress steps (shorter in tests). */
  stepMs?: number;
}

export interface MockModelManager {
  list(): ModelInfo[];
  find(modelId: string): ModelInfo;
  install(modelId: string): void;
  cancelInstall(modelId: string): void;
  remove(modelId: string): void;
  isInstalled(modelId: string): boolean;
}

export function createMockModels(env: ModelManagerEnvironment): MockModelManager {
  const models = sampleModels(env.installed ?? 'sample', env.alsoInstalled ?? []);
  let running: { modelId: string; timer: ReturnType<typeof setInterval> } | null = null;
  const stepMs = env.stepMs ?? 250;

  const find = (modelId: string): ModelInfo => {
    const model = models.find((m) => m.id === modelId);
    if (model === undefined) {
      throw new MockHostError('models.notFound', `Memento does not know a model called '${modelId}'. Nothing was changed.`, modelId);
    }
    return model;
  };

  const update = (modelId: string, change: Partial<ModelInfo>): void => {
    const index = models.findIndex((m) => m.id === modelId);
    const model = models[index];
    if (model !== undefined) {
      models[index] = { ...model, ...change };
    }
  };

  const stop = (): void => {
    if (running !== null) {
      clearInterval(running.timer);
      running = null;
    }
  };

  return {
    list: () => models.map((m) => ({ ...m })),
    find,
    isInstalled: (modelId) => models.some((m) => m.id === modelId && m.installed),
    install: (modelId) => {
      const model = find(modelId);
      if (model.installed) {
        return;
      }
      if (running !== null) {
        const busy = find(running.modelId);
        throw new MockHostError(
          'models.busy',
          `${busy.name} is still downloading. Models download one at a time; wait for it or cancel it, then try again. Nothing was changed.`,
          busy.id,
        );
      }
      const free = env.failure === 'noSpace' ? Math.min(env.freeBytes(), 300 * MB) : env.freeBytes();
      if (model.sizeBytes > free) {
        throw new MockHostError(
          'models.noSpace',
          `${model.name} needs ${formatSize(model.sizeBytes)} and the library drive has ${formatSize(free)} free. Nothing was downloaded. Free up space, then try again.`,
          String(model.sizeBytes),
        );
      }
      const total = model.sizeBytes;
      let percent = 0;
      update(modelId, { installing: { percent: 0, bytesDone: 0 } });
      const timer = setInterval(() => {
        percent = Math.min(100, percent + 4);
        const bytesDone = Math.round((total * percent) / 100);
        if (env.failure === 'network' && percent >= 44) {
          stop();
          update(modelId, { installing: null });
          env.emit('models.progress', {
            modelId,
            percent,
            bytesDone,
            bytesTotal: total,
            state: 'failed',
            message: `The download of ${model.name} stopped at ${percent}%: the connection was lost. The partial file was removed and nothing else changed. Check the connection, then try again.`,
          });
          return;
        }
        if (percent < 100) {
          update(modelId, { installing: { percent, bytesDone } });
          env.emit('models.progress', { modelId, percent, bytesDone, bytesTotal: total, state: 'downloading', message: null });
          return;
        }
        // Verified against its SHA-256, then installed.
        stop();
        env.emit('models.progress', { modelId, percent: 100, bytesDone: total, bytesTotal: total, state: 'verifying', message: null });
        setTimeout(() => {
          update(modelId, { installing: null, installed: true });
          env.emit('models.progress', { modelId, percent: 100, bytesDone: total, bytesTotal: total, state: 'done', message: null });
          env.onInstalled?.(modelId);
        }, stepMs * 2);
      }, stepMs);
      running = { modelId, timer };
    },
    cancelInstall: (modelId) => {
      const model = find(modelId);
      if (running?.modelId === modelId) {
        stop();
        // Like the host: the download's last progress is `failed`, saying it was cancelled, before the call answers.
        const done = model.installing?.bytesDone ?? 0;
        env.emit('models.progress', {
          modelId,
          percent: 0,
          bytesDone: done,
          bytesTotal: model.sizeBytes,
          state: 'failed',
          message: `The download of ${model.name} was cancelled; the partial file was removed.`,
        });
      }
      update(modelId, { installing: null });
    },
    remove: (modelId) => {
      const model = find(modelId);
      if (env.inUse().includes(modelId)) {
        throw new MockHostError(
          'models.inUse',
          `${model.name} is transcribing a recording right now. Wait for it to finish or cancel it, then remove the model. Nothing was removed.`,
          modelId,
        );
      }
      update(modelId, { installed: false, installing: null });
    },
  };
}

/** What engine.status says about one engine, from the installed models and the chosen one. */
export function engineDetail(
  manager: MockModelManager,
  modelId: string,
  device: 'GPU' | 'CPU',
  paused: EngineStatusDetail['paused'],
): EngineStatusDetail {
  const ready = manager.isInstalled(modelId);
  return {
    ready,
    device: ready ? device : null,
    gpuName: device === 'GPU' ? 'NVIDIA GeForce RTX 4070' : null,
    freeVramBytes: device === 'GPU' ? Math.round(9.2 * GB) : null,
    // Like the host: the model it would use, installed or not.
    model: modelId,
    paused,
  };
}
