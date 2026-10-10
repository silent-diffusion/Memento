import type { BridgeLogger, BridgeTransport } from './client';
import { formatSize } from '../format/storage';
import { estimateSizeBytes, isoWithOffset, mockTracks, SAMPLE_SOURCES, sampleProjects, type MockProject } from './mockData';
import { advanceStages, processingOf, queryLibrary, visibleStages } from './mockLibrary';
import { mockMediaUrls } from './mockMedia';
import { createMockModels, engineDetail, MODEL_IDS, type ModelFailureMode } from './mockModels';
import { createMockSession, MockHostError } from './mockSession';
import { createMockTranscription, type StageFlag } from './mockTranscription';
import { LIVE_DRAFT_LINES } from './mockTranscripts';
import { createMockM3, DEFAULT_M3_FLAGS, defaultM3Settings, m3FlagsFromQuery, type M3Flags } from './mockLibraryExtra';
import { createMockM4, DEFAULT_M4_FLAGS, m4FlagsFromQuery, m4Settings, type M4Flags } from './mockGeneration';
import { createMockClipboard } from './mockClipboard';
import { createMockReview } from './mockReview';
import { mockGpuMemory, mockGpuNote } from './mockGpu';
import { linkHistory } from './mockHistory';
import { localModelsInstalled } from './mockLocalModel';
import type {
  AnnotationOrigin,
  BridgeEventEnvelope,
  BridgeRequest,
  BridgeResponse,
  Chapter,
  EventName,
  EventPayload,
  FooterStatusPayload,
  Highlight,
  MethodName,
  MethodParams,
  MethodResult,
  Project,
  RecordingSummary,
  RecoveredRecording,
  SettingsSnapshot,
  StageStatus,
  ThemePreference,
} from './types';

/** How the browser-preview host behaves. `mockOptionsFromQuery` reads them from the page URL. */
export interface MockOptions {
  /** `sample` (default): about 14 recordings; `empty`: the first-run library (`?empty=1`). */
  library?: 'sample' | 'empty';
  /** Show the recovered-recording dialog on first load (default true; `?recovery=0` hides it). */
  recovery?: boolean;
  /** Report low disk space and emit storage.lowSpace (`?lowspace=1`). */
  lowSpace?: boolean;
  /** Lose a source this far into a recording (`?lost=1` = 10 s). */
  lostAfterMs?: number | null;
  /** Initial theme setting (`?theme=dark`). */
  theme?: ThemePreference;
  /** Background activity: processing progress ticks. Off in unit tests. */
  live?: boolean;
  /** The long sample recording's transcript state (`?stage=queued|running|failed|paused`; default done). */
  stage?: StageFlag;
  /** The long sample's segment count (`?segments=10000`), for measuring the transcript list. */
  segments?: number;
  /** Send recording.liveTranscript during a session (`?live=1`); Settings then defaults to "During recording". */
  liveTranscript?: boolean;
  /** How simulated model downloads end (`?models=nospace|fail`). */
  models?: ModelFailureMode;
  /** `?models=empty`: no model installed yet, like a first run. */
  modelsInstalled?: 'sample' | 'none';
  /** Milliseconds between steps of simulated passes and downloads (shorter in tests). */
  stepMs?: number;
  now?: () => number;
  /** M3 failure cases (`?export=fail|unwritable`, `?agenda=ocrmissing|nodrop`, `?import=unsupported`, `?move=busy`). */
  m3?: Partial<M3Flags>;
  /** M4: `?ai=off|nokey|ready|local` (starting AI settings, keys, local model) and `?gen=fail|rate` (the first generation fails). */
  m4?: Partial<M4Flags>;
  /** After 1.2.0: another program holds the clipboard, so transcript.copy and documents.copy answer clipboard.unavailable. */
  clipboardBusy?: boolean;
}

const STAGE_FLAGS: readonly StageFlag[] = ['done', 'queued', 'running', 'failed', 'paused'];

export function mockOptionsFromQuery(search: string): MockOptions {
  const query = new URLSearchParams(search);
  const theme = query.get('theme');
  const stage = query.get('stage');
  const segments = Number(query.get('segments'));
  const models = query.get('models');
  const options: MockOptions = {
    library: query.get('empty') === '1' ? 'empty' : 'sample',
    recovery: query.get('recovery') !== '0',
    lowSpace: query.get('lowspace') === '1',
    lostAfterMs: query.get('lost') === '1' ? 10_000 : null,
    liveTranscript: query.get('live') === '1',
    models: models === 'nospace' ? 'noSpace' : models === 'fail' ? 'network' : 'none',
    modelsInstalled: models === 'empty' ? 'none' : 'sample',
    m3: m3FlagsFromQuery(query),
    m4: m4FlagsFromQuery(query),
  };
  if (theme === 'dark' || theme === 'light' || theme === 'system') {
    options.theme = theme;
  }
  if (stage !== null && (STAGE_FLAGS as readonly string[]).includes(stage)) {
    options.stage = stage as StageFlag;
  }
  if (Number.isInteger(segments) && segments > 0) {
    options.segments = Math.min(50_000, segments);
  }
  return options;
}

const GIB = 1024 ** 3;

type Handlers = { [M in MethodName]: (params: MethodParams<M>) => MethodResult<M> };

/**
 * Stand-in for the host when the page runs in a plain browser (npm run dev): an in-memory library
 * with invented sample recordings, a simulated recording session and every M1 method. It says so in
 * the console. Nothing here is real measurement.
 */
export function createMockTransport(logger: BridgeLogger, options: MockOptions = {}): BridgeTransport {
  const listeners = new Set<(message: unknown) => void>();
  const now = options.now ?? (() => Date.now());
  const live = options.live ?? true;
  const prefersDark = (): boolean =>
    typeof window !== 'undefined' && typeof window.matchMedia === 'function'
      ? window.matchMedia('(prefers-color-scheme: dark)').matches
      : false;

  let settings: SettingsSnapshot = {
    theme: options.theme ?? 'system',
    libraryPath: 'D:\\Memento Library',
    listDensity: 'comfortable',
    recording: {
      defaultType: 'meeting',
      defaultSourceIds: ['mic:usb-mv7', 'system:default-output', 'app:4120'],
      keepSeparateTracks: true,
      storage: { codec: 'flac', bitrateKbps: null, downmixMono: false, keepOnlyMix: false },
      checkpointSeconds: 30,
      lowSpaceGb: 10,
    },
    transcription: {
      auto: true,
      timing: (options.liveTranscript ?? false) ? 'during' : 'after',
      pauseWhenBusy: true,
      modelId: MODEL_IDS.turbo,
      cpuFallbackModelId: MODEL_IDS.small,
      language: 'auto',
      keepWordTimestamps: true,
      lowConfidenceThreshold: 0.5,
      liveOnGpu: false,
    },
    speakers: { identify: true, expectedSpeakers: 'auto', rememberRenamed: true, embeddingModelId: MODEL_IDS.titanet, rememberVoices: false },
    history: { keepVersions: true, keepDays: 90 },
    ...defaultM3Settings(),
    documents: { defaultTemplateId: 'meeting-minutes', defaultStyleId: 'corporate' },
  };
  const m4Flags: M4Flags = { ...DEFAULT_M4_FLAGS, ...options.m4 };
  settings = { ...settings, ...m4Settings(settings, m4Flags.ai, m4Flags.vram) };
  const isDark = (): boolean => settings.theme === 'dark' || (settings.theme === 'system' && prefersDark());

  const projects = new Map<string, MockProject>();
  if (options.library !== 'empty') {
    for (const project of sampleProjects(new Date(now()))) {
      projects.set(project.summary.id, project);
    }
  }
  // A project exists from the moment recording starts (BRIDGE.md: project.updateDetails works during
  // recording); the preview lists it in the Library once it has finalized.
  const listed = () => [...projects.values()].filter((p) => p.summary.state !== 'recording' && p.summary.state !== 'finalizing');
  const summaries = () => listed().map((p) => p.summary);

  const recovered: RecoveredRecording[] = [];
  const recoverySample = projects.get('20261002-153000-sam11');
  if ((options.recovery ?? true) && recoverySample !== undefined) {
    recovered.push({
      recordingId: recoverySample.summary.id,
      title: recoverySample.summary.title,
      startedAt: recoverySample.summary.createdAt,
      tracksIntact: 2,
      tracksTotal: 2,
      lastCheckpointAt: recoverySample.summary.createdAt,
      recoveredDurationMs: recoverySample.summary.durationMs,
      mayBeMissingMs: 20_000,
    });
  }

  // engine.detail is filled in from the model manager once it exists (refreshEngine below).
  let footer: FooterStatusPayload = {
    engine: {
      ready: true,
      device: 'GPU',
      detail: { ready: true, device: 'GPU', gpuName: null, freeVramBytes: null, model: null, paused: null, gpuMemory: null, note: null },
    },
    storage: { freeBytes: (options.lowSpace ?? false) ? 4 * GIB : 212 * GIB, lowSpace: options.lowSpace ?? false },
    recording: { active: false, lastCheckpointAt: null, lostSource: null },
    // The host's only reason in M1 (FooterStatusService.LowSpaceReason), sent while space is low.
    processingPaused: (options.lowSpace ?? false) ? 'Low disk space' : null,
    export: { active: false, percent: null, title: null },
  };

  const deliver = (message: BridgeResponse | BridgeEventEnvelope): void => {
    setTimeout(() => {
      for (const listener of listeners) {
        listener(message);
      }
    }, 0);
  };
  const emit = <E extends EventName>(event: E, payload: EventPayload<E>): void => {
    deliver({ event, payload });
  };
  const emitFooter = (): void => {
    emit('status.footer', footer);
  };
  const changed = (...recordingIds: string[]): void => {
    emit('library.changed', { recordingIds });
  };

  const find = (recordingId: string): MockProject => {
    const project = projects.get(recordingId);
    if (project === undefined) {
      throw new MockHostError(
        'project.notFound',
        'This recording is no longer in the library; it may have been deleted. Nothing was changed. Go back to the Library to see what is there.',
        recordingId,
      );
    }
    return project;
  };

  // Media exists once finalize has written the mix and peaks (the stored stage is done).
  const mediaOf = (project: MockProject) => {
    const { summary } = project;
    const storing = project.stages.some((st) => st.stage === 'stored' && st.state !== 'done');
    if (summary.state === 'recording' || summary.state === 'finalizing' || storing) {
      return null;
    }
    return mockMediaUrls(summary.id, summary.durationMs);
  };

  const toProject = (project: MockProject): Project => {
    // A sample transcript links its highlights and writes its history lines when first built.
    transcription.prepare(project.summary.id);
    const media = mediaOf(project);
    return {
    summary: project.summary,
    details: project.details,
    tracks: project.tracks ?? mockTracks(project.summary.id, project.trackSources, project.summary.durationMs),
    mixUrl: media?.mixUrl ?? null,
    peaksUrl: media?.peaksUrl ?? null,
    chapters: project.chapters,
    highlights: project.highlights,
    topics: project.topics,
    history: project.history,
    integrity: { algorithm: 'sha256', computedAt: null },
    sizeBytes: estimateSizeBytes(project.summary, project.trackSources.length),
    mixOnly: project.mixOnly ?? null,
    };
  };

  // The host assigns annotation ids on add (c…, h…, t…); any id the page sends with an add is ignored.
  let idCounter = 0;
  const nextId = (prefix: 'c' | 'h' | 't'): string => `${prefix}${(++idCounter).toString(16).padStart(10, '0')}`;

  const invalid = (message: string): MockHostError => new MockHostError('bridge.invalidParams', message);
  const missing = (kind: 'chapter' | 'highlight' | 'topic', id: string): MockHostError =>
    new MockHostError(
      'annotations.notFound',
      `That ${kind} is not in this recording any more; it may have been removed. Nothing was changed. Reopen the recording to see its current ${kind}s.`,
      id,
    );
  // The page's JSON is not checked against the types, so an unknown origin can still arrive.
  const ORIGINS: readonly string[] = ['user', 'local', 'ai'] satisfies AnnotationOrigin[];
  const originOf = (origin: AnnotationOrigin | undefined): AnnotationOrigin => {
    if (origin === undefined) {
      return 'user';
    }
    if (!ORIGINS.includes(origin)) {
      throw invalid(`Origin '${origin}' is not one of user, local, ai.`);
    }
    return origin;
  };
  const byTime = <T extends { atMs: number }>(items: T[]): T[] => items.sort((a, b) => a.atMs - b.atMs);

  const setStages = (project: MockProject, stages: StageStatus[]): void => {
    const wasProcessing = project.summary.isProcessing;
    project.stages = stages;
    project.summary = {
      ...project.summary,
      stages: visibleStages(stages),
      isProcessing: stages.some((st) => st.state === 'active' || st.state === 'queued'),
    };
    emit('processing.progress', { recordingId: project.summary.id, stages });
    if (!project.summary.isProcessing || !wasProcessing) {
      changed(project.summary.id);
    }
  };

  const models = createMockModels({
    emit,
    freeBytes: () => footer.storage.freeBytes ?? 0,
    inUse: () => transcription.inUse(),
    failure: options.models ?? 'none',
    installed: options.modelsInstalled ?? 'sample',
    alsoInstalled: m4Flags.llm !== undefined ? localModelsInstalled(m4Flags.llm) : m4Flags.ai === 'local' ? [MODEL_IDS.qwen] : [],
    onInstalled: () => {
      refreshEngine();
      emitFooter();
      transcription.modelInstalled();
    },
    ...(options.stepMs === undefined ? {} : { stepMs: options.stepMs }),
  });

  const transcription = createMockTranscription({
    emit,
    now,
    settings: () => settings,
    projects,
    setStages,
    changed,
    models,
    isRecording: (recordingId) => session.activeRecordingId() === recordingId,
    onPausedChange: () => {
      refreshEngine();
      emitFooter();
    },
    stageFlag: options.stage ?? 'done',
    ...(options.segments === undefined ? {} : { segmentCount: options.segments }),
    ...(options.stepMs === undefined ? {} : { stepMs: options.stepMs }),
  });

  // `?vram=low`: Ollama holds the card, so Large v3 Turbo (2.5 GB) transcribes on the processor and says why.
  const transcriptionDetail = () => {
    const modelId = settings.transcription.modelId;
    const short = m4Flags.vram === 'low' && modelId === MODEL_IDS.turbo;
    const name = models.list().find((m) => m.id === modelId)?.name ?? modelId;
    return engineDetail(models, modelId, short ? 'CPU' : 'GPU', transcription.pausedReason(), {
      gpuMemory: mockGpuMemory(m4Flags.vram),
      note: short ? mockGpuNote(m4Flags.vram, name, '2.5 GB', 'it transcribes on the processor (slower)') : null,
    });
  };
  function refreshEngine(): void {
    // Like the host's footer: the card's memory and the note are for Settings only.
    const detail = { ...transcriptionDetail(), gpuMemory: null, note: null };
    footer = {
      ...footer,
      engine: { ready: detail.ready, device: detail.device, detail },
      processingPaused: footer.storage.lowSpace ? 'Low disk space' : transcription.pausedReason(),
    };
  }
  refreshEngine();

  // The live draft of the Recording session (`?live=1` with Timing set to During recording).
  let liveTimer: ReturnType<typeof setInterval> | null = null;
  const startLiveDraft = (sessionId: string): void => {
    if (!(options.liveTranscript ?? false) || settings.transcription.timing !== 'during') {
      return;
    }
    if (liveTimer !== null) {
      clearInterval(liveTimer);
    }
    liveTimer = setInterval(() => {
      const current = session.current();
      if (current?.sessionId !== sessionId || current.state === 'ready' || current.state === 'stopped' || current.state === 'finalizing') {
        if (liveTimer !== null) {
          clearInterval(liveTimer);
        }
        liveTimer = null;
        return;
      }
      if (current.state !== 'recording') {
        return;
      }
      const seconds = current.elapsedMs / 1000;
      const count = Math.max(1, Math.floor(seconds / 6) + 1);
      const segments = Array.from({ length: count }, (_, i) => {
        const text = LIVE_DRAFT_LINES[i % Math.max(1, LIVE_DRAFT_LINES.length)] ?? '';
        const start = i * 6;
        const newest = i === count - 1;
        // The newest line is still being heard: only its first words so far.
        const words = text.split(' ');
        const heard = newest ? words.slice(0, Math.max(2, Math.round(words.length * Math.min(1, (seconds - start) / 6)))).join(' ') : text;
        return { start, end: Math.min(seconds, start + 5.5), text: heard };
      });
      emit('recording.liveTranscript', { sessionId, segments, state: 'listening', engine: 'Local · CPU · Small', note: null });
    }, 1500);
  };

  const session = createMockSession({
    emit,
    sources: () => SAMPLE_SOURCES,
    now,
    checkpointSeconds: () => settings.recording.checkpointSeconds,
    lostAfterMs: options.lostAfterMs ?? null,
    onFooterChange: (recording) => {
      footer = { ...footer, recording };
      emitFooter();
    },
    onStarted: (result) => {
      const summary: RecordingSummary = {
        id: result.recordingId,
        title: result.title,
        type: result.type,
        createdAt: result.startedAt,
        durationMs: 0,
        participantCount: 0,
        hasVideo: false,
        stages: [],
        people: [],
        isProcessing: false,
        state: 'recording',
        sizeBytes: 0,
        matchSnippet: null,
      };
      projects.set(summary.id, {
        summary,
        stages: [],
        details: {
          title: result.title,
          type: result.type,
          participants: [],
          purpose: '',
          platform: '',
          organization: '',
          location: '',
          notes: '',
          tags: [],
          agenda: { source: null, parsedLocally: true, items: [] },
          whoSpoke: { count: null, names: [] },
        },
        trackSources: result.tracks.map((t) => t.sourceKind),
        tracks: result.tracks,
        chapters: [],
        highlights: [],
        topics: [],
        history: [],
      });
    },
    onHighlight: (recordingId, highlight) => {
      const project = projects.get(recordingId);
      if (project !== undefined) {
        project.highlights = [...project.highlights, highlight].sort((a, b) => a.atMs - b.atMs);
      }
    },
    onFinalizing: (recordingId, durationMs) => {
      const project = projects.get(recordingId);
      if (project !== undefined) {
        project.summary = { ...project.summary, state: 'finalizing', durationMs };
      }
    },
    onFinalized: (result) => {
      const project = projects.get(result.recordingId);
      if (project === undefined) {
        return;
      }
      const trackCount = result.tracks.length;
      project.tracks = result.tracks.map((t) => ({ ...t, file: t.file.replace(/\.wav$/, '.flac') }));
      project.trackSources = result.tracks.map((t) => t.sourceKind);
      // Finalize always stores lossless FLAC; a smaller format in Settings queues the optimize stage after it.
      const smaller = settings.recording.storage.codec !== 'flac';
      // The transcript, speakers and topics stages wait for the stored one (Settings › Transcription, Speakers).
      const queuedStage = (stage: StageStatus['stage']): StageStatus => ({ stage, state: 'queued', percent: null, label: 'Queued' });
      const pipeline: StageStatus[] = [
        { stage: 'stored', state: 'active', percent: 0, label: 'Saving tracks' },
        ...(settings.transcription.auto ? [queuedStage('transcript')] : []),
        ...(settings.transcription.auto && settings.speakers.identify ? [queuedStage('speakers')] : []),
        ...(settings.transcription.auto ? [queuedStage('topics')] : []),
        ...(smaller ? [queuedStage('optimize')] : []),
      ];
      project.stages = pipeline;
      project.summary = {
        ...project.summary,
        durationMs: result.durationMs,
        stages: visibleStages(pipeline),
        isProcessing: true,
        state: 'ready',
      };
      project.summary.sizeBytes = estimateSizeBytes(project.summary, trackCount);
      project.history = [
        {
          at: result.startedAt,
          stage: 'recorded',
          event: 'completed',
          summary: `Recorded ${trackCount} ${trackCount === 1 ? 'track' : 'tracks'}`,
          detail: result.tracks.map((t) => t.name).join(', '),
        },
      ];
      changed(project.summary.id);
      // Storing (FLAC + mix + peaks), then making smaller if asked, finish over a few seconds.
      const label = (st: StageStatus): string =>
        st.stage === 'optimize' ? `${st.percent ?? 0}% · making smaller` : `Saving tracks · ${st.percent ?? 0}%`;
      const finished = (before: StageStatus[], after: StageStatus[], stage: StageStatus['stage']): boolean =>
        before.some((st) => st.stage === stage && st.state !== 'done') && after.some((st) => st.stage === stage && st.state === 'done');
      const timer = setInterval(() => {
        const current = projects.get(result.recordingId);
        if (current === undefined) {
          clearInterval(timer);
          return;
        }
        const stages = advanceStages(current.stages, 25, 'this PC').map((st) => (st.state === 'active' ? { ...st, label: label(st) } : st));
        const at = isoWithOffset(new Date(now()));
        const storedDone = finished(current.stages, stages, 'stored');
        if (storedDone) {
          current.history = [
            ...current.history,
            {
              at,
              stage: 'stored',
              event: 'completed',
              summary: 'Stored as lossless FLAC',
              detail: `${trackCount} ${trackCount === 1 ? 'track' : 'tracks'} · ${formatSize(current.summary.sizeBytes)} on this PC`,
            },
          ];
        }
        if (finished(current.stages, stages, 'optimize')) {
          const codec = settings.recording.storage.codec.toUpperCase();
          current.history = [
            ...current.history,
            { at, stage: 'optimize', event: 'completed', summary: 'Saved smaller files', detail: `${codec} · the lossless FLAC files were replaced` },
          ];
        }
        setStages(current, stages);
        // Once stored, the transcript and speakers stages follow (Settings › Transcription), then optimize.
        if (storedDone || !current.summary.isProcessing) {
          clearInterval(timer);
          transcription.queueNewRecording(current);
        }
      }, 1000);
    },
  });

  // M3: agenda import, attachments, import, export, library move and reclaim, keys, startup.
  const m3 = createMockM3({
    emit,
    now,
    projects,
    find,
    toProject,
    tracks: (project) => project.tracks ?? mockTracks(project.summary.id, project.trackSources, project.summary.durationMs),
    changed,
    settings: () => settings,
    setSettings: (next) => {
      settings = next;
    },
    freeBytes: () => footer.storage.freeBytes ?? 0,
    setFooterExport: (status) => {
      footer = { ...footer, export: status };
      emitFooter();
    },
    setStages,
    queueAfterStored: (project) => {
      transcription.queueNewRecording(project);
    },
    transcriptSegments: (recordingId) => transcription.get(recordingId).transcript?.segments.length ?? null,
    busyTitle: () => {
      const id = session.activeRecordingId();
      return id === null ? null : (projects.get(id)?.summary.title ?? 'the current recording');
    },
    log: (message) => {
      logger.info(message);
    },
    version: '0.3.0-dev',
    flags: { ...DEFAULT_M3_FLAGS, ...options.m3 },
    stepMs: options.stepMs ?? 400,
    documents: (recordingId) => m4.documents.exportable(recordingId),
  });

  // M4: modules, templates, styles, providers, generation and documents.
  const m4 = createMockM4({
    emit,
    now,
    find,
    settings: () => settings,
    models,
    transcript: (recordingId) => {
      const transcript = transcription.get(recordingId).transcript;
      if (transcript === null) {
        return [];
      }
      const names = new Map(transcript.speakers.map((sp) => [sp.id, sp.name]));
      return transcript.segments.slice(0, 40).map((seg) => ({ t: seg.start, speaker: (seg.speaker === null ? null : names.get(seg.speaker)) ?? 'Speaker', text: seg.text }));
    },
    attachmentNames: (recordingId) => m3.handlers['attachments.list']({ recordingId }).attachments.map((a) => a.name),
    flags: m4Flags,
    stepMs: options.stepMs ?? 400,
  });

  // After 1.2.0: transcript.copy and documents.copy, with the copy kept in memory.
  const clipboard = createMockClipboard({
    summary: (recordingId) => find(recordingId).summary,
    transcript: (recordingId) => transcription.get(recordingId).transcript,
    document: (recordingId, documentId) => {
      const { document } = m4.handlers['documents.get']({ recordingId, documentId });
      const { html } = m4.handlers['documents.renderHtml']({ recordingId, documentId, mode: 'print' });
      const markdown = html.replace(/<[^>]+>/g, ' ').replace(/[ \t]+/g, ' ').trim();
      return { title: document.title, markdown, html };
    },
    busy: () => options.clipboardBusy ?? false,
  });

  // 2.0 Review: known voices, suggested chapters, several lines to one speaker.
  const review = createMockReview({ find, transcription, settings: () => settings, now });

  const settingsInvalid = (message: string, detail: string): MockHostError => new MockHostError('settings.invalidValue', message, detail);

  /** Like the host: each field present (not null) replaces the stored one; the others keep their value. */
  function mergeBlock<T extends object>(current: T, patch: Partial<T> | null | undefined): T {
    if (patch == null) {
      return current;
    }
    const merged = { ...current };
    for (const key of Object.keys(patch) as (keyof T)[]) {
      const value = patch[key];
      if (value !== undefined && value !== null) {
        merged[key] = value;
      }
    }
    return merged;
  }

  /**
   * The M2 blocks after the merge: a model must be installed for its engine (one equal to the
   * current setting is accepted as it is, so a whole block can be sent), and values must be ones
   * Settings offers.
   */
  function validateM2Settings(next: Pick<SettingsSnapshot, 'transcription' | 'speakers' | 'history'>): void {
    const usable = (modelId: string, current: string, engine: 'transcription' | 'speakers'): boolean =>
      modelId === current ||
      models.list().some((m) => m.id === modelId && m.engine === engine && m.installed && (engine !== 'speakers' || m.role === 'embedding'));
    const t = next.transcription;
    if (!usable(t.modelId, settings.transcription.modelId, 'transcription') || !usable(t.cpuFallbackModelId, settings.transcription.cpuFallbackModelId, 'transcription')) {
      throw settingsInvalid('That model is not installed yet. Install it first, then make it the default. Nothing was changed.', t.modelId);
    }
    if (!(t.lowConfidenceThreshold > 0 && t.lowConfidenceThreshold < 1) || !['after', 'during'].includes(t.timing)) {
      throw settingsInvalid('That transcription setting is not available. Nothing was changed.', String(t.lowConfidenceThreshold));
    }
    const sp = next.speakers;
    const expected = sp.expectedSpeakers;
    if (expected !== 'auto' && !(Number.isInteger(expected) && expected >= 1 && expected <= 20)) {
      throw settingsInvalid('Expected speakers is Auto or a number from 1 to 20. Nothing was changed.', String(expected));
    }
    if (!usable(sp.embeddingModelId, settings.speakers.embeddingModelId, 'speakers')) {
      throw settingsInvalid('That voice model is not installed yet. Install it first. Nothing was changed.', sp.embeddingModelId);
    }
    const h = next.history;
    if (!(Number.isInteger(h.keepDays) && h.keepDays > 0)) {
      throw settingsInvalid('Versions are kept for a whole number of days. Nothing was changed.', String(h.keepDays));
    }
  }

  const handlers: Handlers = {
    'app.version': () => ({ version: '0.3.0-dev', osVersion: 'Browser preview', isDarkTheme: isDark() }),
    'app.openExternal': (params) => {
      logger.info(`[bridge:mock] would open ${params.url}`);
      return { opened: false };
    },
    'ui.ready': () => ({}),
    'settings.get': () => m4.present(settings),
    'settings.set': (params) => {
      if (params.libraryPath != null && params.libraryPath !== settings.libraryPath) {
        throw new MockHostError(
          'settings.libraryMoveUnavailable',
          `Moving the library is not available in this version. Your recordings stay in ${settings.libraryPath}.`,
          params.libraryPath,
        );
      }
      const m2 = {
        transcription: mergeBlock(settings.transcription, params.transcription),
        speakers: mergeBlock(settings.speakers, params.speakers),
        history: mergeBlock(settings.history, params.history),
      };
      validateM2Settings(m2);
      const themeBefore = isDark();
      const modelBefore = settings.transcription.modelId;
      settings = {
        ...settings,
        theme: params.theme ?? settings.theme,
        listDensity: params.listDensity ?? settings.listDensity,
        // The recording block is replaced whole (the UI always sends all of it).
        recording: params.recording ?? settings.recording,
        ...m2,
      };
      settings = m3.mergeSettings(settings, params);
      settings = m4.mergeSettings(settings, params);
      if (isDark() !== themeBefore) {
        emit('theme.changed', { isDark: isDark() });
      }
      if (settings.transcription.modelId !== modelBefore) {
        refreshEngine();
        emitFooter();
      }
      return m4.present(settings);
    },
    'library.list': (params) => queryLibrary(summaries(), params, (id, query) => transcription.librarySnippet(id, query)),
    'library.processing': () => processingOf(listed()),
    'project.get': (params) => toProject(find(params.recordingId)),
    'project.updateDetails': (params) => {
      const project = find(params.recordingId);
      let details = params.details;
      const whoSpoke = details.whoSpoke;
      if (whoSpoke !== undefined) {
        if (whoSpoke.count !== null && (!Number.isInteger(whoSpoke.count) || whoSpoke.count < 1 || whoSpoke.count > 20)) {
          throw invalid(`The number of speakers is a whole number from 1 to 20, or none to let Memento decide; ${whoSpoke.count} is not.`);
        }
        const names: string[] = [];
        for (const raw of whoSpoke.names) {
          const name = raw.trim();
          if (name === '' || name.length > 100) {
            throw invalid('Each speaker name needs 1 to 100 characters.');
          }
          if (!names.some((n) => n.toLocaleLowerCase() === name.toLocaleLowerCase())) {
            names.push(name);
          }
        }
        if (names.length > 20) {
          throw invalid(`At most 20 speakers can be named; this list has ${names.length}.`);
        }
        details = { ...details, whoSpoke: { count: whoSpoke.count, names } };
      }
      project.details = { ...project.details, ...details };
      project.summary = {
        ...project.summary,
        title: project.details.title,
        type: project.details.type,
        people: project.details.participants,
        participantCount: project.details.participants.length,
      };
      changed(project.summary.id);
      return toProject(project);
    },
    'project.deleteEstimate': (params) => {
      const project = find(params.recordingId);
      const tracks = project.trackSources.length;
      const items = ['the recording', tracks === 1 ? 'its track' : `its ${tracks} tracks`];
      if (project.stages.some((st) => st.stage === 'transcript' && st.state === 'done')) {
        items.push('its transcript');
      }
      if (project.stages.some((st) => st.stage === 'minutes' && st.state === 'done')) {
        items.push('its documents');
      }
      items.push('its details and highlights');
      return {
        title: project.summary.title,
        sizeBytes: estimateSizeBytes(project.summary, tracks),
        items,
      };
    },
    'project.delete': (params) => {
      const project = find(params.recordingId);
      if (session.activeRecordingId() === params.recordingId) {
        throw new MockHostError(
          'project.recording',
          `"${project.summary.title}" is still recording. Stop the recording first; nothing was deleted.`,
        );
      }
      projects.delete(params.recordingId);
      changed(params.recordingId);
      return {};
    },
    'project.rename': (params) => {
      const project = find(params.recordingId);
      const title = params.title.trim();
      if (title === '') {
        throw invalid('A recording needs a title. The old title was kept.');
      }
      project.summary = { ...project.summary, title };
      project.details = { ...project.details, title };
      changed(project.summary.id);
      return toProject(project);
    },
    'annotations.addChapter': (params) => {
      const project = find(params.recordingId);
      const { atMs, title, origin } = params.chapter;
      if (atMs === undefined) {
        throw invalid('A new chapter needs a time (atMs).');
      }
      const chapter: Chapter = { id: nextId('c'), atMs, title: (title ?? '').trim(), origin: originOf(origin) };
      project.chapters = byTime([...project.chapters, chapter]);
      return { chapters: project.chapters };
    },
    'annotations.updateChapter': (params) => {
      const project = find(params.recordingId);
      const { id, atMs, title, origin } = params.chapter;
      if (id === undefined) {
        throw invalid('Say which chapter to change: the chapter needs its id.');
      }
      const existing = project.chapters.find((c) => c.id === id);
      if (existing === undefined) {
        throw missing('chapter', id);
      }
      const changedChapter: Chapter = {
        ...existing,
        atMs: atMs ?? existing.atMs,
        title: title?.trim() ?? existing.title,
        origin: origin === undefined ? existing.origin : originOf(origin),
      };
      project.chapters = byTime(project.chapters.map((c) => (c.id === id ? changedChapter : c)));
      return { chapters: project.chapters };
    },
    'annotations.removeChapter': (params) => {
      const project = find(params.recordingId);
      if (!project.chapters.some((c) => c.id === params.chapterId)) {
        throw missing('chapter', params.chapterId);
      }
      project.chapters = project.chapters.filter((c) => c.id !== params.chapterId);
      return { chapters: project.chapters };
    },
    'annotations.addHighlight': (params) => {
      const project = find(params.recordingId);
      const { atMs, note, origin, segmentId } = params.highlight;
      if (atMs === undefined) {
        throw invalid('A new highlight needs a time (atMs).');
      }
      const highlight: Highlight = { id: nextId('h'), atMs, note: (note ?? '').trim(), origin: originOf(origin), segmentId: segmentId ?? null };
      project.highlights = byTime([...project.highlights, highlight]);
      return { highlights: project.highlights };
    },
    'annotations.updateHighlight': (params) => {
      const project = find(params.recordingId);
      const { id, atMs, note, origin, segmentId } = params.highlight;
      if (id === undefined) {
        throw invalid('Say which highlight to change: the highlight needs its id.');
      }
      const existing = project.highlights.find((h) => h.id === id);
      if (existing === undefined) {
        throw missing('highlight', id);
      }
      const changedHighlight: Highlight = {
        ...existing,
        atMs: atMs ?? existing.atMs,
        note: note?.trim() ?? existing.note,
        origin: origin === undefined ? existing.origin : originOf(origin),
        segmentId: segmentId ?? existing.segmentId,
      };
      project.highlights = byTime(project.highlights.map((h) => (h.id === id ? changedHighlight : h)));
      return { highlights: project.highlights };
    },
    'annotations.removeHighlight': (params) => {
      const project = find(params.recordingId);
      if (!project.highlights.some((h) => h.id === params.highlightId)) {
        throw missing('highlight', params.highlightId);
      }
      project.highlights = project.highlights.filter((h) => h.id !== params.highlightId);
      return { highlights: project.highlights };
    },
    'annotations.addTopic': (params) => {
      const project = find(params.recordingId);
      const label = (params.topic.label ?? '').trim();
      if (label === '') {
        throw invalid('A topic needs a label.');
      }
      const origin = originOf(params.topic.origin);
      // A label the recording already has, ignoring case, changes nothing (as on the host).
      if (!project.topics.some((t) => t.label.toLocaleLowerCase() === label.toLocaleLowerCase())) {
        project.topics = [...project.topics, { id: nextId('t'), label, origin }];
      }
      return { topics: project.topics };
    },
    'annotations.removeTopic': (params) => {
      const project = find(params.recordingId);
      if (!project.topics.some((t) => t.id === params.topicId)) {
        throw missing('topic', params.topicId);
      }
      project.topics = project.topics.filter((t) => t.id !== params.topicId);
      return { topics: project.topics };
    },
    'sources.list':() => ({ audio: [...SAMPLE_SOURCES], videoAvailable: false }),
    'recording.start': (params) => {
      m3.throwIfMoving();
      const started = session.start(params);
      // Like the host: the sources and type of a recording that starts are remembered for the next one.
      settings = { ...settings, recording: { ...settings.recording, defaultSourceIds: [...params.sourceIds], defaultType: params.type } };
      startLiveDraft(started.sessionId);
      return started;
    },
    'recording.setSource': (params) => {
      const tracks = session.setSource(params.sessionId, params.sourceId, params.enabled);
      return { tracks };
    },
    'recording.pause': (params) => {
      session.pause(params.sessionId);
      return {};
    },
    'recording.resume': (params) => {
      session.resume(params.sessionId);
      return {};
    },
    'recording.markHighlight': (params) => ({ highlight: session.markHighlight(params.sessionId, params.note) }),
    'recording.stop': (params) => session.stop(params.sessionId),
    'recording.current': () => ({ session: session.current() }),
    'recovery.list': () => ({ items: [...recovered] }),
    'recovery.acknowledge': (params) => {
      const index = recovered.findIndex((r) => r.recordingId === params.recordingId);
      if (index >= 0) {
        recovered.splice(index, 1);
      }
      return {};
    },
    'dialog.pickFolder': () => ({ path: 'E:\\Recordings\\Memento' }),
    'status.get': () => footer,
    'transcript.get': (params) => transcription.get(params.recordingId),
    'transcript.editSegment': (params) => transcription.editSegment(params),
    'transcript.setSegmentSpeaker': (params) => transcription.setSegmentSpeaker(params),
    'transcript.renameSpeaker': (params) => transcription.renameSpeaker(params),
    'transcript.mergeSpeakers': (params) => transcription.mergeSpeakers(params),
    'transcript.restoreSpeaker': (params) => transcription.restoreSpeaker(params),
    'transcript.removeSpeaker': (params) => transcription.removeSpeaker(params),
    'transcript.restoreSpeakers': (params) => transcription.restoreSpeakers(params),
    'transcript.reduceSpeakers': (params) => transcription.reduceSpeakers(params),
    'transcript.markReviewed': (params) => transcription.markReviewed(params.recordingId, params.reviewed),
    'transcript.search': (params) => ({ matches: transcription.search(params.recordingId, params.query) }),
    'transcript.retranscribe': (params) => {
      transcription.retranscribe(params);
      return {};
    },
    'transcript.versions': (params) => ({ versions: transcription.versions(params.recordingId) }),
    'transcript.restoreVersion': (params) => ({ transcript: transcription.restoreVersion(params.recordingId, params.versionId) }),
    'transcript.getVersion': (params) => ({ transcript: transcription.getVersion(params.recordingId, params.versionId) }),
    'history.links': (params) => {
      const project = find(params.recordingId);
      transcription.prepare(params.recordingId);
      const copies = [...transcription.copies(params.recordingId), ...m4.documents.copies(params.recordingId)];
      return { links: linkHistory(project.history, copies) };
    },
    'processing.retry': (params) => {
      if (params.stage === 'stored') {
        m3.importAgain(params.recordingId);
        return {};
      }
      transcription.retry(params);
      return {};
    },
    'processing.cancel': (params) => {
      transcription.cancel(params.recordingId, params.stage);
      return {};
    },
    'processing.pause': () => {
      transcription.pauseAll();
      return {};
    },
    'processing.resume': () => {
      transcription.resumeAll();
      return {};
    },
    'models.list': () => ({ models: models.list() }),
    'models.install': (params) => {
      models.install(params.modelId);
      return {};
    },
    'models.cancelInstall': (params) => {
      models.cancelInstall(params.modelId);
      return {};
    },
    'models.remove': (params) => {
      models.remove(params.modelId);
      refreshEngine();
      emitFooter();
      return {};
    },
    'engine.status': () => ({
      transcription: transcriptionDetail(),
      speakers: engineDetail(models, settings.speakers.embeddingModelId, 'CPU', transcription.pausedReason()),
    }),
    'engine.refresh': () => {
      refreshEngine();
      emitFooter();
      return {
        transcription: transcriptionDetail(),
        speakers: engineDetail(models, settings.speakers.embeddingModelId, 'CPU', transcription.pausedReason()),
      };
    },
    ...m3.handlers,
    ...m4.handlers,
    ...clipboard.handlers,
    ...review.handlers,
  };

  const answer = (request: BridgeRequest): BridgeResponse => {
    const handler = (handlers as Record<string, ((params: unknown) => unknown) | undefined>)[request.method];
    if (handler === undefined) {
      return {
        id: request.id,
        error: { code: 'bridge.unknownMethod', message: `Memento does not know '${request.method}'.`, detail: null },
      };
    }
    try {
      return { id: request.id, result: handler(request.params) };
    } catch (error) {
      if (error instanceof MockHostError) {
        return { id: request.id, error: { code: error.code, message: error.message, detail: error.detail } };
      }
      return {
        id: request.id,
        error: { code: 'bridge.internal', message: error instanceof Error ? error.message : 'The preview host failed.', detail: null },
      };
    }
  };

  let started = false;
  const startBackground = (): void => {
    if (started) {
      return;
    }
    started = true;
    // The real host pushes the footer status as soon as the page has loaded.
    emitFooter();
    if (options.lowSpace ?? false) {
      setTimeout(() => {
        emit('storage.lowSpace', {
          freeBytes: footer.storage.freeBytes ?? 0,
          thresholdBytes: settings.recording.lowSpaceGb * GIB,
          recordingContinues: true,
          transcriptionPaused: true,
        });
      }, 400);
    }
    if (live) {
      // The sample library's running passes (the processing card's recording) move on slowly.
      setInterval(() => {
        transcription.tick();
      }, 1500);
    }
  };

  logger.info('[bridge] window.chrome.webview is absent; answering with browser-preview mock data.');

  return {
    send: (request) => {
      logger.info(`[bridge:mock] ${request.method}`, request.params);
      deliver(answer(request));
    },
    // The real host reads the dropped files' paths from the message; here their names stand in for them.
    sendWithFiles: (request, files) => {
      logger.info(`[bridge:mock] ${request.method} with ${files.length} file(s)`, request.params);
      const params = files.length > 0 ? { ...(request.params as object), paths: files.map((file) => file.name) } : request.params;
      deliver(answer({ ...request, params } as typeof request));
    },
    subscribe: (listener) => {
      listeners.add(listener);
      startBackground();
      return () => {
        listeners.delete(listener);
      };
    },
  };
}
