import type { JSX } from 'preact';
import { lowSpaceCopy } from '../format/messages';
import { openExternal } from '../state/actions';
import { useServices } from '../state/context';
import { InfoIcon } from './icons';

/** Windows storage settings, where Storage Sense and cleanup recommendations live. */
export const STORAGE_SETTINGS_URL = 'ms-settings:storagesense';

/**
 * The banner slot at the top of a content column (DESIGN.md §5.19). Shows conditions that are still
 * safe and stay until resolved: low disk space today.
 */
export function BannerSlot(): JSX.Element | null {
  const services = useServices();
  const lowSpace = services.store.lowSpace.value;
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
