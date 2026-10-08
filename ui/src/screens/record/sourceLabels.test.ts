import { describe, expect, it } from 'vitest';
import type { AudioSource } from '../../bridge/types';
import { sourceToggleLabels } from './RecordParts';

const source = (id: string, kind: AudioSource['kind'], name: string, detail: string, processId: number | null = null): AudioSource => ({
  id,
  kind,
  name,
  detail,
  isDefault: false,
  processId,
});

describe('source switch names', () => {
  it('stay short when every source is distinct', () => {
    const labels = sourceToggleLabels([
      source('mic:a', 'microphone', 'Microphone Array', 'Built-in'),
      source('system:default', 'system', 'Everything this PC plays', 'Default output'),
      source('app:12', 'application', 'Zoom', 'Only this app', 12),
    ]);

    expect([...labels.values()]).toEqual(['Microphone', 'System audio', 'Zoom']);
  });

  it('tell apart two outputs, two microphones and two copies of one app', () => {
    const labels = sourceToggleLabels([
      source('mic:a', 'microphone', 'Microphone Array', 'Built-in'),
      source('mic:b', 'microphone', 'USB Mic', 'USB'),
      source('system:default', 'system', 'Everything this PC plays', 'Default output'),
      source('system:x', 'system', 'Speakers (Realtek(R) Audio)', 'Output'),
      source('app:1', 'application', 'Browser', 'Only this app', 1),
      source('app:2', 'application', 'Browser', 'Only this app', 2),
    ]);

    expect([...labels.values()]).toEqual([
      'Microphone: Microphone Array · Built-in',
      'Microphone: USB Mic · USB',
      'System audio: Everything this PC plays',
      'System audio: Speakers (Realtek(R) Audio)',
      'Browser: Only this app (1)',
      'Browser: Only this app (2)',
    ]);
    expect(new Set(labels.values()).size).toBe(6);
  });
});
