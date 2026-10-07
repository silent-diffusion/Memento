import { describe, expect, it, vi } from 'vitest';
import type { BridgeClient } from '../bridge/client';
import type { ExportProgressPayload } from '../bridge/types';
import { connectJobEvents, trackExport, type ExportRequest } from './jobs';
import { createStore } from './store';

function fakeBridge(): { bridge: BridgeClient; emit: (payload: ExportProgressPayload) => void } {
  const listeners: ((payload: ExportProgressPayload) => void)[] = [];
  const bridge: BridgeClient = {
    isHosted: false,
    call: vi.fn(() => Promise.resolve({})),
    callWithFiles: vi.fn(() => Promise.resolve({})),
    on: ((event: string, handler: (payload: ExportProgressPayload) => void) => {
      if (event === 'export.progress') {
        listeners.push(handler);
      }
      return () => undefined;
    }) as BridgeClient['on'],
  };
  return {
    bridge,
    emit: (payload) => {
      for (const listener of listeners) {
        listener(payload);
      }
    },
  };
}

const REQUEST: ExportRequest = {
  recordingId: 'r1',
  title: 'Q3 planning sync',
  selection: {
    audioMixed: { on: true, format: 'flac', bitrateKbps: null },
    tracks: { on: false, format: 'flac', bitrateKbps: null },
    transcript: { on: true, formats: ['json', 'srt'] },
    documents: { on: false, documentIds: [], format: 'docx' },
    details: { on: true },
    attachments: { on: true },
  },
  destination: { folder: 'E:\\Exports', createSubfolder: true },
};

const done = (jobId: string): ExportProgressPayload => ({
  jobId,
  recordingId: 'r1',
  percent: 100,
  currentFile: null,
  state: 'done',
  message: null,
  outputFolder: 'E:\\Exports\\Q3 planning sync 2026-10-06',
  files: 7,
  bytes: 30_000_000,
});

describe('export job toasts', () => {
  it('shows the done toast for a small export that finished before export.run answered', () => {
    const store = createStore(false);
    const { bridge, emit } = fakeBridge();
    connectJobEvents(bridge, store);

    // The host can finish a small export and report it before the UI has handled export.run's answer.
    emit(done('x1'));
    expect(store.toasts.items.value).toEqual([]);
    trackExport(store, 'x1', REQUEST);

    const toast = store.toasts.items.value.at(-1);
    expect(toast?.title).toBe('Exported Q3 planning sync');
    expect(toast?.actions.map((a) => a.label)).toEqual(['Open folder', 'Dismiss']);
  });

  it('does not give a new job the previous job’s title', () => {
    const store = createStore(false);
    const { bridge, emit } = fakeBridge();
    connectJobEvents(bridge, store);
    trackExport(store, 'x1', REQUEST);
    emit(done('x1'));
    const shown = store.toasts.items.value.length;

    emit(done('x2'));

    expect(store.toasts.items.value).toHaveLength(shown);
    trackExport(store, 'x2', { ...REQUEST, title: 'Design review' });
    expect(store.toasts.items.value.at(-1)?.title).toBe('Exported Design review');
  });
});
