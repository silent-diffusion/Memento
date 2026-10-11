// 2.1: what the Library's folders and Delete audio do (BRIDGE.md "2.1 — Folders, dates and Delete
// audio"). Each returns the host's own message on a refusal (shown in the dialog), or null.
import type { LibraryViewMode } from '../bridge/types';
import { formatSize } from '../format/storage';
import { reloadLibrary, updateSettings } from './actions';
import type { AppServices } from './context';
import { updateLibraryView } from './data';

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

/** The Folders / Timeline switch: shown at once, remembered in Settings for next time. */
export function setLibraryMode(services: AppServices, mode: LibraryViewMode): void {
  updateLibraryView(services.bridge, services.store, { type: 'mode', mode });
  void updateSettings(services, { general: { libraryView: mode } }).then((message) => {
    if (message !== null) {
      services.store.toasts.show({ tone: 'warning', title: 'The view was not remembered', body: `${message} The Library still shows it now.` });
    }
  });
}

/** Shows one folder (null: the root) in the Folders view. */
export function showFolder(services: AppServices, folder: string | null): void {
  updateLibraryView(services.bridge, services.store, { type: 'folder', folder });
}

/** Asks what Delete audio would free, then opens the confirmation (DESIGN.md §6.2). */
export async function requestDeleteAudio(
  services: AppServices,
  recording: { id: string; title: string; durationMs: number; hasVideo: boolean },
): Promise<void> {
  const { bridge, store } = services;
  try {
    const estimate = await bridge.call('audio.removeEstimate', { recordingIds: [recording.id], olderThanDays: null });
    store.dialog.value = {
      kind: 'deleteAudio',
      recordingId: recording.id,
      title: recording.title,
      durationMs: recording.durationMs,
      bytes: estimate.bytes,
      hasVideo: recording.hasVideo,
    };
  } catch (error) {
    store.toasts.show({ tone: 'danger', title: 'The audio could not be prepared for deletion', body: `${messageOf(error)} Nothing was deleted.` });
  }
}

export async function confirmDeleteAudio(services: AppServices, recordingId: string, title: string): Promise<string | null> {
  try {
    const { freedBytes } = await services.bridge.call('audio.remove', { recordingId });
    services.store.toasts.show({ tone: 'ok', title: 'Audio deleted', body: `“${title}” keeps its transcript and documents. ${formatSize(freedBytes)} freed.` });
  } catch (error) {
    return messageOf(error);
  }
  reloadLibrary(services);
  return null;
}

export async function moveRecordings(services: AppServices, recordingIds: string[], folder: string | null): Promise<string | null> {
  try {
    await services.bridge.call('library.moveToFolder', { recordingIds, folder });
  } catch (error) {
    return messageOf(error);
  }
  reloadLibrary(services);
  return null;
}

export async function createFolder(services: AppServices, parent: string | null, name: string): Promise<string | null> {
  try {
    const { path } = await services.bridge.call('folders.create', { parent, name });
    showFolder(services, path);
  } catch (error) {
    return messageOf(error);
  }
  return null;
}

/** Renames or moves a folder; the view follows it when it was the one shown. */
export async function changeFolder(services: AppServices, path: string, change: { name: string } | { parent: string | null }): Promise<string | null> {
  const { bridge, store } = services;
  try {
    const result = 'name' in change ? await bridge.call('folders.rename', { path, name: change.name }) : await bridge.call('folders.move', { path, parent: change.parent });
    const shown = store.libraryView.value.folder;
    if (shown !== null && (shown.toLocaleLowerCase() === path.toLocaleLowerCase() || shown.toLocaleLowerCase().startsWith(`${path.toLocaleLowerCase()}/`))) {
      showFolder(services, result.path + shown.slice(path.length));
    }
  } catch (error) {
    return messageOf(error);
  }
  reloadLibrary(services);
  return null;
}

export async function deleteFolder(services: AppServices, path: string, parent: string | null): Promise<string | null> {
  const { bridge, store } = services;
  try {
    await bridge.call('folders.delete', { path });
  } catch (error) {
    return messageOf(error);
  }
  const shown = store.libraryView.value.folder;
  if (shown !== null && (shown.toLocaleLowerCase() === path.toLocaleLowerCase() || shown.toLocaleLowerCase().startsWith(`${path.toLocaleLowerCase()}/`))) {
    showFolder(services, parent);
  }
  reloadLibrary(services);
  return null;
}
