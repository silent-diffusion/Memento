import { describe, expect, it } from 'vitest';
import type { AudioExportFormat, ExportEstimate, ExportSelection, TranscriptExportFormat } from '../bridge/types';
import cases from './export-naming.cases.json';
import { exportFileNames, exportFolderName, manifestShare, safeFileStem, summarise } from './export';

interface FileCase {
  kind: string;
  expected: string;
  base?: string;
  format?: string;
  trackName?: string;
  trackId?: string;
  name?: string;
}

function fileName(c: FileCase): string {
  const base = c.base ?? '';
  switch (c.kind) {
    case 'mix':
      return exportFileNames.mix(base, c.format as AudioExportFormat);
    case 'track':
      return exportFileNames.track(base, c.trackName ?? '', c.trackId ?? '', c.format as AudioExportFormat);
    case 'transcript':
      return exportFileNames.transcript(base, c.format as TranscriptExportFormat);
    case 'details':
      return exportFileNames.details(base);
    case 'attachment':
      return exportFileNames.attachment(c.name ?? '');
    default:
      throw new Error(`Unknown case kind ${c.kind}`);
  }
}

describe('export names shared with the host (export-naming.cases.json)', () => {
  // tests/Memento.Core.Tests/M3/ExportNamingSharedCasesTests.cs runs the same cases against ExportNaming.
  it.each(cases.folders)('folder for "$title"', ({ title, createdAt, expected }) => {
    expect(exportFolderName(title, createdAt)).toBe(expected);
  });

  it.each(cases.files as FileCase[])('$kind file $expected', (c) => {
    expect(fileName(c)).toBe(c.expected);
  });

  it('makes a file stem safe like the host, falling back to the id', () => {
    expect(safeFileStem('a\u0007b', 'x')).toBe('a b');
    expect(safeFileStem('LPT1.txt', 'x')).toBe('_LPT1.txt');
    expect(safeFileStem('   ', 'mic')).toBe('mic');
    expect(safeFileStem('é', 'x')).toBe('é');
  });
});

const SELECTION: ExportSelection = {
  audioMixed: { on: true, format: 'flac', bitrateKbps: null },
  tracks: { on: false, format: 'flac', bitrateKbps: null },
  transcript: { on: true, formats: ['json', 'srt'] },
  documents: { on: false, documentIds: [], format: 'docx' },
  details: { on: false },
  attachments: { on: false },
};

describe('export estimate totals', () => {
  // As the host sends it for every row ticked: four items and the manifest (1 file, 600 bytes).
  const estimate: ExportEstimate = {
    files: 5,
    bytes: 1000 + 2000 + 300 + 400 + 600,
    items: [
      { component: 'audioMixed', name: 'B 2026-10-06.flac', bytes: 1000 },
      { component: 'tracks', name: 'B 2026-10-06 - Mic.flac', bytes: 2000 },
      { component: 'transcript', name: 'B 2026-10-06 - transcript.json', bytes: 300 },
      { component: 'transcript', name: 'B 2026-10-06.srt', bytes: 400 },
    ],
    unavailable: [],
  };

  it('counts the manifest the host adds beyond its items', () => {
    expect(manifestShare(estimate)).toEqual({ files: 1, bytes: 600 });
  });

  it('sums the ticked rows and adds the manifest once', () => {
    expect(summarise(estimate, SELECTION)).toEqual({ files: 4, bytes: 1000 + 300 + 400 + 600 });
  });

  it('writes no manifest when nothing is ticked that can be written', () => {
    const nothing = { ...SELECTION, audioMixed: { ...SELECTION.audioMixed, on: false }, transcript: { on: false, formats: [] } };
    expect(summarise(estimate, nothing)).toEqual({ files: 0, bytes: 0 });
    const unavailable = { ...estimate, unavailable: [{ component: 'audioMixed' as const, reason: 'Not saved yet' }, { component: 'transcript' as const, reason: 'Not transcribed yet' }] };
    expect(summarise(unavailable, SELECTION)).toEqual({ files: 0, bytes: 0 });
  });
});
