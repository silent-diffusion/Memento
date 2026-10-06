import { describe, expect, it } from 'vitest';
import { loudnessPerSecond, mockPeaks, mockWav } from './mockMedia';

describe('preview media', () => {
  it('writes a valid 8 kHz 8-bit mono WAV of the requested length', () => {
    const wav = mockWav('rec', 2_000);
    const view = new DataView(wav.buffer);
    const text = (offset: number, length: number): string => String.fromCharCode(...wav.slice(offset, offset + length));
    expect(text(0, 4)).toBe('RIFF');
    expect(text(8, 4)).toBe('WAVE');
    expect(view.getUint16(20, true)).toBe(1);
    expect(view.getUint16(22, true)).toBe(1);
    expect(view.getUint32(24, true)).toBe(8000);
    expect(view.getUint16(34, true)).toBe(8);
    expect(view.getUint32(40, true)).toBe(16_000);
    expect(wav.length).toBe(44 + 16_000);
  });

  it('draws peaks from the same loudness as the audio, deterministically', () => {
    const a = mockPeaks('rec', 60_000, 500);
    expect(a).toMatchObject({ schemaVersion: 1, windowMs: 500 });
    expect(a.peaks).toHaveLength(120);
    expect(a.peaks.every(([rms, peak]) => rms >= 0 && rms <= peak && peak <= 1)).toBe(true);
    expect(mockPeaks('rec', 60_000, 500)).toEqual(a);
    expect(loudnessPerSecond('rec', 60_000)).toHaveLength(60);
  });
});
