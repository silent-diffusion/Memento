import type { BridgeLogger, BridgeTransport } from './client';
import { estimateSizeBytes, mockTracks, SAMPLE_SOURCES, sampleProjects, type MockProject } from './mockData';
import { advanceStages, processingOf, queryLibrary } from './mockLibrary';
import { createMockSession, MockHostError } from './mockSession';
import type {
  BridgeEventEnvelope,
  BridgeRequest,
  BridgeResponse,
  EventName,
  EventPayload,
  FooterStatusPayload,
  MethodName,
  MethodParams,
  MethodResult,
  Project,
  RecoveredRecording,
  SettingsSnapshot,
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
  const summaries = () => [...projects.values()].map((p) => p.summary);

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
    processingPaused: null,
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
      throw new MockHostError('project.notFound', 'That recording is no longer in the library.', recordingId);
    }
    return project;
  };

  const toProject = (project: MockProject): Project => ({
    summary: project.summary,
    details: project.details,
    tracks: mockTracks(project.summary.id, project.trackSources, project.summary.durationMs),
    mixUrl: null,
    peaksUrl: null,
    chapters: project.chapters,
    highlights: project.highlights,
    topics: project.topics,
    history: project.history,
    integrity: { algorithm: 'sha256', computedAt: null },
    sizeBytes: estimateSizeBytes(project.summary, project.trackSources.length),
  });

  let idCounter = 0;
  const nextId = (prefix: string): string => `${prefix}-${(++idCounter).toString(36)}`;

  const setStages = (project: MockProject, stages: MockProject['summary']['stages']): void => {
    project.summary = {
      ...project.summary,
      stages,
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
    onFinalized: (result) => {
      const summary = {
        id: result.recordingId,
        title: result.title,
        type: result.type,
        createdAt: result.startedAt,
        durationMs: result.durationMs,
        participantCount: 0,
        hasVideo: false,
        stages: [{ stage: 'stored' as const, state: 'active' as const, percent: 0, label: 'Saving tracks' }],
        people: [],
        isProcessing: true,
        state: 'ready' as const,
      };
      projects.set(summary.id, {
        summary,
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
        chapters: [],
        highlights: [],
        topics: [],
        history: [
          {
            at: result.startedAt,
            stage: 'recorded',
            event: 'completed',
            summary: `Recorded ${result.tracks.length} ${result.tracks.length === 1 ? 'track' : 'tracks'}`,
            detail: result.tracks.map((t) => t.name).join(', '),
          },
        ],
      });
      changed(summary.id);
      // Storing (FLAC + mix + peaks) finishes over a few seconds.
      const timer = setInterval(() => {
        const project = projects.get(summary.id);
        if (project === undefined) {
          clearInterval(timer);
          return;
        }
        const stages = advanceStages(project.summary.stages, 25, 'this PC').map((st) =>
          st.state === 'active' ? { ...st, label: `Saving tracks · ${st.percent ?? 0}%` } : st,
        );
        setStages(project, stages);
        if (!project.summary.isProcessing) {
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
    'library.processing': () => processingOf(summaries()),
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
      if (project.summary.stages.some((st) => st.stage === 'transcript' && st.state === 'done')) {
        items.push('its transcript');
      }
      if (project.summary.stages.some((st) => st.stage === 'minutes' && st.state === 'done')) {
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
        throw new MockHostError('invalidParams', 'A recording needs a title. The old title was kept.');
      }
      project.summary = { ...project.summary, title };
      project.details = { ...project.details, title };
      changed(project.summary.id);
      return toProject(project);
    },
    'annotations.addChapter': (params) => {
      const project = find(params.recordingId);
      project.chapters = [
        ...project.chapters,
        { id: nextId('chapter'), atMs: params.chapter.atMs ?? 0, title: params.chapter.title ?? 'New chapter', origin: 'user' as const },
      ].sort((a, b) => a.atMs - b.atMs);
      return { chapters: project.chapters };
    },
    'annotations.updateChapter': (params) => {
      const project = find(params.recordingId);
      project.chapters = project.chapters
        .map((c) => (c.id === params.chapter.id ? { ...c, ...params.chapter, id: c.id } : c))
        .sort((a, b) => a.atMs - b.atMs);
      return { chapters: project.chapters };
    },
    'annotations.removeChapter': (params) => {
      const project = find(params.recordingId);
      project.chapters = project.chapters.filter((c) => c.id !== params.chapterId);
      return { chapters: project.chapters };
    },
    'annotations.addHighlight': (params) => {
      const project = find(params.recordingId);
      project.highlights = [
        ...project.highlights,
        {
          id: nextId('highlight'),
          atMs: params.highlight.atMs ?? 0,
          note: params.highlight.note ?? '',
          origin: 'user' as const,
          segmentId: params.highlight.segmentId ?? null,
        },
      ].sort((a, b) => a.atMs - b.atMs);
      return { highlights: project.highlights };
    },
    'annotations.updateHighlight': (params) => {
      const project = find(params.recordingId);
      project.highlights = project.highlights
        .map((h) => (h.id === params.highlight.id ? { ...h, ...params.highlight, id: h.id } : h))
        .sort((a, b) => a.atMs - b.atMs);
      return { highlights: project.highlights };
    },
    'annotations.removeHighlight': (params) => {
      const project = find(params.recordingId);
      project.highlights = project.highlights.filter((h) => h.id !== params.highlightId);
      return { highlights: project.highlights };
    },
    'annotations.addTopic': (params) => {
      const project = find(params.recordingId);
      project.topics = [...project.topics, { id: nextId('topic'), label: params.topic.label ?? '', origin: 'user' }];
      return { topics: project.topics };
    },
    'annotations.removeTopic': (params) => {
      const project = find(params.recordingId);
      project.topics = project.topics.filter((t) => t.id !== params.topicId);
      return { topics: project.topics };
    },
    'sources.list': () => ({ audio: [...SAMPLE_SOURCES], videoAvailable: false }),
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
        error: { code: 'unknownMethod', message: `Memento does not know '${request.method}'.`, detail: null },
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
        error: { code: 'internal', message: error instanceof Error ? error.message : 'The preview host failed.', detail: null },
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
          if (project.summary.isProcessing && project.summary.stages.some((st) => st.stage === 'transcript')) {
            setStages(project, advanceStages(project.summary.stages, 1));
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
