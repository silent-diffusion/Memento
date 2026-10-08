// The browser-preview host's M4 methods (BRIDGE.md, M4): providers and their readiness, the payload
// preview ("Preview exactly what will be sent"), the Builder's skeleton paper, a simulated
// generation job that reports generation.progress through composing → generating per module →
// verifying → rendering → done, and the documents, templates and styles handlers composed into one
// set for mock.ts. URL flags: `?ai=off|nokey|ready|local` set the starting AI settings, keys and
// the local model; `?gen=fail|rate` make the first generation fail (no network, rate limited).
import { formatDuration } from '../format/duration';
import { definedFields } from './settingsMerge';
import { isoWithOffset, type MockProject } from './mockData';
import { createMockDocuments, type MockDocuments, type TranscriptLine } from './mockDocuments';
import { effectiveLocalModelId, localProviderInfo, recommendedLocalModelId, type LocalModelsFlag, type VramFlag } from './mockLocalModel';
import { type MockModelManager } from './mockModels';
import { articleOpen, metaLine, moduleOpen, skeletonHtml, titleBlock } from './mockPaper';
import { MockHostError } from './mockSession';
import { createMockTemplateStore, MODULE_CATALOG, moduleInfo, sampleHtml, type MockTemplateStore } from './mockTemplates';
import type {
  EventName,
  EventPayload,
  GenerationPreviewResult,
  GenerationSendSummary,
  InputSelection,
  MethodName,
  MethodParams,
  MethodResult,
  ProviderId,
  ProviderInfo,
  SettingsSetParams,
  SettingsSnapshot,
  Template,
} from './types';

export type AiFlag = 'off' | 'nokey' | 'ready' | 'local';
export type GenerationFlag = 'ok' | 'fail' | 'rate';

export interface M4Flags {
  ai: AiFlag;
  gen: GenerationFlag;
  /** `?vram=low`: another app holds the graphics card's memory. */
  vram: VramFlag;
  /** `?llm=qwen|ministral|both|none`: the local models installed at start (default: Qwen with `?ai=local`). */
  llm?: LocalModelsFlag;
}

export const DEFAULT_M4_FLAGS: M4Flags = { ai: 'off', gen: 'ok', vram: 'ok' };

export function m4FlagsFromQuery(query: URLSearchParams): Partial<M4Flags> {
  const flags: Partial<M4Flags> = {};
  const ai = query.get('ai');
  if (ai === 'off' || ai === 'nokey' || ai === 'ready' || ai === 'local') {
    flags.ai = ai;
  }
  const gen = query.get('gen');
  if (gen === 'fail' || gen === 'rate') {
    flags.gen = gen;
  }
  if (query.get('vram') === 'low') {
    flags.vram = 'low';
  }
  const llm = query.get('llm');
  if (llm === 'qwen' || llm === 'ministral' || llm === 'both' || llm === 'none') {
    flags.llm = llm;
  }
  return flags;
}

export const M4_METHODS = [
  'modules.list',
  'templates.list',
  'templates.get',
  'templates.save',
  'templates.duplicate',
  'templates.delete',
  'templates.resetBuiltIn',
  'styles.list',
  'styles.get',
  'styles.save',
  'styles.duplicate',
  'styles.delete',
  'styles.resetBuiltIn',
  'styles.sampleHtml',
  'providers.list',
  'generation.preview',
  'generation.previewHtml',
  'generation.start',
  'generation.confirm',
  'generation.cancel',
  'documents.list',
  'documents.get',
  'documents.renderHtml',
  'documents.create',
  'documents.saveEdit',
  'documents.rename',
  'documents.duplicate',
  'documents.delete',
  'documents.makeTemplate',
  'documents.versions',
  'documents.restoreVersion',
  'documents.export',
] as const satisfies readonly MethodName[];

export type M4Method = (typeof M4_METHODS)[number];
export type M4Handlers = { [M in M4Method]: (params: MethodParams<M>) => MethodResult<M> };

/** The M4 settings as the preview starts, for an `?ai=` flag (README defaults: external AI off). */
export function m4Settings(base: SettingsSnapshot, flag: AiFlag, vram: VramFlag = 'ok'): Pick<SettingsSnapshot, 'ai' | 'documents'> {
  const enabled = flag === 'nokey' || flag === 'ready';
  return {
    ai: {
      ...base.ai,
      enabled,
      providers: flag === 'nokey' ? { anthropic: { hasKey: false }, openai: { hasKey: false } } : base.ai.providers,
      defaultProviderId: flag === 'local' ? 'local' : null,
      localModelId: recommendedLocalModelId(vram),
      localModelChosen: false,
    },
    documents: { defaultTemplateId: 'meeting-minutes', defaultStyleId: 'corporate' },
  };
}

const PROVIDER_LABELS: Record<ProviderId, { name: string; vendor: string; model: string }> = {
  anthropic: { name: 'Claude', vendor: 'Anthropic', model: 'Claude Sonnet 4.5' },
  openai: { name: 'ChatGPT', vendor: 'OpenAI', model: 'GPT-5 mini' },
  local: { name: 'Local model', vendor: 'This PC', model: 'Qwen3.5 4B' },
};

const INPUT_NAMES: Record<keyof InputSelection, string> = {
  transcript: 'Transcript',
  details: 'Recording details',
  participants: 'Participants',
  agenda: 'Agenda',
  highlights: 'Highlights and notes',
  attachments: 'Imported documents',
  previousDocuments: 'Earlier documents',
};

export interface MockM4Environment {
  emit: <E extends EventName>(event: E, payload: EventPayload<E>) => void;
  now: () => number;
  find: (recordingId: string) => MockProject;
  settings: () => SettingsSnapshot;
  models: MockModelManager;
  transcript: (recordingId: string) => TranscriptLine[];
  attachmentNames: (recordingId: string) => string[];
  flags: M4Flags;
  stepMs: number;
}

export interface MockM4 {
  handlers: M4Handlers;
  documents: MockDocuments;
  store: MockTemplateStore;
  /** settings.set's `documents` block and the M4 `ai` fields, validated as the host does. */
  mergeSettings(settings: SettingsSnapshot, params: SettingsSetParams): SettingsSnapshot;
  /** The snapshot as settings.get answers it: the local model in effect (an installed one whenever any is). */
  present(settings: SettingsSnapshot): SettingsSnapshot;
}

/** The local model chosen in Settings, or null for the hardware default. */
function chosenLocalModel(ai: SettingsSnapshot['ai']): string | null {
  return ai.localModelChosen === true ? ai.localModelId : null;
}

interface Job {
  jobId: string;
  recordingId: string;
  template: Template;
  provider: ProviderInfo;
  documentId: string | null;
  inputs: InputSelection;
  chunks: number;
  startedAt: number;
  timer: ReturnType<typeof setTimeout> | null;
  waiting: boolean;
  finished: boolean;
}

export function createMockM4(env: MockM4Environment): MockM4 {
  const nowIso = (): string => isoWithOffset(new Date(env.now()));
  const store = createMockTemplateStore({ emit: env.emit, now: nowIso });
  const documents = createMockDocuments({ emit: env.emit, now: env.now, find: env.find, settings: env.settings, store, transcript: env.transcript });
  const jobs = new Map<string, Job>();
  let jobCounter = 0;
  let failuresLeft = env.flags.gen === 'ok' ? 0 : 1;

  const providers = (): ProviderInfo[] => {
    const ai = env.settings().ai;
    const cloud = (id: 'anthropic' | 'openai'): ProviderInfo => {
      const label = PROVIDER_LABELS[id];
      const reason = !ai.enabled ? 'External AI is off' : ai.providers[id].hasKey ? null : 'No key saved';
      return { id, name: label.name, vendor: label.vendor, kind: 'cloud', ready: reason === null, reason, modelLabel: label.model, code: !ai.enabled ? 'ai.disabled' : reason === null ? null : 'ai.noKey', detail: null, modelId: null, gpuMemory: null, gpuNote: null };
    };
    const local = localProviderInfo(env.models.list(), chosenLocalModel(ai), env.flags.vram);
    return [cloud('anthropic'), cloud('openai'), local];
  };

  /** The template's provider, else the Settings default, else the first ready one. */
  const providerFor = (template: Template): ProviderInfo => {
    const list = providers();
    const wanted = template.providerId ?? env.settings().ai.defaultProviderId;
    const chosen = list.find((p) => p.id === wanted) ?? list.find((p) => p.ready) ?? list[0];
    if (chosen === undefined) {
      throw new MockHostError('ai.providerNotReady', 'No AI provider is set up. Add a key or install the local model in Settings › AI and privacy.', null);
    }
    return chosen;
  };

  const hasTranscript = (project: MockProject): boolean => project.stages.some((s) => s.stage === 'transcript' && s.state === 'done');

  /** What will actually be sent: the template's ticks that Settings allows and the recording has. */
  const compose = (recordingId: string, template: Template, provider: ProviderInfo): GenerationPreviewResult => {
    const project = env.find(recordingId);
    const share = env.settings().ai.share;
    const lines = hasTranscript(project) ? env.transcript(recordingId) : [];
    const attachments = env.attachmentNames(recordingId);
    const earlier = documents.list(recordingId);
    const warnings: string[] = [];
    const allowed: Record<keyof InputSelection, boolean> = {
      transcript: share.transcript,
      details: share.details,
      participants: share.participants,
      agenda: share.agenda,
      highlights: share.highlights,
      attachments: share.attachments,
      previousDocuments: share.details,
    };
    const available: Record<keyof InputSelection, boolean> = {
      transcript: lines.length > 0,
      details: true,
      participants: project.details.participants.length > 0,
      agenda: project.details.agenda.items.length > 0,
      highlights: project.highlights.length > 0,
      attachments: attachments.length > 0,
      previousDocuments: earlier.length > 0,
    };
    const used = { ...template.inputs };
    for (const key of Object.keys(used) as (keyof InputSelection)[]) {
      if (!used[key]) {
        continue;
      }
      if (provider.kind === 'cloud' && !allowed[key]) {
        used[key] = false;
        warnings.push(`${INPUT_NAMES[key]} is not allowed in Settings › AI and privacy › What may be shared, so it is left out.`);
      } else if (!available[key]) {
        used[key] = false;
        warnings.push(`This recording has no ${INPUT_NAMES[key].toLowerCase()}, so nothing is sent for it.`);
      }
    }
    const { details } = project;
    const parts: string[] = [];
    const destination =
      provider.kind === 'local' ? `${provider.modelLabel ?? 'the local model'} on this PC (nothing leaves it)` : `${provider.name} (${provider.vendor})`;
    parts.push(`Memento · what ${destination} receives`);
    parts.push(`Template: ${template.name} · ${template.rows.reduce((n, r) => n + r.modules.length, 0)} modules in ${template.rows.length} rows`);
    parts.push('', '[Instructions]');
    template.rows.forEach((row, r) => {
      row.modules.forEach((m, i) => {
        const info = moduleInfo(m.module);
        const where = row.modules.length > 1 ? ` (row ${r + 1}, column ${i + 1})` : ` (row ${r + 1})`;
        parts.push(`${m.customTitle ?? info.name}${where} · ${m.length}${m.linkToTranscript ? ' · cite transcript times' : ''}: ${info.generated ? m.instructions || info.description : 'placed from the recording, not written by the AI'}`);
        if (info.groundingRule !== null) {
          parts.push(`  Rule: ${info.groundingRule}`);
        }
      });
    });
    if (used.details) {
      parts.push('', '[Recording details]', `Title: ${details.title}`, `Recorded: ${project.summary.createdAt} · ${formatDuration(project.summary.durationMs)}`);
      if (details.platform !== '') {
        parts.push(`Platform: ${details.platform}`);
      }
      if (details.purpose !== '') {
        parts.push(`Purpose: ${details.purpose}`);
      }
    }
    if (used.participants) {
      parts.push('', '[Participants]', ...details.participants);
    }
    if (used.agenda) {
      parts.push('', `[Agenda] (${details.agenda.source ?? 'typed in'})`, ...details.agenda.items.map((item, i) => `${i + 1}. ${item.text}`));
    }
    if (used.highlights) {
      parts.push('', '[Highlights and notes]', ...project.highlights.map((h) => `${formatDuration(h.atMs)} ${h.note === '' ? '(no note)' : h.note}`));
    }
    if (used.attachments) {
      parts.push('', '[Imported documents]', ...attachments.map((name) => `${name} (text extracted on this PC)`));
    }
    if (used.previousDocuments) {
      parts.push('', '[Earlier documents]', ...earlier.map((d) => d.name));
    }
    if (used.transcript) {
      parts.push('', '[Transcript]', ...lines.map((l) => `${formatDuration(l.t * 1000)} ${l.speaker}: ${l.text}`));
    }
    parts.push('', 'Audio and video are not sent.');
    const payloadText = parts.join('\n');
    const bytes = new TextEncoder().encode(payloadText).length;
    // The local model reads a smaller window at a time.
    const chunkBytes = provider.kind === 'local' ? 12_000 : 160_000;
    return { payloadText, bytes, chunks: Math.max(1, Math.ceil(bytes / chunkBytes)), inputsUsed: used, warnings };
  };

  const emitProgress = (job: Job, stage: EventPayload<'generation.progress'>['stage'], percent: number, moduleId: string | null, message: string | null, documentId: string | null = null): void => {
    env.emit('generation.progress', { jobId: job.jobId, recordingId: job.recordingId, documentId, stage, moduleId, percent: Math.round(percent), message });
  };

  const finish = (job: Job): void => {
    job.finished = true;
    if (job.timer !== null) {
      clearTimeout(job.timer);
      job.timer = null;
    }
  };

  const failureMessage = (job: Job, moduleName: string): string => {
    const name = job.provider.kind === 'local' ? 'The local model' : job.provider.name;
    return env.flags.gen === 'rate'
      ? `${name} is rate limiting this key and stopped answering while writing ${moduleName}. Wait about a minute, then try again.`
      : `${name} could not be reached while writing ${moduleName}: the network connection was lost.`;
  };

  /** composing → generating (each AI module) → verifying → rendering → done. */
  const run = (job: Job): void => {
    const generated = job.template.rows.flatMap((r) => r.modules).filter((m) => moduleInfo(m.module).generated);
    const steps: (() => boolean)[] = [];
    steps.push(() => {
      emitProgress(job, 'composing', 4, null, null);
      return true;
    });
    generated.forEach((m, i) => {
      steps.push(() => {
        if (failuresLeft > 0 && i === Math.min(2, generated.length - 1)) {
          failuresLeft -= 1;
          finish(job);
          emitProgress(job, 'failed', 8 + (i / Math.max(1, generated.length)) * 76, m.id, failureMessage(job, m.customTitle ?? moduleInfo(m.module).name));
          return false;
        }
        emitProgress(job, 'generating', 8 + (i / Math.max(1, generated.length)) * 76, m.id, null);
        return true;
      });
    });
    steps.push(() => {
      emitProgress(job, 'verifying', 88, null, null);
      return true;
    });
    steps.push(() => {
      emitProgress(job, 'rendering', 96, null, null);
      return true;
    });
    steps.push(() => {
      const label = PROVIDER_LABELS[job.provider.id];
      const documentId = documents.writeGenerated({
        recordingId: job.recordingId,
        template: job.template,
        providerId: job.provider.id,
        modelLabel: job.provider.modelLabel ?? label.model,
        startedAt: isoWithOffset(new Date(job.startedAt)),
        durationMs: Math.max(1000, env.now() - job.startedAt),
        inputs: job.inputs,
        chunks: job.chunks,
        documentId: job.documentId,
      });
      if (env.settings().ai.keepRecord) {
        const project = env.find(job.recordingId);
        const sent = (Object.keys(job.inputs) as (keyof InputSelection)[]).filter((k) => job.inputs[k]).map((k) => INPUT_NAMES[k].toLowerCase());
        project.history = [
          ...project.history,
          {
            at: nowIso(),
            stage: 'minutes',
            event: 'completed',
            summary: `${job.template.name} generated with ${job.provider.name}`,
            detail: `${job.provider.kind === 'local' ? 'Read on this PC' : 'Sent'}: ${sent.join(', ')} · ${job.chunks} ${job.chunks === 1 ? 'chunk' : 'chunks'}. Audio and video were not sent.`,
          },
        ];
      }
      finish(job);
      emitProgress(job, 'done', 100, null, null, documentId);
      return false;
    });
    let index = 0;
    const next = (): void => {
      if (job.finished) {
        return;
      }
      const step = steps[index++];
      if (!step?.()) {
        return;
      }
      job.timer = setTimeout(next, env.stepMs);
    };
    job.startedAt = env.now();
    job.timer = setTimeout(next, env.stepMs);
  };

  const findJob = (jobId: string): Job => {
    const job = jobs.get(jobId);
    if (job === undefined) {
      throw new MockHostError('generation.notFound', 'That generation is not running any more; it may have finished or been cancelled. Nothing was changed.', jobId);
    }
    return job;
  };

  const summaryOf = (job: Job, bytes: number): GenerationSendSummary => ({
    providerId: job.provider.id,
    providerName: job.provider.name,
    modelLabel: job.provider.modelLabel,
    inputsUsed: job.inputs,
    bytes,
    chunks: job.chunks,
  });

  const handlers: M4Handlers = {
    'modules.list': () => ({ modules: MODULE_CATALOG.map((m) => ({ ...m })) }),
    'templates.list': () => ({ templates: store.templates() }),
    'templates.get': (params) => store.template(params.templateId),
    'templates.save': (params) => store.saveTemplate(params.template),
    'templates.duplicate': (params) => store.duplicateTemplate(params.templateId),
    'templates.delete': (params) => {
      store.deleteTemplate(params.templateId);
      return {};
    },
    'templates.resetBuiltIn': (params) => store.resetTemplate(params.templateId),
    'styles.list': () => ({ styles: store.styles() }),
    'styles.get': (params) => store.style(params.styleId),
    'styles.save': (params) => store.saveStyle(params.style),
    'styles.duplicate': (params) => store.duplicateStyle(params.styleId),
    'styles.delete': (params) => {
      store.deleteStyle(params.styleId);
      return {};
    },
    'styles.resetBuiltIn': (params) => store.resetStyle(params.styleId),
    'styles.sampleHtml': (params) => ({ html: sampleHtml(params.settings) }),
    'providers.list': () => ({ providers: providers(), externalAiEnabled: env.settings().ai.enabled }),
    'generation.preview': (params) => compose(params.recordingId, params.template, providerFor(params.template)),
    'generation.previewHtml': (params) => {
      const style = store.style(params.styleId);
      const project = params.recordingId === null ? null : env.find(params.recordingId);
      const title = project?.details.title ?? 'Sample recording';
      const meta =
        project === null
          ? metaLine({ kind: params.template.name, recordedAt: null, durationMs: null, platform: '', participantCount: 0 })
          : metaLine({
              kind: params.template.name,
              recordedAt: project.summary.createdAt,
              durationMs: project.summary.durationMs,
              platform: project.details.platform,
              participantCount: project.details.platform === '' ? project.details.participants.length : 0,
            });
      const rows = params.template.rows.filter((r) => r.modules.length > 0);
      const body =
        rows.length === 0
          ? '<p class="paper-empty">Add modules to see the layout.</p>\n'
          : rows
              .map(
                (row) =>
                  `<div class="paper-row" data-cols="${row.modules.length}">\n${row.modules
                    .map((m) => {
                      const info = moduleInfo(m.module);
                      return moduleOpen(m.id, m.module, m.textSize, m.linkToTranscript, m.customTitle ?? info.name) + skeletonHtml(info.shape, info.id) + '</section>\n';
                    })
                    .join('')}</div>\n`,
              )
              .join('');
      return { html: articleOpen(style.settings, style.id, 'skeleton', null) + titleBlock(title, meta, 'skeleton') + body + '</article>\n' };
    },
    'generation.start': (params) => {
      const project = env.find(params.recordingId);
      const provider = providerFor(params.template);
      if (provider.kind === 'cloud' && !env.settings().ai.enabled) {
        throw new MockHostError(
          'ai.disabled',
          'External AI is off, so nothing can be sent to Claude or ChatGPT. Turn it on in Settings › AI and privacy, or use the local model. Nothing was sent.',
          provider.id,
        );
      }
      if (!provider.ready) {
        throw new MockHostError(
          'ai.providerNotReady',
          `${provider.name} is not ready: ${(provider.reason ?? 'not set up').toLowerCase()}. ${provider.kind === 'local' ? 'Install it in Settings › AI and privacy › Local model.' : 'Add a key in Settings › AI and privacy.'} Nothing was sent.`,
          provider.reason,
        );
      }
      if (!hasTranscript(project)) {
        throw new MockHostError(
          'generation.noTranscript',
          `"${project.summary.title}" has no transcript yet, and documents are written from it. Transcribe it first (Review › More › Reprocess). Nothing was sent.`,
          params.recordingId,
        );
      }
      const running = [...jobs.values()].find((j) => !j.finished);
      if (running !== undefined) {
        throw new MockHostError(
          'generation.busy',
          `${running.template.name} is still being written. One document is generated at a time; wait for it or cancel it. Nothing was sent.`,
          running.jobId,
        );
      }
      if (params.documentId !== undefined) {
        documents.get(params.recordingId, params.documentId);
      }
      const preview = compose(params.recordingId, params.template, provider);
      const job: Job = {
        jobId: `gen-${(++jobCounter).toString().padStart(4, '0')}`,
        recordingId: params.recordingId,
        template: structuredClone(params.template),
        provider,
        documentId: params.documentId ?? null,
        inputs: preview.inputsUsed,
        chunks: preview.chunks,
        startedAt: env.now(),
        timer: null,
        waiting: false,
        finished: false,
      };
      jobs.set(job.jobId, job);
      if (env.settings().ai.askBeforeSend && provider.kind === 'cloud') {
        job.waiting = true;
        return { jobId: job.jobId, confirmationRequired: true, summary: summaryOf(job, preview.bytes) };
      }
      run(job);
      return { jobId: job.jobId };
    },
    'generation.confirm': (params) => {
      const job = findJob(params.jobId);
      if (!job.waiting || job.finished) {
        throw new MockHostError('generation.notFound', 'That generation is not waiting for an answer any more. Nothing was sent.', params.jobId);
      }
      job.waiting = false;
      if (params.approved) {
        run(job);
      } else {
        finish(job);
        emitProgress(job, 'cancelled', 0, null, 'Nothing was sent.');
      }
      return {};
    },
    'generation.cancel': (params) => {
      const job = findJob(params.jobId);
      if (!job.finished) {
        finish(job);
        emitProgress(job, 'cancelled', 0, null, job.provider.kind === 'local' ? 'Stopped. Nothing was written.' : 'Stopped. What was already sent is not sent again, and no document was changed.');
      }
      return {};
    },
    'documents.list': (params) => ({ documents: documents.list(params.recordingId) }),
    'documents.get': (params) => documents.get(params.recordingId, params.documentId),
    'documents.renderHtml': (params) => ({ html: documents.renderHtml(params.recordingId, params.documentId, params.mode) }),
    'documents.create': (params) => documents.create(params.recordingId, params.name, params.styleId),
    'documents.saveEdit': (params) => documents.saveEdit(params.recordingId, params.documentId, params.html),
    'documents.rename': (params) => documents.rename(params.recordingId, params.documentId, params.name),
    'documents.duplicate': (params) => documents.duplicate(params.recordingId, params.documentId),
    'documents.delete': (params) => {
      documents.remove(params.recordingId, params.documentId);
      return {};
    },
    'documents.makeTemplate': (params) => documents.makeTemplate(params.recordingId, params.documentId, params.name),
    'documents.versions': (params) => ({ versions: documents.versions(params.recordingId, params.documentId) }),
    'documents.restoreVersion': (params) => ({ document: documents.restoreVersion(params.recordingId, params.documentId, params.versionId) }),
    'documents.export': (params) => documents.exportOne(params.recordingId, params.documentId, params.format, params.path),
  };

  const invalid = (message: string, detail: string): MockHostError => new MockHostError('settings.invalidValue', message, detail);

  return {
    handlers,
    documents,
    store,
    mergeSettings(settings, params) {
      const documentsBlock = { ...settings.documents, ...definedFields(params.documents) };
      const templates = store.templates();
      if (!templates.some((t) => t.id === documentsBlock.defaultTemplateId)) {
        throw invalid('That template is not in this library any more. Nothing was changed.', documentsBlock.defaultTemplateId);
      }
      if (!store.styles().some((s) => s.id === documentsBlock.defaultStyleId)) {
        throw invalid('That style is not in this library any more. Nothing was changed.', documentsBlock.defaultStyleId);
      }
      const ai = settings.ai;
      const provider = ai.defaultProviderId;
      if (provider !== null && !(['anthropic', 'openai', 'local'] as string[]).includes(provider)) {
        throw invalid('The default provider is Claude, ChatGPT or the local model. Nothing was changed.', provider);
      }
      const local = env.models.list().find((m) => m.id === ai.localModelId);
      if (local?.engine !== 'llm') {
        throw invalid('That is not a local language model. Nothing was changed.', ai.localModelId);
      }
      const chosen = typeof params.ai?.localModelId === 'string' ? true : (ai.localModelChosen ?? false);
      return { ...settings, ai: { ...ai, localModelChosen: chosen }, documents: documentsBlock };
    },
    present(settings) {
      const chosen = chosenLocalModel(settings.ai);
      const localModelId = effectiveLocalModelId(env.models.list(), chosen, env.flags.vram);
      return { ...settings, ai: { ...settings.ai, localModelId, localModelChosen: chosen !== null && chosen === localModelId } };
    },
  };
}
