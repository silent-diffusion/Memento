import { describe, expect, it } from 'vitest';
import { exportFolderName, exportPathPreview } from '../format/export';
import { createBridgeClient, type BridgeClient } from './client';
import type { MockOptions } from './mock';
import { CONTINUES_REASON, DUPLICATE_REASON, MERGED_REASON, parseAgendaText } from './mockAgenda';
import { DEFAULT_EXPORT_SELECTION, M3_METHODS } from './mockLibraryExtra';
import { ERROR_CODES, EVENT_NAMES, METHOD_NAMES } from './types';
import type { BridgeError, EventName, EventPayload, ExportProgressPayload } from './types';

const quiet = { info: () => undefined, warn: () => undefined };
const Q3 = '20261006-100000-q3plan';
const DESIGN = '20261005-160000-dsrev';

function client(mock: MockOptions = {}): BridgeClient {
  return createBridgeClient({ logger: quiet, mock: { live: false, recovery: false, stepMs: 5, ...mock } });
}

async function failure(promise: Promise<unknown>): Promise<BridgeError> {
  return promise.then(
    () => {
      throw new Error('expected the call to fail');
    },
    (e: unknown) => e as BridgeError,
  );
}

function waitFor<E extends EventName>(bridge: BridgeClient, event: E, done: (payload: EventPayload<E>) => boolean): Promise<EventPayload<E>[]> {
  const seen: EventPayload<E>[] = [];
  return new Promise((resolve) => {
    const off = bridge.on(event, (payload) => {
      seen.push(payload);
      if (done(payload)) {
        off();
        resolve(seen);
      }
    });
  });
}

describe('M3 contract names', () => {
  it('lists every M3 method, event and error code in types.ts', () => {
    for (const method of M3_METHODS) {
      expect(METHOD_NAMES, method).toContain(method);
    }
    expect(EVENT_NAMES).toEqual(expect.arrayContaining(['export.progress', 'library.moveProgress', 'storage.reclaimProgress']));
    // BRIDGE.md › Error codes (M3), in the order the contract lists them.
    const m3Codes = [
      'agenda.fileTooLarge',
      'agenda.imageTooLarge',
      'agenda.unsupportedFormat',
      'agenda.unreadable',
      'agenda.protected',
      'agenda.noText',
      'agenda.noItems',
      'agenda.ocrUnavailable',
      'agenda.itemTooLong',
      'agenda.tooManyItems',
      'agenda.dropUnavailable',
      'attachments.tooLarge',
      'attachments.notFound',
      'library.importUnsupported',
      'library.busy',
      'export.destinationUnwritable',
      'export.nothingSelected',
      'export.notFound',
    ];
    expect(ERROR_CODES.slice(-m3Codes.length)).toEqual(m3Codes);
  });
});

describe('browser-preview host: agenda import (M3)', () => {
  it('parses pasted text: numbering and bullets go, indentation becomes a level, doubts carry a reason', () => {
    const parsed = parseAgendaText(
      ['Agenda: Weekly sync', '1. Q2 recap', '2. Launch date Options: 2nd week of Nov', '   - Beta feedback', '3. Budget asks and', '-', '4. Q2 recap'].join('\n'),
    );
    expect(parsed.title).toBe('Weekly sync');
    expect(parsed.items.map((i) => i.text)).toEqual(['Q2 recap', 'Launch date Options: 2nd week of Nov', 'Beta feedback', 'Budget asks and', 'Q2 recap']);
    expect(parsed.items.map((i) => i.level)).toEqual([0, 0, 1, 0, 0]);
    expect(parsed.items.map((i) => i.uncertainReason)).toEqual([null, MERGED_REASON, null, CONTINUES_REASON, DUPLICATE_REASON]);
    expect(parsed.items[1]?.location).toBe('line 3');
    expect(parsed.warnings).toEqual([{ code: 'markersOnly', message: '1 line held only a number or a bullet and was skipped.' }]);
  });

  it('reads sample files by extension, keeps the original until apply, and stores the agenda with History', async () => {
    const bridge = client();
    const { preview } = await bridge.call('agenda.importDropped', { recordingId: Q3, paths: ['agenda.docx'] });
    expect(preview?.sourceKind).toBe('docx');
    expect(preview?.items.filter((i) => i.uncertain).map((i) => i.uncertainReason)).toEqual([MERGED_REASON]);
    expect(preview?.attachmentToken).not.toBeNull();
    const before = (await bridge.call('attachments.list', { recordingId: Q3 })).attachments.length;
    const project = await bridge.call('agenda.apply', {
      recordingId: Q3,
      items: (preview?.items ?? []).map((i) => ({ text: i.text, uncertain: i.uncertain, uncertainReason: i.uncertainReason })),
      source: preview?.source ?? '',
      sourceKind: 'docx',
      attachmentToken: preview?.attachmentToken ?? null,
    });
    expect(project.details.agenda.source).toBe('agenda.docx');
    expect(project.details.agenda.items.every((i) => !i.covered && i.id !== '')).toBe(true);
    expect(project.history.at(-1)?.summary).toBe('Agenda imported');
    // The earlier agenda.docx is replaced by the new original, not doubled.
    expect((await bridge.call('attachments.list', { recordingId: Q3 })).attachments.length).toBe(before);

    const pdf = await bridge.call('agenda.importFile', { recordingId: null, path: 'C:\\Agendas\\board.pdf' });
    expect(pdf.preview?.warnings[0]?.message).toContain('two columns');
    expect(pdf.preview?.items[1]?.location).toBe('page 1, line 9');
    const photo = await bridge.call('agenda.importDropped', { recordingId: null, paths: ['offsite.jpg', 'notes.txt'] });
    expect(photo.preview?.ocrEngine).toBe('Windows OCR');
    expect(photo.preview?.warnings[0]?.code).toBe('drop.several');
  });

  it('refuses long items and too many items, unknown formats, and honours the OCR and drop flags', async () => {
    const bridge = client({ m3: { agenda: 'ocrMissing' } });
    const long = await failure(
      bridge.call('agenda.apply', {
        recordingId: Q3,
        items: [{ text: 'x'.repeat(201), uncertain: false, uncertainReason: null }],
        source: 'Pasted text',
        sourceKind: 'pastedText',
        attachmentToken: null,
      }),
    );
    expect(long.code).toBe('agenda.itemTooLong');
    const many = await failure(
      bridge.call('agenda.apply', {
        recordingId: Q3,
        items: Array.from({ length: 201 }, (_, i) => ({ text: `Item ${i}`, uncertain: false, uncertainReason: null })),
        source: 'Pasted text',
        sourceKind: 'pastedText',
        attachmentToken: null,
      }),
    );
    expect(many.code).toBe('agenda.tooManyItems');
    expect((await failure(bridge.call('agenda.importDropped', { recordingId: null, paths: ['agenda.zip'] }))).code).toBe('agenda.unsupportedFormat');
    const ocr = await failure(bridge.call('agenda.importDropped', { recordingId: null, paths: ['photo.png'] }));
    expect(ocr.code).toBe('agenda.ocrUnavailable');
    expect(ocr.detail).toBe('ms-settings:regionlanguage');
    expect((await failure(bridge.call('agenda.parseText', { recordingId: null, text: '-\n\n2.' }))).code).toBe('agenda.noItems');
    const noDrop = client({ m3: { agenda: 'noDrop' } });
    expect((await failure(noDrop.call('agenda.importDropped', { recordingId: null, paths: ['agenda.docx'] }))).code).toBe('agenda.dropUnavailable');
  });

  it('marks agenda items covered', async () => {
    const bridge = client();
    const project = await bridge.call('project.get', { recordingId: Q3 });
    const item = project.details.agenda.items[3];
    const { agenda } = await bridge.call('agenda.setCovered', { recordingId: Q3, itemId: item?.id ?? '', covered: true });
    expect(agenda.items[3]?.covered).toBe(true);
  });
});

describe('browser-preview host: export (M3)', () => {
  it('estimates every component with the unavailable ones named', async () => {
    const bridge = client();
    const all = { ...DEFAULT_EXPORT_SELECTION, tracks: { ...DEFAULT_EXPORT_SELECTION.tracks, on: true }, details: { on: true }, attachments: { on: true } };
    const estimate = await bridge.call('export.estimate', { recordingId: DESIGN, selection: all });
    expect(new Set(estimate.items.map((i) => i.component))).toEqual(new Set(['audioMixed', 'tracks', 'transcript', 'details', 'attachments']));
    expect(estimate.items.filter((i) => i.component === 'tracks')).toHaveLength(3);
    expect(estimate.unavailable).toEqual([{ component: 'documents', reason: 'Documents arrive in a later version' }]);
    const mp3 = await bridge.call('export.estimate', {
      recordingId: DESIGN,
      selection: { ...all, audioMixed: { on: true, format: 'mp3', bitrateKbps: 192 } },
    });
    const flacMix = estimate.items.find((i) => i.component === 'audioMixed')?.bytes ?? 0;
    expect(mp3.items.find((i) => i.component === 'audioMixed')?.bytes).toBeLessThan(flacMix);
    const notTranscribed = await bridge.call('export.estimate', { recordingId: '20261006-100000-q3plan', selection: all });
    expect(notTranscribed.unavailable.map((u) => u.component)).toContain('transcript');
  });

  it('runs a job with progress, a footer line, a manifest and remembered defaults', async () => {
    const bridge = client();
    const footers: (string | null)[] = [];
    bridge.on('status.footer', (f) => {
      footers.push(f.export?.title ?? null);
    });
    const finished = waitFor(bridge, 'export.progress', (p) => p.state !== 'running');
    const { jobId } = await bridge.call('export.run', {
      recordingId: DESIGN,
      selection: DEFAULT_EXPORT_SELECTION,
      destination: { folder: 'E:\\Exports', createSubfolder: true },
      remember: true,
    });
    const events: ExportProgressPayload[] = await finished;
    const last = events.at(-1);
    expect(last?.state).toBe('done');
    expect(last?.outputFolder).toBe(`E:\\Exports\\${exportFolderName('Design review: library screen', (await bridge.call('project.get', { recordingId: DESIGN })).summary.createdAt)}`);
    expect(last?.files).toBe(2);
    expect(events.some((e) => e.state === 'running' && e.percent > 0 && e.percent < 100)).toBe(true);
    expect(footers).toContain('Design review: library screen');
    expect((await bridge.call('settings.get')).export.defaultFolder).toBe('E:\\Exports');
    await bridge.call('export.openFolder', { jobId });
    expect((await failure(bridge.call('export.cancel', { jobId: 'nope' }))).code).toBe('export.notFound');
  });

  it('fails part-way with ?export=fail and refuses an unwritable folder before writing', async () => {
    const bridge = client({ m3: { export: 'fail' } });
    const failed = waitFor(bridge, 'export.progress', (p) => p.state !== 'running');
    await bridge.call('export.run', { recordingId: DESIGN, selection: DEFAULT_EXPORT_SELECTION, destination: { folder: 'E:\\Exports', createSubfolder: false }, remember: false });
    const last = (await failed).at(-1);
    expect(last?.state).toBe('failed');
    expect(last?.message).toContain('Nothing inside Memento was changed.');
    const refused = await failure(
      client({ m3: { export: 'unwritable' } }).call('export.run', {
        recordingId: DESIGN,
        selection: DEFAULT_EXPORT_SELECTION,
        destination: { folder: 'E:\\Exports', createSubfolder: false },
        remember: false,
      }),
    );
    expect(refused.code).toBe('export.destinationUnwritable');
    const nothing = await failure(
      bridge.call('export.run', {
        recordingId: DESIGN,
        selection: { ...DEFAULT_EXPORT_SELECTION, audioMixed: { ...DEFAULT_EXPORT_SELECTION.audioMixed, on: false }, transcript: { on: false, formats: [] } },
        destination: { folder: 'E:\\Exports', createSubfolder: false },
        remember: false,
      }),
    );
    expect(nothing.code).toBe('export.nothingSelected');
  });

  it('names the subfolder after the sanitised title and the recording date', () => {
    expect(exportFolderName('Design review: library screen', '2026-10-05T16:00:00-06:00')).toBe('Design review - library screen 2026-10-05');
    expect(exportFolderName('a/b\\c?', '2026-01-02T00:00:00+00:00')).toBe('a - b - c 2026-01-02');
    expect(exportFolderName('...', '2026-01-02T00:00:00+00:00')).toBe('Recording 2026-01-02');
    expect(exportPathPreview('D:\\Exports\\', true, 'Q3', '2026-10-06T10:00:00+02:00')).toBe('D:\\Exports\\Q3 2026-10-06\\');
    expect(exportPathPreview('D:\\Exports', false, 'Q3', '2026-10-06T10:00:00+02:00')).toBe('D:\\Exports\\');
  });
});

describe('browser-preview host: library, storage, keys and startup (M3)', () => {
  it('imports media as a new project that is stored and then queued for transcription', async () => {
    const bridge = client();
    const stored = waitFor(bridge, 'processing.progress', (p) => p.stages.some((s) => s.stage === 'transcript'));
    const { recordingId } = await bridge.call('library.importMedia', { path: 'C:\\Audio\\Team call.mp4' });
    expect(recordingId).not.toBeNull();
    const project = await bridge.call('project.get', { recordingId: recordingId ?? '' });
    expect(project.tracks.map((t) => t.sourceId)).toEqual(['imported']);
    expect(project.history[0]?.detail).toContain('Only the audio track');
    await stored;
    const unsupported = await failure(client({ m3: { import: 'unsupported' } }).call('library.importMedia', {}));
    expect(unsupported.code).toBe('library.importUnsupported');
  });

  it('reports usage, sorts by size, moves the library with progress and refuses while busy', async () => {
    const bridge = client();
    const usage = await bridge.call('library.usage');
    expect(usage.count).toBe(14);
    expect(usage.largest).not.toBeNull();
    const bySize = await bridge.call('library.list', { sort: 'size' });
    expect(bySize.recordings[0]?.id).toBe(usage.largest?.recordingId);
    expect(bySize.recordings.map((r) => r.sizeBytes)).toEqual([...bySize.recordings.map((r) => r.sizeBytes)].sort((a, b) => b - a));
    const moved = waitFor(bridge, 'library.moveProgress', (p) => p.state === 'done');
    await bridge.call('library.move', { newPath: 'E:\\Memento Library' });
    const events = await moved;
    expect(events.length).toBeGreaterThan(2);
    expect((await bridge.call('settings.get')).libraryPath).toBe('E:\\Memento Library');
    expect((await failure(client({ m3: { move: 'busy' } }).call('library.move', { newPath: 'F:\\Lib' }))).code).toBe('library.busy');
    expect((await bridge.call('library.rebuildIndex')).recordings).toBe(14);
  });

  it('stores only whether a key exists, sets startup, and validates the M3 settings blocks', async () => {
    const bridge = client();
    expect((await bridge.call('settings.get')).ai.providers).toEqual({ anthropic: { hasKey: true }, openai: { hasKey: false } });
    expect(await bridge.call('ai.setKey', { provider: 'openai', key: 'sk-test-0123456789abcdefghij' })).toEqual({ hasKey: true });
    const snapshot = await bridge.call('settings.get');
    expect(JSON.stringify(snapshot)).not.toContain('sk-test');
    expect(snapshot.ai.providers.openai.hasKey).toBe(true);
    expect((await failure(bridge.call('ai.setKey', { provider: 'openai', key: 'short' }))).code).toBe('bridge.invalidParams');
    expect(await bridge.call('ai.clearKey', { provider: 'anthropic' })).toEqual({ hasKey: false });
    expect(await bridge.call('app.setStartup', { startWithWindows: true })).toEqual({ startWithWindows: true });
    expect((await bridge.call('settings.get')).general.startWithWindows).toBe(true);
    const next = await bridge.call('settings.set', { storage: { reclaimOlderThanDays: 30 } });
    expect(next.storage.reclaimOlderThanDays).toBe(30);
    expect((await failure(bridge.call('settings.set', { storage: { reclaimOlderThanDays: -1 } }))).code).toBe('settings.invalidValue');
    const reclaimed = waitFor(bridge, 'storage.reclaimProgress', (p) => p.state === 'done');
    await bridge.call('storage.reclaim', { recordingIds: null, downmixMono: true, codec: 'aac', bitrateKbps: 128 });
    const last = (await reclaimed).at(-1);
    expect(last?.recordingsDone).toBeGreaterThan(0);
    expect(last?.bytesFreed).toBeGreaterThan(0);
  });

  it('adds, opens and removes attachments, refusing files over 100 MB', async () => {
    const bridge = client();
    const { attachment } = await bridge.call('attachments.add', { recordingId: Q3 });
    expect(attachment?.name).toBe('project-brief.pdf');
    await bridge.call('attachments.open', { recordingId: Q3, attachmentId: attachment?.id ?? '' });
    await bridge.call('attachments.remove', { recordingId: Q3, attachmentId: attachment?.id ?? '' });
    expect((await bridge.call('attachments.list', { recordingId: Q3 })).attachments.map((a) => a.name)).toEqual(['agenda.docx']);
    expect((await failure(bridge.call('attachments.add', { recordingId: Q3, path: 'D:\\huge-video.mov' }))).code).toBe('attachments.tooLarge');
    expect((await failure(bridge.call('attachments.remove', { recordingId: Q3, attachmentId: 'gone' }))).code).toBe('attachments.notFound');
    const changed = await bridge.call('project.changeType', { recordingId: Q3, type: 'Board meeting' });
    expect(changed.summary.type).toBe('Board meeting');
  });
});
