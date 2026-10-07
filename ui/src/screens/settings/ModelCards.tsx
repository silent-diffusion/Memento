// Model cards for Settings › Transcription and Speakers (DESIGN.md §11; model onboarding is §18 "not
// yet designed", so this follows §5.6 cards, §5.5 progress, §5.9 radios and the §17 inline error):
// name, Recommended tag, description, size, licence, what it runs on, installed state, Install with
// the size shown before it starts, a progress bar with Cancel, Remove, and the radio that makes it
// the default.
import type { JSX } from 'preact';
import { useEffect, useReducer } from 'preact/hooks';
import { BridgeCallError, type BridgeClient } from '../../bridge/client';
import type { ModelEngine, ModelInfo } from '../../bridge/types';
import { formatSize } from '../../format/storage';
import { INITIAL_MODELS, isBusy, modelsReducer, phaseOf, type ModelPhase, type ModelsState } from '../../state/models';

function codeOf(error: unknown): string | null {
  return error instanceof BridgeCallError ? error.code : null;
}

function messageOf(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

export interface ModelsApi {
  state: ModelsState;
  install: (modelId: string) => void;
  cancel: (modelId: string) => void;
  remove: (modelId: string) => void;
  dismiss: (modelId: string) => void;
}

/** models.list once, then models.progress as downloads move. */
export function useModels(bridge: BridgeClient): ModelsApi {
  const [state, dispatch] = useReducer(modelsReducer, INITIAL_MODELS);

  useEffect(() => {
    let live = true;
    bridge
      .call('models.list')
      .then(({ models }) => {
        if (live) {
          dispatch({ type: 'loaded', models });
        }
      })
      .catch((e: unknown) => {
        if (live) {
          dispatch({ type: 'loadFailed', message: messageOf(e, 'The models could not be listed.') });
        }
      });
    const off = bridge.on('models.progress', (payload) => {
      dispatch({ type: 'progress', payload });
    });
    return () => {
      live = false;
      off();
    };
  }, [bridge]);

  return {
    state,
    install: (modelId) => {
      dispatch({ type: 'installRequested', modelId });
      bridge.call('models.install', { modelId }).catch((e: unknown) => {
        dispatch({ type: 'installRefused', modelId, code: codeOf(e), message: messageOf(e, 'The download did not start. Nothing was changed.') });
      });
    },
    cancel: (modelId) => {
      dispatch({ type: 'cancelRequested', modelId });
      bridge
        .call('models.cancelInstall', { modelId })
        .then(() => {
          dispatch({ type: 'cancelled', modelId });
        })
        .catch((e: unknown) => {
          dispatch({ type: 'installRefused', modelId, code: codeOf(e), message: messageOf(e, 'The download could not be cancelled.') });
        });
    },
    remove: (modelId) => {
      dispatch({ type: 'removeRequested', modelId });
      bridge
        .call('models.remove', { modelId })
        .then(() => {
          dispatch({ type: 'removed', modelId });
        })
        .catch((e: unknown) => {
          dispatch({ type: 'removeRefused', modelId, code: codeOf(e), message: messageOf(e, 'The model was not removed.') });
        });
    },
    dismiss: (modelId) => {
      dispatch({ type: 'dismiss', modelId });
    },
  };
}

const RUNS_ON: Record<ModelInfo['runsOn'], string> = { gpu: 'Graphics card', cpu: 'Processor (CPU)', either: 'Graphics card or CPU' };

/** "1.5 GB · MIT · Graphics card, 4 GB video memory". */
export function modelFacts(model: ModelInfo): string {
  const runs = model.minVramBytes === null ? RUNS_ON[model.runsOn] : `${RUNS_ON[model.runsOn]}, ${formatSize(model.minVramBytes)} video memory`;
  return [formatSize(model.sizeBytes), model.license, runs].join(' · ');
}

/** The headline of a failed install, by the host's code (DESIGN.md §17: name the thing first). */
function failureLead(model: ModelInfo, phase: Extract<ModelPhase, { kind: 'failed' }>): string {
  switch (phase.code) {
    case 'models.noSpace':
      return `Not enough space for ${model.name}.`;
    case 'models.inUse':
      return `${model.name} is in use.`;
    default:
      return `Couldn't download ${model.name}.`;
  }
}

interface ModelCardProps {
  model: ModelInfo;
  phase: ModelPhase;
  isDefault: boolean;
  /** Why this model cannot be removed (another setting uses it), or null. */
  keepReason: string | null;
  /** Another model is downloading; one at a time. */
  busy: boolean;
  group: string;
  onDefault: () => void;
  onInstall: () => void;
  onCancel: () => void;
  onRemove: () => void;
  onDismiss: () => void;
}

function ModelCard({ model, phase, isDefault, keepReason, busy, group, onDefault, onInstall, onCancel, onRemove, onDismiss }: ModelCardProps): JSX.Element {
  const downloading = phase.kind === 'starting' || phase.kind === 'downloading' || phase.kind === 'verifying' || phase.kind === 'cancelling';
  const radioId = `${group}-${model.id}`;
  const status = model.installed ? (isDefault ? 'Installed · default' : 'Installed') : downloading ? 'Downloading' : 'Not installed';
  return (
    <div class={isDefault ? 'model-card model-card--default' : 'model-card'} data-model-id={model.id}>
      <div class="model-main">
        <input
          id={radioId}
          class="chk model-radio"
          type="radio"
          name={group}
          checked={isDefault}
          disabled={!model.installed}
          aria-describedby={`${radioId}-desc`}
          onChange={onDefault}
        />
        <div class="model-text">
          <label class="model-name" for={radioId}>
            {model.name}
            {model.recommended ? <span class="model-tag">Recommended</span> : null}
            <span class="model-note">{model.accuracyNote}</span>
          </label>
          <span id={`${radioId}-desc`} class="model-desc">
            {model.description}
          </span>
          <span class="model-facts">
            {modelFacts(model)} · <span class={model.installed ? 'model-installed' : ''}>{status}</span>
          </span>
        </div>
        <div class="model-actions">
          {model.installed ? (
            <button
              class="btn ghost small-btn"
              type="button"
              aria-label={`Remove ${model.name}`}
              disabled={isDefault || keepReason !== null || phase.kind === 'removing'}
              title={isDefault ? 'This is the default model. Choose another default first.' : (keepReason ?? undefined)}
              onClick={onRemove}
            >
              {phase.kind === 'removing' ? 'Removing…' : 'Remove'}
            </button>
          ) : downloading ? (
            <button class="btn ghost small-btn" type="button" aria-label={`Cancel the download of ${model.name}`} disabled={phase.kind === 'cancelling'} onClick={onCancel}>
              Cancel
            </button>
          ) : (
            <button
              class="btn ghost small-btn"
              type="button"
              aria-label={`Install ${model.name}, ${formatSize(model.sizeBytes)}`}
              disabled={busy}
              title={busy ? 'Another model is downloading. Models download one at a time.' : undefined}
              onClick={onInstall}
            >
              Install · {formatSize(model.sizeBytes)}
            </button>
          )}
        </div>
      </div>
      {downloading ? (
        <div class="model-progress">
          <div class="model-progress-line">
            <span>{phase.kind === 'verifying' ? 'Checking the download' : phase.kind === 'cancelling' ? 'Cancelling' : 'Downloading'}</span>
            <span class="model-progress-status">
              {phase.kind === 'downloading'
                ? `${Math.round(phase.percent)}% · ${formatSize(phase.bytesDone)} of ${formatSize(phase.bytesTotal)}`
                : phase.kind === 'verifying'
                  ? '100%'
                  : 'Starting'}
            </span>
          </div>
          <div
            class="model-track"
            role="progressbar"
            aria-label={`Downloading ${model.name}`}
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={phase.kind === 'downloading' ? Math.round(phase.percent) : phase.kind === 'verifying' ? 100 : 0}
          >
            <div class="model-fill" style={{ width: `${phase.kind === 'downloading' ? phase.percent : phase.kind === 'verifying' ? 100 : 0}%` }} />
          </div>
        </div>
      ) : null}
      {phase.kind === 'failed' ? (
        <div class="model-error" role="alert">
          <span class="model-error-text">
            <span class="model-error-lead">{failureLead(model, phase)}</span> {phase.message}
          </span>
          <span class="model-error-actions">
            {phase.code === 'models.inUse' ? null : (
              <button class="btn ghost small-btn" type="button" disabled={busy} onClick={onInstall}>
                Try again
              </button>
            )}
            <button class="btn ghost small-btn" type="button" onClick={onDismiss}>
              Dismiss
            </button>
          </span>
        </div>
      ) : null}
    </div>
  );
}

interface ModelCardsProps {
  api: ModelsApi;
  engine: ModelEngine;
  /** The model the setting points at. */
  defaultId: string;
  label: string;
  onDefault: (modelId: string) => void;
  /** Models another setting relies on, with the reason they cannot be removed. */
  keep?: Readonly<Record<string, string>>;
}

/** The catalog for one engine as a radio group of cards. */
export function ModelCards({ api, engine, defaultId, label, onDefault, keep = {} }: ModelCardsProps): JSX.Element {
  const { state } = api;
  if (!state.loaded) {
    return <p class="settings-row-desc model-loading">Reading the installed models…</p>;
  }
  if (state.error !== null) {
    return (
      <p class="settings-inline" role="alert">
        {state.error}
      </p>
    );
  }
  const models = state.models.filter((m) => m.engine === engine);
  const busy = isBusy(state);
  return (
    <div class="model-cards" role="radiogroup" aria-label={label}>
      {models.map((model) => {
        const phase = phaseOf(state, model.id);
        const ownDownload = phase.kind === 'starting' || phase.kind === 'downloading' || phase.kind === 'verifying';
        return (
          <ModelCard
            key={model.id}
            model={model}
            phase={phase}
            group={`default-${engine}`}
            isDefault={model.id === defaultId}
            keepReason={keep[model.id] ?? null}
            busy={busy && !ownDownload}
            onDefault={() => {
              onDefault(model.id);
            }}
            onInstall={() => {
              api.install(model.id);
            }}
            onCancel={() => {
              api.cancel(model.id);
            }}
            onRemove={() => {
              api.remove(model.id);
            }}
            onDismiss={() => {
              api.dismiss(model.id);
            }}
          />
        );
      })}
    </div>
  );
}
