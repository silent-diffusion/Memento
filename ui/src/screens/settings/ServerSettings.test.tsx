import { act } from 'preact/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import type { MockOptions } from '../../bridge/mock';
import type { AiServerProbeParams, AiSetKeyParams, SettingsSetParams } from '../../bridge/types';
import { button, click, mountApp, press, type, until, type Harness } from '../../testing/appHarness';
import { requestServerFocus } from './serverFocus';
import { INSECURE_WARNING, SERVER_ADDRESS_HELP, SERVER_CONTEXT_NOTE } from './ServerSettings';

/**
 * Settings › AI and privacy › Your own server (2.5, DESIGN.md §11): the address saved as typed, Test connection and
 * the model list it fills (with Other model…), the context size, the optional key, and the line under the card that
 * says where the server is. Against the browser-preview host's pretend Ollama on localhost:11434.
 */
describe('Your own server in Settings (against the browser-preview host)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const open = async (mock: MockOptions = {}): Promise<void> => {
    h = await mountApp({ name: 'settings', section: 'ai-privacy' }, mock);
    await until(() => document.querySelector('#server-address') !== null);
  };
  const address = (): HTMLInputElement => {
    const el = document.querySelector<HTMLInputElement>('#server-address');
    if (el === null) {
      throw new Error('no address field');
    }
    return el;
  };
  const rowOf = (label: string): HTMLElement => {
    const row = [...document.querySelectorAll<HTMLElement>('.settings-row')].find((r) => r.querySelector('.settings-row-label')?.textContent === label && r.closest('.settings-group')?.querySelector('.settings-group-label')?.textContent === 'Your own server');
    if (row === undefined) {
      throw new Error(`no row ${label}`);
    }
    return row;
  };
  const serverSets = (): NonNullable<NonNullable<NonNullable<SettingsSetParams['ai']>['providers']>['server']>[] =>
    (h.callsOf('settings.set') as SettingsSetParams[]).map((p) => p.ai?.providers?.server).filter((s) => s !== undefined);
  const place = (): string => [...document.querySelectorAll('.server-place')].map((p) => p.textContent).join(' | ');
  const probeLine = (): string => document.querySelector('.server-probe')?.textContent ?? '';
  const options = (): string[] => [...document.querySelectorAll('[role="option"]')].map((o) => o.textContent.trim());
  const chooseModel = async (name: string): Promise<void> => {
    await click(document.querySelector('[aria-label^="Server model: "]'));
    await click([...document.querySelectorAll('[role="option"]')].find((o) => o.textContent.trim() === name));
  };
  const blur = async (el: HTMLElement): Promise<void> => {
    await act(async () => {
      // As leaving the field does: focus moves away (blur and focusout).
      el.focus();
      el.blur();
      await Promise.resolve();
    });
  };
  const server = (): NonNullable<Harness['store']['settings']['value']>['ai']['providers']['server'] | undefined => h.store.settings.value?.ai.providers.server;

  it('sits after the cloud services with its help text, and says nothing about where until there is an address', async () => {
    await open();
    const groups = [...document.querySelectorAll('.settings-group-label')].map((l) => l.textContent);
    expect(groups.indexOf('Your own server')).toBe(groups.indexOf('Providers') + 1);
    expect(groups.indexOf('Your own server')).toBeLessThan(groups.indexOf('Writing documents'));
    expect(rowOf('Address').querySelector('.settings-row-desc')?.textContent).toBe(SERVER_ADDRESS_HELP);
    expect(address().placeholder).toBe('http://localhost:11434');
    expect(rowOf('Context size').querySelector('.settings-row-desc')?.textContent).toBe(SERVER_CONTEXT_NOTE);
    expect(document.querySelector<HTMLInputElement>('#server-context')?.value).toBe('8192');
    expect(button('Test connection').disabled).toBe(true);
    expect(rowOf('Key').textContent).toContain('No key');
    expect(document.querySelector('.server-place')).toBeNull();
    // The Test connection button is a plain ghost: no per-screen size or paint class beside it.
    expect(button('Test connection').className).toBe('btn ghost');
  });

  it('saves the address on Enter and on leaving the field, not only after a test, and refuses one that is not an address', async () => {
    await open();
    await type(address(), 'http://localhost:11434');
    await press(address(), 'Enter');
    await until(() => server()?.address === 'http://localhost:11434');
    expect(serverSets().at(-1)).toEqual({ address: 'http://localhost:11434' });
    expect(h.callsOf('ai.serverProbe')).toHaveLength(0);
    expect(place()).toBe('On this PC · nothing leaves it');

    await type(address(), 'http://192.168.1.20:11434');
    await blur(address());
    await until(() => server()?.address === 'http://192.168.1.20:11434');
    expect(place()).toBe('On your network · needs Allow external AI services');

    const before = serverSets().length;
    await type(address(), 'ftp://files.example');
    await press(address(), 'Enter');
    expect(rowOf('Address').querySelector('[role="alert"]')?.textContent).toBe("'ftp://files.example' is not a server address. Type it like http://localhost:11434. Nothing was changed.");
    expect(address().getAttribute('aria-invalid')).toBe('true');
    expect(serverSets()).toHaveLength(before);

    // Clearing the field clears the address.
    await type(address(), '');
    await press(address(), 'Enter');
    await until(() => server()?.address === null);
    expect(serverSets().at(-1)).toEqual({ address: null });
    expect(document.querySelector('.server-place')).toBeNull();
  });

  it('Test connection asks the typed address, says what answered and fills the model list', async () => {
    await open();
    await type(address(), 'http://localhost:11434');
    await click(button('Test connection'));
    await until(() => probeLine() !== '');
    expect((h.callsOf('ai.serverProbe') as AiServerProbeParams[]).at(-1)).toEqual({ address: 'http://localhost:11434' });
    expect(probeLine()).toBe('Ollama 0.12.3 · 3 models');
    expect(document.querySelector('.server-probe')?.getAttribute('role')).toBe('status');
    // Testing saved the typed address as well.
    expect(server()?.address).toBe('http://localhost:11434');
    expect(document.querySelector('#server-model-note')?.textContent).toBe('Test the connection to list the server’s models, or type one under Other model….');

    await click(document.querySelector('[aria-label="Server model: Not chosen"]'));
    expect(options()).toEqual(['Not chosen', 'qwen3:8b', 'llama3.1:8b', 'gemma3:4b', 'Other model…']);
    await click([...document.querySelectorAll('[role="option"]')].find((o) => o.textContent.trim() === 'llama3.1:8b'));
    await until(() => server()?.model === 'llama3.1:8b');
    expect(serverSets().at(-1)).toEqual({ model: 'llama3.1:8b' });
    expect(document.querySelector('#server-model-note')?.textContent).toBe('Requests go to llama3.1:8b.');
    expect(document.querySelector('[aria-label="Server model: llama3.1:8b"]')).not.toBeNull();
  });

  it('shows the §17 sentence when nothing answers, and the insecure warning for plain http to a public address', async () => {
    await open();
    await type(address(), 'http://localhost:1234');
    await click(button('Test connection'));
    await until(() => probeLine() !== '');
    expect(probeLine()).toBe(
      'Nothing answered at localhost:1234. Check that the server is running and that the address and port are right (Ollama listens on 11434, LM Studio on 1234), then test again. Nothing was sent.',
    );
    expect(document.querySelector('.server-probe')?.getAttribute('role')).toBe('alert');
    expect(document.querySelector('.server-probe')?.getAttribute('data-code')).toBe('ai.serverUnreachable');

    await type(address(), 'http://203.0.113.7:11434');
    await click(button('Test connection'));
    await until(() => document.querySelector('.server-probe')?.getAttribute('data-code') === 'ai.serverInsecure');
    expect(probeLine()).toContain('203.0.113.7:11434 is a public address over plain http');
    await until(() => server()?.insecure === true);
    expect(place()).toBe(`Remote · needs Allow external AI services and https | ${INSECURE_WARNING}`);
    expect(document.querySelector('.server-place--warn')?.getAttribute('role')).toBe('alert');

    await type(address(), 'https://llm.example.com');
    await press(address(), 'Enter');
    await until(() => server()?.insecure === false);
    expect(place()).toBe('Remote · needs Allow external AI services and https');
  });

  it('Other model… checks a typed id before saving it, then saves it as typed', async () => {
    await open({ m4: { server: 'unreachable' } });
    await chooseModel('Other model…');
    const field = document.querySelector<HTMLInputElement>('#server-model-other');
    expect(field?.placeholder).toBe('qwen3:8b');
    await type(field, 'qwen 3');
    await click(button('Use this model'));
    expect(rowOf('Model').querySelector('[role="alert"]')?.textContent).toBe("'qwen 3' is not a model id. Use letters, digits and . _ : / - and start with a letter or digit. Nothing was changed.");
    expect(field?.getAttribute('aria-invalid')).toBe('true');
    expect(serverSets()).toHaveLength(0);
    await type(field, 'hf.co/org/model:Q4_K_M');
    await press(field, 'Enter');
    await until(() => server()?.model === 'hf.co/org/model:Q4_K_M');
    expect(serverSets().at(-1)).toEqual({ model: 'hf.co/org/model:Q4_K_M' });
    expect(document.querySelector('#server-model-note')?.textContent).toBe('Requests go to hf.co/org/model:Q4_K_M.');
  });

  it('clamps the context size to what Memento uses and saves it on Enter', async () => {
    await open();
    const context = document.querySelector<HTMLInputElement>('#server-context');
    if (context === null) {
      throw new Error('no context field');
    }
    await type(context, '100');
    await press(context, 'Enter');
    await until(() => server()?.contextTokens === 2048);
    expect(serverSets().at(-1)).toEqual({ contextTokens: 2048 });
    expect(context.value).toBe('2048');
    expect(rowOf('Context size').querySelector('[role="status"]')?.textContent).toBe('A context runs from 2,048 to 1,048,576 tokens, so 2,048 was saved.');

    await type(context, '32768');
    await blur(context);
    await until(() => server()?.contextTokens === 32768);
    expect(rowOf('Context size').querySelector('[role="status"]')).toBeNull();

    const before = serverSets().length;
    await type(context, '');
    await press(context, 'Enter');
    expect(rowOf('Context size').querySelector('[role="alert"]')?.textContent).toBe('Type the context size in tokens, for example 8192. Nothing was changed.');
    expect(serverSets()).toHaveLength(before);
  });

  it('saves and removes the optional key through ai.setKey and ai.clearKey without showing it', async () => {
    await open();
    await click(button('Add a key for your own server'));
    expect(document.querySelector('[role="dialog"] h2')?.textContent).toBe('Add a key for your own server');
    const input = document.querySelector<HTMLInputElement>('#ai-key-input');
    expect(input?.type).toBe('password');
    await type(input, 'lmstudio-fictional-0123');
    await click(button('Save key'));
    await until(() => document.querySelector('#ai-key-input') === null);
    expect((h.callsOf('ai.setKey') as AiSetKeyParams[]).at(-1)?.provider).toBe('server');
    expect(server()?.hasKey).toBe(true);
    expect(document.querySelector('[aria-label="Key for your own server stored"]')?.textContent).toBe('••••••••••••');
    expect(document.body.innerHTML).not.toContain('lmstudio-fictional-0123');
    expect(JSON.stringify(h.store.settings.value)).not.toContain('lmstudio-fictional-0123');
    // The cloud keys are untouched.
    expect(h.store.settings.value?.ai.providers.anthropic.hasKey).toBe(true);

    await click(button('Remove the key for your own server'));
    await until(() => server()?.hasKey === false);
    expect(h.callsOf('ai.clearKey')).toEqual([{ provider: 'server' }]);
    expect(rowOf('Key').textContent).toContain('No key');
  });

  it('opens at the field the Builder asked for', async () => {
    h = await mountApp({ name: 'library' });
    requestServerFocus(h.store, 'address');
    await act(async () => {
      h.store.route.value = { name: 'settings', section: 'ai-privacy' };
      await Promise.resolve();
    });
    await until(() => document.querySelector('#server-address') !== null);
    await until(() => document.activeElement === document.querySelector('#server-address'));
  });
});
