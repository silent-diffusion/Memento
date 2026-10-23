// Small Settings dialogs (DESIGN.md §5.19, §11): adding or replacing an AI provider key (a password
// field; the key is never shown again) and confirming a library move (copy, verify, then delete).
import type { JSX } from 'preact';
import { useState } from 'preact/hooks';
import { withKey } from '../../bridge/serverSettings';
import type { AiKeyProvider } from '../../bridge/types';
import { Dialog } from '../../components/Overlay';
import { useServices } from '../../state/context';
import type { DialogRequest } from '../../state/dialogs';
import { jobsOf } from '../../state/jobs';

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

export const PROVIDER_NAMES: Record<AiKeyProvider, string> = {
  anthropic: 'Claude (Anthropic)',
  openai: 'ChatGPT (OpenAI)',
  server: 'Your own server',
};

const PROVIDER_SHORT: Record<AiKeyProvider, string> = { anthropic: 'Claude', openai: 'ChatGPT', server: 'your own server' };

/** "Add a Claude key", "Replace the key for your own server". */
function keyTitle(provider: AiKeyProvider, replacing: boolean): string {
  if (provider === 'server') {
    return replacing ? 'Replace the key for your own server' : 'Add a key for your own server';
  }
  return replacing ? `Replace the ${PROVIDER_SHORT[provider]} key` : `Add a ${PROVIDER_SHORT[provider]} key`;
}

export function AiKeyDialog({ request, close }: { request: Extract<DialogRequest, { kind: 'aiKey' }>; close: () => void }): JSX.Element {
  const { bridge, store } = useServices();
  const [key, setKey] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const server = request.provider === 'server';
  const save = (): void => {
    if (key.trim() === '' || busy) {
      return;
    }
    setBusy(true);
    setError(null);
    bridge
      .call('ai.setKey', { provider: request.provider, key: key.trim() })
      .then(({ hasKey }) => {
        const settings = store.settings.value;
        if (settings !== null) {
          store.settings.value = { ...settings, ai: { ...settings.ai, providers: withKey(settings.ai.providers, request.provider, hasKey) } };
        }
        setKey('');
        close();
      })
      .catch((e: unknown) => {
        setBusy(false);
        setError(messageOf(e));
      });
  };
  return (
    <Dialog
      titleId="ai-key-title"
      title={keyTitle(request.provider, request.replacing)}
      onEscape={close}
      actions={
        <>
          <button class="btn g" type="button" onClick={close}>
            Cancel
          </button>
          <button class="btn p" type="submit" form="ai-key-form" disabled={key.trim() === '' || busy}>
            {request.replacing ? 'Replace key' : 'Save key'}
          </button>
        </>
      }
    >
      <p class="dialog-body">
        {server
          ? 'The key is stored on this PC, encrypted for your Windows account, and is never shown again. It is sent only to your own server’s address, as a bearer token.'
          : 'The key is stored on this PC, encrypted for your Windows account, and is never shown again. It is used only while external AI is allowed, and you are asked before anything is sent.'}
        {request.replacing ? ' The key stored now is replaced.' : ''}
      </p>
      <form
        id="ai-key-form"
        class="dialog-form"
        onSubmit={(event) => {
          event.preventDefault();
          save();
        }}
      >
        <label class="field-label" for="ai-key-input">
          {server ? 'Key for your own server' : `${PROVIDER_NAMES[request.provider]} API key`}
        </label>
        <input
          id="ai-key-input"
          class="field dialog-field"
          type="password"
          autocomplete="off"
          spellcheck={false}
          placeholder="Paste the API key"
          data-autofocus
          value={key}
          onInput={(event) => {
            setKey(event.currentTarget.value);
          }}
        />
        <p class="field-note">
          {server
            ? 'The key the server asks for (for vLLM, the one given with --api-key). Memento checks only that it looks complete.'
            : 'Create a key in the provider’s console. Memento checks only that it looks complete.'}
        </p>
      </form>
      {error === null ? null : (
        <p class="dialog-error" role="alert">
          {error}
        </p>
      )}
    </Dialog>
  );
}

export function libraryMoveCopy(from: string, to: string): string {
  return `Memento copies every recording from ${from} to ${to}, checks each copied file against its SHA-256 hash, switches to the new folder, and only then deletes the old one. If any file does not match, the old folder is kept and nothing is deleted. Recording and processing wait until the move is done.`;
}

export function LibraryMoveDialog({ request, close }: { request: Extract<DialogRequest, { kind: 'libraryMove' }>; close: () => void }): JSX.Element {
  const { bridge, store } = useServices();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  return (
    <Dialog
      titleId="library-move-title"
      title="Move the library?"
      onEscape={close}
      actions={
        <>
          <button class="btn g" type="button" data-autofocus onClick={close}>
            Cancel
          </button>
          <button
            class="btn p"
            type="button"
            disabled={busy}
            onClick={() => {
              setBusy(true);
              setError(null);
              bridge
                .call('library.move', { newPath: request.to })
                .then(({ jobId }) => {
                  jobsOf(store).move.value = { jobId, percent: 0, state: 'running', message: null, newPath: request.to };
                  close();
                })
                .catch((e: unknown) => {
                  setBusy(false);
                  setError(messageOf(e));
                });
            }}
          >
            Move library
          </button>
        </>
      }
    >
      <p class="dialog-body">{libraryMoveCopy(request.from, request.to)}</p>
      <dl class="move-paths">
        <dt>From</dt>
        <dd class="mono">{request.from}</dd>
        <dt>To</dt>
        <dd class="mono">{request.to}</dd>
      </dl>
      {error === null ? null : (
        <p class="dialog-error" role="alert">
          {error}
        </p>
      )}
    </Dialog>
  );
}
