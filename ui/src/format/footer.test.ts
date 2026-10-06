import { describe, expect, it } from 'vitest';
import type { FooterStatusPayload } from '../bridge/types';
import { engineLine, footerStorageLine, statusLine, storageLine } from './footer';
import { formatFreeSpace } from './storage';

const GIB = 1024 ** 3;

describe('formatFreeSpace', () => {
  it.each([
    [212 * GIB, '212 GB'],
    [212.9 * GIB, '212 GB'],
    [10 * GIB, '10 GB'],
    [9.99 * GIB, '9.9 GB'],
    [4 * GIB, '4 GB'],
    [4.56 * GIB, '4.5 GB'],
    [GIB, '1 GB'],
    [640 * 1024 ** 2, '640 MB'],
    [0, '0 MB'],
    [-5, '0 MB'],
  ])('%d bytes reads %s', (bytes, expected) => {
    expect(formatFreeSpace(bytes)).toBe(expected);
  });
});

describe('footer lines', () => {
  it('shows neutral placeholders until the host reports', () => {
    expect(engineLine(null)).toEqual({ text: 'Checking transcription engine', tone: 'neutral' });
    expect(storageLine(null)).toEqual({ text: 'Everything is stored on this PC', low: false });
  });

  it('says plainly when no engine is set up', () => {
    expect(engineLine({ ready: false, device: null })).toEqual({ text: 'Local transcription is not set up yet', tone: 'neutral' });
  });

  it('matches the design copy when an engine is ready', () => {
    expect(engineLine({ ready: true, device: 'GPU' })).toEqual({ text: 'Local transcription ready · GPU', tone: 'ok' });
  });

  it('states free space when known', () => {
    expect(storageLine({ freeBytes: 212 * GIB, lowSpace: false })).toEqual({
      text: 'Everything is stored on this PC · 212 GB free',
      low: false,
    });
  });

  it('switches to the low-space wording', () => {
    expect(storageLine({ freeBytes: 4 * GIB, lowSpace: true })).toEqual({ text: 'Low disk space · 4 GB free', low: true });
  });

  it('never shows a number the host could not measure', () => {
    expect(storageLine({ freeBytes: null, lowSpace: false }).text).toBe('Everything is stored on this PC');
  });
});

describe('footer variants (DESIGN.md §17)', () => {
  const status = (overrides: Partial<FooterStatusPayload> = {}): FooterStatusPayload => ({
    engine: { ready: true, device: 'GPU' },
    storage: { freeBytes: 212 * GIB, lowSpace: false },
    recording: { active: false, lastCheckpointAt: null, lostSource: null },
    processingPaused: null,
    ...overrides,
  });

  it('is the engine line with an ok dot normally', () => {
    expect(statusLine(status())).toEqual({ text: 'Local transcription ready · GPU', tone: 'ok' });
    expect(statusLine(null)).toEqual({ text: 'Checking transcription engine', tone: 'neutral' });
    expect(footerStorageLine(status())).toEqual({ text: 'Everything is stored on this PC · 212 GB free', low: false });
  });

  it('turns accent when processing is paused', () => {
    expect(statusLine(status({ processingPaused: 'Low disk space' }))).toEqual({ text: 'Transcription paused · Low disk space', tone: 'accent' });
  });

  it('turns danger and names the lost source while recording', () => {
    const lost = status({ recording: { active: true, lastCheckpointAt: null, lostSource: 'Zoom' } });
    expect(statusLine(lost)).toEqual({ tone: 'danger', strong: 'Zoom lost', text: ' · other tracks recording' });
    expect(statusLine(lost, (41 * 60 + 12) * 1000).strong).toBe('Zoom lost at 00:41:12');
    expect(footerStorageLine(lost)).toEqual({ text: 'Saving continuously · 212 GB free', low: false });
  });

  it('keeps the storage warning while recording', () => {
    const low = status({
      storage: { freeBytes: 4 * GIB, lowSpace: true },
      recording: { active: true, lastCheckpointAt: null, lostSource: null },
    });
    expect(footerStorageLine(low)).toEqual({ text: 'Low disk space · 4 GB free', low: true });
  });
});
