// The browser preview's model manager: a small catalog of transcription and speaker models, one
// simulated download at a time with models.progress, cancel and remove. Sizes and notes are
// illustrative; nothing is downloaded.
import { formatSize } from '../format/storage';
import { MockHostError } from './mockSession';
import type { EngineStatusDetail, EventName, EventPayload, ModelInfo } from './types';

const MB = 1024 ** 2;
const GB = 1024 ** 3;

/** How a simulated download ends: normally, refused for space, or dropped part-way (`?models=nospace|fail`). */
export type ModelFailureMode = 'none' | 'noSpace' | 'network';

export function sampleModels(): ModelInfo[] {
  const model = (m: Omit<ModelInfo, 'installing'>): ModelInfo => ({ ...m, installing: null });
  return [
    model({
      id: 'large-v3',
      engine: 'transcription',
      name: 'Large v3',
      description: 'The most accurate model, for meetings with several voices, accents and technical words.',
      sizeBytes: Math.round(1.5 * GB),
      license: 'MIT',
      installed: true,
      recommended: true,
      runsOn: 'gpu',
      minVramBytes: 4 * GB,
      accuracyNote: 'Most accurate',
    }),
    model({
      id: 'large-v3-turbo',
      engine: 'transcription',
      name: 'Large v3 Turbo',
      description: 'Nearly as accurate as Large v3 and several times faster on a graphics card.',
      sizeBytes: Math.round(0.8 * GB),
      license: 'MIT',
      installed: false,
      recommended: false,
      runsOn: 'gpu',
      minVramBytes: 3 * GB,
      accuracyNote: 'Fast and accurate',
    }),
    model({
      id: 'medium',
      engine: 'transcription',
      name: 'Medium',
      description: 'A balance of speed and accuracy that also runs on older graphics cards.',
      sizeBytes: Math.round(0.77 * GB),
      license: 'MIT',
      installed: false,
      recommended: false,
      runsOn: 'either',
      minVramBytes: 2 * GB,
      accuracyNote: 'Balanced',
    }),
    model({
      id: 'small',
      engine: 'transcription',
      name: 'Small',
      description: 'Used when no graphics card is available. Good for clear speech and dictation.',
      sizeBytes: 466 * MB,
      license: 'MIT',
      installed: true,
      recommended: false,
      runsOn: 'cpu',
      minVramBytes: null,
      accuracyNote: 'Fast on CPU',
    }),
    model({
      id: 'base',
      engine: 'transcription',
      name: 'Base',
      description: 'The quickest model; expect more mistakes with names and numbers.',
      sizeBytes: 142 * MB,
      license: 'MIT',
      installed: false,
      recommended: false,
      runsOn: 'cpu',
      minVramBytes: null,
      accuracyNote: 'Fastest, least accurate',
    }),
    model({
      id: 'voice-resnet34',
      engine: 'speakers',
      name: 'Voice embeddings · ResNet34',
      description: 'Tells voices apart in meetings of up to about eight people.',
      sizeBytes: 26 * MB,
      license: 'Apache-2.0',
      installed: true,
      recommended: true,
      runsOn: 'either',
      minVramBytes: null,
      accuracyNote: 'Recommended for meetings',
    }),
    model({
      id: 'voice-ecapa',
      engine: 'speakers',
      name: 'Voice embeddings · ECAPA',
      description: 'More accurate with many similar voices, a little slower.',
      sizeBytes: 83 * MB,
      license: 'Apache-2.0',
      installed: false,
      recommended: false,
      runsOn: 'either',
      minVramBytes: null,
      accuracyNote: 'Best with many speakers',
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
  const models = sampleModels();
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
          'models.downloadFailed',
          `${busy.name} is still downloading. Models download one at a time; wait for it or cancel it, then try again. Nothing was changed.`,
          'busy',
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
        }, stepMs * 2);
      }, stepMs);
      running = { modelId, timer };
    },
    cancelInstall: (modelId) => {
      find(modelId);
      if (running?.modelId === modelId) {
        stop();
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
  paused: string | null,
): EngineStatusDetail {
  const ready = manager.isInstalled(modelId);
  return {
    ready,
    device: ready ? device : null,
    gpuName: device === 'GPU' ? 'NVIDIA GeForce RTX 4070' : null,
    freeVramBytes: device === 'GPU' ? Math.round(9.2 * GB) : null,
    model: ready ? modelId : null,
    paused,
  };
}
