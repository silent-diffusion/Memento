// How settings.set merges a patch into the snapshot (BRIDGE.md › settings.set and the M2/M3
// settings sections): the UI's optimistic copy and the browser-preview host both use it, so they
// agree with the host. `recording` is replaced whole; the M2 and M3 blocks merge field by field,
// where a missing or null field keeps its value; `export.defaultFolder: null` and
// `storage.reclaimOlderThanDays: null` clear theirs; `export.defaults` is replaced whole.
import type { ExportSettings, SettingsSetParams, SettingsSnapshot } from './types';

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
        : // The providers are read-only here; only ai.setKey and ai.clearKey change them.
          { ...before.ai, ...definedFields(patch.ai), share: { ...before.ai.share, ...definedFields(patch.ai.share) }, providers: before.ai.providers },
    storage:
      patch.storage != null && 'reclaimOlderThanDays' in patch.storage ? { reclaimOlderThanDays: patch.storage.reclaimOlderThanDays ?? null } : before.storage,
  };
}
