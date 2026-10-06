import { describe, expect, it } from 'vitest';
import { engineLine, storageLine } from './footer';
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
