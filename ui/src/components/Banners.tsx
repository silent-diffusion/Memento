import type { JSX } from 'preact';
import type { FooterStatusPayload, StorageLowSpacePayload } from '../bridge/types';
import { lowSpaceCopy } from '../format/messages';
import { openExternal } from '../state/actions';
import { useServices } from '../state/context';
import { InfoIcon } from './icons';

/** Windows storage settings, where Storage Sense and cleanup recommendations live. */
export const STORAGE_SETTINGS_URL = 'ms-settings:storagesense';

/**
 * What the low-space banner says: the host's storage.lowSpace (sent while recording), else the footer's own reading,
 * so the banner also shows when space runs low with no recording and when Memento starts with a full drive
 * (DESIGN.md §17). Whether recording continues follows the footer, which knows when the recording ended.
 */
export function lowSpaceNow(lowSpace: StorageLowSpacePayload | null, footer: FooterStatusPayload | null): StorageLowSpacePayload | null {
  if (lowSpace !== null) {
    return footer === null ? lowSpace : { ...lowSpace, recordingContinues: footer.recording.active };
  }
  if (footer?.storage.lowSpace === true && footer.storage.freeBytes !== null) {
    return { freeBytes: footer.storage.freeBytes, thresholdBytes: 0, recordingContinues: footer.recording.active, transcriptionPaused: true };
  }
  return null;
}

/**
 * The banner slot at the top of a content column (DESIGN.md §5.19). Shows conditions that are still
 * safe and stay until resolved: low disk space today.
 */
export function BannerSlot(): JSX.Element | null {
  const services = useServices();
  const lowSpace = lowSpaceNow(services.store.lowSpace.value, services.store.footer.value);
  if (lowSpace === null) {
    return null;
  }
  const copy = lowSpaceCopy(lowSpace);
  return (
    <div class="banner" role="status">
      <InfoIcon size={18} class="banner-icon" />
      <span class="banner-text">
        <span class="banner-lead">{copy.lead}</span>
        {copy.rest}
      </span>
      <button
        class="btn banner-action"
        type="button"
        onClick={() => {
          openExternal(services, STORAGE_SETTINGS_URL, 'Storage settings');
        }}
      >
        Free up space
      </button>
    </div>
  );
}
