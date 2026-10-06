import type { BridgeLogger, BridgeTransport } from './client';
import { formatSize } from '../format/storage';
import { estimateSizeBytes, isoWithOffset, mockTracks, SAMPLE_SOURCES, sampleProjects, type MockProject } from './mockData';
import { advanceStages, processingOf, queryLibrary, visibleStages } from './mockLibrary';
import { mockMediaUrls } from './mockMedia';
import { createMockSession, MockHostError } from './mockSession';
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
  now?: () => number;
}

export function mockOptionsFromQuery(search: string): MockOptions {
  const query = new URLSearchParams(search);
  const theme = query.get('theme');
  const options: MockOptions = {
    library: query.get('empty') === '1' ? 'empty' : 'sample',
    recovery: query.get('recovery') !== '0',
    lowSpace: query.get('lowspace') === '1',
    lostAfterMs: query.get('lost') === '1' ? 10_000 : null,
  };
  if (theme === 'dark' || theme === 'light' || theme === 'system') {
    options.theme = theme;
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
  };
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

  let footer: FooterStatusPayload = {
    engine: { ready: true, device: 'GPU' },
    storage: { freeBytes: (options.lowSpace ?? false) ? 4 * GIB : 212 * GIB, lowSpace: options.lowSpace ?? false },
    recording: { active: false, lastCheckpointAt: null, lostSource: null },
    // The host's only reason in M1 (FooterStatusService.LowSpaceReason), sent while space is low.
    processingPaused: (options.lowSpace ?? false) ? 'Low disk space' : null,
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
    project.stages = stages;
    project.summary = {
      ...project.summary,
      stages: visibleStages(stages),
      isProcessing: stages.some((st) => st.state === 'active' || st.state === 'queued'),
    };
    emit('processing.progress', { recordingId: project.summary.id, stages });
    if (!project.summary.isProcessing) {
      changed(project.summary.id);
    }
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
      const pipeline: StageStatus[] = [
        { stage: 'stored', state: 'active', percent: 0, label: 'Saving tracks' },
        ...(smaller ? [{ stage: 'optimize', state: 'queued', percent: null, label: 'Queued' } satisfies StageStatus] : []),
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
        if (finished(current.stages, stages, 'stored')) {
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
        if (!current.summary.isProcessing) {
          clearInterval(timer);
        }
      }, 1000);
    },
  });

  const handlers: Handlers = {
    'app.version': () => ({ version: '0.2.0-dev', osVersion: 'Browser preview', isDarkTheme: isDark() }),
    'app.openExternal': (params) => {
      logger.info(`[bridge:mock] would open ${params.url}`);
      return { opened: false };
    },
    'ui.ready': () => ({}),
    'settings.get': () => settings,
    'settings.set': (params) => {
      if (params.libraryPath != null && params.libraryPath !== settings.libraryPath) {
        throw new MockHostError(
          'settings.libraryMoveUnavailable',
          `Moving the library is not available in this version. Your recordings stay in ${settings.libraryPath}.`,
          params.libraryPath,
        );
      }
      const themeBefore = isDark();
      settings = {
        ...settings,
        theme: params.theme ?? settings.theme,
        listDensity: params.listDensity ?? settings.listDensity,
        recording: params.recording ?? settings.recording,
      };
      if (isDark() !== themeBefore) {
        emit('theme.changed', { isDark: isDark() });
      }
      return settings;
    },
    'library.list': (params) => queryLibrary(summaries(), params),
    'library.processing': () => processingOf(listed()),
    'project.get': (params) => toProject(find(params.recordingId)),
    'project.updateDetails': (params) => {
      const project = find(params.recordingId);
      project.details = { ...project.details, ...params.details };
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
    'recording.start': (params) => session.start(params),
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
      setInterval(() => {
        for (const project of projects.values()) {
          if (project.summary.isProcessing && project.stages.some((st) => st.stage === 'transcript')) {
            setStages(project, advanceStages(project.stages, 1));
          }
        }
      }, 3000);
    }
  };

  logger.info('[bridge] window.chrome.webview is absent; answering with browser-preview mock data.');

  return {
    send: (request) => {
      logger.info(`[bridge:mock] ${request.method}`, request.params);
      deliver(answer(request));
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
