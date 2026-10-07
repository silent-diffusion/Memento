// M3 background jobs the UI follows (BRIDGE.md M3 events): exports, the library move and storage
// reclaim. Kept beside the store (one set per store) so screens and the footer read the same state.
import { signal, type Signal } from '@preact/signals';
import type { BridgeClient } from '../bridge/client';
import type {
  ExportDestination,
  ExportProgressPayload,
  ExportSelection,
  LibraryMoveProgressPayload,
  StorageReclaimProgressPayload,
} from '../bridge/types';
import { fileCount } from '../format/export';
import { formatSize } from '../format/storage';
import type { AppStore } from './store';

/** What the dialog sent for a job, so a failure can be tried again with the same choices. */
export interface ExportRequest {
  recordingId: string;
  title: string;
  selection: ExportSelection;
  destination: ExportDestination;
}

export interface ExportJob {
  request: ExportRequest;
  progress: ExportProgressPayload;
}

export interface JobsState {
  /** The latest export the UI started, while running and after it ended. */
  export: Signal<ExportJob | null>;
  move: Signal<LibraryMoveProgressPayload | null>;
  reclaim: Signal<StorageReclaimProgressPayload | null>;
  /** Import audio or video failed: shown inline in the Library until dismissed (DESIGN.md §17). */
  importError: Signal<string | null>;
}

const registry = new WeakMap<AppStore, JobsState>();
const requests = new WeakMap<AppStore, Map<string, ExportRequest>>();
/**
 * export.progress that came before export.run's answer was handled (a small export can finish in a
 * few milliseconds): kept by job id until trackExport learns the job, then handled.
 */
const early = new WeakMap<AppStore, Map<string, ExportProgressPayload>>();
const handlers = new WeakMap<AppStore, (progress: ExportProgressPayload) => void>();

export function jobsOf(store: AppStore): JobsState {
  let jobs = registry.get(store);
  if (jobs === undefined) {
    jobs = {
      export: signal<ExportJob | null>(null),
      move: signal<LibraryMoveProgressPayload | null>(null),
      reclaim: signal<StorageReclaimProgressPayload | null>(null),
      importError: signal<string | null>(null),
    };
    registry.set(store, jobs);
  }
  return jobs;
}

/** The dialog started a job: remember its choices until it ends. */
export function trackExport(store: AppStore, jobId: string, request: ExportRequest): void {
  let map = requests.get(store);
  if (map === undefined) {
    map = new Map();
    requests.set(store, map);
  }
  map.set(jobId, request);
  jobsOf(store).export.value = {
    request,
    progress: { jobId, recordingId: request.recordingId, percent: 0, currentFile: null, state: 'running', message: null, outputFolder: null, files: 0, bytes: 0 },
  };
  const before = early.get(store)?.get(jobId);
  if (before !== undefined) {
    early.get(store)?.delete(jobId);
    handlers.get(store)?.(before);
  }
}

/** "Exported Q3 planning sync" toast body: "3 files · 420 MB in E:\Exports\Q3 planning sync 2026-10-06". */
export function exportDoneBody(progress: ExportProgressPayload): string {
  const where = progress.outputFolder === null ? '' : ` in ${progress.outputFolder}`;
  return `${fileCount(progress.files)} · ${formatSize(progress.bytes)}${where}. The recording inside Memento was not changed.`;
}

/**
 * Follows the M3 job events: export progress (a toast when it ends: Open folder, or Try again and
 * Choose another folder), the library move (settings are read again when it is done) and reclaim.
 */
export function connectJobEvents(bridge: BridgeClient, store: AppStore): () => void {
  const jobs = jobsOf(store);
  const onExport = (progress: ExportProgressPayload): void => {
    const request = requests.get(store)?.get(progress.jobId) ?? null;
    if (request === null) {
      // Not tracked yet (export.run has not answered): keep the latest word on it for trackExport.
      let map = early.get(store);
      if (map === undefined) {
        map = new Map();
        early.set(store, map);
      }
      map.set(progress.jobId, progress);
      return;
    }
    jobs.export.value = { request, progress };
    if (progress.state === 'running') {
      return;
    }
    requests.get(store)?.delete(progress.jobId);
    if (progress.state === 'done') {
      store.toasts.show({
        key: `export:${progress.jobId}`,
        tone: 'ok',
        title: `Exported ${request.title}`,
        body: exportDoneBody(progress),
        actions: [
          {
            label: 'Open folder',
            run: () => {
              bridge.call('export.openFolder', { jobId: progress.jobId }).catch((error: unknown) => {
                store.toasts.show({
                  tone: 'warning',
                  title: 'The folder could not be opened',
                  body: `${error instanceof Error ? error.message : 'Memento did not answer.'} The exported files are where they were written.`,
                });
              });
            },
          },
          { label: 'Dismiss', quiet: true, run: () => undefined },
        ],
      });
    } else if (progress.state === 'failed') {
      const message = progress.message ?? `The export of ${request.title} stopped. Nothing inside Memento was changed.`;
      const reopen = (pickFolder: boolean): void => {
        store.dialog.value = {
          kind: 'export',
          recordingId: request.recordingId,
          retry: { selection: request.selection, destination: request.destination, message, pickFolder },
        };
      };
      store.toasts.show({
        key: `export:${progress.jobId}`,
        tone: 'danger',
        title: `${request.title} was not exported`,
        body: message,
        actions: [
          {
            label: 'Try again',
            run: () => {
              reopen(false);
            },
          },
          {
            label: 'Choose another folder',
            run: () => {
              reopen(true);
            },
          },
        ],
      });
    }
  };
  handlers.set(store, onExport);
  const offs = [
    bridge.on('export.progress', onExport),
    bridge.on('library.moveProgress', (progress) => {
      jobs.move.value = progress;
      if (progress.state === 'done') {
        bridge
          .call('settings.get')
          .then((settings) => {
            store.settings.value = settings;
          })
          .catch((error: unknown) => {
            console.warn('[library] settings could not be read after the move', error);
          });
      }
    }),
    bridge.on('storage.reclaimProgress', (progress) => {
      jobs.reclaim.value = progress;
    }),
  ];
  return () => {
    for (const off of offs) {
      off();
    }
  };
}
