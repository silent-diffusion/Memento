// Generated media for the browser preview: a quiet, speech-like WAV for the mix and a peaks.json
// drawn from the same loudness envelope, both as in-memory blob URLs. The real host returns
// https://library.memento/ URLs instead; the UI never builds either kind itself.
import { seedFromId } from '../format/waveform';

/** 8 kHz, 8-bit unsigned mono keeps an hour of preview audio near 29 MB. */
const SAMPLE_RATE = 8000;
const BYTES_PER_SAMPLE = 1;

/** The peaks.json the audio layer writes (BRIDGE.md): one `[rms, peak]` pair, both 0..1, per window of the mix. */
export interface PeaksFile {
  schemaVersion: 1;
  windowMs: number;
  peaks: [number, number][];
}

/** One loudness value (0..1) per second, speech-like: mostly moderate with pauses and a few peaks. */
export function loudnessPerSecond(id: string, durationMs: number): Float32Array {
  const seconds = Math.max(1, Math.ceil(durationMs / 1000));
  const out = new Float32Array(seconds);
  let value = seedFromId(id);
  let level = 0.4;
  for (let i = 0; i < seconds; i++) {
    value = (value * 9301 + 49_297) % 233_280;
    const r = value / 233_280;
    // Now and then a pause; otherwise drift toward a new target.
    const target = r < 0.08 ? 0.03 : 0.15 + r * r * 0.85;
    level += (target - level) * 0.55;
    out[i] = Math.min(1, Math.max(0, level));
  }
  return out;
}

/** Syllable-rate modulation so the waveform and the audio have texture within each second. */
function syllable(tSeconds: number): number {
  return 0.45 + 0.55 * Math.abs(Math.sin(2 * Math.PI * 3.7 * tSeconds));
}

export function mockPeaks(id: string, durationMs: number, windowMs = Math.max(50, Math.ceil(durationMs / 1200))): PeaksFile {
  const loudness = loudnessPerSecond(id, durationMs);
  const count = Math.max(1, Math.ceil(durationMs / windowMs));
  const peaks: [number, number][] = [];
  for (let i = 0; i < count; i++) {
    const t = (i * windowMs) / 1000;
    const second = Math.min(loudness.length - 1, Math.floor(t));
    const peak = Number(((loudness[second] ?? 0) * syllable(t)).toFixed(3));
    peaks.push([Number((peak * 0.6).toFixed(3)), peak]);
  }
  return { schemaVersion: 1, windowMs, peaks };
}

/** A complete RIFF/WAVE file: 8-bit unsigned PCM, mono. */
export function mockWav(id: string, durationMs: number): Uint8Array {
  const sampleCount = Math.max(1, Math.round((durationMs / 1000) * SAMPLE_RATE));
  const dataBytes = sampleCount * BYTES_PER_SAMPLE;
  const bytes = new Uint8Array(44 + dataBytes);
  const view = new DataView(bytes.buffer);
  const ascii = (offset: number, text: string): void => {
    for (let i = 0; i < text.length; i++) {
      bytes[offset + i] = text.charCodeAt(i);
    }
  };
  ascii(0, 'RIFF');
  view.setUint32(4, 36 + dataBytes, true);
  ascii(8, 'WAVE');
  ascii(12, 'fmt ');
  view.setUint32(16, 16, true);
  view.setUint16(20, 1, true); // PCM
  view.setUint16(22, 1, true); // mono
  view.setUint32(24, SAMPLE_RATE, true);
  view.setUint32(28, SAMPLE_RATE * BYTES_PER_SAMPLE, true);
  view.setUint16(32, BYTES_PER_SAMPLE, true);
  view.setUint16(34, 8, true);
  ascii(36, 'data');
  view.setUint32(40, dataBytes, true);

  // One second of a soft voiced tone (140 Hz with two harmonics), scaled per second by loudness.
  const base = new Float32Array(SAMPLE_RATE);
  for (let i = 0; i < SAMPLE_RATE; i++) {
    const t = i / SAMPLE_RATE;
    const tone = Math.sin(2 * Math.PI * 140 * t) * 0.6 + Math.sin(2 * Math.PI * 280 * t) * 0.3 + Math.sin(2 * Math.PI * 420 * t) * 0.1;
    base[i] = tone * syllable(t);
  }
  const loudness = loudnessPerSecond(id, durationMs);
  for (let i = 0; i < sampleCount; i++) {
    const second = Math.floor(i / SAMPLE_RATE);
    const amplitude = (loudness[second] ?? 0) * 0.22;
    bytes[44 + i] = Math.round(128 + 127 * amplitude * (base[i % SAMPLE_RATE] ?? 0));
  }
  return bytes;
}

export interface MockMediaUrls {
  mixUrl: string;
  peaksUrl: string;
}

const cache = new Map<string, MockMediaUrls>();
const CACHE_LIMIT = 3;

/** Blob URLs for one recording, or null where blob URLs do not exist (unit tests under jsdom). */
export function mockMediaUrls(id: string, durationMs: number): MockMediaUrls | null {
  if (typeof URL.createObjectURL !== 'function' || typeof Blob === 'undefined') {
    return null;
  }
  const key = `${id}:${durationMs}`;
  const known = cache.get(key);
  if (known !== undefined) {
    // Most recently used last, so the recording on screen is never the one revoked.
    cache.delete(key);
    cache.set(key, known);
    return known;
  }
  const urls: MockMediaUrls = {
    mixUrl: URL.createObjectURL(new Blob([mockWav(id, durationMs) as BlobPart], { type: 'audio/wav' })),
    peaksUrl: URL.createObjectURL(new Blob([JSON.stringify(mockPeaks(id, durationMs))], { type: 'application/json' })),
  };
  cache.set(key, urls);
  // Long previews are tens of megabytes; keep only the last few recordings opened.
  for (const [oldestKey, oldest] of cache) {
    if (cache.size <= CACHE_LIMIT) {
      break;
    }
    URL.revokeObjectURL(oldest.mixUrl);
    URL.revokeObjectURL(oldest.peaksUrl);
    cache.delete(oldestKey);
  }
  return urls;
}
