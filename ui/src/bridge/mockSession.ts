// A simulated recording session for the browser preview: recording.state once per second, levels at
// 20 Hz with smooth pseudo-random movement, checkpoints, pause, highlights, source changes, an
// optional source loss, and finalize after stop.
import { isoWithOffset } from './mockData';
import type {
  AudioSource,
  ErrorCode,
  EventName,
  EventPayload,
  Highlight,
  RecordingSessionState,
  RecordingStartParams,
  RecordingStartResult,
  RecordingStatePayload,
  SourceLevel,
  Track,
} from './types';

export class MockHostError extends Error {
  readonly code: ErrorCode;
  readonly detail: string | null;

  constructor(code: ErrorCode, message: string, detail: string | null = null) {
    super(message);
    this.name = 'MockHostError';
    this.code = code;
    this.detail = detail;
  }
}

export interface SessionEnvironment {
  emit<E extends EventName>(event: E, payload: EventPayload<E>): void;
  sources(): readonly AudioSource[];
  now(): number;
  checkpointSeconds(): number;
  /** Elapsed time at which one source is lost (the `?lost=1` flag), or null. */
  lostAfterMs: number | null;
  onFooterChange(recording: { active: boolean; lastCheckpointAt: string | null; lostSource: string | null }): void;
  /** The session opened its project folder: the project exists (state 'recording') from now on. */
  onStarted(result: { recordingId: string; title: string; type: string; startedAt: string; tracks: Track[] }): void;
  /** A highlight was marked; it belongs to the project straight away. */
  onHighlight(recordingId: string, highlight: Highlight): void;
  /** Stop was pressed: the project is finalizing. */
  onFinalizing(recordingId: string, durationMs: number): void;
  /** The session finished finalizing: add it to the library. */
  onFinalized(result: { recordingId: string; title: string; type: string; startedAt: string; durationMs: number; tracks: Track[] }): void;
}

interface LevelState {
  rms: number;
  target: number;
  peak: number;
}

const LEVEL_INTERVAL_MS = 50;
const STATE_INTERVAL_MS = 1000;

export interface MockSession {
  start(params: RecordingStartParams): RecordingStartResult;
  pause(sessionId: string): void;
  resume(sessionId: string): void;
  markHighlight(sessionId: string, note: string | undefined): Highlight;
  setSource(sessionId: string, sourceId: string, enabled: boolean): Track[];
  stop(sessionId: string): { recordingId: string };
  current(): RecordingStatePayload | null;
  activeRecordingId(): string | null;
}

export function createMockSession(env: SessionEnvironment): MockSession {
  let session:
    | {
        sessionId: string;
        recordingId: string;
        title: string;
        type: string;
        state: RecordingSessionState;
        startedAt: string;
        accumulatedMs: number;
        resumedAt: number | null;
        tracks: Track[];
        highlights: Highlight[];
        lastCheckpointAt: string | null;
        lastCheckpointElapsed: number;
        lostSource: string | null;
        lostDone: boolean;
        levels: Map<string, LevelState>;
        stateTimer: ReturnType<typeof setInterval> | null;
        levelTimer: ReturnType<typeof setInterval> | null;
      }
    | null = null;
  let counter = 0;

  const elapsed = (): number => {
    if (session === null) {
      return 0;
    }
    return session.accumulatedMs + (session.resumedAt === null ? 0 : env.now() - session.resumedAt);
  };

  const payload = (): RecordingStatePayload | null => {
    if (session === null) {
      return null;
    }
    const at = elapsed();
    return {
      sessionId: session.sessionId,
      recordingId: session.recordingId,
      state: session.state,
      startedAt: session.startedAt,
      elapsedMs: at,
      tracks: session.tracks.map((t) => ({ ...t, durationMs: t.endedEarlyAtMs ?? at })),
      lastCheckpointAt: session.lastCheckpointAt,
      highlightsCount: session.highlights.length,
    };
  };

  const emitState = (): void => {
    const state = payload();
    if (state !== null) {
      env.emit('recording.state', state);
    }
  };

  const emitFooter = (): void => {
    env.onFooterChange({
      active: session !== null && (session.state === 'recording' || session.state === 'paused'),
      lastCheckpointAt: session?.lastCheckpointAt ?? null,
      lostSource: session?.lostSource ?? null,
    });
  };

  const requireSession = (sessionId: string): NonNullable<typeof session> => {
    if (session?.sessionId !== sessionId || session.state === 'finalizing' || session.state === 'ready') {
      throw new MockHostError('recording.noSession', 'There is no recording in progress with that session.', sessionId);
    }
    return session;
  };

  const liveTracks = (): Track[] => session?.tracks.filter((t) => t.endedEarlyAtMs === null) ?? [];

  const newTrack = (source: AudioSource): Track => {
    counter += 1;
    const index = (session?.tracks.length ?? 0) + 1;
    return {
      id: `track-${counter}`,
      sourceId: source.id,
      sourceKind: source.kind,
      name: source.name,
      file: `tracks/${String(index).padStart(2, '0')}-${source.kind}.wav`,
      sampleRate: 48_000,
      channels: source.kind === 'microphone' ? 1 : 2,
      durationMs: 0,
      sha256: null,
      startOffsetMs: Math.round(elapsed()),
      endedEarlyAtMs: null,
    };
  };

  const loseSource = (): void => {
    if (session === null) {
      return;
    }
    const live = liveTracks();
    const victim = live.find((t) => t.sourceKind === 'application') ?? live.at(-1);
    if (victim === undefined) {
      return;
    }
    const at = elapsed();
    victim.endedEarlyAtMs = at;
    session.lostSource = victim.name;
    session.levels.delete(victim.sourceId);
    env.emit('recording.sourceLost', {
      sessionId: session.sessionId,
      sourceId: victim.sourceId,
      name: victim.name,
      atMs: at,
      remaining: liveTracks().map((t) => t.name),
    });
    emitFooter();
    emitState();
  };

  const tickState = (): void => {
    if (session?.state !== 'recording') {
      return;
    }
    const at = elapsed();
    if (at - session.lastCheckpointElapsed >= env.checkpointSeconds() * 1000) {
      session.lastCheckpointElapsed = at;
      session.lastCheckpointAt = isoWithOffset(new Date(env.now()));
      emitFooter();
    }
    if (!session.lostDone && env.lostAfterMs !== null && at >= env.lostAfterMs) {
      session.lostDone = true;
      loseSource();
    }
    emitState();
  };

  const tickLevels = (): void => {
    if (session?.state !== 'recording') {
      return;
    }
    const levels: SourceLevel[] = [];
    for (const track of liveTracks()) {
      let level = session.levels.get(track.sourceId);
      if (level === undefined) {
        level = { rms: 0, target: 0.2, peak: 0 };
        session.levels.set(track.sourceId, level);
      }
      // Speech-like: the target jumps now and then, the level eases toward it, peaks decay.
      if (Math.random() < 0.08) {
        level.target = 0.04 + Math.random() ** 1.6 * (track.sourceKind === 'microphone' ? 0.75 : 0.55);
      }
      level.rms += (level.target - level.rms) * 0.22;
      level.peak = Math.max(level.peak * 0.92, Math.min(1, level.rms * 1.3 + Math.random() * 0.06));
      levels.push({ sourceId: track.sourceId, rms: Number(level.rms.toFixed(3)), peak: Number(level.peak.toFixed(3)) });
    }
    env.emit('recording.levels', { sessionId: session.sessionId, levels });
  };

  const stopTimers = (): void => {
    if (session?.stateTimer != null) {
      clearInterval(session.stateTimer);
    }
    if (session?.levelTimer != null) {
      clearInterval(session.levelTimer);
    }
    if (session !== null) {
      session.stateTimer = null;
      session.levelTimer = null;
    }
  };

  return {
    start(params) {
      if (session !== null && (session.state === 'recording' || session.state === 'paused')) {
        throw new MockHostError(
          'recording.alreadyActive',
          `"${session.title}" is still recording. Stop it before starting another; it keeps recording until you do.`,
          session.sessionId,
        );
      }
      if (params.sourceIds.length === 0) {
        throw new MockHostError('recording.noSources', 'Choose at least one audio source to record.');
      }
      const available = env.sources();
      const chosen: AudioSource[] = [];
      for (const id of params.sourceIds) {
        const source = available.find((s) => s.id === id);
        if (source === undefined) {
          throw new MockHostError(
            'recording.sourceUnavailable',
            'One of the chosen sources could not be opened. Nothing was recorded.',
            id,
          );
        }
        chosen.push(source);
      }
      counter += 1;
      const startedAt = new Date(env.now());
      const stamp = isoWithOffset(startedAt).slice(0, 19).replace(/[-:]/g, '').replace('T', '-');
      session = {
        sessionId: `session-${counter}`,
        recordingId: `${stamp}-new${String(counter).padStart(3, '0')}`,
        title: params.title,
        type: params.type,
        state: 'recording',
        startedAt: isoWithOffset(startedAt),
        accumulatedMs: 0,
        resumedAt: env.now(),
        tracks: [],
        highlights: [],
        lastCheckpointAt: null,
        lastCheckpointElapsed: 0,
        lostSource: null,
        lostDone: false,
        levels: new Map(),
        stateTimer: null,
        levelTimer: null,
      };
      session.tracks = chosen.map(newTrack);
      session.stateTimer = setInterval(tickState, STATE_INTERVAL_MS);
      session.levelTimer = setInterval(tickLevels, LEVEL_INTERVAL_MS);
      env.onStarted({
        recordingId: session.recordingId,
        title: session.title,
        type: session.type,
        startedAt: session.startedAt,
        tracks: session.tracks.map((t) => ({ ...t })),
      });
      emitState();
      emitFooter();
      return { sessionId: session.sessionId, recordingId: session.recordingId, startedAt: session.startedAt };
    },
    pause(sessionId) {
      const active = requireSession(sessionId);
      if (active.state !== 'recording') {
        return;
      }
      active.accumulatedMs = elapsed();
      active.resumedAt = null;
      active.state = 'paused';
      env.emit('recording.levels', {
        sessionId,
        levels: liveTracks().map((t) => ({ sourceId: t.sourceId, rms: 0, peak: 0 })),
      });
      emitState();
      emitFooter();
    },
    resume(sessionId) {
      const active = requireSession(sessionId);
      if (active.state !== 'paused') {
        return;
      }
      active.resumedAt = env.now();
      active.state = 'recording';
      emitState();
      emitFooter();
    },
    markHighlight(sessionId, note) {
      const active = requireSession(sessionId);
      counter += 1;
      const highlight: Highlight = {
        id: `highlight-${counter}`,
        atMs: elapsed(),
        note: note ?? '',
        origin: 'user',
        segmentId: null,
      };
      active.highlights.push(highlight);
      env.onHighlight(active.recordingId, highlight);
      emitState();
      return highlight;
    },
    setSource(sessionId, sourceId, enabled) {
      const active = requireSession(sessionId);
      const live = liveTracks().find((t) => t.sourceId === sourceId);
      if (enabled && live === undefined) {
        const source = env.sources().find((s) => s.id === sourceId);
        if (source === undefined) {
          throw new MockHostError('recording.sourceUnavailable', 'That source could not be opened. The other tracks keep recording.', sourceId);
        }
        active.tracks.push(newTrack(source));
        if (active.lostSource === source.name) {
          active.lostSource = null;
        }
      } else if (!enabled && live !== undefined) {
        live.endedEarlyAtMs = elapsed();
        active.levels.delete(sourceId);
      }
      emitState();
      emitFooter();
      return active.tracks.map((t) => ({ ...t }));
    },
    stop(sessionId) {
      const active = requireSession(sessionId);
      active.accumulatedMs = elapsed();
      active.resumedAt = null;
      active.state = 'finalizing';
      stopTimers();
      env.onFinalizing(active.recordingId, active.accumulatedMs);
      emitState();
      emitFooter();
      const finished = active;
      setTimeout(() => {
        finished.state = 'ready';
        emitState();
        env.onFinalized({
          recordingId: finished.recordingId,
          title: finished.title,
          type: finished.type,
          startedAt: finished.startedAt,
          durationMs: finished.accumulatedMs,
          tracks: finished.tracks.map((t) => ({ ...t, durationMs: t.endedEarlyAtMs ?? finished.accumulatedMs })),
        });
      }, 1200);
      return { recordingId: active.recordingId };
    },
    current() {
      if (session === null || session.state === 'ready') {
        return null;
      }
      return payload();
    },
    activeRecordingId() {
      return session !== null && session.state !== 'ready' ? session.recordingId : null;
    },
  };
}
