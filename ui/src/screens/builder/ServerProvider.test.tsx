import { afterEach, describe, expect, it } from 'vitest';
import type { MockOptions } from '../../bridge/mock';
import type { GenerationStartParams, SettingsSetParams } from '../../bridge/types';
import { button, click, mountApp, until, type Harness } from '../../testing/appHarness';

const DESIGN = '20261005-160000-dsrev';

/**
 * The Builder with Your own server (2.5, DESIGN.md §10): its provider card and what a card that is not ready offers,
 * the send confirmation only for a server that is not on this PC (keyed on leavesPc), Live output streamed as for
 * the local model, the preview sheet, "How this was made" and the failure card's specific fix.
 */
describe('Your own server in the Builder (against the browser-preview host)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const open = async (mock: MockOptions): Promise<void> => {
    h = await mountApp({ name: 'builder', recordingId: DESIGN, templateId: null, documentId: null }, mock);
    await until(() => document.querySelector('.builder-paper article.paper') !== null && document.querySelector('.mod') !== null);
    await click(document.querySelector('#builder-tab-inputs'));
    await until(() => document.querySelector('.provider[data-provider="server"]') !== null);
  };
  const card = (): HTMLElement => {
    const el = document.querySelector<HTMLElement>('.provider[data-provider="server"]');
    if (el === null) {
      throw new Error('no server card');
    }
    return el;
  };
  const cardText = (): string => [card().querySelector('.provider-name')?.textContent, card().querySelector('.provider-note')?.textContent].join(' · ');
  const radio = (): HTMLInputElement | null => card().querySelector<HTMLInputElement>('input[type="radio"]');

  it('offers a ready server on this PC while external AI is off, and writes without asking first', async () => {
    await open({ m4: { server: 'local' }, stepMs: 120 });
    expect(document.querySelector('.provider-off')?.textContent).toContain('Your own server on this PC can still write it too.');
    expect([...document.querySelectorAll('.provider .provider-name')].map((n) => n.textContent)).toEqual(['Your own server', 'Local model']);
    expect(cardText()).toBe('Your own server · qwen3:8b at localhost:11434 · on this PC');
    expect(radio()?.disabled).toBe(false);
    // The first ready provider: the local model is not installed in this preview.
    expect(radio()?.checked).toBe(true);
    // Nothing leaves the PC, so What may be shared locks nothing (Attachments is not allowed in Settings).
    const attachments = [...document.querySelectorAll('.input-check')].find((r) => r.querySelector('.input-name')?.textContent === 'Imported documents');
    expect(attachments?.querySelector('input')?.disabled).toBe(false);

    await click(button('Generate minutes'));
    await until(() => document.querySelector('.gen-card') !== null);
    // Ask before every send is on, yet nothing is asked: the server is on this PC.
    expect(document.querySelector('#confirm-send-title')).toBeNull();
    expect(h.callsOf('generation.confirm')).toHaveLength(0);
    expect((h.callsOf('generation.start') as GenerationStartParams[]).at(-1)?.template.providerId).toBe('server');
    expect(document.querySelector('.gen-note')?.textContent).toBe('Running on this PC. Nothing leaves it.');

    await click(button('Show live output'));
    await until(() => document.querySelector('.live-request') !== null);
    expect(document.querySelector('.live-sub')?.textContent).toMatch(/^Your own server · qwen3:8b at localhost:11434 \(this PC\) · (Writing|Finished · read only)$/);
    expect(document.querySelector('#live-request-label')?.textContent).toBe('Request · read by your own server on this PC');
    expect(document.querySelector('.sheet-footer-note')?.textContent).toBe('Read by your own server on this PC; nothing leaves it. Shown only here and not saved.');
    // Streamed like the local model: no "arrive whole" note.
    expect(document.querySelector('.live-whole')?.textContent ?? '').not.toContain('arrive whole');
    await click(button('Done'));

    await until(() => h.store.route.value.name === 'document', 15_000);
    await until(() => document.querySelector('.how-made .how-facts') !== null);
    const facts = [...document.querySelectorAll('.how-made .how-facts dt')].map((dt) => [dt.textContent, dt.nextElementSibling?.textContent]);
    expect(facts).toContainEqual(['Provider', 'Your own server · qwen3:8b at localhost:11434 (this PC)']);
    expect(facts.map(([dt]) => dt)).not.toContain('Model');
    expect(document.querySelector('.how-made')?.textContent).toContain('Everything stayed on this PC. Audio and video were not used.');
  }, 30_000);

  it('asks before sending to a server on the network, names it, and keeps What may be shared', async () => {
    await open({ m4: { server: 'network', ai: 'ready' } });
    await click(radio());
    expect(cardText()).toBe('Your own server · qwen3:8b at 192.168.1.20:11434 · on your network');
    const attachments = [...document.querySelectorAll('.input-check')].find((r) => r.querySelector('.input-name')?.textContent === 'Imported documents');
    expect(attachments?.querySelector('.input-note')?.textContent).toBe('off in Settings');
    await click(button('Generate minutes'));
    await until(() => document.querySelector('#confirm-send-title') !== null);
    const dialog = document.querySelector('[role="dialog"]');
    expect(dialog?.querySelector('h2')?.textContent).toBe('Send to your own server?');
    expect(dialog?.textContent).toContain('sends these parts of the recording to your own server (qwen3:8b at 192.168.1.20:11434)');
    expect(dialog?.textContent).toContain('Your own server · qwen3:8b at 192.168.1.20:11434');
    await click(dialog?.querySelector('.btn.g'));
    expect(document.querySelector('#confirm-send-title')).toBeNull();
  });

  it('says what a server that is not ready needs, and opens Settings at the field that fixes it', async () => {
    await open({ m4: {} });
    expect(cardText()).toBe('Your own server · no server address');
    expect(radio()?.disabled).toBe(true);
    await click(card().querySelector('.provider-detail--action button'));
    expect(h.store.route.value).toEqual({ name: 'settings', section: 'ai-privacy' });
    await until(() => document.activeElement === document.querySelector('#server-address'));
    h.unmount();

    await open({ m4: { server: 'unreachable' } });
    expect(cardText()).toBe('Your own server · no model chosen');
    expect(card().querySelector('.provider-detail--action')?.textContent).toBe('Choose a model');
    await click(button('Choose a model'));
    await until(() => document.activeElement === document.querySelector('[aria-label^="Server model: "]'));
    h.unmount();

    await open({ m4: { server: 'network' } });
    expect(cardText()).toBe('Your own server · external ai is off');
    expect(card().querySelector('.provider-detail--action')?.textContent).toBe('Allow external AI services');
    await click(button('Allow external AI services'));
    expect(h.store.route.value).toEqual({ name: 'settings', section: 'ai-privacy' });
    h.unmount();

    await open({ m4: { server: 'public', ai: 'ready' } });
    expect(cardText()).toBe('Your own server · plain http to a public address');
    expect(card().querySelector('.provider-detail--action')?.textContent).toBe('Change the address');
  });

  it('previews what a server on this PC reads, without sending anything', async () => {
    await open({ m4: { server: 'local' } });
    await click(button('Preview exactly what will be sent'));
    await until(() => document.querySelector('.preview-sheet .preview-inputs') !== null);
    expect(document.querySelector('#preview-title')?.textContent).toBe('What your own server reads');
    expect(document.querySelector('#preview-sub')?.textContent).toBe('Your own server · qwen3:8b at localhost:11434 (this PC) · nothing leaves it');
    const facts = [...document.querySelectorAll('.preview-facts dt')].map((dt) => [dt.textContent, dt.nextElementSibling?.textContent]);
    expect(facts[0]).toEqual(['Read by', 'Your own server · qwen3:8b at localhost:11434 (this PC)']);
    expect(facts[1]).toEqual(['Ask before every send', 'Not needed: nothing leaves this PC']);
    expect(h.callsOf('generation.start')).toHaveLength(0);
  });

  it('leads the failure card with the specific fix when the server does not serve the model', async () => {
    await open({ m4: { server: 'local' } });
    await h.bridge.call('settings.set', { ai: { providers: { server: { model: 'mistral:7b' } } } });
    h.store.settings.value = await h.bridge.call('settings.get');
    await click(document.querySelector('#builder-tab-preview'));
    await click(document.querySelector('#builder-tab-inputs'));
    await until(() => cardText().includes('mistral:7b'));
    await click(button('Generate minutes'));
    await until(() => document.querySelector('.ai-failure') !== null, 10_000);
    const failure = document.querySelector('.ai-failure');
    expect(failure?.getAttribute('data-code')).toBe('ai.serverModelUnknown');
    expect(failure?.querySelector('.ai-failure-lead')?.textContent).toBe('The server at localhost:11434 does not serve mistral:7b');
    expect([...(failure?.querySelectorAll('.ai-failure-actions button') ?? [])].map((b) => b.textContent)[0]).toBe('Choose a model');
    await click(failure?.querySelector('.ai-failure-actions button'));
    expect(h.store.route.value).toEqual({ name: 'settings', section: 'ai-privacy' });
    await until(() => document.activeElement === document.querySelector('[aria-label^="Server model: "]'));
  });

  it('is offered as the default provider in Settings and chosen by the Builder when it is', async () => {
    h = await mountApp({ name: 'settings', section: 'ai-privacy' }, { m4: { server: 'local' } });
    await until(() => document.querySelector('[aria-label^="Default provider: "]') !== null);
    await click(document.querySelector('[aria-label^="Default provider: "]'));
    await until(() => [...document.querySelectorAll('[role="option"]')].some((o) => o.textContent.trim() === 'Your own server'));
    await click([...document.querySelectorAll('[role="option"]')].find((o) => o.textContent.trim() === 'Your own server'));
    await until(() => h.store.settings.value?.ai.defaultProviderId === 'server');
    expect((h.callsOf('settings.set') as SettingsSetParams[]).at(-1)).toEqual({ ai: { defaultProviderId: 'server' } });
  });
});
