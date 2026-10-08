// Live output (DESIGN.md §10, §11): the exchange between Memento and the model while a document is
// generated, from generation.output. Kept in memory beside the store, never written anywhere: it
// lives from the moment a generation starts until the Builder or the document viewer that shows it
// is left (or the next generation starts), so the finished exchange can still be read afterwards.
import { signal, type Signal } from '@preact/signals';
import type { GenerationOutput, GenerationOutputStep, GenerationProgress, ProviderInfo } from '../../bridge/types';
import type { AppServices } from '../../state/context';
import type { AppStore } from '../../state/store';
import { generationOf } from './generation';

export type LivePassStatus = 'running' | 'done' | 'stopped';

export interface LivePass {
  id: string;
  step: GenerationOutputStep;
  title: string;
  /** Work done in code (segment, reduce, grounding): no request, the summary is the reply. */
  inCode: boolean;
  /** The exact text sent ('' for a step done in code). */
  request: string;
  /** The reply so far (streamed) or whole; for a step done in code, what it did. */
  reply: string;
  /** The reply streams in as tokens (the local model) or arrives whole (a cloud provider). */
  streamed: boolean;
  status: LivePassStatus;
  outputTokens: number | null;
  promptTokens: number | null;
  tokensPerSecond: number | null;
  elapsedMs: number | null;
  stopReason: string | null;
}

export type LiveOutcome = 'running' | 'done' | 'failed' | 'cancelled';

export interface LiveOutput {
  jobId: string;
  recordingId: string;
  provider: Pick<ProviderInfo, 'id' | 'name' | 'kind' | 'modelLabel'>;
  /** The document regenerated into, or written (once done). */
  documentId: string | null;
  passes: LivePass[];
  /** generation.output said done: nothing more arrives. */
  ended: boolean;
  outcome: LiveOutcome;
}

interface LiveRegistry {
  live: Signal<LiveOutput | null>;
  open: Signal<boolean>;
  /** The screens that can show it: `builder:<recordingId>`, `viewer:<documentId>`. */
  holders: Set<string>;
  /** Done in a Builder that then opened the viewer: kept until that viewer is left. */
  handedOver: boolean;
}

const registries = new WeakMap<AppStore, LiveRegistry>();

function registryOf(store: AppStore): LiveRegistry {
  let registry = registries.get(store);
  if (registry === undefined) {
    registry = { live: signal(null), open: signal(false), holders: new Set(), handedOver: false };
    registries.set(store, registry);
  }
  return registry;
}

export function liveOutputOf(store: AppStore): Signal<LiveOutput | null> {
  return registryOf(store).live;
}

export function liveOutputOpen(store: AppStore): Signal<boolean> {
  return registryOf(store).open;
}

/**
 * Opens the sheet. Before the first event of a running generation it opens empty ("Loading the model…") and takes
 * the job's events as they come.
 */
export function openLiveOutput(store: AppStore): void {
  const registry = registryOf(store);
  if (registry.live.value === null) {
    const job = generationOf(store).value;
    if (job === null || job.phase === 'failed' || job.phase === 'done' || job.phase === 'cancelled') {
      return;
    }
    registry.live.value = fresh(job.jobId ?? '', job);
  }
  registry.open.value = true;
}

function fresh(jobId: string, job: NonNullable<ReturnType<typeof generationOf>['value']>): LiveOutput {
  return {
    jobId,
    recordingId: job.recordingId,
    provider: { id: job.provider.id, name: job.provider.name, kind: job.provider.kind, modelLabel: job.provider.modelLabel },
    documentId: job.documentId,
    passes: [],
    ended: false,
    outcome: 'running',
  };
}

/** The exchange belongs to this job (an empty sheet opened before generation.start answered takes the first job). */
function belongs(live: LiveOutput, jobId: string): boolean {
  return live.jobId === jobId || (live.jobId === '' && live.outcome === 'running');
}

export function closeLiveOutput(store: AppStore): void {
  registryOf(store).open.value = false;
}

function clear(registry: LiveRegistry): void {
  registry.live.value = null;
  registry.open.value = false;
  registry.handedOver = false;
}

/** A new generation starts: the previous exchange goes. */
export function resetLiveOutput(store: AppStore): void {
  clear(registryOf(store));
}

function held(registry: LiveRegistry, live: LiveOutput): boolean {
  return registry.holders.has(`builder:${live.recordingId}`) || (live.documentId !== null && registry.holders.has(`viewer:${live.documentId}`));
}

/** A Builder or viewer that can show the live output is on screen. */
export function holdLiveOutput(store: AppStore, holder: string): void {
  const registry = registryOf(store);
  registry.holders.add(holder);
  const live = registry.live.value;
  if (live !== null && registry.handedOver && holder === `viewer:${live.documentId ?? ''}`) {
    registry.handedOver = false;
  }
}

/** That screen is left: a finished exchange nobody can show any more is dropped. */
export function releaseLiveOutput(store: AppStore, holder: string): void {
  const registry = registryOf(store);
  registry.holders.delete(holder);
  const live = registry.live.value;
  if (live === null || !live.ended || live.outcome === 'running' || held(registry, live)) {
    return;
  }
  // The Builder opens the document it wrote; the viewer takes the exchange over.
  if (holder.startsWith('builder:') && live.outcome === 'done' && live.documentId !== null && !registry.handedOver) {
    registry.handedOver = true;
    return;
  }
  clear(registry);
}

function emptyPass(event: GenerationOutput): LivePass {
  return {
    id: event.passId,
    step: event.step ?? 'map',
    title: event.title ?? event.passId,
    inCode: event.kind === 'step',
    request: event.kind === 'request' ? event.text : '',
    reply: event.kind === 'step' ? event.text : '',
    streamed: event.streamed === true,
    status: event.kind === 'step' ? 'done' : 'running',
    outputTokens: null,
    promptTokens: null,
    tokensPerSecond: null,
    elapsedMs: event.kind === 'step' ? event.elapsedMs : null,
    stopReason: null,
  };
}

/** One generation.output event applied to the exchange (pure, for tests). */
export function applyOutput(live: LiveOutput, event: GenerationOutput): LiveOutput {
  switch (event.kind) {
    case 'step':
    case 'request':
      return { ...live, passes: [...live.passes, emptyPass(event)] };
    case 'token':
    case 'reply': {
      const index = live.passes.findIndex((p) => p.id === event.passId);
      const pass = live.passes[index];
      if (pass?.status !== 'running') {
        return live;
      }
      const next: LivePass =
        event.kind === 'token'
          ? {
              ...pass,
              reply: pass.reply + event.text,
              outputTokens: event.outputTokens ?? pass.outputTokens,
              tokensPerSecond: event.tokensPerSecond ?? pass.tokensPerSecond,
              elapsedMs: event.elapsedMs ?? pass.elapsedMs,
            }
          : {
              ...pass,
              reply: event.text,
              status: 'done',
              outputTokens: event.outputTokens,
              promptTokens: event.promptTokens,
              tokensPerSecond: event.tokensPerSecond ?? pass.tokensPerSecond,
              elapsedMs: event.elapsedMs,
              stopReason: event.stopReason,
            };
      const passes = live.passes.slice();
      passes[index] = next;
      return { ...live, passes };
    }
    case 'done':
      return { ...live, ended: true, passes: live.passes.map((p) => (p.status === 'running' ? { ...p, status: 'stopped' } : p)) };
  }
}

function finalOutcome(progress: GenerationProgress): LiveOutcome | null {
  switch (progress.stage) {
    case 'done':
    case 'failed':
    case 'cancelled':
      return progress.stage;
    default:
      return null;
  }
}

/**
 * Follows generation.output for the job the Builder started (events that come before generation.start
 * answers belong to it too: only one generation runs at a time), and the job's final progress.
 */
export function connectLiveOutput(services: Pick<AppServices, 'bridge' | 'store'>): () => void {
  const { bridge, store } = services;
  const registry = registryOf(store);
  const offOutput = bridge.on('generation.output', (event) => {
    let live = registry.live.value;
    if (live !== null && belongs(live, event.jobId)) {
      live = live.jobId === event.jobId ? live : { ...live, jobId: event.jobId };
    } else {
      const job = generationOf(store).value;
      if (job === null || (job.jobId !== null && job.jobId !== event.jobId)) {
        return;
      }
      live = fresh(event.jobId, job);
      registry.handedOver = false;
    }
    registry.live.value = applyOutput(live, event);
  });
  const offProgress = bridge.on('generation.progress', (progress) => {
    const live = registry.live.value;
    const outcome = finalOutcome(progress);
    if (live === null || !belongs(live, progress.jobId) || outcome === null) {
      return;
    }
    const next: LiveOutput = { ...live, jobId: progress.jobId, outcome, ended: true, documentId: progress.documentId ?? live.documentId, passes: live.passes.map((p) => (p.status === 'running' ? { ...p, status: 'stopped' } : p)) };
    registry.live.value = next;
    if (!held(registry, next)) {
      clear(registry);
    }
  });
  return () => {
    offOutput();
    offProgress();
  };
}
