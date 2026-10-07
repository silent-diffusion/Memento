import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { BridgeLogger } from './client';
import { createMockTransport, type MockOptions } from './mock';
import type { BridgeError, MethodName, MethodParams, MethodResult, StageStatus } from './types';

const quiet: BridgeLogger = { info: () => undefined, warn: () => undefined };
const LONG = '20261005-160000-dsrev';
const FAILED = '20260930-130500-onbrd';

interface Envelope {
  id?: number | null;
  result?: unknown;
  error?: BridgeError;
  event?: string;
  payload?: unknown;
}

/** Drives the preview host directly, settling its zero-delay deliveries with fake timers. */
function previewHost(options: MockOptions = {}) {
  const messages: Envelope[] = [];
  const transport = createMockTransport(quiet, { live: false, recovery: false, ...options });
  transport.subscribe((message) => messages.push(message as Envelope));
  let nextId = 0;
  const send = (method: string, params: unknown): Envelope => {
    const id = ++nextId;
    transport.send({ id, method: method as MethodName, params: params as never });
    vi.advanceTimersByTime(0);
    const response = messages.find((m) => m.id === id);
    if (response === undefined) {
      throw new Error(`no answer to ${method}`);
    }
    return response;
  };
  return {
    messages,
    call<M extends MethodName>(method: M, params: MethodParams<M>): MethodResult<M> {
      const response = send(method, params);
      if (response.error !== undefined) {
        throw new Error(`${method} failed: ${response.error.code} ${response.error.message}`);
      }
      return response.result as MethodResult<M>;
    },
    fail(method: MethodName, params: unknown): BridgeError {
      const response = send(method, params);
      if (response.error === undefined) {
        throw new Error(`${method} succeeded`);
      }
      return response.error;
    },
  };
}

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

const states = (stages: readonly StageStatus[] | undefined): string[] => (stages ?? []).map((st) => `${st.stage}:${st.state}`);

describe('preview host stages (M2 pipeline)', () => {
  const progress = (messages: Envelope[]): StageStatus[][] =>
    messages.filter((m) => m.event === 'processing.progress').map((m) => (m.payload as { stages: StageStatus[] }).stages);

  it('stores, transcribes, identifies speakers, then makes smaller files when the format is AAC', () => {
    const host = previewHost({ library: 'empty', stepMs: 100 });
    const settings = host.call('settings.get', {});
    host.call('settings.set', { recording: { ...settings.recording, storage: { ...settings.recording.storage, codec: 'aac', bitrateKbps: 128 } } });
    const sourceIds = [host.call('sources.list', {}).audio[0]?.id ?? ''];
    const { sessionId, recordingId } = host.call('recording.start', { title: 'Smaller', type: 'meeting', sourceIds });
    vi.advanceTimersByTime(3_000);
    host.call('recording.stop', { sessionId });
    vi.advanceTimersByTime(1_200);

    const card = host.call('library.processing', {}).current;
    expect(card?.recordingId).toBe(recordingId);
    expect(states(card?.stages)).toEqual(['stored:active', 'transcript:queued', 'speakers:queued', 'topics:queued', 'optimize:queued']);
    expect(host.call('transcript.get', { recordingId }).status).toBe('queued');

    // Step until the transcript stage runs.
    let running = host.call('library.processing', {}).current;
    for (let i = 0; i < 100 && running?.stages[1]?.state !== 'active'; i++) {
      vi.advanceTimersByTime(100);
      running = host.call('library.processing', {}).current;
    }
    expect(states(running?.stages)).toEqual(['stored:done', 'transcript:active', 'speakers:queued', 'topics:queued', 'optimize:queued']);
    expect(running?.stages[1]?.label).toMatch(/^\d+% · local GPU$/);
    expect(host.call('transcript.get', { recordingId }).status).toBe('running');

    vi.advanceTimersByTime(4_000);
    expect(host.call('library.processing', {}).current).toBeNull();
    const project = host.call('project.get', { recordingId });
    // A finished topics stage is left out of rows, like stored and optimize.
    expect(states(project.summary.stages)).toEqual(['transcript:done', 'speakers:done']);
    expect(project.history.map((h) => `${h.stage}:${h.event}`)).toEqual([
      'recorded:completed',
      'stored:completed',
      'transcript:started',
      'transcript:completed',
      'speakers:started',
      'speakers:completed',
      'topics:completed',
      'optimize:started',
      'optimize:completed',
    ]);
    expect(states(progress(host.messages).at(-1))).toEqual(['stored:done', 'transcript:done', 'speakers:done', 'topics:done', 'optimize:done']);
    const { transcript, status } = host.call('transcript.get', { recordingId });
    expect(status).toBe('done');
    expect(transcript?.segments.length).toBeGreaterThan(0);
    expect(transcript?.speakers.map((s) => s.name)).toEqual(['Speaker 1', 'Speaker 2']);
    expect(host.messages.filter((m) => m.event === 'transcript.changed').map((m) => (m.payload as { reason: string }).reason)).toEqual([
      'transcribed',
      'speakers',
    ]);
  });

  it('runs only the stored stage when automatic transcription is off and the format is FLAC', () => {
    const host = previewHost({ library: 'empty', stepMs: 100 });
    const settings = host.call('settings.get', {});
    host.call('settings.set', { transcription: { ...settings.transcription, auto: false } });
    const sourceIds = [host.call('sources.list', {}).audio[0]?.id ?? ''];
    const { sessionId, recordingId } = host.call('recording.start', { title: 'Lossless', type: 'meeting', sourceIds });
    vi.advanceTimersByTime(2_000);
    host.call('recording.stop', { sessionId });
    vi.advanceTimersByTime(1_200 + 4_000 + 1);

    expect(progress(host.messages).at(-1)).toEqual([{ stage: 'stored', state: 'done', percent: null, label: 'Done' }]);
    expect(host.call('project.get', { recordingId }).summary.stages).toEqual([]);
    expect(host.call('transcript.get', { recordingId })).toEqual({ transcript: null, status: 'none', failure: null });
  });

  it('waits for a model on a first run and starts by itself once one is installed', () => {
    const host = previewHost({ library: 'empty', modelsInstalled: 'none', stepMs: 10 });
    const sourceIds = [host.call('sources.list', {}).audio[0]?.id ?? ''];
    const { sessionId, recordingId } = host.call('recording.start', { title: 'First', type: 'meeting', sourceIds });
    vi.advanceTimersByTime(2_000);
    host.call('recording.stop', { sessionId });
    vi.advanceTimersByTime(6_000);

    const waiting = host.call('transcript.get', { recordingId });
    expect(waiting.status).toBe('failed');
    expect(waiting.failure?.message).toBe('Transcription needs the Large v3 Turbo model, and it is not installed.');
    const row = host.call('project.get', { recordingId }).summary.stages;
    expect(row.find((s) => s.stage === 'transcript')).toMatchObject({ state: 'failed', label: 'Waiting for a model' });
    // Speakers and topics wait for the transcript; they are queued again with it.
    expect(row.some((s) => s.stage === 'speakers' || s.stage === 'topics')).toBe(false);
    expect(host.call('status.get', {}).engine).toMatchObject({ ready: false, device: null });

    for (const modelId of ['whisper-large-v3-turbo', 'pyannote-segmentation-3-0', 'nemo-titanet-small']) {
      host.call('models.install', { modelId });
      vi.advanceTimersByTime(400);
    }
    vi.advanceTimersByTime(5_000);
    const done = host.call('transcript.get', { recordingId });
    expect(done.status).toBe('done');
    expect(done.failure).toBeNull();
    expect(done.transcript?.speakers.length).toBeGreaterThan(0);
    expect(host.call('status.get', {}).engine.ready).toBe(true);
  });
});

describe('preview host transcripts (M2)', () => {
  it('has a long sample transcript with speakers, low-confidence words, uncertain speakers and one edited line', () => {
    const host = previewHost();
    const { transcript, status, failure } = host.call('transcript.get', { recordingId: LONG });
    expect(status).toBe('done');
    expect(failure).toBeNull();
    if (transcript === null) {
      throw new Error('no transcript');
    }
    expect(transcript.segments.length).toBeGreaterThan(120);
    expect(transcript.speakers.map((s) => s.name)).toEqual(['Sam Okafor', 'Aiko Tanaka', 'Lena Fischer', 'Speaker 4']);
    expect(transcript.speakers.every((s) => s.talkTimeMs > 0)).toBe(true);
    expect(transcript.segments.some((s) => s.words.some((w) => w.c < transcript.lowConfidenceThreshold))).toBe(true);
    expect(transcript.segments.some((s) => (s.speakerConfidence ?? 1) < 0.7)).toBe(true);
    expect(transcript.segments.filter((s) => s.edited !== null)).toHaveLength(1);
    const starts = transcript.segments.map((s) => s.start);
    expect(starts).toEqual([...starts].sort((a, b) => a - b));
    const render = transcript.segments.find((s) => s.text.startsWith('Proposal: rows stay at 68 pixels'));
    expect(render?.start).toBe(18 * 60 + 42);
    // Highlights attach to the segment they fall in.
    const project = host.call('project.get', { recordingId: LONG });
    expect(project.highlights.find((h) => h.note === 'Row height agreed at 68 px')?.segmentId).toBe(render?.id);
    expect(project.history.map((h) => h.stage)).toEqual(expect.arrayContaining(['transcript', 'speakers', 'topics', 'edited']));
  });

  it('edits a line, keeps the original and re-aligns its words', () => {
    const host = previewHost();
    const segment = host.call('transcript.get', { recordingId: LONG }).transcript?.segments.find((s) => s.text.includes('Figma'));
    if (segment === undefined) {
      throw new Error('no Figma line');
    }
    const text = segment.text.replace('Figma file', 'design file');
    const { segment: edited, version } = host.call('transcript.editSegment', { recordingId: LONG, segmentId: segment.id, text });
    expect(edited.text).toBe(text);
    expect(edited.edited?.original).toBe(segment.text);
    expect(edited.words.map((w) => w.w).join(' ')).toBe(text);
    expect(edited.words.find((w) => w.w === 'design')?.c).toBe(1);
    expect(edited.words[0]).toEqual(segment.words[0]);
    const again = host.call('transcript.editSegment', { recordingId: LONG, segmentId: segment.id, text: `${text} Thanks.` });
    expect(again.segment.edited?.original).toBe(segment.text);
    expect(again.version).toBe(version + 1);
    expect(host.fail('transcript.editSegment', { recordingId: LONG, segmentId: 'nope', text: 'x' }).code).toBe('transcript.segmentNotFound');
    expect(host.fail('transcript.editSegment', { recordingId: LONG, segmentId: segment.id, text: '  ' }).code).toBe('bridge.invalidParams');
    expect(host.messages.some((m) => m.event === 'transcript.changed' && (m.payload as { reason: string }).reason === 'edited')).toBe(true);
  });

  it('reassigns, creates, renames and merges speakers', () => {
    const host = previewHost();
    const transcript = host.call('transcript.get', { recordingId: LONG }).transcript;
    const first = transcript?.segments[0];
    if (first === undefined || transcript === null) {
      throw new Error('no transcript');
    }
    const moved = host.call('transcript.setSegmentSpeaker', { recordingId: LONG, segmentId: first.id, speakerId: 'sp2' });
    expect(moved.segment).toMatchObject({ speaker: 'sp2', speakerConfidence: 1 });

    const created = host.call('transcript.setSegmentSpeaker', { recordingId: LONG, segmentId: first.id, speakerId: null, newSpeakerName: 'Jonah Berg' });
    const jonah = created.speakers.find((s) => s.name === 'Jonah Berg');
    expect(jonah).toMatchObject({ renamed: true, color: 1 });
    expect(created.segment.speaker).toBe(jonah?.id);

    const renamed = host.call('transcript.renameSpeaker', { recordingId: LONG, speakerId: 'sp4', name: ' Dana Whitfield ' });
    expect(renamed.speakers.find((s) => s.id === 'sp4')).toMatchObject({ name: 'Dana Whitfield', renamed: true });
    expect(host.call('library.list', { query: 'Dana Whitfield' }).recordings.map((r) => r.id)).toContain(LONG);

    const sp4Count = transcript.segments.filter((s) => s.speaker === 'sp4').length;
    const merged = host.call('transcript.mergeSpeakers', { recordingId: LONG, fromSpeakerId: 'sp4', intoSpeakerId: 'sp3' });
    expect(merged.segmentsChanged).toBe(sp4Count);
    expect(merged.speakers.some((s) => s.id === 'sp4')).toBe(false);
    expect(host.fail('transcript.mergeSpeakers', { recordingId: LONG, fromSpeakerId: 'sp4', intoSpeakerId: 'sp3' }).code).toBe('transcript.speakerNotFound');
    expect(host.fail('transcript.renameSpeaker', { recordingId: LONG, speakerId: 'sp1', name: '' }).code).toBe('bridge.invalidParams');
  });

  it('marks reviewed and searches case-insensitively on word boundaries with snippets', () => {
    const host = previewHost();
    expect(host.call('transcript.markReviewed', { recordingId: LONG, reviewed: true })).toEqual({ reviewed: true });
    expect(host.call('transcript.get', { recordingId: LONG }).transcript?.reviewed).toBe(true);
    const { matches } = host.call('transcript.search', { recordingId: LONG, query: 'DARK' });
    expect(matches.length).toBeGreaterThan(3);
    expect(matches.every((m) => /dark/i.test(m.snippet))).toBe(true);
    expect(host.call('transcript.search', { recordingId: LONG, query: 'dar' }).matches).toEqual([]);
    expect(host.call('transcript.search', { recordingId: LONG, query: ' ' }).matches).toEqual([]);
  });

  it('matches transcript text in library.list with a snippet', () => {
    const host = previewHost();
    const { recordings } = host.call('library.list', { query: 'processing card' });
    const hit = recordings.find((r) => r.id === LONG);
    expect(hit?.matchSnippet).toMatch(/processing card/i);
    // Title and people matches carry no snippet.
    expect(host.call('library.list', { query: 'Design review' }).recordings.find((r) => r.id === LONG)?.matchSnippet).toBeNull();
    expect(host.call('library.list', {}).recordings.every((r) => r.matchSnippet === null)).toBe(true);
  });

  it('keeps versions and restores one, keeping the replaced transcript as a version', () => {
    const host = previewHost();
    const { versions } = host.call('transcript.versions', { recordingId: LONG });
    expect(versions.map((v) => v.reason)).toEqual(['retranscribed', 'transcribed']);
    // As the host writes it: engine and model.
    expect(versions[1]?.engine).toBe('whisper.cpp whisper-medium');
    const target = versions[0];
    if (target === undefined) {
      throw new Error('no versions');
    }
    const { transcript } = host.call('transcript.restoreVersion', { recordingId: LONG, versionId: target.id });
    expect(transcript.segments.every((s) => s.edited === null)).toBe(true);
    expect(host.call('transcript.versions', { recordingId: LONG }).versions.map((v) => v.reason)).toEqual(['edited', 'retranscribed', 'transcribed']);
    expect(host.fail('transcript.restoreVersion', { recordingId: LONG, versionId: 'v-gone' }).code).toBe('transcript.versionNotFound');

    const settings = host.call('settings.get', {});
    host.call('settings.set', { history: { ...settings.history, keepVersions: false } });
    expect(host.call('transcript.versions', { recordingId: LONG }).versions).toEqual([]);
  });

  it('transcribes again: queued, running, then done after a few seconds, keeping the old one as a version', () => {
    const host = previewHost({ stepMs: 100 });
    const before = host.call('transcript.get', { recordingId: LONG }).transcript;
    // The Large v3 Turbo sample dropped a passage, which the notice offers to fix with Small.
    expect(before?.coverageGaps).toHaveLength(1);
    host.call('transcript.retranscribe', { recordingId: LONG, modelId: 'whisper-small' });
    expect(host.call('transcript.get', { recordingId: LONG }).status).toBe('queued');
    vi.advanceTimersByTime(250);
    expect(host.call('transcript.get', { recordingId: LONG }).status).toBe('running');
    // The current transcript stays readable while the new pass runs.
    expect(host.call('transcript.get', { recordingId: LONG }).transcript?.segments.length).toBe(before?.segments.length);
    vi.advanceTimersByTime(3_000);
    const after = host.call('transcript.get', { recordingId: LONG });
    expect(after.status).toBe('done');
    expect(after.transcript?.engine).toMatchObject({ model: 'whisper-small', device: 'GPU' });
    expect(after.transcript?.coverageGaps).toEqual([]);
    expect(after.transcript?.segments.length).toBeGreaterThan(before?.segments.length ?? 0);
    expect(host.call('transcript.versions', { recordingId: LONG }).versions[0]?.reason).toBe('edited');
    expect(host.fail('transcript.retranscribe', { recordingId: LONG, modelId: 'nonsense' }).code).toBe('models.notFound');
  });

  it('lists the coverage gap in History when a pass leaves one', () => {
    const host = previewHost({ stepMs: 100 });
    host.call('transcript.retranscribe', { recordingId: LONG, modelId: 'whisper-large-v3-turbo' });
    vi.advanceTimersByTime(4_000);
    const { transcript } = host.call('transcript.get', { recordingId: LONG });
    const gap = transcript?.coverageGaps[0];
    expect(gap?.end).toBeGreaterThan((gap?.start ?? 0) + 10);
    expect(transcript?.segments.some((s) => gap !== undefined && s.start >= gap.start && s.start < gap.end)).toBe(false);
    const lines = host.call('project.get', { recordingId: LONG }).history.filter((h) => h.stage === 'transcript' && h.event === 'info');
    expect(lines.at(-1)?.summary).toMatch(/^Speech without a transcript at \d+:\d\d–\d+:\d\d$/);
  });

  it('reports a failed pass with its partial transcript and retries with a remedy', () => {
    const host = previewHost({ stepMs: 100 });
    const failed = host.call('transcript.get', { recordingId: FAILED });
    expect(failed.status).toBe('failed');
    expect(failed.failure).toMatchObject({ stage: 'transcript', message: 'The GPU ran out of memory at 64%.' });
    expect(failed.failure?.remedies.map((r) => r.id)).toEqual(['cpu', 'model:whisper-small', 'retry']);
    expect(failed.transcript?.segments.length).toBeGreaterThan(5);
    expect(host.fail('processing.retry', { recordingId: FAILED, stage: 'transcript', remedyId: 'model:nonsense' }).code).toBe('models.notFound');
    expect(host.fail('processing.retry', { recordingId: FAILED, stage: 'transcript', remedyId: 'gpu' }).code).toBe('bridge.invalidParams');

    host.call('processing.retry', { recordingId: FAILED, stage: 'transcript', remedyId: 'cpu' });
    vi.advanceTimersByTime(250);
    const running = host.call('library.list', {}).recordings.find((r) => r.id === FAILED);
    expect(running?.stages.find((s) => s.stage === 'transcript')?.label).toMatch(/% · CPU$/);
    vi.advanceTimersByTime(3_000);
    const done = host.call('transcript.get', { recordingId: FAILED });
    expect(done.status).toBe('done');
    expect(done.failure).toBeNull();
    expect(done.transcript?.engine.device).toBe('CPU');
  });

  it('cancels a stage: failed with "Cancelled" and one remedy, retry, which runs it again', () => {
    const host = previewHost({ stepMs: 100 });
    host.call('transcript.retranscribe', { recordingId: LONG, modelId: 'whisper-small' });
    vi.advanceTimersByTime(250);
    host.call('processing.cancel', { recordingId: LONG, stage: 'transcript' });
    const cancelled = host.call('transcript.get', { recordingId: LONG });
    expect(cancelled.status).toBe('failed');
    expect(cancelled.failure?.remedies).toEqual([{ id: 'retry', label: 'Continue transcribing' }]);
    const stages = host.call('project.get', { recordingId: LONG }).summary.stages;
    expect(stages.find((s) => s.stage === 'transcript')).toMatchObject({ state: 'failed', label: 'Cancelled' });
    // The speakers and topics that waited for it are taken out of the queue.
    expect(stages.some((s) => s.state === 'queued')).toBe(false);

    host.call('processing.retry', { recordingId: LONG, stage: 'transcript', remedyId: 'retry' });
    vi.advanceTimersByTime(4_000);
    expect(host.call('transcript.get', { recordingId: LONG })).toMatchObject({ status: 'done', failure: null });
  });

  it('falls back to the speakers stage failure while the transcript is done', () => {
    const host = previewHost({ stepMs: 100 });
    host.call('processing.retry', { recordingId: LONG, stage: 'speakers' });
    vi.advanceTimersByTime(150);
    host.call('processing.cancel', { recordingId: LONG, stage: 'speakers' });
    const result = host.call('transcript.get', { recordingId: LONG });
    expect(result.status).toBe('done');
    expect(result.failure).toMatchObject({ stage: 'speakers', remedies: [{ id: 'retry', label: 'Identify speakers' }] });
    host.call('processing.retry', { recordingId: LONG, stage: 'speakers', remedyId: 'retry' });
    vi.advanceTimersByTime(2_000);
    expect(host.call('transcript.get', { recordingId: LONG }).failure).toBeNull();
  });

  it('drives the long sample through the ?stage= states', () => {
    for (const [flag, status] of [
      ['queued', 'queued'],
      ['running', 'running'],
      ['paused', 'paused'],
      ['failed', 'failed'],
    ] as const) {
      const host = previewHost({ stage: flag });
      const result = host.call('transcript.get', { recordingId: LONG });
      expect(result.status, flag).toBe(status);
      expect(result.transcript === null, flag).toBe(flag !== 'failed');
    }
    expect(previewHost({ stage: 'paused' }).call('status.get', {}).engine.detail.paused).toBe('PC is busy');
    expect(previewHost({ segments: 10_000 }).call('transcript.get', { recordingId: LONG }).transcript?.segments).toHaveLength(10_000);
  });

  it('pauses and resumes processing globally and reports it in the footer', () => {
    const host = previewHost();
    host.call('processing.pause', {});
    expect(host.call('status.get', {})).toMatchObject({ processingPaused: 'Paused by you', engine: { detail: { paused: 'Paused by you' } } });
    host.call('processing.resume', {});
    expect(host.call('status.get', {})).toMatchObject({ processingPaused: null, engine: { detail: { paused: null } } });
  });

  it('answers transcript.none and project.notFound', () => {
    const host = previewHost();
    expect(host.call('transcript.get', { recordingId: '20260909-143000-retro' })).toEqual({ transcript: null, status: 'none', failure: null });
    expect(host.fail('transcript.markReviewed', { recordingId: '20260909-143000-retro', reviewed: true }).code).toBe('transcript.none');
    expect(host.fail('transcript.get', { recordingId: 'gone' }).code).toBe('project.notFound');
  });
});

describe('preview host models and engine (M2)', () => {
  const progress = (messages: Envelope[]): string[] =>
    messages
      .filter((m) => m.event === 'models.progress')
      .map((m) => {
        const p = m.payload as { state: string; percent: number };
        return `${p.state}:${p.percent}`;
      });

  it('lists the catalog and the engine status', () => {
    const host = previewHost();
    const { models } = host.call('models.list', {});
    // The host's fixed catalog ids (BRIDGE.md M2 clarification 8).
    expect(models.map((m) => m.id)).toEqual([
      'whisper-large-v3-turbo',
      'whisper-medium',
      'whisper-small',
      'whisper-base',
      'pyannote-segmentation-3-0',
      'nemo-titanet-small',
      '3dspeaker-eres2net-base',
      'tesseract-eng',
      // M4: the Local provider's language models.
      'qwen3.5-4b-instruct-q4',
      'ministral-3-3b-instruct-q4',
    ]);
    expect(models.filter((m) => m.engine === 'speakers').map((m) => m.role)).toEqual(['segmentation', 'embedding', 'embedding']);
    expect(models.find((m) => m.recommended && m.engine === 'transcription')?.id).toBe('whisper-large-v3-turbo');
    const status = host.call('engine.status', {});
    expect(status.transcription).toMatchObject({ ready: true, device: 'GPU', model: 'whisper-large-v3-turbo', paused: null });
    expect(status.speakers).toMatchObject({ ready: true, device: 'CPU', freeVramBytes: null });
    expect(previewHost({ modelsInstalled: 'none' }).call('models.list', {}).models.some((m) => m.installed)).toBe(false);
  });

  it('installs with progress, verifies, and is then installed', () => {
    const host = previewHost({ stepMs: 10 });
    host.call('models.install', { modelId: 'whisper-medium' });
    vi.advanceTimersByTime(30);
    expect(host.call('models.list', {}).models.find((m) => m.id === 'whisper-medium')?.installing?.percent).toBeGreaterThan(0);
    const busy = host.fail('models.install', { modelId: 'whisper-base' });
    expect(busy).toMatchObject({ code: 'models.busy', detail: 'whisper-medium' });
    vi.advanceTimersByTime(400);
    const seen = progress(host.messages);
    expect(seen[0]).toBe('downloading:4');
    expect(seen.slice(-2)).toEqual(['verifying:100', 'done:100']);
    expect(host.call('models.list', {}).models.find((m) => m.id === 'whisper-medium')).toMatchObject({ installed: true, installing: null });
    host.call('settings.set', { transcription: { modelId: 'whisper-medium' } });
    expect(host.call('engine.status', {}).transcription.model).toBe('whisper-medium');
  });

  it('cancels a download (its last progress is failed, with a message) and removes a model', () => {
    const host = previewHost({ stepMs: 10 });
    host.call('models.install', { modelId: 'whisper-base' });
    vi.advanceTimersByTime(25);
    host.call('models.cancelInstall', { modelId: 'whisper-base' });
    const last = host.messages.filter((m) => m.event === 'models.progress').at(-1)?.payload as { state: string; message: string | null };
    expect(last).toMatchObject({ state: 'failed', message: 'The download of Base was cancelled; the partial file was removed.' });
    vi.advanceTimersByTime(500);
    expect(host.call('models.list', {}).models.find((m) => m.id === 'whisper-base')).toMatchObject({ installed: false, installing: null });
    host.call('models.remove', { modelId: 'whisper-small' });
    expect(host.call('models.list', {}).models.find((m) => m.id === 'whisper-small')?.installed).toBe(false);
    expect(host.fail('models.remove', { modelId: 'nope' }).code).toBe('models.notFound');
  });

  it('refuses a download without space and fails one part-way', () => {
    expect(previewHost({ models: 'noSpace' }).fail('models.install', { modelId: 'whisper-medium' }).code).toBe('models.noSpace');
    const host = previewHost({ models: 'network', stepMs: 10 });
    host.call('models.install', { modelId: 'whisper-medium' });
    vi.advanceTimersByTime(500);
    expect(progress(host.messages).at(-1)).toBe('failed:44');
    expect(host.call('models.list', {}).models.find((m) => m.id === 'whisper-medium')).toMatchObject({ installed: false, installing: null });
  });

  it('refuses to remove a model a running stage uses', () => {
    // The Q3 sample is transcribing with the default model.
    expect(previewHost().fail('models.remove', { modelId: 'whisper-large-v3-turbo' }).code).toBe('models.inUse');
  });

  it('round-trips the M2 settings blocks, merging them field by field, and validates them', () => {
    const host = previewHost();
    const settings = host.call('settings.get', {});
    expect(settings.transcription).toMatchObject({
      auto: true,
      timing: 'after',
      modelId: 'whisper-large-v3-turbo',
      cpuFallbackModelId: 'whisper-small',
      language: 'auto',
      lowConfidenceThreshold: 0.5,
    });
    expect(settings.speakers).toEqual({ identify: true, expectedSpeakers: 'auto', rememberRenamed: true, embeddingModelId: 'nemo-titanet-small' });
    expect(settings.history).toEqual({ keepVersions: true, keepDays: 90 });
    const next = host.call('settings.set', {
      transcription: { timing: 'during', lowConfidenceThreshold: 0.6, language: 'de' },
      speakers: { expectedSpeakers: 4 },
      history: { keepDays: 30 },
    });
    expect(next.transcription).toEqual({ ...settings.transcription, timing: 'during', lowConfidenceThreshold: 0.6, language: 'de' });
    expect(next.speakers).toEqual({ ...settings.speakers, expectedSpeakers: 4 });
    expect(next.history).toEqual({ keepVersions: true, keepDays: 30 });
    // Null keeps a value too.
    expect(host.call('settings.set', { history: { keepVersions: null as unknown as boolean, keepDays: 90 } }).history).toEqual({ keepVersions: true, keepDays: 90 });
    expect(host.call('settings.get', {}).speakers).toEqual(next.speakers);
    expect(host.fail('settings.set', { transcription: { modelId: 'whisper-base' } }).code).toBe('settings.invalidValue');
    // The segmentation model is not a voice model.
    expect(host.fail('settings.set', { speakers: { embeddingModelId: 'pyannote-segmentation-3-0' } }).code).toBe('settings.invalidValue');
    for (const expectedSpeakers of [0, 21, 2.5]) {
      expect(host.fail('settings.set', { speakers: { expectedSpeakers } }).code).toBe('settings.invalidValue');
    }
    expect(host.call('settings.set', { speakers: { expectedSpeakers: 20 } }).speakers.expectedSpeakers).toBe(20);
  });

  it('streams a live draft during a session with ?live=1', () => {
    const host = previewHost({ liveTranscript: true });
    expect(host.call('settings.get', {}).transcription.timing).toBe('during');
    const sourceIds = [host.call('sources.list', {}).audio[0]?.id ?? ''];
    const { sessionId } = host.call('recording.start', { title: 'Live', type: 'meeting', sourceIds });
    vi.advanceTimersByTime(8_000);
    const drafts = host.messages.filter((m) => m.event === 'recording.liveTranscript');
    expect(drafts.length).toBeGreaterThan(2);
    const last = drafts.at(-1)?.payload as { sessionId: string; segments: { start: number; text: string }[] };
    expect(last.sessionId).toBe(sessionId);
    expect(last.segments.length).toBeGreaterThan(1);
    const quietHost = previewHost();
    const quietSources = [quietHost.call('sources.list', {}).audio[0]?.id ?? ''];
    quietHost.call('recording.start', { title: 'Quiet', type: 'meeting', sourceIds: quietSources });
    vi.advanceTimersByTime(5_000);
    expect(quietHost.messages.some((m) => m.event === 'recording.liveTranscript')).toBe(false);
  });
});
