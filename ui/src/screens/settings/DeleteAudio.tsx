// 2.1, Settings › Storage and history › Delete audio tracks older than (DESIGN.md §11, §6.2): the
// same row as Remove video older than: what the chosen age would free now (audio.removeEstimate),
// confirmed in place before an age that deletes existing audio is saved, since it cannot be undone.
import type { JSX } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import type { AudioRemoveEstimate } from '../../bridge/types';
import { SelectMenu } from '../../components/Menus';
import { formatSize } from '../../format/storage';
import { updateSettings } from '../../state/actions';
import { useServices } from '../../state/context';
import { SettingsRow } from './SettingsParts';

/** Exactly the ages the host accepts (BRIDGE.md 2.1); null is Never. */
export const DELETE_AUDIO_DAYS = [1, 7, 30, 90, 365, 730, 1095, 1460, 1825] as const;

/** "a day", "a week", "a month", "90 days", "a year", "2 years" … "5 years". */
export function audioAgeWords(days: number): string {
  switch (days) {
    case 1:
      return 'a day';
    case 7:
      return 'a week';
    case 30:
      return 'a month';
    case 365:
      return 'a year';
    default:
      return days % 365 === 0 ? `${days / 365} years` : `${days} days`;
  }
}

const recordingsWord = (n: number): string => `${n} ${n === 1 ? 'recording' : 'recordings'}`;

const KEPT = 'The transcript, documents and video are kept; the recording stays in the Library to read.';

/** The row's description: what the chosen age does right now. `estimate` null while it is asked for. */
export function deleteAudioText(days: number | null, estimate: AudioRemoveEstimate | null): string {
  if (days === null) {
    return 'Audio is kept until you delete it. When it is deleted, the transcript, documents and video are kept.';
  }
  if (estimate === null) {
    return KEPT;
  }
  if (estimate.recordings === 0) {
    return `No audio is older than ${audioAgeWords(days)} now. ${KEPT}`;
  }
  return `Would free ${formatSize(estimate.bytes)} now, from ${recordingsWord(estimate.recordings)}. ${KEPT}`;
}

/** The confirmation (§17): the age, the recordings and size, what is kept, that it cannot be undone. */
export function deleteAudioConfirmText(days: number, estimate: AudioRemoveEstimate): string {
  return `Delete the audio tracks and mix of ${recordingsWord(estimate.recordings)} older than ${audioAgeWords(days)} (${formatSize(estimate.bytes)})? Their transcripts, documents and any video are kept, and later recordings lose their audio as they pass ${audioAgeWords(days)}. The audio can’t be played, exported or transcribed again. This cannot be undone.`;
}

const OPTIONS = [{ value: 'never', label: 'Never' }, ...DELETE_AUDIO_DAYS.map((d) => ({ value: String(d), label: audioAgeWords(d) }))];

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

/** `reload` changes when the library changed, so the figures are asked for again. */
export function DeleteAudioRow({ reload }: { reload: number }): JSX.Element | null {
  const services = useServices();
  const { bridge, store } = services;
  const days = store.settings.value?.storage.deleteAudioOlderThanDays ?? null;
  const [estimate, setEstimate] = useState<AudioRemoveEstimate | null>(null);
  const [pending, setPending] = useState<{ days: number; estimate: AudioRemoveEstimate } | null>(null);
  const [asking, setAsking] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setEstimate(null);
    if (days === null) {
      return undefined;
    }
    let live = true;
    bridge
      .call('audio.removeEstimate', { recordingIds: null, olderThanDays: days })
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
    void updateSettings(services, { storage: { deleteAudioOlderThanDays: next } }).then(setError);
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
      .call('audio.removeEstimate', { recordingIds: null, olderThanDays: next })
      .then((result) => {
        if (result.recordings === 0) {
          saveDays(next);
        } else {
          setPending({ days: next, estimate: result });
        }
      })
      .catch((e: unknown) => {
        setError(`Nothing was changed: what it would delete could not be worked out. ${messageOf(e)}`);
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
      label="Delete audio tracks older than"
      description={pending === null ? deleteAudioText(days, estimate) : deleteAudioText(pending.days, pending.estimate)}
      below={
        <>
          {pending === null ? null : (
            <div class="settings-confirm" role="group" aria-label="Confirm deleting audio">
              <p class="settings-inline">{deleteAudioConfirmText(pending.days, pending.estimate)}</p>
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
                  Delete audio
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
        label="Delete audio tracks older than"
        value={shown === null ? 'never' : String(shown)}
        options={OPTIONS}
        disabled={asking}
        onChange={choose}
      />
    </SettingsRow>
  );
}
