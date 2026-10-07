import { describe, expect, it } from 'vitest';
import type { FooterStatusPayload } from '../bridge/types';
import { sourceFormat } from './estimate';
import { agoWording, recordStatusLine, recordStorageLine } from './recordFooter';

const GIB = 1024 ** 3;
const status = (overrides: Partial<FooterStatusPayload> = {}): FooterStatusPayload => ({
  engine: { ready: true, device: 'GPU', detail: { ready: true, device: 'GPU', gpuName: null, freeVramBytes: null, model: 'large-v3', paused: null } },
  storage: { freeBytes: 212 * GIB, lowSpace: false },
  recording: { active: true, lastCheckpointAt: null, lostSource: null },
  processingPaused: null,
  ...overrides,
});

describe('Recording session footer', () => {
  const now = Date.parse('2026-10-06T10:12:08+02:00');

  it('says what is being saved in each phase', () => {
    expect(recordStatusLine('ready', status(), null, null, now)).toEqual({ tone: 'ok', text: 'Ready · recordings save to this PC as they happen' });
    expect(recordStatusLine('recording', status(), '2026-10-06T10:12:00+02:00', null, now).text).toBe(
      'Saving continuously · last checkpoint 8 s ago',
    );
    expect(recordStatusLine('recording', status(), null, null, now).text).toBe('Saving continuously');
    expect(recordStatusLine('paused', status(), null, null, now).tone).toBe('accent');
  });

  it('names a lost source in danger while the others keep recording', () => {
    const lost = status({ recording: { active: true, lastCheckpointAt: null, lostSource: 'Zoom' } });
    expect(recordStatusLine('recording', lost, null, 41_000, now)).toEqual({
      tone: 'danger',
      strong: 'Zoom lost at 00:00:41',
      text: ' · other tracks recording',
    });
  });

  it('words the checkpoint age', () => {
    expect(agoWording(8_400)).toBe('8 s');
    expect(agoWording(125_000)).toBe('2 min');
    expect(agoWording(3_900_000)).toBe('1 h 5 min');
    expect(agoWording(-5)).toBe('0 s');
  });

  it('pairs free space with the time left at this quality', () => {
    const tracks = [sourceFormat({ kind: 'microphone' }), sourceFormat({ kind: 'system' }), sourceFormat({ kind: 'application' })];
    expect(recordStorageLine(status(), tracks)).toEqual({ text: '212 GB free · about 87 hours at this quality', low: false });
    expect(recordStorageLine(status({ storage: { freeBytes: 4 * GIB, lowSpace: true } }), tracks)).toEqual({
      text: 'Low disk space · 4 GB free · about 1 hour at this quality',
      low: true,
    });
    expect(recordStorageLine(status(), []).text).toBe('212 GB free');
    expect(recordStorageLine(null, tracks).text).toBe('Everything is stored on this PC');
  });
});
