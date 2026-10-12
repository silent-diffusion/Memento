// Settings › AI and privacy › Providers (after 2.1): which model each cloud provider is sent, under its key. The models
// Memento lists (from the settings snapshot) each with a short note, the default first, then "Other model…" for an id
// typed by hand: checked as it is saved (the host checks it too), saved as typed, and marked "not in Memento's list".
// Nothing is sent from here; the model is named wherever a request is made or described.
import type { JSX } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import { modelIdProblem } from '../../bridge/cloudModels';
import type { AiProvider } from '../../bridge/types';
import { updateSettings } from '../../state/actions';
import { useServices } from '../../state/context';
import { SettingsRow } from './SettingsParts';

const SHORT: Record<AiProvider, string> = {
  anthropic: 'Claude',
  openai: 'ChatGPT',
};

const OTHER = 'other';

export function CloudModelSetting({ provider }: { provider: AiProvider }): JSX.Element | null {
  const services = useServices();
  const settings = services.store.settings.value;
  const state = settings?.ai.providers[provider] ?? null;
  const [other, setOther] = useState(state?.custom ?? false);
  const [typed, setTyped] = useState(state?.custom === true ? state.model : '');
  const [message, setMessage] = useState<string | null>(null);
  const saved = state?.model ?? null;
  const custom = state?.custom ?? false;

  // A model saved elsewhere (or a refused save put back) shows here: "Other model…" stays open for a typed one.
  useEffect(() => {
    if (custom && saved !== null) {
      setOther(true);
      setTyped(saved);
    }
  }, [custom, saved]);

  if (state === null || state.choices.length === 0) {
    return null;
  }
  const name = SHORT[provider];
  const group = `${provider}-model`;
  const fieldId = `${provider}-model-other`;
  const choose = (model: string | null): void => {
    setMessage(null);
    void updateSettings(services, {
      ai: { providers: { [provider]: { model } } },
    }).then(setMessage);
  };
  const useTyped = (): void => {
    const problem = modelIdProblem(typed);
    if (problem !== null) {
      setMessage(problem);
      return;
    }
    choose(typed.trim());
  };
  const firstId = state.choices[0]?.id;
  return (
    <SettingsRow
      label={`${name} model`}
      description={`Every request to ${name} names this model, and no other is tried in its place.`}
      below={
        <>
          <div class="model-choices" role="radiogroup" aria-label={`${name} model`}>
            {state.choices.map((choice) => (
              <label key={choice.id} class={!other && state.model === choice.id ? 'model-choice on' : 'model-choice'}>
                <input
                  class="chk"
                  type="radio"
                  name={group}
                  value={choice.id}
                  checked={!other && state.model === choice.id}
                  onChange={() => {
                    setOther(false);
                    // The list's first is Memento's default: saved as "the default", so it follows a later default.
                    choose(choice.id === firstId ? null : choice.id);
                  }}
                />
                <span class="model-choice-text">
                  <span class="mono model-choice-id">{choice.id}</span>
                  <span class="model-choice-note">{choice.note}</span>
                </span>
              </label>
            ))}
            <label class={other ? 'model-choice on' : 'model-choice'}>
              <input
                class="chk"
                type="radio"
                name={group}
                value={OTHER}
                checked={other}
                onChange={() => {
                  setMessage(null);
                  setOther(true);
                }}
              />
              <span class="model-choice-text">
                <span class="model-choice-id">Other model…</span>
                <span class="model-choice-note">Any other model id {name}’s service offers, typed exactly as it names it.</span>
              </span>
            </label>
          </div>
          {other ? (
            <div class="model-other">
              <label class="sr" for={fieldId}>
                {`${name} model id`}
              </label>
              <input
                id={fieldId}
                class="field mono model-other-field"
                type="text"
                spellcheck={false}
                autocomplete="off"
                placeholder={provider === 'anthropic' ? 'claude-opus-4-8' : 'model id'}
                value={typed}
                aria-invalid={message !== null}
                aria-describedby={`${fieldId}-note`}
                onInput={(event) => {
                  setTyped(event.currentTarget.value);
                  setMessage(null);
                }}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') {
                    event.preventDefault();
                    useTyped();
                  }
                }}
              />
              <button class="btn ghost small-btn" type="button" disabled={typed.trim() === state.model && custom} onClick={useTyped}>
                Use this model
              </button>
            </div>
          ) : null}
          <p id={`${fieldId}-note`} class="cloud-model-in-use" role="status" data-custom={custom ? 'true' : 'false'}>
            {custom ? (
              <>
                Requests to {name} go to <span class="mono">{state.model}</span>. <span class="model-unlisted">{state.modelNote}</span> If {name} does not offer it,
                generating says so and nothing is sent to another model.
              </>
            ) : (
              <>
                Requests to {name} go to <span class="mono">{state.model}</span>.
              </>
            )}
          </p>
          {message === null ? null : (
            <p class="settings-inline" role="alert">
              {message}
            </p>
          )}
        </>
      }
    />
  );
}
