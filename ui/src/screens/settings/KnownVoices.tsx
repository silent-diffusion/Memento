// Settings › Speakers › Known voices (DESIGN.md §19, renders/Features20.dc.html): each voice Memento learned from a
// name confirmed in Review, with its dot, name, "Confirmed in n recordings · last {date}", a "Suggest this voice"
// switch and a ghost Forget; then the footnote and Forget all (asked once in place, since it cannot be undone).
// Voices are signatures from the voice model, never audio, and stay on this PC (BRIDGE.md "Review (2.0)").
import type { JSX } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import type { KnownVoiceInfo, KnownVoicesResult } from '../../bridge/types';
import { Toggle } from '../../components/Controls';
import { speakerColourVar } from '../../format/transcript';
import { formatShortDate, parseIso } from '../../format/when';
import { useServices } from '../../state/context';
import './voices.css';

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

/** "Confirmed in 4 recordings · last Oct 5". */
export function voiceSubline(voice: KnownVoiceInfo, now: Date): string {
  const count = `Confirmed in ${voice.recordings} ${voice.recordings === 1 ? 'recording' : 'recordings'}`;
  return `${count} · last ${formatShortDate(parseIso(voice.lastConfirmedAt), now)}`;
}

export function KnownVoices(): JSX.Element {
  const services = useServices();
  const { bridge } = services;
  const remember = services.store.settings.value?.speakers.rememberVoices ?? false;
  const [state, setState] = useState<KnownVoicesResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let live = true;
    bridge
      .call('voices.list')
      .then((result) => {
        if (live) {
          setState(result);
          setError(null);
        }
      })
      .catch((e: unknown) => {
        if (live) {
          setError(messageOf(e));
        }
      });
    return () => {
      live = false;
    };
  }, [bridge, remember]);

  const run = (call: () => Promise<KnownVoicesResult>): void => {
    setBusy(true);
    call()
      .then((result) => {
        setState(result);
        setError(null);
      })
      .catch((e: unknown) => {
        setError(messageOf(e));
      })
      .finally(() => {
        setBusy(false);
        setConfirming(false);
      });
  };

  const voices = state?.voices ?? [];
  const now = services.now();
  return (
    <div class="settings-row known-voices" aria-busy={state === null}>
      <div class="known-head">
        <span class="settings-row-label">Known voices</span>
        <span class="known-head-note">Stored on this PC as voice signatures, never as audio</span>
      </div>
      {state === null && error === null ? <p class="known-empty">Reading the known voices…</p> : null}
      {state !== null && voices.length === 0 ? (
        <p class="known-empty">
          {remember
            ? 'No voices yet. Name a speaker in Review and Memento learns their voice.'
            : 'No voices yet. Turn on Remember speakers by voice, then name a speaker in Review.'}
        </p>
      ) : null}
      {voices.length > 0 ? (
        <ul class="known-list" aria-label="Known voices">
          {voices.map((voice, i) => (
            <li key={voice.id} class="known-row" data-voice-id={voice.id}>
              <span class="known-dot" aria-hidden="true" style={{ background: speakerColourVar(i + 1) }} />
              <span class="known-text">
                <span class="known-name">{voice.name}</span>
                <span class="known-sub">{voiceSubline(voice, now)}</span>
              </span>
              <Toggle
                label={`Suggest ${voice.name}'s voice in other recordings`}
                checked={voice.suggest}
                disabled={busy}
                onChange={(suggest) => {
                  run(() => bridge.call('voices.setSuggest', { voiceId: voice.id, suggest }));
                }}
              />
              <button
                class="btn g small-btn known-forget"
                type="button"
                aria-label={`Forget ${voice.name}'s voice`}
                disabled={busy}
                onClick={() => {
                  run(() => bridge.call('voices.forget', { voiceId: voice.id }));
                }}
              >
                Forget
              </button>
            </li>
          ))}
        </ul>
      ) : null}
      <p class="known-foot">A voice is learned only when you confirm a name in Review. Forgetting it removes the signature immediately.</p>
      {voices.length > 1 ? (
        confirming ? (
          <div class="known-confirm" role="group" aria-label="Forget every known voice">
            <span class="known-confirm-text">
              Forget all {voices.length} voices? Their signatures are removed from this PC at once and cannot be brought back; your recordings and the names in them stay.
            </span>
            <button
              class="btn g small-btn"
              type="button"
              onClick={() => {
                setConfirming(false);
              }}
            >
              Cancel
            </button>
            <button
              class="btn d small-btn"
              type="button"
              disabled={busy}
              onClick={() => {
                run(() => bridge.call('voices.forgetAll'));
              }}
            >
              Forget all
            </button>
          </div>
        ) : (
          <button
            class="btn link known-forget-all"
            type="button"
            onClick={() => {
              setConfirming(true);
            }}
          >
            Forget all voices…
          </button>
        )
      ) : null}
      {error === null ? null : (
        <p class="settings-inline" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
