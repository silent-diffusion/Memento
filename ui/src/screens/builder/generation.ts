// Generating a document (BRIDGE.md, M4: generation.*): start, the "ask before every send"
// confirmation (DESIGN.md §5.19), generation.progress, cancel, and the §17 failure. Kept beside the
// store so the Builder shows the same job after a round trip to the Style editor, and a job that
// finishes while the Builder is closed still says so.
import { signal, type Signal } from '@preact/signals';
import type { BridgeClient } from '../../bridge/client';
import { BridgeCallError } from '../../bridge/client';
import type { GenerationProgress, GenerationSendSummary, ProviderInfo, Template } from '../../bridge/types';
import { documentWord } from '../../format/documents';
import type { AppServices } from '../../state/context';
import type { AppStore } from '../../state/store';

export type GenerationPhase = 'starting' | 'confirm' | 'running' | 'failed' | 'done' | 'cancelled';

export interface ActiveGeneration {
  /** Null until generation.start answers. */
  jobId: string | null;
  recordingId: string;
  template: Template;
  provider: ProviderInfo;
  /** Regenerate into this document. */
  documentId: string | null;
  phase: GenerationPhase;
  /** Ask before every send: what the dialog shows. */
  summary: GenerationSendSummary | null;
  progress: GenerationProgress | null;
  /** Failed: the host's words, and its code when the call itself was refused. */
  failure: string | null;
  failureCode: string | null;
  /** Done: the document to open. */
  resultId: string | null;
  /** The Builder for this recording is on screen (it opens the result itself). */
  watched: boolean;
}

const registry = new WeakMap<AppStore, Signal<ActiveGeneration | null>>();
/** Progress that came before generation.start answered, by job id. */
const early = new WeakMap<AppStore, Map<string, GenerationProgress>>();

export function generationOf(store: AppStore): Signal<ActiveGeneration | null> {
  let job = registry.get(store);
  if (job === undefined) {
    job = signal<ActiveGeneration | null>(null);
    registry.set(store, job);
  }
  return job;
}

/** The job as it is now (after an await, not as it was assigned before it). */
function read(job: Signal<ActiveGeneration | null>): ActiveGeneration | null {
  return job.value;
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

function apply(job: ActiveGeneration, progress: GenerationProgress): ActiveGeneration {
  switch (progress.stage) {
    case 'done':
      return { ...job, phase: 'done', progress, resultId: progress.documentId };
    case 'failed':
      return { ...job, phase: 'failed', progress, failure: progress.message ?? `${job.provider.name} stopped before the document was finished.`, failureCode: null };
    case 'cancelled':
      return { ...job, phase: 'cancelled', progress };
    default:
      return { ...job, phase: 'running', progress };
  }
}

/** Follows generation.progress. A job that ends while its Builder is closed says so in a toast. */
export function connectGeneration(services: AppServices): () => void {
  const { bridge, store, router } = services;
  const job = generationOf(store);
  return bridge.on('generation.progress', (progress) => {
    const current = job.value;
    if (current === null) {
      return;
    }
    if (current.jobId === null) {
      let pending = early.get(store);
      if (pending === undefined) {
        pending = new Map();
        early.set(store, pending);
      }
      pending.set(progress.jobId, progress);
      return;
    }
    if (current.jobId !== progress.jobId) {
      return;
    }
    const next = apply(current, progress);
    job.value = next;
    if (next.watched) {
      return;
    }
    if (next.phase === 'done' && next.resultId !== null) {
      const documentId = next.resultId;
      job.value = null;
      store.toasts.show({
        tone: 'ok',
        title: `${next.template.name} is ready`,
        body: 'It is saved inside the recording.',
        actions: [
          {
            label: 'Open',
            run: () => {
              router.navigate({ name: 'document', recordingId: next.recordingId, documentId });
            },
          },
        ],
      });
    } else if (next.phase === 'failed') {
      store.toasts.show({ tone: 'danger', title: `${next.template.name} was not written`, body: failureWords(next.failure ?? '') });
    }
  });
}

/** DESIGN.md §17: what is safe after a provider failure. */
export const NOTHING_TWICE = 'Nothing was sent twice and no document was changed.';

/** The host's failure with the §17 promise, unless it says it already. */
export function failureWords(message: string): string {
  // The host's §17 copy usually says what is safe already ("Nothing left this PC and no document was changed.").
  return /sent twice|no document was changed/i.test(message) ? message : `${message} ${NOTHING_TWICE}`;
}

/** The failure card's lead: the provider and what happened, from the failure code. */
export function failureLead(job: Pick<ActiveGeneration, 'template' | 'provider' | 'failureCode' | 'progress'>): string {
  const name = job.provider.kind === 'local' ? 'The local model' : job.provider.name;
  if (job.failureCode !== null) {
    return `The ${job.template.name.toLocaleLowerCase()} could not be started`;
  }
  switch (job.progress?.code) {
    case 'ai.invalidKey':
      return `${name} did not accept the key`;
    case 'ai.rateLimited':
      return `${name} is limiting requests`;
    case 'ai.network':
      return `${name} could not be reached`;
    case 'ai.notEnoughVram':
      return `${name} ran out of video memory`;
    case 'ai.contentTooLong':
      return `Too much for ${job.provider.kind === 'local' ? 'the local model' : name}`;
    case 'ai.workerCrashed':
      return `${name} stopped unexpectedly`;
    default:
      return `${name} didn't respond`;
  }
}

export async function startGeneration(
  { bridge, store }: Pick<AppServices, 'bridge' | 'store'>,
  request: { recordingId: string; template: Template; provider: ProviderInfo; documentId: string | null },
): Promise<void> {
  const job = generationOf(store);
  const template: Template = { ...request.template, providerId: request.provider.id };
  job.value = {
    jobId: null,
    recordingId: request.recordingId,
    template,
    provider: request.provider,
    documentId: request.documentId,
    phase: 'starting',
    summary: null,
    progress: null,
    failure: null,
    failureCode: null,
    resultId: null,
    watched: true,
  };
  try {
    const result = await bridge.call('generation.start', {
      recordingId: request.recordingId,
      template,
      ...(request.documentId === null ? {} : { documentId: request.documentId }),
    });
    const current = read(job);
    // Dismissed, or another job started meanwhile.
    if (current?.jobId !== null) {
      return;
    }
    let next: ActiveGeneration = {
      ...current,
      jobId: result.jobId,
      phase: result.confirmationRequired === true ? 'confirm' : 'running',
      summary: result.summary ?? null,
    };
    const pending = early.get(store)?.get(result.jobId);
    if (pending !== undefined) {
      early.get(store)?.delete(result.jobId);
      next = apply(next, pending);
    }
    job.value = next;
  } catch (error) {
    const current = read(job);
    if (current !== null) {
      job.value = { ...current, phase: 'failed', failure: messageOf(error), failureCode: error instanceof BridgeCallError ? error.code : null };
    }
  }
}

/** The §5.19 dialog's answer. */
export async function confirmGeneration(bridge: BridgeClient, store: AppStore, approved: boolean): Promise<void> {
  const job = generationOf(store);
  const current = job.value;
  const jobId = current?.jobId ?? null;
  if (current === null || jobId === null) {
    return;
  }
  job.value = approved ? { ...current, phase: 'running', summary: null } : null;
  try {
    await bridge.call('generation.confirm', { jobId, approved });
  } catch (error) {
    if (approved) {
      job.value = { ...current, phase: 'failed', failure: messageOf(error), failureCode: error instanceof BridgeCallError ? error.code : null };
    }
  }
}

export async function cancelGeneration(bridge: BridgeClient, store: AppStore): Promise<void> {
  const job = generationOf(store);
  const jobId = job.value?.jobId ?? null;
  if (jobId === null) {
    return;
  }
  job.value = null;
  try {
    await bridge.call('generation.cancel', { jobId });
  } catch {
    // It had already ended; nothing more to stop.
  }
}

export function dismissGeneration(store: AppStore): void {
  generationOf(store).value = null;
}

/** The Builder for this job's recording is on screen (or not any more). */
export function watchGeneration(store: AppStore, watched: boolean): void {
  const job = generationOf(store);
  if (job.value !== null && job.value.watched !== watched) {
    job.value = { ...job.value, watched };
  }
}

/** "Writing Decisions · 45%" for the progress card. */
export function progressWords(job: ActiveGeneration, moduleName: (moduleId: string) => string | null): { title: string; detail: string; percent: number } {
  const word = documentWord(job.template.name);
  const progress = job.progress;
  const percent = progress?.percent ?? 0;
  const where = job.provider.kind === 'local' ? 'on this PC' : `with ${job.provider.name}`;
  switch (progress?.stage) {
    case undefined:
    case 'composing':
      return { title: `Preparing the ${word}`, detail: job.provider.kind === 'local' ? 'Putting the inputs together for the local model' : `Putting together what is sent to ${job.provider.name}`, percent };
    case 'generating': {
      const name = progress.moduleId === null ? null : moduleName(progress.moduleId);
      return { title: `Writing the ${word} ${where}`, detail: name === null ? 'Writing' : `Writing ${name}`, percent };
    }
    case 'verifying':
      return { title: `Checking the ${word}`, detail: 'Checking every point against the transcript', percent };
    case 'rendering':
    case 'done':
      return { title: `Laying out the ${word}`, detail: 'Saving it inside the recording', percent };
    default:
      return { title: `Writing the ${word}`, detail: '', percent };
  }
}
