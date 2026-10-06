import { describe, expect, it } from 'vitest';
import { estimateWording, hoursAtThisQuality, recordingBytesPerSecond, sourceFormat } from './estimate';

const GIB = 1024 ** 3;

describe('recording time left at this quality', () => {
  it('counts int24 PCM per channel per track', () => {
    const mic = sourceFormat({ kind: 'microphone' });
    const system = sourceFormat({ kind: 'system' });
    expect(mic).toEqual({ sampleRate: 48_000, channels: 1 });
    expect(system).toEqual({ sampleRate: 48_000, channels: 2 });
    expect(recordingBytesPerSecond([mic])).toBe(144_000);
    expect(recordingBytesPerSecond([mic, system, system])).toBe(720_000);
  });

  it('turns free space into whole hours', () => {
    const tracks = [sourceFormat({ kind: 'microphone' }), sourceFormat({ kind: 'system' }), sourceFormat({ kind: 'application' })];
    // 212 GiB / 720 000 B/s = 316 151 s = 87.8 h
    expect(hoursAtThisQuality(212 * GIB, tracks)).toBe(87);
    expect(hoursAtThisQuality(212 * GIB, [])).toBeNull();
    expect(estimateWording(212 * GIB, tracks)).toBe('about 87 hours at this quality');
    expect(estimateWording(0.4 * GIB, tracks)).toBe('under an hour at this quality');
    expect(estimateWording(0.6 * GIB, [sourceFormat({ kind: 'microphone' })])).toBe('about 1 hour at this quality');
  });
});
