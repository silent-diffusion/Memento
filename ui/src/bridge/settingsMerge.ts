// How settings.set merges a patch into the snapshot (BRIDGE.md › settings.set and the M2/M3
// settings sections): the UI's optimistic copy and the browser-preview host both use it, so they
// agree with the host. `recording` is replaced whole; the M2 and M3 blocks merge field by field,
// where a missing or null field keeps its value; `export.defaultFolder: null` and
// `storage.reclaimOlderThanDays: null` clear theirs; `export.defaults` is replaced whole.
import { providerState } from './cloudModels';
import type { AiProvidersPatch, AiSettings, ExportSettings, SettingsSetParams, SettingsSnapshot } from './types';

/** The cloud models: each provider named in the patch takes its model (null: the default); keys never change here. */
export function mergeProviders(before: AiSettings['providers'], patch: AiProvidersPatch | null | undefined): AiSettings['providers'] {
  if (patch == null) {
    return before;
  }
  const next = { ...before };
  for (const provider of ['anthropic', 'openai'] as const) {
    const change = patch[provider];
    if (change !== undefined && 'model' in change) {
      next[provider] = providerState(provider, before[provider].hasKey, change.model);
    }
  }
  return next;
}

/** The fields of a patch that carry a value: missing and null ones keep the stored value. */
export function definedFields<T extends object>(patch: Partial<T> | null | undefined): Partial<T> {
  if (patch == null) {
    return {};
  }
  return Object.fromEntries(Object.entries(patch).filter(([, value]) => value !== undefined && value !== null)) as Partial<T>;
}

/** The export block: `defaultFolder: null` clears the folder. */
export function mergeExport(before: ExportSettings, patch: Partial<ExportSettings> | null | undefined): ExportSettings {
  if (patch == null) {
    return before;
  }
  const merged = { ...before, ...definedFields(patch) };
  return 'defaultFolder' in patch ? { ...merged, defaultFolder: patch.defaultFolder ?? null } : merged;
}

/** `before` with every block of `patch` applied, as the host stores it. Validation is the caller's. */
export function mergeSettings(before: SettingsSnapshot, patch: SettingsSetParams): SettingsSnapshot {
  return {
    ...before,
    theme: patch.theme ?? before.theme,
    listDensity: patch.listDensity ?? before.listDensity,
    recording: patch.recording ?? before.recording,
    transcription: { ...before.transcription, ...definedFields(patch.transcription) },
    speakers: { ...before.speakers, ...definedFields(patch.speakers) },
    history: { ...before.history, ...definedFields(patch.history) },
    general: { ...before.general, ...definedFields(patch.general) },
    export: mergeExport(before.export, patch.export),
    ai:
      patch.ai == null
        ? before.ai
        : // Only the providers' models change here; ai.setKey and ai.clearKey change the keys.
          {
            ...before.ai,
            ...definedFields(patch.ai),
            share: { ...before.ai.share, ...definedFields(patch.ai.share) },
            providers: mergeProviders(before.ai.providers, patch.ai.providers),
            // M4: a null default provider clears it.
            defaultProviderId: 'defaultProviderId' in patch.ai ? (patch.ai.defaultProviderId ?? null) : before.ai.defaultProviderId,
          },
    // Field by field; a null age clears it (Never), for making smaller and (2.0) for removing video.
    storage:
      patch.storage == null
        ? before.storage
        : {
            reclaimOlderThanDays: 'reclaimOlderThanDays' in patch.storage ? (patch.storage.reclaimOlderThanDays ?? null) : before.storage.reclaimOlderThanDays,
            removeVideoOlderThanDays: 'removeVideoOlderThanDays' in patch.storage ? (patch.storage.removeVideoOlderThanDays ?? null) : before.storage.removeVideoOlderThanDays,
            deleteAudioOlderThanDays: 'deleteAudioOlderThanDays' in patch.storage ? (patch.storage.deleteAudioOlderThanDays ?? null) : before.storage.deleteAudioOlderThanDays,
          },
    // M4
    documents: { ...before.documents, ...definedFields(patch.documents) },
  };
}
