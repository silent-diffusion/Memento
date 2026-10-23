// Settings › AI and privacy › Your own server (2.5, DESIGN.md §11): an OpenAI-compatible server the user runs (Ollama,
// LM Studio, vLLM, llama.cpp's server). The address is saved as it is typed (on Enter or leaving the field); Test
// connection asks that address which models it serves (ai.serverProbe: GET /v1/models, nothing else) and fills the
// model list; the model, the context size and the optional key are saved like the cloud services' settings. Under the
// card, one line says where the server is and what that needs. Nothing is sent from here.
import type { JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import { modelIdProblem } from '../../bridge/cloudModels';
import { clampContext, SERVER_CONTEXT_MAX, SERVER_CONTEXT_MIN, withKey } from '../../bridge/serverSettings';
import type { AiServerProbeResult, AiServerStatus } from '../../bridge/types';
import { SelectMenu } from '../../components/Menus';
import { readServerAddress, serverPlace } from '../../format/serverAddress';
import { updateSettings } from '../../state/actions';
import { useServices } from '../../state/context';
import type { AppStore } from '../../state/store';
import { takeServerFocus } from './serverFocus';
import { SettingsGroup, SettingsRow } from './SettingsParts';

export const SERVER_ADDRESS_HELP = 'An OpenAI-compatible server on this PC or your network: Ollama, LM Studio, vLLM, llama.cpp.';
export const SERVER_CONTEXT_NOTE = "What the server actually runs. Ollama's default is 4096 unless num_ctx was raised; a transcript is split to fit a quarter of this.";
const MASKED_KEY = '••••••••••••';
const OTHER = '\u0000other';
const NONE = '\u0000none';

/** The last answer of Test connection, per store, so the model list survives a trip to the Builder and back. */
const probes = new WeakMap<AppStore, { address: string; result: AiServerProbeResult }>();

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

const count = (n: number): string => n.toLocaleString('en-US');

/** "Ollama 0.12.3 · 5 models", or the §17 sentence when the server did not answer as one. */
export function probeWords(result: AiServerProbeResult, address: string): string {
  const hostPort = readServerAddress(address)?.hostPort ?? address.trim();
  if (!result.reachable) {
    if (result.detail !== null && result.detail !== '') {
      return result.detail;
    }
    switch (result.code) {
      case 'ai.serverInsecure':
        return `${hostPort} is a public address over plain http, so a key or a transcript sent to it could be read on the way. Memento does not send to it. Use the server's https address.`;
      case 'ai.serverNeedsKey':
        return `The server at ${hostPort} asks for a key. Add it under Key below, then test again. Nothing was sent.`;
      case 'ai.serverNotCompatible':
        return `Something answered at ${hostPort}, but not as an OpenAI-compatible server (it has no /v1/models). Check the address and port. Nothing was sent.`;
      default:
        return `Nothing answered at ${hostPort}. Check that the server is running and that the address and port are right, then test again. Nothing was sent.`;
    }
  }
  const name = result.serverName ?? 'An OpenAI-compatible server';
  const n = result.models.length;
  return n === 0 ? `${name} · no models yet. Pull or load one on the server, then test again.` : `${name} · ${String(n)} ${n === 1 ? 'model' : 'models'}`;
}

/** The line under the card: where the server is and what sending to it needs. */
export function placeWords(server: AiServerStatus): string | null {
  if (server.address === null) {
    return null;
  }
  switch (serverPlace(server, server.address)) {
    case 'loopback':
      return 'On this PC · nothing leaves it';
    case 'network':
      return 'On your network · needs Allow external AI services';
    case 'remote':
      return 'Remote · needs Allow external AI services and https';
  }
}

export const INSECURE_WARNING =
  'Plain http to a public address: a key or a transcript sent to it could be read on the way, so Memento does not send to it. Use the server’s https address.';

export function ServerSettings(): JSX.Element | null {
  const services = useServices();
  const { bridge, store } = services;
  const server = store.settings.value?.ai.providers.server ?? null;
  const savedAddress = server?.address ?? null;
  const savedModel = server?.model ?? null;
  const savedContext = server?.contextTokens ?? null;
  const [address, setAddress] = useState(savedAddress ?? '');
  const [addressMessage, setAddressMessage] = useState<string | null>(null);
  const [testing, setTesting] = useState(false);
  const [probe, setProbe] = useState<{ address: string; result: AiServerProbeResult } | null>(() => probes.get(store) ?? null);
  const [probeError, setProbeError] = useState<string | null>(null);
  const [other, setOther] = useState(false);
  const [typedModel, setTypedModel] = useState('');
  const [modelMessage, setModelMessage] = useState<string | null>(null);
  const [context, setContext] = useState(savedContext === null ? '' : String(savedContext));
  const [contextMessage, setContextMessage] = useState<{ text: string; tone: 'alert' | 'quiet' } | null>(null);
  const [keyMessage, setKeyMessage] = useState<string | null>(null);
  const addressRef = useRef<HTMLInputElement | null>(null);
  const modelRef = useRef<HTMLDivElement | null>(null);
  const keyRef = useRef<HTMLDivElement | null>(null);

  // A value saved elsewhere (or a refused save put back, or the host's tidied address) shows in the fields.
  useEffect(() => {
    if (document.activeElement !== addressRef.current) {
      setAddress(savedAddress ?? '');
    }
  }, [savedAddress]);
  useEffect(() => {
    setContext(savedContext === null ? '' : String(savedContext));
  }, [savedContext]);

  // The Builder's "Set the address" / "Choose a model" / "Add the key" bring the field into view and focus it.
  useEffect(() => {
    const target = takeServerFocus(store);
    const el =
      target === 'address' ? addressRef.current : target === 'model' ? modelRef.current?.querySelector<HTMLElement>('button, input') : target === 'key' ? keyRef.current?.querySelector<HTMLElement>('button') : null;
    if (el != null) {
      // jsdom has no scrollIntoView.
      if (typeof Element.prototype.scrollIntoView === 'function') {
        el.scrollIntoView({ block: 'center' });
      }
      el.focus();
    }
  }, [store]);

  if (server === null) {
    return null;
  }

  const save = (patch: { address?: string | null; model?: string | null; contextTokens?: number }, onError: (message: string | null) => void): void => {
    void updateSettings(services, { ai: { providers: { server: patch } } }).then(onError);
  };

  const saveAddress = (): void => {
    const typed = address.trim();
    if (typed !== '' && readServerAddress(typed) === null) {
      setAddressMessage(`'${typed}' is not a server address. Type it like http://localhost:11434. Nothing was changed.`);
      return;
    }
    setAddressMessage(null);
    const next = typed === '' ? null : typed;
    if (next !== savedAddress) {
      save({ address: next }, setAddressMessage);
    }
  };

  const test = (): void => {
    const typed = address.trim();
    if (typed === '' || testing) {
      return;
    }
    saveAddress();
    setTesting(true);
    setProbeError(null);
    bridge
      .call('ai.serverProbe', { address: typed })
      .then((result) => {
        const answer = { address: typed, result };
        probes.set(store, answer);
        setProbe(answer);
      })
      .catch((e: unknown) => {
        setProbe(null);
        setProbeError(messageOf(e));
      })
      .finally(() => {
        setTesting(false);
      });
  };

  // The probe's models belong to the address it asked; another address lists only what is saved.
  const listed = probe !== null && probe.result.reachable && probe.address === (savedAddress ?? address.trim()) ? probe.result.models : [];
  const modelOptions = [
    ...(savedModel === null ? [{ value: NONE, label: 'Not chosen' }] : []),
    ...listed.map((m) => ({ value: m, label: m })),
    ...(savedModel !== null && !listed.includes(savedModel) ? [{ value: savedModel, label: savedModel }] : []),
    { value: OTHER, label: 'Other model…' },
  ];
  const chooseModel = (value: string): void => {
    setModelMessage(null);
    if (value === OTHER) {
      setOther(true);
      setTypedModel(savedModel !== null && !listed.includes(savedModel) ? savedModel : '');
      return;
    }
    if (value === NONE) {
      return;
    }
    setOther(false);
    save({ model: value }, setModelMessage);
  };
  const saveTyped = (): void => {
    const problem = modelIdProblem(typedModel, 'qwen3:8b');
    if (problem !== null) {
      setModelMessage(problem);
      return;
    }
    setModelMessage(null);
    save({ model: typedModel.trim() }, setModelMessage);
  };

  const saveContext = (): void => {
    const typed = context.trim();
    const clamped = typed === '' ? null : clampContext(typed);
    if (clamped === null) {
      setContextMessage({ text: `Type the context size in tokens, for example 8192. Nothing was changed.`, tone: 'alert' });
      return;
    }
    const asTyped = Number(typed.replace(/[\s,_]/g, ''));
    setContext(String(clamped));
    setContextMessage(
      clamped !== asTyped
        ? { text: `A context runs from ${count(SERVER_CONTEXT_MIN)} to ${count(SERVER_CONTEXT_MAX)} tokens, so ${count(clamped)} was saved.`, tone: 'quiet' }
        : null,
    );
    if (clamped !== savedContext) {
      save({ contextTokens: clamped }, (message) => {
        if (message !== null) {
          setContextMessage({ text: message, tone: 'alert' });
        }
      });
    }
  };

  const removeKey = (): void => {
    setKeyMessage(null);
    bridge
      .call('ai.clearKey', { provider: 'server' })
      .then(({ hasKey }) => {
        const current = store.settings.value;
        if (current !== null) {
          store.settings.value = { ...current, ai: { ...current.ai, providers: withKey(current.ai.providers, 'server', hasKey) } };
        }
      })
      .catch((e: unknown) => {
        setKeyMessage(`The key for your own server was not removed. ${messageOf(e)}`);
      });
  };

  const place = placeWords(server);
  const probeLine = probeError ?? (probe !== null && probe.address === address.trim() ? probeWords(probe.result, probe.address) : null);
  const probeOk = probeError === null && probe?.result.reachable === true;
  const unlisted = savedModel !== null && listed.length > 0 && !listed.includes(savedModel);

  return (
    <SettingsGroup
      label="Your own server"
      after={
        place === null ? null : (
          <div class="server-place-lines">
            <p class="server-place" role="status" data-place={serverPlace(server, server.address)}>
              {place}
            </p>
            {server.insecure ? (
              <p class="server-place server-place--warn" role="alert">
                {INSECURE_WARNING}
              </p>
            ) : null}
          </div>
        )
      }
    >
      <SettingsRow
        label="Address"
        description={SERVER_ADDRESS_HELP}
        below={
          <>
            {probeLine === null ? null : (
              <p class={probeOk ? 'settings-inline settings-inline--ok server-probe' : 'settings-inline server-probe'} role={probeOk ? 'status' : 'alert'} data-code={probe?.result.code ?? undefined}>
                {probeLine}
              </p>
            )}
            {addressMessage === null ? null : (
              <p class="settings-inline" role="alert">
                {addressMessage}
              </p>
            )}
          </>
        }
      >
        <label class="sr" for="server-address">
          Server address
        </label>
        <input
          ref={addressRef}
          id="server-address"
          class="field mono server-address"
          type="text"
          inputMode="url"
          spellcheck={false}
          autocomplete="off"
          placeholder="http://localhost:11434"
          value={address}
          aria-invalid={addressMessage !== null}
          onInput={(event) => {
            setAddress(event.currentTarget.value);
            setAddressMessage(null);
          }}
          onBlur={saveAddress}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              event.preventDefault();
              saveAddress();
            }
          }}
        />
        <button class="btn ghost" type="button" disabled={testing || address.trim() === ''} onClick={test}>
          {testing ? 'Testing…' : 'Test connection'}
        </button>
      </SettingsRow>
      <SettingsRow
        label="Model"
        description="Every request to your own server names this model, and no other is tried in its place."
        below={
          <>
            {other ? (
              <div class="model-other">
                <label class="sr" for="server-model-other">
                  Server model id
                </label>
                <input
                  id="server-model-other"
                  class="field mono model-other-field"
                  type="text"
                  spellcheck={false}
                  autocomplete="off"
                  placeholder="qwen3:8b"
                  value={typedModel}
                  aria-invalid={modelMessage !== null}
                  aria-describedby="server-model-note"
                  onInput={(event) => {
                    setTypedModel(event.currentTarget.value);
                    setModelMessage(null);
                  }}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') {
                      event.preventDefault();
                      saveTyped();
                    }
                  }}
                />
                <button class="btn ghost" type="button" disabled={typedModel.trim() === '' || typedModel.trim() === savedModel} onClick={saveTyped}>
                  Use this model
                </button>
              </div>
            ) : null}
            <p id="server-model-note" class="cloud-model-in-use" role="status">
              {savedModel === null ? (
                <>Test the connection to list the server’s models, or type one under Other model….</>
              ) : (
                <>
                  Requests go to <span class="mono">{savedModel}</span>.{unlisted ? <> <span class="model-unlisted">The server did not list it at the last test.</span></> : null}
                </>
              )}
            </p>
            {modelMessage === null ? null : (
              <p class="settings-inline" role="alert">
                {modelMessage}
              </p>
            )}
          </>
        }
      >
        <div ref={modelRef} class="server-model">
          <SelectMenu<string> label="Server model" value={other ? OTHER : (savedModel ?? NONE)} options={modelOptions} onChange={chooseModel} />
        </div>
      </SettingsRow>
      <SettingsRow
        label="Context size"
        description={SERVER_CONTEXT_NOTE}
        below={
          contextMessage === null ? undefined : (
            <p class={contextMessage.tone === 'alert' ? 'settings-inline' : 'settings-inline settings-inline--quiet'} role={contextMessage.tone === 'alert' ? 'alert' : 'status'}>
              {contextMessage.text}
            </p>
          )
        }
      >
        <label class="sr" for="server-context">
          Context size in tokens
        </label>
        <input
          id="server-context"
          class="field mono server-context"
          type="number"
          inputMode="numeric"
          min={SERVER_CONTEXT_MIN}
          max={SERVER_CONTEXT_MAX}
          step={1024}
          value={context}
          onInput={(event) => {
            setContext(event.currentTarget.value);
            setContextMessage(null);
          }}
          onBlur={saveContext}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              event.preventDefault();
              saveContext();
            }
          }}
        />
        <span class="settings-note">tokens</span>
      </SettingsRow>
      <SettingsRow
        label="Key"
        description={server.hasKey ? 'Sent only to this server, as a bearer token.' : 'Optional. Only for a server that asks for one, such as vLLM started with --api-key.'}
        below={
          keyMessage === null ? undefined : (
            <p class="settings-inline" role="alert">
              {keyMessage}
            </p>
          )
        }
      >
        <div ref={keyRef} class="server-key">
          {server.hasKey ? (
            <span class="mono settings-key" aria-label="Key for your own server stored">
              {MASKED_KEY}
            </span>
          ) : (
            <span class="settings-note">No key</span>
          )}
          {server.hasKey ? (
            <button class="btn ghost" type="button" aria-label="Remove the key for your own server" onClick={removeKey}>
              Remove
            </button>
          ) : null}
          <button
            class="btn ghost"
            type="button"
            aria-haspopup="dialog"
            aria-label={server.hasKey ? 'Replace the key for your own server' : 'Add a key for your own server'}
            onClick={() => {
              store.dialog.value = { kind: 'aiKey', provider: 'server', replacing: server.hasKey };
            }}
          >
            {server.hasKey ? 'Replace' : 'Add'}
          </button>
        </div>
      </SettingsRow>
    </SettingsGroup>
  );
}
