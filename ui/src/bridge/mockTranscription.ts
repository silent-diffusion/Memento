// The browser preview's transcription: sample transcripts, the transcript and speakers stages as a
// simulated pipeline, and every transcript.* and processing.* method with real mutations (edits with
// word re-alignment, speaker changes, versions and restore). Nothing here is real measurement.
import { formatTotalDuration } from '../format/duration';
import { matchesQuery, queryRanges, realignWords, type TextRange } from '../format/transcript';
import type { MockProject } from './mockData';
import { isoWithOffset } from './mockData';
import { MODEL_IDS, type MockModelManager } from './mockModels';
import { buildTranscript, LONG_SAMPLE_ID, scriptSpeakers, withTalkTimes } from './mockTranscripts';
import { MockHostError } from './mockSession';
import type {
  EventName,
  EventPayload,
  HistoryEntry,
  ProcessingRetryParams,
  SettingsSnapshot,
  Speaker,
  SpeakerColour,
  StageFailure,
  StageName,
  StageStatus,
  Transcript,
  TranscriptEditSegmentParams,
  TranscriptGetResult,
  TranscriptMergeSpeakersParams,
  TranscriptRenameSpeakerParams,
  TranscriptRetranscribeParams,
  TranscriptSearchMatch,
  TranscriptSegment,
  TranscriptSetSegmentSpeakerParams,
  TranscriptStatus,
  TranscriptVersion,
  TranscriptVersionReason,
} from './types';

/** The `?stage=` flag: the long sample recording's transcript state (default done). */
export type StageFlag = 'done' | 'queued' | 'running' | 'failed' | 'paused';

export interface TranscriptionEnvironment {
  emit<E extends EventName>(event: E, payload: EventPayload<E>): void;
  now(): number;
  settings(): SettingsSnapshot;
  projects: Map<string, MockProject>;
  /** Replaces a project's stages and reports them (processing.progress, library.changed when done). */
  setStages(project: MockProject, stages: StageStatus[]): void;
  changed(recordingId: string): void;
  models: MockModelManager;
  isRecording(recordingId: string): boolean;
  /** Global pause changed: the footer shows the reason. */
  onPausedChange(reason: string | null): void;
  stageFlag: StageFlag;
  /** `?segments=10000`: the long sample's segment count. */
  segmentCount?: number;
  /** Milliseconds between steps of a pass the user started (shorter in tests). */
  stepMs?: number;
}

interface Pass {
  device: 'GPU' | 'CPU';
  modelId: string;
  /** Replacing a finished transcript: the old one becomes a version. */
  retranscribe: boolean;
  startedAt: number;
}

interface Entry {
  transcript: Transcript | null;
  /** How the current transcript came about: a version's reason once it is replaced. */
  origin: TranscriptVersionReason;
  failure: StageFailure | null;
  /** The speakers stage's failure; transcript.get falls back to it. */
  speakersFailure: StageFailure | null;
  versions: { meta: TranscriptVersion; snapshot: Transcript }[];
  /** Kept in its `?stage=` state until the user acts. */
  held: boolean;
  /** Waiting for a busy PC. */
  paused: 'PC is busy' | null;
  pass: Pass | null;
  timer: ReturnType<typeof setInterval> | null;
  editCount: number;
}

const PIPELINE: readonly StageName[] = ['stored', 'transcript', 'speakers', 'topics', 'minutes', 'optimize'];
/** Stages the preview runs itself; minutes (documents) arrives in a later milestone. */
const RUNNABLE: ReadonlySet<StageName> = new Set<StageName>(['transcript', 'speakers', 'topics', 'optimize']);
/** The host's label for a stage whose model is not installed (BRIDGE.md M2 clarification 14). */
export const WAITING_FOR_MODEL = 'Waiting for a model';

const clone = <T>(value: T): T => structuredClone(value);

function sortStages(stages: StageStatus[]): StageStatus[] {
  return [...stages].sort((a, b) => PIPELINE.indexOf(a.stage) - PIPELINE.indexOf(b.stage));
}

export interface MockTranscription {
  get(recordingId: string): TranscriptGetResult;
  /** Builds a sample recording's transcript (and its history and highlight links) if not yet done. */
  prepare(recordingId: string): void;
  editSegment(params: TranscriptEditSegmentParams): { segment: TranscriptSegment; version: number };
  setSegmentSpeaker(params: TranscriptSetSegmentSpeakerParams): { segment: TranscriptSegment; speakers: Speaker[] };
  renameSpeaker(params: TranscriptRenameSpeakerParams): { speakers: Speaker[] };
  mergeSpeakers(params: TranscriptMergeSpeakersParams): { speakers: Speaker[]; segmentsChanged: number };
  markReviewed(recordingId: string, reviewed: boolean): { reviewed: boolean };
  search(recordingId: string, query: string): TranscriptSearchMatch[];
  retranscribe(params: TranscriptRetranscribeParams): void;
  versions(recordingId: string): TranscriptVersion[];
  restoreVersion(recordingId: string, versionId: string): Transcript;
  retry(params: ProcessingRetryParams): void;
  cancel(recordingId: string, stage: StageName): void;
  pauseAll(): void;
  resumeAll(): void;
  /** For library.list: a snippet around the first transcript match, or null. */
  librarySnippet(recordingId: string, query: string): string | null;
  /** A recording made in the preview finished storing: queue its transcript and speakers. */
  queueNewRecording(project: MockProject): void;
  /** One background step for passes the sample library started (when `live`). */
  tick(): void;
  /** Model ids a running stage is using. */
  inUse(): string[];
  /** Reason for the footer while something is paused, or null. */
  pausedReason(): 'PC is busy' | 'Paused by you' | null;
  /** A model finished installing: stages waiting for one are queued again. */
  modelInstalled(): void;
}

/** "…the processing card should disappear when a filter is active…": about 90 characters around a match. */
export function snippetAround(text: string, range: TextRange, before = 36, after = 56): string {
  let start = Math.max(0, range.start - before);
  let end = Math.min(text.length, range.end + after);
  if (start > 0) {
    const space = text.indexOf(' ', start);
    start = space >= 0 && space < range.start ? space + 1 : start;
  }
  if (end < text.length) {
    const space = text.lastIndexOf(' ', end);
    end = space > range.end ? space : end;
  }
  return `${start > 0 ? '…' : ''}${text.slice(start, end).trim()}${end < text.length ? '…' : ''}`;
}

export function createMockTranscription(env: TranscriptionEnvironment): MockTranscription {
  const entries = new Map<string, Entry>();
  const stepMs = env.stepMs ?? 300;
  let globalPaused: 'Paused by you' | null = null;
  let versionCounter = 0;

  const at = (): string => isoWithOffset(new Date(env.now()));

  const project = (recordingId: string): MockProject => {
    const p = env.projects.get(recordingId);
    if (p === undefined) {
      throw new MockHostError(
        'project.notFound',
        'This recording is no longer in the library; it may have been deleted. Nothing was changed. Go back to the Library to see what is there.',
        recordingId,
      );
    }
    return p;
  };

  // A sample recording's transcript is built the first time anything asks for it (seed below).
  const seeded = new Set<string>();
  const entryOf = (recordingId: string): Entry => {
    let entry = entries.get(recordingId);
    if (entry === undefined) {
      entry = {
        transcript: null,
        origin: 'transcribed',
        failure: null,
        speakersFailure: null,
        versions: [],
        held: false,
        paused: null,
        pass: null,
        timer: null,
        editCount: 0,
      };
      entries.set(recordingId, entry);
    }
    const p = env.projects.get(recordingId);
    if (!seeded.has(recordingId) && p !== undefined) {
      seeded.add(recordingId);
      seed(p);
    }
    return entry;
  };

  const transcriptOf = (recordingId: string): { entry: Entry; transcript: Transcript } => {
    project(recordingId);
    const entry = entryOf(recordingId);
    if (entry.transcript === null) {
      throw new MockHostError(
        'transcript.none',
        'This recording has no transcript yet. Nothing was changed. Transcribe it first, from the transcript area in Review.',
        recordingId,
      );
    }
    return { entry, transcript: entry.transcript };
  };

  const segmentOf = (transcript: Transcript, segmentId: string): TranscriptSegment => {
    const segment = transcript.segments.find((s) => s.id === segmentId);
    if (segment === undefined) {
      throw new MockHostError(
        'transcript.segmentNotFound',
        'That line is not in the transcript any more; it may have been replaced by a newer pass. Nothing was changed. Reopen the transcript to see the current lines.',
        segmentId,
      );
    }
    return segment;
  };

  const speakerOf = (transcript: Transcript, speakerId: string): Speaker => {
    const speaker = transcript.speakers.find((s) => s.id === speakerId);
    if (speaker === undefined) {
      throw new MockHostError(
        'transcript.speakerNotFound',
        'That speaker is not in this transcript any more; they may have been merged into someone else. Nothing was changed.',
        speakerId,
      );
    }
    return speaker;
  };

  const invalid = (message: string): MockHostError => new MockHostError('bridge.invalidParams', message);

  const engineFor = (modelId: string, device: 'GPU' | 'CPU', durationMs: number): Transcript['engine'] => ({
    name: 'whisper.cpp',
    model: modelId,
    device,
    version: '1.7.2',
    durationMs: Math.round(durationMs * (device === 'GPU' ? 0.06 : 0.24)),
  });

  const build = (p: MockProject, pass: { modelId: string; device: 'GPU' | 'CPU' }, extra: { identify?: boolean; upTo?: number } = {}): Transcript => {
    const settings = env.settings();
    const isLong = p.summary.id === LONG_SAMPLE_ID;
    return buildTranscript(p.summary.id, {
      durationMs: p.summary.durationMs,
      trackId: `${p.summary.id}-t1`,
      threshold: settings.transcription.lowConfidenceThreshold,
      keepWords: settings.transcription.keepWordTimestamps,
      engine: engineFor(pass.modelId, pass.device, p.summary.durationMs),
      editedAt: isoWithOffset(new Date(Date.parse(p.summary.createdAt) + p.summary.durationMs + 40 * 60_000)),
      ...(isLong && env.segmentCount !== undefined ? { segmentCount: env.segmentCount } : {}),
      // The long sample's Large v3 Turbo pass drops a passage, as turbo did in testing; Small keeps it.
      ...(isLong && pass.modelId === MODEL_IDS.turbo && env.segmentCount === undefined ? { gapAt: 0.3 } : {}),
      ...extra,
    });
  };

  const bump = (transcript: Transcript): number => {
    transcript.version += 1;
    return transcript.version;
  };

  const notify = (recordingId: string, transcript: Transcript, reason: EventPayload<'transcript.changed'>['reason']): void => {
    env.emit('transcript.changed', { recordingId, version: transcript.version, reason });
  };

  const historyOn = (): boolean => env.settings().history.keepVersions;

  const keepVersion = (entry: Entry, transcript: Transcript, when: string = at()): void => {
    if (!historyOn()) {
      return;
    }
    versionCounter += 1;
    entry.versions.unshift({
      meta: {
        id: `v${String(versionCounter).padStart(4, '0')}`,
        at: when,
        reason: entry.origin,
        // As the host writes it: engine and model.
        engine: `${transcript.engine.name} ${transcript.engine.model}`,
        segments: transcript.segments.length,
      },
      snapshot: clone(transcript),
    });
  };

  const addHistory = (p: MockProject, entry: HistoryEntry): void => {
    p.history = [...p.history, entry];
  };

  const transcriptDetail = (t: Transcript): string =>
    `${t.engine.model} · ${t.engine.device} · ${formatTotalDuration(t.engine.durationMs)} · ${t.language === 'en' ? 'English' : t.language} · ${t.segments.length.toLocaleString('en-US')} segments`;

  const speakersDetail = (t: Transcript): string => {
    const renamed = t.speakers.filter((s) => s.renamed).length;
    return `${t.speakers.length} ${t.speakers.length === 1 ? 'speaker' : 'speakers'}${renamed === 0 ? '' : ` · ${renamed} renamed by you`}`;
  };

  /** People for search and the meta line: participants plus renamed speakers. */
  const refreshPeople = (p: MockProject, t: Transcript | null): void => {
    const names = new Set(p.details.participants);
    for (const s of t?.speakers ?? []) {
      if (s.renamed) {
        names.add(s.name);
      }
    }
    p.summary = { ...p.summary, people: [...names] };
  };

  const recountTalk = (t: Transcript): void => {
    t.speakers = withTalkTimes(t.segments, t.speakers);
  };

  /** Speakers from the sample script (or "Speaker 1" for one voice) assigned to every segment. */
  const identify = (p: MockProject, t: Transcript): void => {
    const named = build(p, { modelId: t.engine.model, device: 'GPU' }, { identify: true });
    const byId = new Map(named.segments.map((s) => [s.id, s]));
    let speakers = named.speakers;
    if (speakers.length === 0) {
      speakers = [{ id: 'sp1', name: scriptSpeakers(p.summary.id)[0]?.[0] ?? 'Speaker 1', renamed: false, color: 1, talkTimeMs: 0 }];
    }
    t.segments = t.segments.map((segment) => {
      const match = byId.get(segment.id);
      return {
        ...segment,
        speaker: match?.speaker ?? speakers[0]?.id ?? null,
        speakerConfidence: match?.speakerConfidence ?? 0.9,
      };
    });
    t.speakers = speakers;
    recountTalk(t);
  };

  const stageOf = (p: MockProject, stage: StageName): StageStatus | undefined => p.stages.find((s) => s.stage === stage);

  const labelFor = (stage: StageName, percent: number, entry: Entry): string => {
    const paused = entry.paused ?? globalPaused;
    if (paused !== null) {
      return `Paused · ${paused}`;
    }
    if (stage === 'optimize') {
      return `${percent}% · making smaller`;
    }
    if (stage === 'speakers') {
      return `${percent}% · CPU`;
    }
    if (stage === 'topics') {
      return `${percent}% · on this PC`;
    }
    return entry.pass?.device === 'CPU' ? `${percent}% · CPU` : `${percent}% · local GPU`;
  };

  const clock = (seconds: number): string => {
    const total = Math.round(seconds);
    const h = Math.floor(total / 3600);
    const m = Math.floor((total % 3600) / 60);
    const s = String(total % 60).padStart(2, '0');
    return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${s}` : `${m}:${s}`;
  };

  const complete = (p: MockProject, entry: Entry, stage: StageName): void => {
    const id = p.summary.id;
    if (stage === 'transcript') {
      const pass = entry.pass ?? { device: 'GPU' as const, modelId: env.settings().transcription.modelId, retranscribe: false, startedAt: env.now() };
      const fresh = build(p, pass, { identify: false });
      const previous = entry.transcript;
      if (previous !== null && pass.retranscribe && entry.failure === null) {
        keepVersion(entry, previous);
        fresh.version = previous.version + 1;
        entry.origin = 'retranscribed';
      } else {
        fresh.version = (previous?.version ?? 0) + 1;
        entry.origin = previous === null ? 'transcribed' : 'retranscribed';
      }
      entry.transcript = fresh;
      entry.failure = null;
      entry.editCount = 0;
      addHistory(p, { at: at(), stage: 'transcript', event: 'completed', summary: 'Transcribed locally', detail: transcriptDetail(fresh) });
      for (const gap of fresh.coverageGaps) {
        addHistory(p, {
          at: at(),
          stage: 'transcript',
          event: 'info',
          summary: `Speech without a transcript at ${clock(gap.start)}–${clock(gap.end)}`,
          detail: 'The track has speech there but the engine wrote nothing; listen to that part, or transcribe again with another model.',
        });
      }
      refreshPeople(p, fresh);
      notify(id, fresh, 'transcribed');
    } else if (stage === 'speakers' && entry.transcript !== null) {
      identify(p, entry.transcript);
      entry.speakersFailure = null;
      bump(entry.transcript);
      addHistory(p, { at: at(), stage: 'speakers', event: 'completed', summary: 'Speakers identified', detail: speakersDetail(entry.transcript) });
      refreshPeople(p, entry.transcript);
      notify(id, entry.transcript, 'speakers');
    } else if (stage === 'topics') {
      const local = p.topics.filter((t) => t.origin === 'local').length;
      addHistory(p, { at: at(), stage: 'topics', event: 'completed', summary: 'Topics found locally', detail: `${local} ${local === 1 ? 'topic' : 'topics'} · on this PC` });
    } else if (stage === 'optimize') {
      addHistory(p, { at: at(), stage: 'optimize', event: 'completed', summary: 'Saved smaller files', detail: env.settings().recording.storage.codec.toUpperCase() });
    }
  };

  /** One step of whatever runs next for this recording. Returns false once nothing is left to run. */
  const advance = (recordingId: string, step: number): boolean => {
    const p = env.projects.get(recordingId);
    if (p === undefined) {
      return false;
    }
    const entry = entryOf(recordingId);
    const active = p.stages.find((s) => s.state === 'active' && RUNNABLE.has(s.stage));
    if (active === undefined) {
      const next = sortStages(p.stages).find((s) => s.state === 'queued' && RUNNABLE.has(s.stage));
      if (next === undefined) {
        entry.pass = null;
        return false;
      }
      if (next.stage === 'speakers' && !env.settings().speakers.identify) {
        env.setStages(p, p.stages.filter((s) => s.stage !== 'speakers'));
        return true;
      }
      const waiting = missingModel(next.stage, entry);
      if (waiting !== null) {
        failWaiting(p, entry, waiting);
        return true;
      }
      // The quick topics pass writes only its result, as on the host.
      if (next.stage !== 'topics') {
        addHistory(p, {
          at: at(),
          stage: next.stage,
          event: 'started',
          summary: next.stage === 'transcript' ? 'Transcribing locally' : next.stage === 'speakers' ? 'Identifying speakers' : 'Making smaller files',
          detail: next.stage === 'transcript' ? `${entry.pass?.modelId ?? env.settings().transcription.modelId} · ${entry.pass?.device ?? 'GPU'}` : null,
        });
      }
      env.setStages(
        p,
        p.stages.map((s) => (s === next ? { ...s, state: 'active', percent: 0, label: labelFor(s.stage, 0, entry) } : s)),
      );
      return true;
    }
    if ((entry.paused ?? globalPaused) !== null) {
      env.setStages(p, p.stages.map((s) => (s === active ? { ...s, label: labelFor(s.stage, s.percent ?? 0, entry) } : s)));
      return true;
    }
    const percent = Math.min(100, (active.percent ?? 0) + step);
    if (percent < 100) {
      env.setStages(p, p.stages.map((s) => (s === active ? { ...s, percent, label: labelFor(s.stage, percent, entry) } : s)));
      return true;
    }
    complete(p, entry, active.stage);
    env.setStages(p, p.stages.map((s) => (s === active ? { ...s, state: 'done', percent: null, label: 'Done' } : s)));
    return true;
  };

  /** A pass the user started runs on its own timer, live or not. */
  const run = (recordingId: string): void => {
    const entry = entryOf(recordingId);
    entry.held = false;
    if (entry.timer !== null) {
      return;
    }
    entry.timer = setInterval(() => {
      if (!advance(recordingId, 20)) {
        if (entry.timer !== null) {
          clearInterval(entry.timer);
        }
        entry.timer = null;
      }
    }, stepMs);
  };

  const setStage = (p: MockProject, stage: StageName, change: Partial<StageStatus>): void => {
    const existing = stageOf(p, stage);
    const stages = existing === undefined ? [...p.stages, { stage, state: 'queued', percent: null, label: 'Queued', ...change } satisfies StageStatus] : p.stages.map((s) => (s.stage === stage ? { ...s, ...change } : s));
    env.setStages(p, sortStages(stages));
  };

  const failureFor = (percent: number): StageFailure => {
    const fallback = env.settings().transcription.cpuFallbackModelId;
    return {
      stage: 'transcript',
      message: `The GPU ran out of memory at ${percent}%.`,
      kept: 'The recording is safe and the partial transcript was kept. Continuing starts from there.',
      remedies: [
        { id: 'cpu', label: 'Retry on CPU' },
        { id: `model:${fallback}`, label: `Use the ${env.models.list().find((m) => m.id === fallback)?.name ?? fallback} model` },
        { id: 'retry', label: 'Try again' },
      ],
    };
  };

  /** The failure for a stage whose model is not installed, or null when it can run. */
  const missingModel = (stage: StageName, entry: Entry): StageFailure | null => {
    const settings = env.settings();
    if (stage === 'transcript') {
      const modelId = entry.pass?.modelId ?? settings.transcription.modelId;
      if (env.models.isInstalled(modelId)) {
        return null;
      }
      const name = env.models.list().find((m) => m.id === modelId)?.name ?? modelId;
      const installed = env.models.list().find((m) => m.engine === 'transcription' && m.installed);
      return {
        stage,
        message: `Transcription needs the ${name} model, and it is not installed.`,
        kept: 'The recording is safe. Install the model in Settings › Transcription and transcription starts by itself.',
        remedies: [...(installed === undefined ? [] : [{ id: `model:${installed.id}`, label: `Use the ${installed.name} model (installed)` }]), { id: 'retry', label: 'Try again' }],
      };
    }
    if (stage === 'speakers') {
      const missing = [MODEL_IDS.segmentation, settings.speakers.embeddingModelId].filter((id) => !env.models.isInstalled(id));
      if (missing.length === 0) {
        return null;
      }
      const names = missing.map((id) => env.models.list().find((m) => m.id === id)?.name ?? id);
      return {
        stage,
        message: `Speaker identification needs ${names.join(' and ')}, and ${missing.length === 1 ? 'it is' : 'they are'} not installed.`,
        kept: 'The transcript is kept without speakers. Install the speaker models in Settings › Speakers and speakers are identified by themselves.',
        remedies: [{ id: 'retry', label: 'Try again' }],
      };
    }
    return null;
  };

  /** "Waiting for a model": failed, retried by itself once a model is installed (modelInstalled). */
  const failWaiting = (p: MockProject, entry: Entry, failure: StageFailure): void => {
    if (failure.stage === 'speakers') {
      entry.speakersFailure = failure;
    } else {
      entry.failure = failure;
    }
    addHistory(p, {
      at: at(),
      stage: failure.stage,
      event: 'failed',
      summary: failure.stage === 'transcript' ? 'Waiting for a transcription model' : 'Waiting for the speaker models',
      detail: `${failure.message} ${failure.kept}`,
    });
    // Speakers and topics wait for a transcript; they are queued again with it.
    const dropDependents = failure.stage === 'transcript';
    env.setStages(
      p,
      p.stages
        .filter((s) => !(dropDependents && s.state === 'queued' && (s.stage === 'speakers' || s.stage === 'topics')))
        .map((s) => (s.stage === failure.stage ? { ...s, state: 'failed', percent: null, label: WAITING_FOR_MODEL } : s)),
    );
  };

  // ---------------------------------------------------------------------------------------------
  // The sample library's transcripts
  // ---------------------------------------------------------------------------------------------

  const seed = (p: MockProject): void => {
    const id = p.summary.id;
    const entry = entryOf(id);
    const flag = id === LONG_SAMPLE_ID ? env.stageFlag : 'done';
    const created = Date.parse(p.summary.createdAt) + p.summary.durationMs;
    const when = (minutes: number): string => isoWithOffset(new Date(created + minutes * 60_000));
    const settingsModel = env.settings().transcription.modelId;

    if (flag !== 'done') {
      entry.held = true;
      const base = p.history.filter((h) => h.stage === 'recorded' || h.stage === 'stored');
      const stored: StageStatus = { stage: 'stored', state: 'done', percent: null, label: 'Done' };
      const speakersQueued: StageStatus = { stage: 'speakers', state: 'queued', percent: null, label: 'Queued' };
      const topicsQueued: StageStatus = { stage: 'topics', state: 'queued', percent: null, label: 'Queued' };
      if (flag === 'queued') {
        p.stages = [stored, { stage: 'transcript', state: 'queued', percent: null, label: 'Queued' }, speakersQueued, topicsQueued];
        p.history = base;
      } else if (flag === 'running') {
        p.stages = [stored, { stage: 'transcript', state: 'active', percent: 64, label: '64% · local GPU' }, speakersQueued, topicsQueued];
        p.history = [...base, { at: when(1), stage: 'transcript', event: 'started', summary: 'Transcribing locally', detail: `${settingsModel} · GPU` }];
      } else if (flag === 'paused') {
        entry.paused = 'PC is busy';
        p.stages = [stored, { stage: 'transcript', state: 'active', percent: 40, label: 'Paused · PC is busy' }, speakersQueued, topicsQueued];
        p.history = [
          ...base,
          { at: when(1), stage: 'transcript', event: 'started', summary: 'Transcribing locally', detail: `${settingsModel} · GPU` },
          { at: when(3), stage: 'transcript', event: 'info', summary: 'Transcription paused', detail: 'The PC is busy; it resumes on its own.' },
        ];
      } else {
        entry.failure = failureFor(64);
        entry.transcript = build(p, { modelId: settingsModel, device: 'GPU' }, { identify: false, upTo: 0.64 });
        // A failed transcript leaves speakers and topics out until it is retried (as the host does).
        p.stages = [stored, { stage: 'transcript', state: 'failed', percent: null, label: 'Transcript failed' }];
        p.history = [
          ...base,
          { at: when(1), stage: 'transcript', event: 'started', summary: 'Transcribing locally', detail: `${settingsModel} · GPU` },
          { at: when(5), stage: 'transcript', event: 'failed', summary: 'Transcription failed', detail: `${entry.failure.message} ${entry.failure.kept}` },
        ];
      }
      p.summary = { ...p.summary, stages: p.stages.filter((s) => !(s.stage === 'stored' && s.state === 'done')), isProcessing: flag !== 'failed' };
      return;
    }

    const transcriptStage = stageOf(p, 'transcript');
    if (transcriptStage === undefined) {
      return;
    }
    const speakersDone = stageOf(p, 'speakers')?.state === 'done';
    if (transcriptStage.state === 'failed') {
      entry.failure = failureFor(64);
      entry.transcript = build(p, { modelId: settingsModel, device: 'GPU' }, { identify: false, upTo: 0.64 });
      p.history = p.history.map((h) =>
        h.stage === 'transcript' && h.event === 'failed' ? { ...h, detail: `${entry.failure?.message ?? ''} ${entry.failure?.kept ?? ''}`.trim() } : h,
      );
      return;
    }
    if (transcriptStage.state !== 'done') {
      return;
    }
    const transcript = build(p, { modelId: settingsModel, device: 'GPU' }, { identify: speakersDone });
    entry.transcript = transcript;
    // Highlights attach to the segment they fall in.
    p.highlights = p.highlights.map((h) => {
      const segment = [...transcript.segments].reverse().find((s) => s.start * 1000 <= h.atMs);
      return { ...h, segmentId: segment?.id ?? null };
    });
    p.history = p.history.map((h) => {
      if (h.stage === 'transcript' && h.event === 'completed') {
        return { ...h, detail: transcriptDetail(transcript) };
      }
      if (h.stage === 'speakers' && h.event === 'completed') {
        return { ...h, detail: speakersDetail(transcript) };
      }
      return h;
    });
    if (p.topics.some((t) => t.origin === 'local')) {
      addHistory(p, {
        at: when(8),
        stage: 'topics',
        event: 'completed',
        summary: 'Topics found locally',
        detail: `${p.topics.filter((t) => t.origin === 'local').length} topics · on this PC`,
      });
    }
    const edited = transcript.segments.filter((s) => s.edited !== null).length;
    if (edited > 0) {
      // The long sample's history: a medium pass on the CPU, a large pass on the GPU, then edits.
      const first = build(p, { modelId: MODEL_IDS.medium, device: 'CPU' }, { identify: speakersDone });
      const second = clone(transcript);
      second.segments = second.segments.map((s) => (s.edited === null ? s : { ...s, text: s.edited.original, edited: null, words: realignWords([], s.edited.original, s.start, s.end).map((w) => ({ ...w, c: 0.9 })) }));
      entry.origin = 'transcribed';
      keepVersion(entry, first, when(4));
      entry.origin = 'retranscribed';
      keepVersion(entry, second, when(12));
      entry.origin = 'edited';
      entry.editCount = edited;
      transcript.version = 3 + edited;
      addHistory(p, {
        at: when(40),
        stage: 'edited',
        event: 'info',
        summary: 'Transcript edited',
        detail: `${edited} ${edited === 1 ? 'correction' : 'corrections'} by you · version history on`,
      });
    }
    p.history.sort((a, b) => Date.parse(a.at) - Date.parse(b.at));
    refreshPeople(p, transcript);
  };

  // The `?stage=` flag changes the long sample's stages, which the Library shows straight away.
  if (env.stageFlag !== 'done') {
    entryOf(LONG_SAMPLE_ID);
  }

  const statusOf = (p: MockProject, entry: Entry): TranscriptStatus => {
    const stage = stageOf(p, 'transcript');
    if (stage === undefined) {
      return entry.transcript === null ? 'none' : 'done';
    }
    if ((stage.state === 'active' || stage.state === 'queued') && (entry.paused ?? globalPaused) !== null) {
      return 'paused';
    }
    switch (stage.state) {
      case 'done':
        return 'done';
      case 'active':
        return 'running';
      case 'queued':
        return 'queued';
      case 'failed':
        return 'failed';
    }
  };

  const recordEdit = (p: MockProject, entry: Entry): void => {
    entry.editCount += 1;
    const detail = `${entry.editCount} ${entry.editCount === 1 ? 'correction' : 'corrections'} by you · version history ${historyOn() ? 'on' : 'off'}`;
    const last = p.history.at(-1);
    if (last?.stage === 'edited' && last.summary === 'Transcript edited') {
      p.history = [...p.history.slice(0, -1), { ...last, at: at(), detail }];
    } else {
      addHistory(p, { at: at(), stage: 'edited', event: 'info', summary: 'Transcript edited', detail });
    }
  };

  const startTranscriptPass = (p: MockProject, entry: Entry, pass: Omit<Pass, 'startedAt'>): void => {
    if (env.isRecording(p.summary.id)) {
      throw new MockHostError('project.recording', `"${p.summary.title}" is still recording. It is transcribed once it has stopped; nothing was queued.`);
    }
    entry.pass = { ...pass, startedAt: env.now() };
    entry.paused = null;
    env.setStages(p, sortStages([...p.stages.filter((s) => s.stage !== 'transcript' && s.stage !== 'speakers' && s.stage !== 'topics'), ...transcriptAndDependents()]));
    run(p.summary.id);
  };

  /** A transcript pass queues speakers (when on) and topics after it (BRIDGE.md M2 clarification 13). */
  const transcriptAndDependents = (): StageStatus[] => {
    const queued = (stage: StageName): StageStatus => ({ stage, state: 'queued', percent: null, label: 'Queued' });
    return [queued('transcript'), ...(env.settings().speakers.identify ? [queued('speakers')] : []), queued('topics')];
  };

  return {
    prepare: (recordingId) => {
      entryOf(recordingId);
    },
    get: (recordingId) => {
      const p = project(recordingId);
      const entry = entryOf(recordingId);
      return {
        transcript: entry.transcript === null ? null : clone(entry.transcript),
        status: statusOf(p, entry),
        // The transcript stage's failure, or else the speakers stage's (BRIDGE.md, transcript.get).
        failure: entry.failure ?? entry.speakersFailure,
      };
    },

    editSegment: ({ recordingId, segmentId, text }) => {
      const p = project(recordingId);
      const { entry, transcript } = transcriptOf(recordingId);
      const segment = segmentOf(transcript, segmentId);
      const next = text.replace(/\s+/g, ' ').trim();
      if (next === '') {
        throw invalid('A line cannot be empty. The old text was kept.');
      }
      if (entry.origin !== 'edited') {
        keepVersion(entry, transcript);
        entry.origin = 'edited';
      }
      const words = realignWords(segment.words, next, segment.start, segment.end);
      const changed: TranscriptSegment = {
        ...segment,
        text: next,
        words: transcript.segments.length > 0 && segment.words.length === 0 ? [] : words,
        confidence: words.length === 0 ? 1 : Math.min(...words.map((w) => w.c)),
        edited: { at: segment.edited?.at ?? at(), original: segment.edited?.original ?? segment.text },
      };
      transcript.segments = transcript.segments.map((s) => (s.id === segmentId ? changed : s));
      const version = bump(transcript);
      recordEdit(p, entry);
      notify(recordingId, transcript, 'edited');
      return { segment: clone(changed), version };
    },

    setSegmentSpeaker: ({ recordingId, segmentId, speakerId, newSpeakerName }) => {
      const p = project(recordingId);
      const { transcript } = transcriptOf(recordingId);
      const segment = segmentOf(transcript, segmentId);
      let target: string | null = speakerId;
      if (newSpeakerName !== undefined) {
        const name = newSpeakerName.trim();
        if (name === '') {
          throw invalid('A new speaker needs a name. Nothing was changed.');
        }
        const used = new Set(transcript.speakers.map((s) => s.id));
        let n = transcript.speakers.length + 1;
        while (used.has(`sp${n}`)) {
          n += 1;
        }
        const created: Speaker = { id: `sp${n}`, name, renamed: true, color: ((transcript.speakers.length % 4) + 1) as SpeakerColour, talkTimeMs: 0 };
        transcript.speakers = [...transcript.speakers, created];
        target = created.id;
      } else if (speakerId !== null) {
        speakerOf(transcript, speakerId);
      }
      const changed: TranscriptSegment = { ...segment, speaker: target, speakerConfidence: target === null ? null : 1 };
      transcript.segments = transcript.segments.map((s) => (s.id === segmentId ? changed : s));
      recountTalk(transcript);
      bump(transcript);
      refreshPeople(p, transcript);
      env.changed(recordingId);
      notify(recordingId, transcript, 'speakers');
      return { segment: clone(changed), speakers: clone(transcript.speakers) };
    },

    renameSpeaker: ({ recordingId, speakerId, name }) => {
      const p = project(recordingId);
      const { transcript } = transcriptOf(recordingId);
      speakerOf(transcript, speakerId);
      const trimmed = name.trim();
      if (trimmed === '') {
        throw invalid('A speaker needs a name. The old name was kept.');
      }
      transcript.speakers = transcript.speakers.map((s) => (s.id === speakerId ? { ...s, name: trimmed, renamed: true } : s));
      bump(transcript);
      refreshPeople(p, transcript);
      env.changed(recordingId);
      notify(recordingId, transcript, 'speakers');
      return { speakers: clone(transcript.speakers) };
    },

    mergeSpeakers: ({ recordingId, fromSpeakerId, intoSpeakerId }) => {
      const p = project(recordingId);
      const { transcript } = transcriptOf(recordingId);
      speakerOf(transcript, fromSpeakerId);
      speakerOf(transcript, intoSpeakerId);
      if (fromSpeakerId === intoSpeakerId) {
        throw invalid('A speaker cannot be merged into themselves. Nothing was changed.');
      }
      let segmentsChanged = 0;
      transcript.segments = transcript.segments.map((s) => {
        if (s.speaker !== fromSpeakerId) {
          return s;
        }
        segmentsChanged += 1;
        return { ...s, speaker: intoSpeakerId };
      });
      transcript.speakers = transcript.speakers.filter((s) => s.id !== fromSpeakerId);
      recountTalk(transcript);
      bump(transcript);
      refreshPeople(p, transcript);
      env.changed(recordingId);
      notify(recordingId, transcript, 'speakers');
      return { speakers: clone(transcript.speakers), segmentsChanged };
    },

    markReviewed: (recordingId, reviewed) => {
      const { transcript } = transcriptOf(recordingId);
      transcript.reviewed = reviewed;
      bump(transcript);
      return { reviewed };
    },

    search: (recordingId, query) => {
      project(recordingId);
      const transcript = entryOf(recordingId).transcript;
      if (transcript === null || query.trim() === '') {
        return [];
      }
      const matches: TranscriptSearchMatch[] = [];
      for (const segment of transcript.segments) {
        for (const range of queryRanges(segment.text, query)) {
          matches.push({ segmentId: segment.id, start: segment.start, snippet: snippetAround(segment.text, range) });
        }
      }
      return matches;
    },

    retranscribe: ({ recordingId, modelId, language }) => {
      const p = project(recordingId);
      const entry = entryOf(recordingId);
      const settings = env.settings();
      const chosen = modelId ?? settings.transcription.modelId;
      // An unknown model answers models.notFound; one that is not installed is queued and waits for it.
      const model = env.models.find(chosen);
      if (language !== undefined && language !== 'auto' && !/^[a-z]{2,3}$/.test(language)) {
        throw invalid(`'${language}' is not a language code. Nothing was queued.`);
      }
      startTranscriptPass(p, entry, { device: model.runsOn === 'cpu' ? 'CPU' : 'GPU', modelId: chosen, retranscribe: entry.transcript !== null });
    },

    versions: (recordingId) => {
      project(recordingId);
      return historyOn() ? entryOf(recordingId).versions.map((v) => ({ ...v.meta })) : [];
    },

    restoreVersion: (recordingId, versionId) => {
      const p = project(recordingId);
      const entry = entryOf(recordingId);
      const version = entry.versions.find((v) => v.meta.id === versionId);
      if (version === undefined) {
        throw new MockHostError(
          'transcript.versionNotFound',
          'That version is no longer kept; versions older than the history setting are removed. Nothing was changed.',
          versionId,
        );
      }
      const current = entry.transcript;
      if (current !== null) {
        keepVersion(entry, current);
      }
      const restored = clone(version.snapshot);
      restored.version = (current?.version ?? 0) + 1;
      entry.transcript = restored;
      entry.origin = 'restored';
      entry.editCount = 0;
      addHistory(p, { at: at(), stage: 'edited', event: 'info', summary: 'Transcript version restored', detail: `From ${version.meta.reason} · ${version.meta.segments} segments` });
      refreshPeople(p, restored);
      env.changed(recordingId);
      notify(recordingId, restored, 'restored');
      return clone(restored);
    },

    retry: ({ recordingId, stage, remedyId }) => {
      const p = project(recordingId);
      const entry = entryOf(recordingId);
      if (!PIPELINE.includes(stage)) {
        throw invalid(`'${stage}' is not a stage. Nothing was retried.`);
      }
      if (remedyId !== undefined && remedyId !== 'retry' && remedyId !== 'cpu' && !remedyId.startsWith('model:')) {
        throw invalid(`Remedy '${remedyId}' is not one Memento offers. Choose one of the fixes shown with the failure.`);
      }
      if (stage === 'transcript') {
        const modelId =
          remedyId?.startsWith('model:') === true ? remedyId.slice('model:'.length) : (entry.pass?.modelId ?? env.settings().transcription.modelId);
        // An unknown model answers models.notFound; one that is not installed waits for it.
        const model = env.models.find(modelId);
        const device = remedyId === 'cpu' ? 'CPU' : model.runsOn === 'cpu' ? 'CPU' : 'GPU';
        entry.failure = null;
        startTranscriptPass(p, entry, { device, modelId, retranscribe: false });
        return;
      }
      if (stage === 'speakers') {
        // Also "Identify speakers again" on a finished stage, with the current Settings.
        transcriptOf(recordingId);
        entry.paused = null;
        entry.speakersFailure = null;
        setStage(p, 'speakers', { state: 'queued', percent: null, label: 'Queued' });
        run(recordingId);
        return;
      }
      const existing = stageOf(p, stage);
      if (existing?.state === 'failed' && RUNNABLE.has(stage)) {
        setStage(p, stage, { state: 'queued', percent: null, label: 'Queued' });
        run(recordingId);
      }
    },

    cancel: (recordingId, stage) => {
      const p = project(recordingId);
      const entry = entryOf(recordingId);
      const existing = stageOf(p, stage);
      if (existing === undefined || (existing.state !== 'active' && existing.state !== 'queued')) {
        return;
      }
      if (entry.timer !== null) {
        clearInterval(entry.timer);
        entry.timer = null;
      }
      // Like the host: failed, "Cancelled", with one remedy that continues from what was kept.
      const failure: StageFailure = {
        stage,
        message: stage === 'transcript' ? `Transcription was cancelled at ${existing.percent ?? 0}%.` : stage === 'speakers' ? 'Speaker identification was cancelled.' : `The ${stage} stage was cancelled.`,
        kept:
          stage === 'speakers'
            ? 'The transcript is kept without speakers.'
            : entry.transcript === null
              ? 'The recording is safe; nothing else changed.'
              : 'The recording is safe, and anything already finished is kept.',
        remedies: [{ id: 'retry', label: stage === 'transcript' ? 'Continue transcribing' : stage === 'speakers' ? 'Identify speakers' : 'Start again' }],
      };
      if (stage === 'transcript') {
        entry.failure = failure;
      } else if (stage === 'speakers') {
        entry.speakersFailure = failure;
      }
      addHistory(p, { at: at(), stage, event: 'info', summary: 'Cancelled', detail: failure.kept });
      // A cancelled transcript takes the speakers and topics waiting for it out of the queue.
      env.setStages(
        p,
        p.stages
          .filter((s) => !(stage === 'transcript' && s.state === 'queued' && (s.stage === 'speakers' || s.stage === 'topics')))
          .map((s) => (s.stage === stage ? { ...s, state: 'failed', percent: null, label: 'Cancelled' } : s)),
      );
    },

    pauseAll: () => {
      globalPaused = 'Paused by you';
      env.onPausedChange(globalPaused);
    },

    resumeAll: () => {
      globalPaused = null;
      // Resuming also carries on with passes that waited for a busy PC.
      for (const [id, entry] of entries) {
        if (entry.paused !== null) {
          entry.paused = null;
          run(id);
        }
      }
      env.onPausedChange(null);
    },

    librarySnippet: (recordingId, query) => {
      if (query.trim() === '' || !env.projects.has(recordingId)) {
        return null;
      }
      const transcript = entryOf(recordingId).transcript;
      if (transcript === null) {
        return null;
      }
      for (const segment of transcript.segments) {
        if (matchesQuery(segment.text, query)) {
          const range = queryRanges(segment.text, query)[0];
          return range === undefined ? segment.text : snippetAround(segment.text, range);
        }
      }
      return null;
    },

    queueNewRecording: (p) => {
      const settings = env.settings();
      if (!settings.transcription.auto) {
        // Nothing to transcribe automatically; a queued optimize stage still runs.
        if (p.stages.some((s) => s.state === 'queued' && RUNNABLE.has(s.stage))) {
          run(p.summary.id);
        }
        return;
      }
      const entry = entryOf(p.summary.id);
      entry.pass = { device: 'GPU', modelId: settings.transcription.modelId, retranscribe: false, startedAt: env.now() };
      env.setStages(p, sortStages([...p.stages.filter((s) => s.stage !== 'transcript' && s.stage !== 'speakers' && s.stage !== 'topics'), ...transcriptAndDependents()]));
      run(p.summary.id);
    },

    modelInstalled: () => {
      for (const [id, p] of env.projects) {
        const entry = entryOf(id);
        for (const stage of p.stages.filter((s) => s.state === 'failed' && s.label === WAITING_FOR_MODEL)) {
          if (stage.stage === 'transcript') {
            entry.failure = null;
            startTranscriptPass(p, entry, { device: entry.pass?.device ?? 'GPU', modelId: entry.pass?.modelId ?? env.settings().transcription.modelId, retranscribe: false });
          } else if (stage.stage === 'speakers' && entry.transcript !== null) {
            entry.speakersFailure = null;
            setStage(p, 'speakers', { state: 'queued', percent: null, label: 'Queued' });
            run(id);
          }
        }
      }
    },

    tick: () => {
      for (const [id, p] of env.projects) {
        const entry = entryOf(id);
        if (entry.held || entry.timer !== null || !p.summary.isProcessing) {
          continue;
        }
        if (p.stages.some((s) => (s.stage === 'transcript' || s.stage === 'speakers' || s.stage === 'topics') && (s.state === 'active' || s.state === 'queued'))) {
          advance(id, 2);
        }
      }
    },

    inUse: () => {
      const ids: string[] = [];
      for (const [id, p] of env.projects) {
        if (stageOf(p, 'transcript')?.state === 'active') {
          ids.push(entries.get(id)?.pass?.modelId ?? env.settings().transcription.modelId);
        }
      }
      return ids;
    },

    pausedReason: () => {
      if (globalPaused !== null) {
        return globalPaused;
      }
      for (const entry of entries.values()) {
        if (entry.paused !== null) {
          return entry.paused;
        }
      }
      return null;
    },
  };
}
