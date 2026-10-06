import { describe, expect, it } from 'vitest';
import { formatSize } from './storage';
import { formatSpan, formatTimecode } from './duration';
import { deleteCopy, joinList, lowSpaceCopy, recoveryCopy, sourceLostCopy, stoppedByHostCopy } from './messages';

const GIB = 1024 ** 3;
const MIB = 1024 ** 2;

describe('joinList', () => {
  it.each([
    [[], ''],
    [['a'], 'a'],
    [['a', 'b'], 'a and b'],
    [['a', 'b', 'c'], 'a, b and c'],
  ])('%j reads %s', (items, expected) => {
    expect(joinList(items)).toBe(expected);
  });
});

describe('sizes and spans', () => {
  it.each([
    [410 * MIB, '410 MB'],
    [1.24 * GIB, '1.2 GB'],
    [2 * GIB, '2 GB'],
    [148.6 * GIB, '149 GB'],
    [12_000, '12 KB'],
    [0, '0 KB'],
  ])('%d bytes is %s', (bytes, expected) => {
    expect(formatSize(bytes)).toBe(expected);
  });

  it('formats timecodes and spans', () => {
    expect(formatTimecode((41 * 60 + 12) * 1000)).toBe('00:41:12');
    expect(formatTimecode((3600 + 2 * 60 + 14) * 1000)).toBe('01:02:14');
    expect(formatSpan(20_000)).toBe('20 seconds');
    expect(formatSpan(1000)).toBe('1 second');
    expect(formatSpan(120_000)).toBe('2 minutes');
  });
});

describe('§17 copy', () => {
  const all: string[] = [];
  const record = (...texts: string[]): void => {
    all.push(...texts);
  };

  it('delete: names it, says what is removed and its size, that exports are untouched and it cannot be undone', () => {
    const copy = deleteCopy({ title: 'Weekly 1:1 with Sam', sizeBytes: 410 * MIB, items: ['the recording', 'its transcript', 'documents'] });
    record(copy.title, copy.body);
    expect(copy.title).toBe('Delete "Weekly 1:1 with Sam"?');
    expect(copy.body).toBe(
      'Removes the recording, its transcript and documents from this PC (410 MB). Exported copies are not affected. This cannot be undone.',
    );
  });

  it('delete: still names the recording when the host lists no items', () => {
    expect(deleteCopy({ title: 'X', sizeBytes: GIB, items: [] }).body).toContain('Removes the recording from this PC (1 GB).');
  });

  it('recovery: what was saved, how many tracks are intact, what may be missing', () => {
    const copy = recoveryCopy({
      recordingId: 'r',
      title: 'Q3 planning sync',
      startedAt: '2026-10-06T10:00:00+00:00',
      tracksIntact: 3,
      tracksTotal: 3,
      lastCheckpointAt: null,
      recoveredDurationMs: (3600 + 2 * 60 + 14) * 1000,
      mayBeMissingMs: 20_000,
    });
    record(copy.title, copy.lead + copy.rest);
    expect(copy.title).toBe('Recovered an interrupted recording');
    expect(copy.lead + copy.rest).toBe(
      'Q3 planning sync was saved up to 1:02:14 before the recording was interrupted. All 3 tracks are intact. The last 20 seconds may be missing.',
    );
  });

  it('recovery: partial tracks and nothing missing', () => {
    const copy = recoveryCopy({
      recordingId: 'r',
      title: 'T',
      startedAt: '',
      tracksIntact: 2,
      tracksTotal: 3,
      lastCheckpointAt: null,
      recoveredDurationMs: 61_000,
      mayBeMissingMs: 0,
    });
    expect(copy.rest).toBe(' was saved up to 1:01 before the recording was interrupted. 2 of 3 tracks are intact.');
  });

  it('source lost: the source, the time, and what is still recording', () => {
    const copy = sourceLostCopy({
      sessionId: 's',
      sourceId: 'app:1',
      name: 'Zoom',
      atMs: (41 * 60 + 12) * 1000,
      remaining: ['Microphone', 'System audio'],
    });
    record(copy.title, copy.body);
    expect(copy).toEqual({ title: 'Zoom stopped at 00:41:12', body: 'Microphone and System audio are still recording.' });
    expect(sourceLostCopy({ sessionId: 's', sourceId: 'x', name: 'Mic', atMs: 0, remaining: ['Zoom'] }).body).toBe(
      'Zoom is still recording.',
    );
    expect(sourceLostCopy({ sessionId: 's', sourceId: 'x', name: 'Mic', atMs: 0, remaining: [] }).body).toBe(
      'Nothing else is recording. Everything up to that point is saved.',
    );
  });

  it('low space: amount, what continues and what pauses', () => {
    const copy = lowSpaceCopy({ freeBytes: 4 * GIB, thresholdBytes: 10 * GIB, recordingContinues: true, transcriptionPaused: true });
    record(copy.lead, copy.rest);
    expect(copy.lead + copy.rest).toBe('Low disk space · 4 GB free. Recording continues. Transcription is paused until there is room.');
    expect(lowSpaceCopy({ freeBytes: 4 * GIB, thresholdBytes: 10 * GIB, recordingContinues: false, transcriptionPaused: false }).rest).toBe(
      ' Everything already recorded is safe.',
    );
  });

  it('stopped by the host: says when and that what came before is saved', () => {
    const copy = stoppedByHostCopy({ sessionId: 's', recordingId: 'r', reason: 'diskFull', atMs: (1 * 3600 + 42 * 60 + 10) * 1000, message: '' });
    record(copy.title, copy.body);
    expect(copy.title).toBe('Recording stopped at 01:42:10 · drive full');
    expect(copy.body).toBe('Everything up to that point is saved and will transcribe once there is room.');
  });

  it('never exclaims or apologises', () => {
    for (const text of all) {
      expect(text).not.toMatch(/!|Oops|Something went wrong/);
    }
  });
});
