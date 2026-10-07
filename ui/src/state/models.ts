// Settings › Transcription and Speakers model cards (DESIGN.md §11, §18 "Onboarding of transcription
// models"): the catalog from models.list and, per model, where its install or removal stands. A
// pure reducer, driven by the page's own requests and the host's models.progress events.
import type { ModelInfo, ModelsProgressPayload } from '../bridge/types';

export type ModelPhase =
  | { kind: 'idle' }
  /** models.install was sent; no progress yet. */
  | { kind: 'starting' }
  | { kind: 'downloading'; percent: number; bytesDone: number; bytesTotal: number }
  | { kind: 'verifying' }
  | { kind: 'cancelling' }
  | { kind: 'removing' }
  /** The install was refused (models.noSpace, …) or the download failed; `message` is the host's. */
  | { kind: 'failed'; message: string; code: string | null };

export interface ModelsState {
  models: ModelInfo[];
  phases: Record<string, ModelPhase>;
  loaded: boolean;
  /** models.list itself failed. */
  error: string | null;
}

export type ModelsAction =
  | { type: 'loaded'; models: ModelInfo[] }
  | { type: 'loadFailed'; message: string }
  | { type: 'installRequested'; modelId: string }
  | { type: 'installRefused'; modelId: string; code: string | null; message: string }
  | { type: 'progress'; payload: ModelsProgressPayload }
  | { type: 'cancelRequested'; modelId: string }
  | { type: 'cancelled'; modelId: string }
  | { type: 'removeRequested'; modelId: string }
  | { type: 'removed'; modelId: string }
  | { type: 'removeRefused'; modelId: string; code: string | null; message: string }
  | { type: 'dismiss'; modelId: string };

export const INITIAL_MODELS: ModelsState = { models: [], phases: {}, loaded: false, error: null };

const IDLE: ModelPhase = { kind: 'idle' };

export function phaseOf(state: ModelsState, modelId: string): ModelPhase {
  return state.phases[modelId] ?? IDLE;
}

/** True while some model is downloading: the host downloads one at a time. */
export function isBusy(state: ModelsState): boolean {
  return Object.values(state.phases).some((p) => p.kind === 'starting' || p.kind === 'downloading' || p.kind === 'verifying');
}

function withPhase(state: ModelsState, modelId: string, phase: ModelPhase): ModelsState {
  return { ...state, phases: { ...state.phases, [modelId]: phase } };
}

function withModel(state: ModelsState, modelId: string, change: Partial<ModelInfo>): ModelsState {
  return { ...state, models: state.models.map((m) => (m.id === modelId ? { ...m, ...change } : m)) };
}

export function modelsReducer(state: ModelsState, action: ModelsAction): ModelsState {
  switch (action.type) {
    case 'loaded': {
      // A download already running (the page was reloaded) shows its progress straight away.
      const phases: Record<string, ModelPhase> = {};
      for (const model of action.models) {
        const previous = state.phases[model.id];
        if (model.installing !== null) {
          phases[model.id] = { kind: 'downloading', percent: model.installing.percent, bytesDone: model.installing.bytesDone, bytesTotal: model.sizeBytes };
        } else if (previous?.kind === 'failed') {
          phases[model.id] = previous;
        }
      }
      return { models: action.models, phases, loaded: true, error: null };
    }
    case 'loadFailed':
      return { ...state, loaded: true, error: action.message };
    case 'installRequested':
      return withPhase(state, action.modelId, { kind: 'starting' });
    case 'installRefused':
      return withPhase(state, action.modelId, { kind: 'failed', message: action.message, code: action.code });
    case 'progress': {
      const { modelId, state: progress } = action.payload;
      const current = phaseOf(state, modelId);
      // A cancel the page asked for wins over a late progress event.
      if (current.kind === 'cancelling' && progress !== 'done') {
        return state;
      }
      switch (progress) {
        case 'downloading':
          return withModel(withPhase(state, modelId, {
            kind: 'downloading',
            percent: action.payload.percent,
            bytesDone: action.payload.bytesDone,
            bytesTotal: action.payload.bytesTotal,
          }), modelId, { installing: { percent: action.payload.percent, bytesDone: action.payload.bytesDone } });
        case 'verifying':
          return withPhase(state, modelId, { kind: 'verifying' });
        case 'done':
          return withModel(withPhase(state, modelId, IDLE), modelId, { installed: true, installing: null });
        case 'failed':
          return withModel(
            withPhase(state, modelId, {
              kind: 'failed',
              message: action.payload.message ?? 'The download did not finish. The partial file was removed; nothing else changed.',
              code: 'models.downloadFailed',
            }),
            modelId,
            { installing: null },
          );
      }
      return state;
    }
    case 'cancelRequested':
      return withPhase(state, action.modelId, { kind: 'cancelling' });
    case 'cancelled':
      return withModel(withPhase(state, action.modelId, IDLE), action.modelId, { installing: null });
    case 'removeRequested':
      return withPhase(state, action.modelId, { kind: 'removing' });
    case 'removed':
      return withModel(withPhase(state, action.modelId, IDLE), action.modelId, { installed: false, installing: null });
    case 'removeRefused':
      return withPhase(state, action.modelId, { kind: 'failed', message: action.message, code: action.code });
    case 'dismiss':
      return withPhase(state, action.modelId, IDLE);
  }
}
