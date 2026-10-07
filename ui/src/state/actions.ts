// User intents that touch the host. Each returns the host's message on failure so the caller can
// show it inline (dialogs, settings rows); nothing here invents an error text of its own.
import type { RecordingType, RecoveredRecording, SettingsSetParams, SettingsSnapshot } from '../bridge/types';
import type { AppServices } from './context';
import { refreshLibrary } from './data';
import type { SettingsSection } from './router';

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

export function openRecording({ store, router }: AppServices, recordingId: string): void {
  store.libraryView.value = { ...store.libraryView.value, selectedId: recordingId };
  router.navigate({ name: 'review', recordingId });
}

export function openSettings({ router }: AppServices, section: SettingsSection = 'general'): void {
  router.navigate({ name: 'settings', section });
}

export function openRecord({ store, router }: AppServices): void {
  router.navigate({ name: 'record', sessionId: store.recording.value?.sessionId ?? null });
}

export function goToLibrary({ router }: AppServices): void {
  router.navigate({ name: 'library' });
}

/** Refetches the Library, reporting a failure the same way the first load does. */
export function reloadLibrary({ bridge, store }: AppServices): void {
  refreshLibrary(bridge, store).catch((error: unknown) => {
    store.loadError.value = messageOf(error);
  });
}

/** Asks the host what deleting would remove, then opens the confirmation. */
export async function requestDelete(services: AppServices, recordingId: string): Promise<void> {
  const { bridge, store } = services;
  store.libraryView.value = { ...store.libraryView.value, selectedId: recordingId };
  try {
    const estimate = await bridge.call('project.deleteEstimate', { recordingId });
    store.dialog.value = { kind: 'delete', recordingId, estimate };
  } catch (error) {
    store.toasts.show({ tone: 'danger', title: 'The recording could not be prepared for deletion', body: `${messageOf(error)} Nothing was deleted.` });
  }
}

export async function confirmDelete(services: AppServices, recordingId: string): Promise<string | null> {
  const { bridge, store } = services;
  try {
    await bridge.call('project.delete', { recordingId });
  } catch (error) {
    return messageOf(error);
  }
  if (store.libraryView.value.selectedId === recordingId) {
    store.libraryView.value = { ...store.libraryView.value, selectedId: null };
  }
  // Deleted from its own Review: there is nothing left to show, so go back to the Library.
  const route = store.route.value;
  if (route.name === 'review' && route.recordingId === recordingId) {
    services.router.navigate({ name: 'library' });
  }
  reloadLibrary(services);
  return null;
}

export async function renameRecording(services: AppServices, recordingId: string, title: string): Promise<string | null> {
  try {
    await services.bridge.call('project.rename', { recordingId, title: title.trim() });
  } catch (error) {
    return messageOf(error);
  }
  reloadLibrary(services);
  return null;
}

export async function changeRecordingType(
  services: AppServices,
  recordingId: string,
  type: RecordingType,
): Promise<string | null> {
  try {
    // M3: project.changeType (custom types are any name of 1 to 40 characters).
    await services.bridge.call('project.changeType', { recordingId, type });
  } catch (error) {
    return messageOf(error);
  }
  reloadLibrary(services);
  return null;
}

/** Later and Open recording both dismiss the dialog for this project; the next one (if any) follows. */
export function resolveRecovery(services: AppServices, item: RecoveredRecording, open: boolean): void {
  const { bridge, store } = services;
  store.recoveryQueue.value = store.recoveryQueue.value.filter((r) => r.recordingId !== item.recordingId);
  bridge.call('recovery.acknowledge', { recordingId: item.recordingId }).catch((error: unknown) => {
    console.warn('[recovery] acknowledge failed; the dialog may show again next launch', error);
  });
  if (open) {
    openRecording(services, item.recordingId);
  }
}

/** The fields of a settings patch that change something: null and missing fields keep their value. */
function definedFields<T extends object>(patch: Partial<T> | null | undefined): Partial<T> {
  if (patch == null) {
    return {};
  }
  return Object.fromEntries(Object.entries(patch).filter(([, value]) => value !== undefined && value !== null)) as Partial<T>;
}

/**
 * Saves a settings change. The store updates first so the control answers at once; on failure it
 * returns to the host's value and the host's message comes back for the row to show.
 */
export async function updateSettings(services: AppServices, patch: SettingsSetParams): Promise<string | null> {
  const { bridge, store } = services;
  const before = store.settings.value;
  if (before !== null) {
    const optimistic: SettingsSnapshot = {
      ...before,
      theme: patch.theme ?? before.theme,
      listDensity: patch.listDensity ?? before.listDensity,
      recording: patch.recording ?? before.recording,
      // The M2 blocks merge field by field on the host; the optimistic copy does the same.
      transcription: { ...before.transcription, ...definedFields(patch.transcription) },
      speakers: { ...before.speakers, ...definedFields(patch.speakers) },
      history: { ...before.history, ...definedFields(patch.history) },
      // M3
      general: patch.general ?? before.general,
      export: patch.export ?? before.export,
      ai: patch.ai == null ? before.ai : { ...patch.ai, providers: before.ai.providers },
      storage: patch.storage ?? before.storage,
    };
    store.settings.value = optimistic;
  }
  try {
    store.settings.value = await bridge.call('settings.set', patch);
    return null;
  } catch (error) {
    store.settings.value = before;
    return messageOf(error);
  }
}

/** Opens an https: or ms-settings: page; `what` names it in the failure toast ("Storage settings"). */
export function openExternal({ bridge, store }: AppServices, url: string, what: string): void {
  bridge.call('app.openExternal', { url }).catch((error: unknown) => {
    store.toasts.show({ tone: 'warning', title: `${what} could not be opened`, body: messageOf(error) });
  });
}
