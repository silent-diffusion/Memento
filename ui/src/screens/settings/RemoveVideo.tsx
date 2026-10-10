// 2.0 video in Settings › Storage and history (DESIGN.md §18.5, renders/SettingsStorageVideo.dc.html):
// "Remove video older than" with what it would free now (video.removeEstimate), confirmed in place
// before an age that removes existing video is saved, since that cannot be undone (§18.6, §5.19).
import type { JSX } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import type { VideoRemoveEstimate } from '../../bridge/types';
import { SelectMenu } from '../../components/Menus';
import { formatSize } from '../../format/storage';
import { updateSettings } from '../../state/actions';
import { useServices } from '../../state/context';
import { SettingsRow } from './SettingsParts';

/** The ages the host accepts (BRIDGE.md: null = Never, else 30, 90, 180 or 365). */
export const REMOVE_VIDEO_DAYS = [30, 90, 180, 365] as const;

/** "30 days", "1 year". */
export function ageWords(days: number): string {
  return days === 365 ? '1 year' : `${days} days`;
}

const recordingsWord = (n: number): string => `${n} ${n === 1 ? 'recording' : 'recordings'}`;

const KEPT = 'Audio tracks, transcript and documents are kept; the Library shows "Video removed".';

/** The row's description: what the chosen age does right now. `estimate` null while it is asked for. */
export function removeVideoText(days: number | null, estimate: VideoRemoveEstimate | null): string {
  if (days === null) {
    return 'Video is kept until you remove it. When it is removed, audio tracks, transcript and documents are kept.';
  }
  if (estimate === null) {
    return KEPT;
  }
  if (estimate.recordings === 0) {
    return `No video is older than ${ageWords(days)} now. ${KEPT}`;
  }
  return `Would free ${formatSize(estimate.bytes)} now, from ${recordingsWord(estimate.recordings)}. ${KEPT}`;
}

/** The confirmation (§17, §18.6 Remove video): the age, the recordings and size, what is kept, that it cannot be undone. */
export function removeVideoConfirmText(days: number, estimate: VideoRemoveEstimate): string {
  return `Remove the video of ${recordingsWord(estimate.recordings)} older than ${ageWords(days)} (${formatSize(estimate.bytes)})? Audio tracks, transcript and documents are kept, and later recordings lose their video as they pass ${ageWords(days)}. This cannot be undone.`;
}

const OPTIONS = [{ value: 'never', label: 'Never' }, ...REMOVE_VIDEO_DAYS.map((d) => ({ value: String(d), label: ageWords(d) }))];

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

/** `reload` changes when the library changed, so the figures are asked for again. */
export function RemoveVideoRow({ reload }: { reload: number }): JSX.Element | null {
  const services = useServices();
  const { bridge, store } = services;
  const days = store.settings.value?.storage.removeVideoOlderThanDays ?? null;
  const [estimate, setEstimate] = useState<VideoRemoveEstimate | null>(null);
  // An age that would remove existing video, waiting for the confirmation.
  const [pending, setPending] = useState<{ days: number; estimate: VideoRemoveEstimate } | null>(null);
  const [asking, setAsking] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setEstimate(null);
    if (days === null) {
      return undefined;
    }
    let live = true;
    bridge
      .call('video.removeEstimate', { recordingIds: null, olderThanDays: days })
      .then((result) => {
        if (live) {
          setEstimate(result);
        }
      })
      .catch((e: unknown) => {
        if (live) {
          setError(`What it would free could not be worked out. ${messageOf(e)}`);
        }
      });
    return () => {
      live = false;
    };
  }, [bridge, days, reload]);

  const saveDays = (next: number | null): void => {
    setPending(null);
    void updateSettings(services, { storage: { removeVideoOlderThanDays: next } }).then(setError);
  };

  const choose = (value: string): void => {
    setError(null);
    if (value === 'never') {
      saveDays(null);
      return;
    }
    const next = Number(value);
    setAsking(true);
    bridge
      .call('video.removeEstimate', { recordingIds: null, olderThanDays: next })
      .then((result) => {
        if (result.recordings === 0) {
          // Nothing exists now that this age would remove: nothing to confirm.
          saveDays(next);
        } else {
          setPending({ days: next, estimate: result });
        }
      })
      .catch((e: unknown) => {
        setError(`Nothing was changed: what it would remove could not be worked out. ${messageOf(e)}`);
      })
      .finally(() => {
        setAsking(false);
      });
  };

  if (store.settings.value === null) {
    return null;
  }
  const shown = pending?.days ?? days;
  return (
    <SettingsRow
      label="Remove video older than"
      // While an age waits for its confirmation, the row speaks of that age.
      description={pending === null ? removeVideoText(days, estimate) : removeVideoText(pending.days, pending.estimate)}
      below={
        <>
          {pending === null ? null : (
            <div class="settings-confirm" role="group" aria-label="Confirm removing video">
              <p class="settings-inline">{removeVideoConfirmText(pending.days, pending.estimate)}</p>
              <div class="settings-confirm-actions">
                <button
                  class="btn g"
                  type="button"
                  onClick={() => {
                    setPending(null);
                  }}
                >
                  Cancel
                </button>
                <button
                  class="btn d"
                  type="button"
                  onClick={() => {
                    saveDays(pending.days);
                  }}
                >
                  Remove video
                </button>
              </div>
            </div>
          )}
          {error === null ? null : (
            <p class="settings-inline" role="alert">
              {error}
            </p>
          )}
        </>
      }
    >
      <SelectMenu<string>
        label="Remove video older than"
        value={shown === null ? 'never' : String(shown)}
        options={OPTIONS}
        disabled={asking}
        onChange={choose}
      />
    </SettingsRow>
  );
}
