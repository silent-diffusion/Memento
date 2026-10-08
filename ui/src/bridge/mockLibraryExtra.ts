// The browser-preview host's M3 methods beyond agenda and export (BRIDGE.md M3): attachments,
// importing audio or video, library usage, moving the library, reclaiming space, the AI keys
// (only whether one is stored), Start with Windows, and the M3 settings blocks. It composes the
// agenda and export parts into one set of handlers for mock.ts.
import { createMockAgenda, type AgendaFlag, type MockAgenda, type PendingOriginal } from './mockAgenda';
import { isoWithOffset, type MockProject } from './mockData';
import { createMockExport, type ExportFlag, type MockExport } from './mockExport';
import { visibleStages } from './mockLibrary';
import { MODEL_IDS } from './mockModels';
import { MockHostError } from './mockSession';
import { mergeSettings } from './settingsMerge';
import type {
  AiProvider,
  Attachment,
  EventName,
  EventPayload,
  ExportSelection,
  FooterExportStatus,
  JobState,
  MethodName,
  MethodParams,
  MethodResult,
  Project,
  RecordingSummary,
  SettingsSetParams,
  SettingsSnapshot,
  StageStatus,
  Track,
  UpdateStatus,
} from './types';

/**
 * URL flags for the failure cases: `?export=fail|unwritable`, `?agenda=ocrmissing|nodrop`,
 * `?import=unsupported|interrupted` (the first import stops at 40% as if Memento had closed), `?move=busy`,
 * `?attachment=toolarge` (the picked attachment is over 100 MB).
 */
export interface M3Flags {
  export: ExportFlag;
  agenda: AgendaFlag;
  import: 'ok' | 'unsupported' | 'interrupted';
  move: 'ok' | 'busy';
  attachment: 'ok' | 'tooLarge';
}

export const DEFAULT_M3_FLAGS: M3Flags = { export: 'ok', agenda: 'ok', import: 'ok', move: 'ok', attachment: 'ok' };

export function m3FlagsFromQuery(query: URLSearchParams): Partial<M3Flags> {
  const flags: Partial<M3Flags> = {};
  const exportFlag = query.get('export');
  if (exportFlag === 'fail' || exportFlag === 'unwritable') {
    flags.export = exportFlag;
  }
  const agenda = query.get('agenda');
  if (agenda === 'ocrmissing') {
    flags.agenda = 'ocrMissing';
  } else if (agenda === 'nodrop') {
    flags.agenda = 'noDrop';
  }
  const importFlag = query.get('import');
  if (importFlag === 'unsupported' || importFlag === 'interrupted') {
    flags.import = importFlag;
  }
  if (query.get('move') === 'busy') {
    flags.move = 'busy';
  }
  if (query.get('attachment') === 'toolarge') {
    flags.attachment = 'tooLarge';
  }
  return flags;
}

export const M3_METHODS = [
  'agenda.importFile',
  'agenda.importDropped',
  'agenda.parseText',
  'agenda.apply',
  'agenda.discard',
  'agenda.setCovered',
  'attachments.list',
  'attachments.add',
  'attachments.remove',
  'attachments.open',
  'library.importMedia',
  'project.changeType',
  'export.estimate',
  'export.run',
  'export.cancel',
  'export.openFolder',
  'library.usage',
  'library.rebuildIndex',
  'library.move',
  'storage.reclaim',
  'ai.setKey',
  'ai.clearKey',
  'app.setStartup',
  'updates.status',
  'updates.check',
  'updates.apply',
] as const satisfies readonly MethodName[];

export type M3Method = (typeof M3_METHODS)[number];
export type M3Handlers = { [M in M3Method]: (params: MethodParams<M>) => MethodResult<M> };

export const DEFAULT_EXPORT_SELECTION: ExportSelection = {
  audioMixed: { on: true, format: 'flac', bitrateKbps: null },
  tracks: { on: false, format: 'flac', bitrateKbps: null },
  transcript: { on: true, formats: ['json'] },
  documents: { on: false, documentIds: [], format: 'docx' },
  details: { on: false },
  attachments: { on: false },
};

/** The M3 blocks of the preview's settings (defaults from the README: local first, AI off, nothing written outside). */
export function defaultM3Settings(): Pick<SettingsSnapshot, 'general' | 'export' | 'ai' | 'storage'> {
  return {
    general: { startWithWindows: false, keepRunningInTray: true, language: 'en', autoUpdate: true },
    export: {
      saveCopiesOutside: false,
      defaultFolder: 'D:\\Exports',
      askWhereEachTime: true,
      createSubfolder: true,
      defaults: DEFAULT_EXPORT_SELECTION,
    },
    ai: {
      enabled: false,
      askBeforeSend: true,
      keepRecord: true,
      share: { transcript: true, details: true, participants: true, agenda: true, highlights: true, attachments: false },
      // The preview pretends a Claude key is stored so the masked row and Replace can be seen.
      providers: { anthropic: { hasKey: true }, openai: { hasKey: false } },
      // M4
      defaultProviderId: null,
      localModelId: MODEL_IDS.qwen,
    },
    storage: { reclaimOlderThanDays: null },
  };
}

export interface MockM3Environment {
  emit: <E extends EventName>(event: E, payload: EventPayload<E>) => void;
  now: () => number;
  projects: Map<string, MockProject>;
  find: (recordingId: string) => MockProject;
  toProject: (project: MockProject) => Project;
  tracks: (project: MockProject) => Track[];
  changed: (...recordingIds: string[]) => void;
  settings: () => SettingsSnapshot;
  setSettings: (next: SettingsSnapshot) => void;
  freeBytes: () => number;
  setFooterExport: (status: FooterExportStatus) => void;
  setStages: (project: MockProject, stages: StageStatus[]) => void;
  /** Once stored, the transcript and speakers stages follow (as after a recording). */
  queueAfterStored: (project: MockProject) => void;
  transcriptSegments: (recordingId: string) => number | null;
  /** A recording is running or being saved: moving the library waits. */
  busyTitle: () => string | null;
  log: (message: string) => void;
  version: string;
  flags: M3Flags;
  stepMs: number;
  /** M4: the documents a recording holds, for the Export dialog's Documents row. */
  documents?: (recordingId: string) => { id: string; name: string; sizeBytes: number }[];
}

export interface MockM3 {
  handlers: M3Handlers;
  agenda: MockAgenda;
  exports: MockExport;
  /** recording.start while the library is being copied answers library.busy, as the host. */
  throwIfMoving(): void;
  /** processing.retry of `stored` on an import that stopped: imports the same file again (BRIDGE.md M3 integration). */
  importAgain(recordingId: string): void;
  /** settings.set for the M3 blocks: validated, then merged into `settings`. */
  mergeSettings(settings: SettingsSnapshot, params: SettingsSetParams): SettingsSnapshot;
}

const MB = 1024 * 1024;
const ATTACHMENT_LIMIT = 100 * MB;
const PICKED_ATTACHMENTS: readonly { name: string; sizeBytes: number; contentType: string }[] = [
  { name: 'project-brief.pdf', sizeBytes: Math.round(1.4 * MB), contentType: 'application/pdf' },
  { name: 'whiteboard.jpg', sizeBytes: Math.round(2.6 * MB), contentType: 'image/jpeg' },
  { name: 'budget-draft.xlsx', sizeBytes: 86_016, contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' },
];
const PICKED_MEDIA = ['Customer interview.m4a', 'Lecture recording.mp3', 'Team call.mp4'];
const VIDEO_EXTENSIONS = /\.(mp4|mkv|mov|avi|webm|wmv)$/i;
const AUDIO_EXTENSIONS = /\.(wav|flac|mp3|m4a|aac|wma|ogg|opus)$/i;

const PICKED_TOO_LARGE = { name: 'site-walkthrough.mov', sizeBytes: 240 * MB, contentType: 'video/quicktime' };

/**
 * The page never names a file (security audit SA-08): the picker methods refuse a path as the host does, so script in
 * the page cannot make the host read, copy or connect to a file of its choosing.
 */
function refusePath(method: string, path: string | undefined): void {
  if (path !== undefined) {
    throw new MockHostError('bridge.invalidParams', `'${method}' does not take a path from the interface; the host shows its file picker. Nothing was read.`);
  }
}

function contentTypeOf(name: string): string | null {
  const ext = (/\.([a-z0-9]+)$/i.exec(name)?.[1] ?? '').toLowerCase();
  const types: Record<string, string> = {
    pdf: 'application/pdf',
    docx: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    jpg: 'image/jpeg',
    jpeg: 'image/jpeg',
    png: 'image/png',
    txt: 'text/plain',
  };
  return types[ext] ?? null;
}

export function createMockM3(env: MockM3Environment): MockM3 {
  const attachments = new Map<string, Attachment[]>();
  let attachmentCounter = 0;
  let pickIndex = 0;
  let mediaIndex = 0;
  let importCounter = 0;
  let interruptedOnce = false;
  let moving = false;
  let jobCounter = 0;
  let updates: UpdateStatus = { currentVersion: '0.5.0', state: 'idle', availableVersion: null, percent: null, lastCheckedAt: null, message: null, deferred: false };

  const at = (): string => isoWithOffset(new Date(env.now()));
  const newAttachment = (name: string, sizeBytes: number, kind: Attachment['kind'], contentType: string | null): Attachment => ({
    id: `f${(++attachmentCounter).toString(16).padStart(8, '0')}`,
    name,
    sizeBytes,
    addedAt: at(),
    kind,
    contentType,
  });

  // Sample recordings whose agenda came from a file carry that file; one has a second attachment.
  for (const project of env.projects.values()) {
    const list: Attachment[] = [];
    const source = project.details.agenda.source;
    if (source !== null && project.details.agenda.items.length > 0) {
      list.push({ ...newAttachment(source, 38_912, 'agenda', contentTypeOf(source)), addedAt: project.summary.createdAt });
    }
    if (project.summary.id === '20261005-160000-dsrev') {
      list.push({ ...newAttachment('library-mockups-v3.pdf', Math.round(4.2 * MB), 'file', 'application/pdf'), addedAt: project.summary.createdAt });
    }
    if (list.length > 0) {
      attachments.set(project.summary.id, list);
    }
  }
  const listOf = (recordingId: string): Attachment[] => attachments.get(recordingId) ?? [];
  const findAttachment = (recordingId: string, attachmentId: string): Attachment => {
    env.find(recordingId);
    const found = listOf(recordingId).find((a) => a.id === attachmentId);
    if (found === undefined) {
      throw new MockHostError(
        'attachments.notFound',
        'That attachment is not in this recording any more; it may have been removed. Nothing was changed.',
        attachmentId,
      );
    }
    return found;
  };

  const agenda = createMockAgenda({
    now: env.now,
    find: env.find,
    toProject: env.toProject,
    changed: (id) => {
      env.changed(id);
    },
    attach: (recordingId, original: PendingOriginal, kind) => {
      // A new agenda file replaces the earlier one of the same name.
      const rest = listOf(recordingId).filter((a) => !(a.kind === 'agenda' && a.name === original.name));
      attachments.set(recordingId, [...rest, newAttachment(original.name, original.sizeBytes, kind, original.contentType)]);
    },
    flag: env.flags.agenda,
  });

  const exports = createMockExport({
    emit: env.emit,
    now: env.now,
    find: env.find,
    tracks: env.tracks,
    transcriptSegments: env.transcriptSegments,
    attachments: listOf,
    settings: env.settings,
    setSettings: env.setSettings,
    setFooterExport: env.setFooterExport,
    version: env.version,
    flag: env.flags.export,
    stepMs: env.stepMs,
    ...(env.documents === undefined ? {} : { documents: env.documents }),
  });

  /** A background job with percent steps; `finish` runs at 100 and may return a final message. */
  const runJob = (
    every: number,
    step: number,
    progress: (jobId: string, percent: number, state: JobState, message: string | null) => void,
    finish: () => string | null,
  ): string => {
    const jobId = `job-${(++jobCounter).toString(16)}`;
    let percent = 0;
    progress(jobId, 0, 'running', null);
    const timer = setInterval(() => {
      percent = Math.min(100, percent + step);
      if (percent >= 100) {
        clearInterval(timer);
        const message = finish();
        progress(jobId, 100, 'done', message);
        return;
      }
      progress(jobId, percent, 'running', null);
    }, every);
    return jobId;
  };

  const startImport = (name: string, title: string | undefined, type: string | undefined): string => {
    const now = new Date(env.now());
    const pad = (n: number): string => String(n).padStart(2, '0');
    const id = `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}-${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}-imp${(++importCounter).toString(16)}`;
    const durationMs = (23 * 60 + 17) * 1000;
    const video = VIDEO_EXTENSIONS.test(name);
    const settings = env.settings();
    const pipeline: StageStatus[] = [{ stage: 'stored', state: 'active', percent: 0, label: 'Importing · 0%' }];
    const summary: RecordingSummary = {
      id,
      title: title !== undefined && title.trim() !== '' ? title.trim() : name.replace(/\.[^.]+$/, ''),
      type: type ?? settings.recording.defaultType,
      createdAt: isoWithOffset(now),
      durationMs,
      participantCount: 0,
      hasVideo: false,
      stages: visibleStages(pipeline),
      people: [],
      isProcessing: true,
      state: 'ready',
      sizeBytes: Math.round((durationMs / 1000) * 110_000),
      matchSnippet: null,
    };
    const track: Track = {
      id: `${id}-t1`,
      sourceId: 'imported',
      sourceKind: 'imported',
      name,
      file: 'tracks/01-imported.flac',
      sampleRate: 48_000,
      channels: 2,
      durationMs,
      sha256: null,
      startOffsetMs: 0,
      endedEarlyAtMs: null,
    };
    const project: MockProject = {
      summary,
      stages: pipeline,
      details: {
        title: summary.title,
        type: summary.type,
        participants: [],
        purpose: '',
        platform: '',
        organization: '',
        location: '',
        notes: '',
        tags: [],
        agenda: { source: null, parsedLocally: true, items: [] },
      },
      trackSources: ['microphone'],
      tracks: [track],
      chapters: [],
      highlights: [],
      topics: [],
      history: [
        {
          at: summary.createdAt,
          stage: 'recorded',
          event: 'info',
          summary: `Imported ${name}`,
          detail: video ? 'Only the audio track was imported; video is not kept in this version.' : 'One track, imported',
        },
      ],
    };
    env.projects.set(id, project);
    env.changed(id);
    env.emit('processing.progress', { recordingId: id, stages: pipeline });
    runImport(id, name);
    return id;
  };

  /** Decoding and storing, 20% a step; with `?import=interrupted` the first import stops at 40%. */
  const runImport = (id: string, name: string): void => {
    const interrupt = env.flags.import === 'interrupted' && !interruptedOnce;
    interruptedOnce ||= interrupt;
    const timer = setInterval(() => {
      const current = env.projects.get(id);
      if (current === undefined) {
        clearInterval(timer);
        return;
      }
      const stored = current.stages.find((st) => st.stage === 'stored');
      const percent = Math.min(100, (stored?.percent ?? 0) + 20);
      if (interrupt && percent >= 40) {
        clearInterval(timer);
        current.summary = { ...current.summary, state: 'failed' };
        current.history = [
          ...current.history,
          {
            at: at(),
            stage: 'recorded',
            event: 'failed',
            summary: 'Import interrupted',
            detail: `Importing ${name} stopped because Memento closed before it had finished. The original file was not changed. The half-imported copy was removed. Use Import again in the Library, or delete the recording.`,
          },
        ];
        env.setStages(current, [{ stage: 'stored', state: 'failed', percent: null, label: 'Import interrupted' }]);
        return;
      }
      if (percent < 100) {
        env.setStages(current, current.stages.map((st) => (st.stage === 'stored' ? { ...st, percent, label: `Importing · ${percent}%` } : st)));
        return;
      }
      clearInterval(timer);
      current.history = [...current.history, { at: at(), stage: 'stored', event: 'completed', summary: 'Stored as lossless FLAC', detail: '1 track on this PC' }];
      env.setStages(current, current.stages.map((st) => (st.stage === 'stored' ? { ...st, state: 'done', percent: null, label: 'Done' } : st)));
      env.queueAfterStored(current);
    }, Math.max(200, env.stepMs * 2));
  };

  const importAgain = (recordingId: string): void => {
    const project = env.find(recordingId);
    const stored = project.stages.find((st) => st.stage === 'stored');
    const name = project.tracks?.[0]?.name ?? project.summary.title;
    if (project.summary.state !== 'failed' || stored?.state !== 'failed') {
      throw new MockHostError('bridge.invalidParams', `"${project.summary.title}" is not an import that stopped, so there is nothing to import again. Nothing was changed.`);
    }
    project.summary = { ...project.summary, state: 'ready' };
    project.history = [...project.history, { at: at(), stage: 'recorded', event: 'info', summary: 'Importing again', detail: `From ${name}.` }];
    env.setStages(project, [{ stage: 'stored', state: 'active', percent: 0, label: 'Importing · 0%' }]);
    runImport(recordingId, name);
  };

  const setProvider = (provider: AiProvider, hasKey: boolean): { hasKey: boolean } => {
    const settings = env.settings();
    env.setSettings({ ...settings, ai: { ...settings.ai, providers: { ...settings.ai.providers, [provider]: { hasKey } } } });
    return { hasKey };
  };
  const checkProvider = (provider: string): AiProvider => {
    if (provider !== 'anthropic' && provider !== 'openai') {
      throw new MockHostError('bridge.invalidParams', `'${provider}' is not a provider Memento knows. Nothing was changed.`, provider);
    }
    return provider;
  };

  const handlers: M3Handlers = {
    'agenda.importFile': (params) => {
      refusePath('agenda.importFile', params.path);
      return agenda.importFile();
    },
    'agenda.importDropped': (params) => agenda.importDropped(params.paths),
    'agenda.parseText': (params) => ({ preview: agenda.parseText(params.text) }),
    'agenda.apply': (params) => agenda.apply(params),
    'agenda.discard': (params) => {
      agenda.discard(params.attachmentToken);
      return {};
    },
    'agenda.setCovered': (params) => agenda.setCovered(params.recordingId, params.itemId, params.covered),
    'attachments.list': (params) => {
      env.find(params.recordingId);
      return { attachments: listOf(params.recordingId) };
    },
    'attachments.add': (params) => {
      env.find(params.recordingId);
      refusePath('attachments.add', params.path);
      const picked = env.flags.attachment === 'tooLarge' ? PICKED_TOO_LARGE : (PICKED_ATTACHMENTS[pickIndex++ % PICKED_ATTACHMENTS.length] ?? PICKED_TOO_LARGE);
      const { name, sizeBytes } = picked;
      if (sizeBytes > ATTACHMENT_LIMIT) {
        throw new MockHostError(
          'attachments.tooLarge',
          `${name} is ${Math.round(sizeBytes / MB)} MB; an attachment can be up to 100 MB. Nothing was added. Keep large files beside the recording instead.`,
          name,
        );
      }
      const attachment = newAttachment(name, sizeBytes, 'file', picked.contentType);
      attachments.set(params.recordingId, [...listOf(params.recordingId), attachment]);
      env.changed(params.recordingId);
      return { attachment, cancelled: false };
    },
    'attachments.remove': (params) => {
      findAttachment(params.recordingId, params.attachmentId);
      attachments.set(
        params.recordingId,
        listOf(params.recordingId).filter((a) => a.id !== params.attachmentId),
      );
      env.changed(params.recordingId);
      return {};
    },
    'attachments.open': (params) => {
      const attachment = findAttachment(params.recordingId, params.attachmentId);
      env.log(`[bridge:mock] would open ${attachment.name} with its Windows default app`);
      return {};
    },
    'library.importMedia': (params) => {
      refusePath('library.importMedia', params.path);
      const name = PICKED_MEDIA[mediaIndex++ % PICKED_MEDIA.length] ?? 'Recording.m4a';
      if (env.flags.import === 'unsupported' || !(AUDIO_EXTENSIONS.test(name) || VIDEO_EXTENSIONS.test(name))) {
        throw new MockHostError(
          'library.importUnsupported',
          `${env.flags.import === 'unsupported' ? 'Voice note.amr' : name} could not be imported: Windows cannot decode its audio. Nothing was added to the library. Convert it to WAV, FLAC or MP3 and import it again.`,
          name,
        );
      }
      return { recordingId: startImport(name, params.title, params.type), cancelled: false };
    },
    'project.changeType': (params) => {
      const project = env.find(params.recordingId);
      const type = params.type.trim();
      if (type === '' || type.length > 40) {
        throw new MockHostError('bridge.invalidParams', 'A type name needs 1 to 40 characters. The type was not changed.', params.type);
      }
      project.details = { ...project.details, type };
      project.summary = { ...project.summary, type };
      env.changed(project.summary.id);
      return env.toProject(project);
    },
    'export.estimate': (params) => exports.estimate(params.recordingId, params.selection),
    'export.run': (params) => exports.run(params),
    'export.cancel': (params) => {
      exports.cancel(params.jobId);
      return {};
    },
    'export.openFolder': (params) => {
      env.log(`[bridge:mock] would open ${exports.openFolder(params.jobId)} in File Explorer`);
      return {};
    },
    'library.usage': () => {
      const listed = [...env.projects.values()].filter((p) => p.summary.state !== 'recording');
      const largest = listed.reduce<MockProject | null>((best, p) => (best === null || p.summary.sizeBytes > best.summary.sizeBytes ? p : best), null);
      return {
        totalBytes: listed.reduce((sum, p) => sum + p.summary.sizeBytes, 0),
        freeBytes: env.freeBytes(),
        count: listed.length,
        largest: largest === null ? null : { recordingId: largest.summary.id, title: largest.summary.title, sizeBytes: largest.summary.sizeBytes },
      };
    },
    'library.rebuildIndex': () => {
      const ids = [...env.projects.keys()];
      env.changed(...ids);
      return { recordings: ids.filter((id) => env.projects.get(id)?.summary.state !== 'recording').length };
    },
    'library.move': (params) => {
      const settings = env.settings();
      const newPath = params.newPath.trim();
      if (newPath === '' || newPath.toLocaleLowerCase() === settings.libraryPath.toLocaleLowerCase()) {
        throw new MockHostError('library.moveRefused', `The library is already in ${settings.libraryPath}. Nothing was changed.`, newPath);
      }
      if (/unwritable|readonly/i.test(newPath)) {
        throw new MockHostError('library.moveRefused', `Memento can't create ${newPath}: Windows denied access. Nothing was changed. Choose another folder.`, newPath);
      }
      const busy = env.flags.move === 'busy' ? 'Q3 planning sync' : env.busyTitle();
      if (busy !== null) {
        throw new MockHostError(
          'library.busy',
          `Memento is still recording or processing "${busy}". The library can move once that has finished; nothing was moved.`,
          busy,
        );
      }
      moving = true;
      return {
        jobId: runJob(
          Math.max(120, env.stepMs * 3),
          10,
          (jobId, percent, state, message) => {
            env.emit('library.moveProgress', { jobId, percent, state, message, newPath });
          },
          () => {
            moving = false;
            const current = env.settings();
            env.setSettings({ ...current, libraryPath: newPath });
            return `Every file was copied to ${newPath} and checked, and the old folder was removed.`;
          },
        ),
      };
    },
    'storage.reclaim': (params) => {
      const settings = env.settings();
      const days = settings.storage.reclaimOlderThanDays;
      const cutoff = days === null ? null : env.now() - days * 86_400_000;
      const chosen = [...env.projects.values()].filter((p) =>
        params.recordingIds === null ? cutoff !== null && Date.parse(p.summary.createdAt) < cutoff : params.recordingIds.includes(p.summary.id),
      );
      if (chosen.length === 0) {
        throw new MockHostError(
          'storage.nothingToReclaim',
          params.recordingIds !== null
            ? 'No recording was chosen, so there is nothing to make smaller. Nothing was changed. Choose at least one recording.'
            : days === null
              ? 'No age is set for making recordings smaller, so none were chosen. Nothing was changed. Choose an age for "Downmix tracks older than" in Settings › Storage and history first.'
              : `No recording is older than ${days} ${days === 1 ? 'day' : 'days'}, so there is nothing to make smaller yet. Nothing was changed.`,
        );
      }
      let freed = 0;
      let done = 0;
      return {
        jobId: runJob(
          Math.max(120, env.stepMs * 3),
          chosen.length === 0 ? 100 : Math.max(10, Math.ceil(100 / chosen.length)),
          (jobId, percent, state, message) => {
            const reached = Math.min(chosen.length, Math.round((percent / 100) * chosen.length));
            for (; done < reached; done++) {
              const project = chosen[done];
              if (project !== undefined) {
                const saved = Math.round(project.summary.sizeBytes * (params.downmixMono ? 0.8 : 0.65));
                freed += saved;
                project.summary = { ...project.summary, sizeBytes: project.summary.sizeBytes - saved };
                project.history = [
                  ...project.history,
                  { at: at(), stage: 'optimize', event: 'completed', summary: 'Saved smaller files', detail: `${params.codec.toUpperCase()} ${params.bitrateKbps} kbps${params.downmixMono ? ', mono' : ''} · the transcript was not touched` },
                ];
              }
            }
            env.emit('storage.reclaimProgress', { jobId, percent, state, message, recordingsDone: done, bytesFreed: freed });
          },
          () => {
            env.changed(...chosen.map((p) => p.summary.id));
            return chosen.length === 0 ? 'No recordings were old enough; nothing was changed.' : null;
          },
        ),
      };
    },
    'ai.setKey': (params) => {
      const provider = checkProvider(params.provider);
      const key = params.key.trim();
      // As the host: 8 to 500 characters, no spaces.
      if (key.length < 8 || key.length > 500 || /\s/.test(key)) {
        throw new MockHostError(
          'bridge.invalidParams',
          'That does not look like an API key: a key is 8 to 500 characters with no spaces. Nothing was saved. Copy the whole key from the provider\u2019s console and paste it again.',
        );
      }
      return setProvider(provider, true);
    },
    'ai.clearKey': (params) => setProvider(checkProvider(params.provider), false),
    'app.setStartup': (params) => {
      const settings = env.settings();
      env.setSettings({ ...settings, general: { ...settings.general, startWithWindows: params.startWithWindows } });
      return { startWithWindows: params.startWithWindows };
    },
    // The preview is the newest version; Check now says so.
    'updates.status': () => ({ ...updates }),
    'updates.check': () => {
      if (updates.state === 'ready' || updates.state === 'downloading') {
        return { ...updates };
      }
      updates = { ...updates, state: 'idle', lastCheckedAt: isoWithOffset(new Date()), message: `Memento ${updates.currentVersion} is the newest version.` };
      env.emit('updates.progress', { ...updates });
      return { ...updates };
    },
    'updates.apply': () => {
      if (updates.state !== 'ready') {
        throw new MockHostError('updates.notReady', 'No update has been downloaded yet, so there is nothing to install. Use Check now in Settings › General; Memento keeps working as it is.');
      }
      return {};
    },
  };

  const invalid = (message: string, detail: string): MockHostError => new MockHostError('settings.invalidValue', message, detail);

  return {
    handlers,
    agenda,
    exports,
    importAgain,
    throwIfMoving() {
      if (moving) {
        throw new MockHostError(
          'library.busy',
          'The library is being copied to its new folder, so a recording can\'t start right now. Nothing was started. Recording is possible again as soon as the move has finished; its progress is in Settings › Storage and history.',
          'move',
        );
      }
    },
    mergeSettings(settings, params) {
      // Field by field, as the host: a null default folder or reclaim age clears it.
      const merged = mergeSettings(settings, { general: params.general ?? null, export: params.export ?? null, ai: params.ai ?? null, storage: params.storage ?? null });
      const { general, storage } = merged;
      // The page's JSON is not checked against the types, so another language can still arrive.
      if ((general.language as string) !== 'en') {
        throw invalid('English is the only interface language in this version. Nothing was changed.', general.language);
      }
      // As the host, a blank folder is no folder.
      const folder = merged.export.defaultFolder?.trim() ?? '';
      const exportBlock = { ...merged.export, defaultFolder: folder === '' ? null : folder };
      const days = storage.reclaimOlderThanDays;
      if (days !== null && !(Number.isInteger(days) && days >= 1 && days <= 3650)) {
        throw invalid('Recordings are made smaller after a whole number of days, from 1 to 3650. Nothing was changed.', String(days));
      }
      return { ...merged, export: exportBlock };
    },
  };
}
