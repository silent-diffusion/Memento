import { act } from 'preact/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import type { MockOptions } from '../../bridge/mock';
import type { GenerationConfirmParams, GenerationPreviewParams, GenerationStartParams } from '../../bridge/types';
import { button, click, mountApp, press, settle, type, until, type Harness } from '../../testing/appHarness';
import { expectNeverSentRows } from '../../testing/neverSent';
import { PREVIEW_DEBOUNCE_MS, PROVIDER_RECHECK_MS } from './BuilderScreen';

const DESIGN = '20261005-160000-dsrev';

describe('Document builder (DESIGN.md §10, against the browser-preview host)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const open = async (mock: MockOptions = { m4: { ai: 'ready' } }): Promise<void> => {
    h = await mountApp({ name: 'builder', recordingId: DESIGN, templateId: null, documentId: null }, mock);
    await until(() => document.querySelector('.builder-paper article.paper') !== null && document.querySelector('.mod') !== null);
  };
  const order = (): string[] => [...document.querySelectorAll('.structure-row')].map((row) => [...row.querySelectorAll('.mod-name')].map((n) => n.textContent).join(' | '));
  const head = (name: string): HTMLButtonElement => {
    const card = [...document.querySelectorAll<HTMLElement>('.mod')].find((m) => m.querySelector('.mod-name')?.textContent === name);
    const el = card?.querySelector<HTMLButtonElement>('.modhead');
    if (el === undefined || el === null) {
      throw new Error(`no card ${name}`);
    }
    return el;
  };
  const keydown = async (el: Element, key: string, init: KeyboardEventInit = {}): Promise<void> => {
    await act(async () => {
      el.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init }));
      await Promise.resolve();
    });
  };
  const fire = async (el: Element | null, name: string): Promise<void> => {
    await act(async () => {
      el?.dispatchEvent(new Event(name, { bubbles: true, cancelable: true }));
      await Promise.resolve();
    });
  };

  it('opens the default template for the recording: header, palette, rows and the live paper', async () => {
    await open();
    expect(document.querySelector<HTMLInputElement>('#tpl-name')?.value).toBe('Meeting minutes');
    expect(document.querySelector('.spoke-title-edit .pill')?.textContent).toBe('Template · 9 modules');
    expect(button('Generate minutes').disabled).toBe(false);
    expect([...document.querySelectorAll('.palette-label')].map((l) => l.textContent)).toEqual(['Structure', 'Detail', 'Custom']);
    expect(document.querySelectorAll('.pal')).toHaveLength(23);
    expect(document.querySelector('.structure-head .lbl')?.textContent).toBe('Structure · 9 modules in 6 rows');
    expect(order()).toEqual([
      'Executive summary',
      'Meeting purpose | Participants',
      'Agenda',
      'Discussion summary',
      'Decisions | Action items',
      'Open questions | Next meeting',
    ]);
    const paper = document.querySelector('.builder-paper article.paper');
    expect(paper?.className).toBe('paper paper-skeleton caps title-rule th-fill');
    expect([...(paper?.querySelectorAll('.paper-row') ?? [])].map((r) => r.getAttribute('data-cols'))).toEqual(['1', '2', '1', '1', '2', '2']);
    expect(document.querySelector('.preview-caption')?.textContent).toContain('How the minutes will be laid out · Corporate style · Letter');
  });

  it('asks for the preview paper once after a pause in changes', async () => {
    await open();
    const before = h.callsOf('generation.previewHtml').length;
    const name = document.querySelector<HTMLInputElement>('#tpl-name');
    await type(name, 'Minutes A');
    await type(name, 'Minutes AB');
    await type(name, 'Minutes ABC');
    expect(h.callsOf('generation.previewHtml')).toHaveLength(before);
    await settle(PREVIEW_DEBOUNCE_MS + 60);
    const calls = h.callsOf('generation.previewHtml') as GenerationPreviewParams[];
    expect(calls).toHaveLength(before + 1);
    expect(calls[calls.length - 1]?.template.name).toBe('Minutes ABC');
    await until(() => document.querySelector('.builder-paper .paper-meta')?.textContent.startsWith('Minutes ABC') === true);
  });

  it('adds from the palette by click, filters by search, and expands the selected card with its settings', async () => {
    await open();
    await type(document.querySelector('#mod-search'), 'remark');
    expect([...document.querySelectorAll('.pal-name')].map((n) => n.textContent)).toEqual(['Quote']);
    await click(button('Add Quote'));
    expect(order().at(-1)).toBe('Quote');
    expect(document.querySelector('.structure-head .lbl')?.textContent).toBe('Structure · 10 modules in 7 rows');
    const quote = head('Quote');
    expect(quote.getAttribute('aria-expanded')).toBe('true');
    const card = quote.closest('.mod');
    expect(card?.querySelector('textarea')?.value).toBe('One remark that captures the meeting, word for word.');
    expect([...(card?.querySelectorAll('.seg-well') ?? [])].map((g) => g.getAttribute('aria-label'))).toEqual(['Length of Quote', 'Text size of Quote']);
    await click(button('Larger'));
    await click(card?.querySelector('[role="switch"]'));
    await settle(PREVIEW_DEBOUNCE_MS + 60);
    const last = (h.callsOf('generation.previewHtml') as GenerationPreviewParams[]).at(-1);
    expect(last?.template.rows.at(-1)?.modules[0]).toMatchObject({ module: 'quote', textSize: 'larger', linkToTranscript: false });
    await type(document.querySelector('#mod-search'), 'zzz');
    expect(document.querySelector('.palette-empty')?.textContent).toBe('No module matches “zzz”.');
  });

  it('moves cards with the keyboard and the buttons, removes with Delete and puts it back with Ctrl+Z', async () => {
    await open();
    const summary = head('Executive summary');
    summary.focus();
    await keydown(summary, 'ArrowDown', { altKey: true });
    expect(order().slice(0, 2)).toEqual(['Meeting purpose | Participants', 'Executive summary']);
    expect((document.activeElement as HTMLElement | null)?.closest('.mod')?.querySelector('.mod-name')?.textContent).toBe('Executive summary');
    await keydown(head('Participants'), 'ArrowLeft', { altKey: true });
    expect(order()[0]).toBe('Participants | Meeting purpose');
    // In a shared row, "up" pulls the module out into its own row above.
    await click(button('Move Meeting purpose into its own row above'));
    expect(order().slice(0, 3)).toEqual(['Meeting purpose', 'Participants', 'Executive summary']);
    await keydown(head('Agenda'), 'Delete');
    expect(order()).not.toContain('Agenda');
    expect(document.querySelector('[role="status"].sr')?.textContent).toBe('Agenda removed. Press Ctrl+Z to put it back.');
    await keydown(document.activeElement ?? document.body, 'z', { ctrlKey: true });
    expect(order()).toContain('Agenda');
    await click(button('Remove Next meeting'));
    expect(order().at(-1)).toBe('Open questions');
  });

  it('drops a palette module in a gap and a card beside another while the drop zones are shown', async () => {
    await open();
    await fire(document.querySelector('.pal[data-module="highlight"]'), 'dragstart');
    expect(document.querySelectorAll('.dz.live')).toHaveLength(7);
    expect(document.querySelector('.dz[data-gap="1"]')?.textContent).toBe('Drop here for a new row');
    expect(document.querySelector('.dz[data-gap="6"]')?.textContent).toBe('Drop here to add at the end');
    // Rows with fewer than three modules show the beside slot.
    expect(document.querySelectorAll('.side.live')).toHaveLength(6);
    await fire(document.querySelector('.dz[data-gap="1"]'), 'dragover');
    expect(document.querySelector('.dz[data-gap="1"]')?.classList.contains('hot')).toBe(true);
    await fire(document.querySelector('.dz[data-gap="1"]'), 'drop');
    expect(order()[1]).toBe('Highlight');
    expect(document.querySelectorAll('.dz.live')).toHaveLength(0);
    // A card by its handle, beside the first row.
    await fire(head('Agenda').closest('.mod')?.querySelector('.handle') ?? null, 'dragstart');
    expect(document.querySelector('[data-card] .mod-name') !== null).toBe(true);
    expect([...document.querySelectorAll('.mod.ghosted .mod-name')].map((n) => n.textContent)).toEqual(['Agenda']);
    await fire(document.querySelector('.side[data-side="0"]'), 'drop');
    expect(order()[0]).toBe('Executive summary | Agenda');
  });

  it('sends exactly the ticked inputs to generation.preview and shows the payload read-only', async () => {
    await open();
    await click(document.querySelector('#builder-tab-inputs'));
    const checks = [...document.querySelectorAll<HTMLElement>('.input-check')];
    expect(checks.map((c) => c.querySelector('.input-name')?.textContent)).toEqual([
      'Transcript',
      'Recording details',
      'Participants',
      'Agenda',
      'Highlights and notes',
      'Imported documents',
      'Earlier documents',
      'Audio',
      'Video',
    ]);
    // Audio and video are never sent: greyed rows with a lock and a note, not checkboxes.
    expect(document.querySelectorAll('#builder-panel-inputs input[type="checkbox"]')).toHaveLength(7);
    expectNeverSentRows(checks.slice(7));
    // Attachments are not allowed in Settings › AI and privacy by default.
    expect(checks[5]?.querySelector('input')?.disabled).toBe(true);
    expect(checks[5]?.textContent).toContain('off in Settings');
    await click(checks[3]?.querySelector('input'));
    await click(button('Preview exactly what will be sent'));
    await until(() => document.querySelector('.payload-text') !== null);
    const params = (h.callsOf('generation.preview') as GenerationPreviewParams[]).at(-1);
    expect(params?.recordingId).toBe(DESIGN);
    expect(params?.template.inputs).toEqual({ transcript: true, details: true, participants: true, agenda: false, highlights: true, attachments: false, previousDocuments: false });
    expect(params?.template.rows.flatMap((r) => r.modules)).toHaveLength(9);
    const sheet = document.querySelector('.sheet');
    expect(sheet?.querySelector('h2')?.textContent).toBe('What will be sent');
    expect(sheet?.querySelector<HTMLTextAreaElement>('.payload-text')?.readOnly).toBe(true);
    expect(sheet?.querySelector<HTMLTextAreaElement>('.payload-text')?.value).not.toContain('[Agenda]');
    expect([...(sheet?.querySelectorAll('.send-pills .pill') ?? [])].map((p) => p.textContent)).toEqual(['Transcript', 'Details', 'Participants', 'Highlights']);
    expect(sheet?.textContent).toContain('Audio and video are never sent.');
  });

  it('shows provider readiness from providers.list, and the explanation when external AI is off', async () => {
    await open({ m4: { ai: 'nokey' } });
    await click(document.querySelector('#builder-tab-inputs'));
    const providers = (): string[] => [...document.querySelectorAll('.provider')].map((p) => p.textContent.trim());
    expect(providers()).toEqual(['ClaudeAnthropic · no key saved', 'ChatGPTOpenAI · no key saved', 'Local modelThis PC · model not installed']);
    expect([...document.querySelectorAll<HTMLInputElement>('.provider input')].every((r) => r.disabled)).toBe(true);
    expect(button('Generate minutes').disabled).toBe(true);
    h.unmount();

    await open({ m4: { ai: 'off' } });
    await click(document.querySelector('#builder-tab-inputs'));
    expect(document.querySelector('.provider-off')?.textContent).toContain('External AI is off in Settings › AI and privacy');
    expect(providers()).toEqual(['Local modelThis PC · model not installed']);
    expect(button('Generate minutes').disabled).toBe(true);
    await click(button('Open AI and privacy settings'));
    expect(h.store.route.value).toEqual({ name: 'settings', section: 'ai-privacy' });
    h.unmount();

    await open({ m4: { ai: 'local' } });
    await click(document.querySelector('#builder-tab-inputs'));
    expect(providers()).toEqual(['Local modelQwen3.5 4B · on this PC']);
    expect(document.querySelector<HTMLInputElement>('.provider.on input')?.checked).toBe(true);
    expect(button('Generate minutes').disabled).toBe(false);
  });

  it('asks for provider readiness again while the chosen provider is not ready', async () => {
    // The local model is not ready while other apps hold the graphics card's memory; nothing announces when they let go.
    await open({ m4: { ai: 'nokey' } });
    const before = h.callsOf('providers.list').length;
    await settle(PROVIDER_RECHECK_MS + 300);
    expect(h.callsOf('providers.list').length).toBeGreaterThan(before);
    h.unmount();

    await open();
    const ready = h.callsOf('providers.list').length;
    await settle(PROVIDER_RECHECK_MS + 300);
    expect(h.callsOf('providers.list')).toHaveLength(ready);
  }, 20_000);

  it('asks before sending, shows progress, and opens the new document when it is written', async () => {
    await open();
    await click(button('Generate minutes'));
    await until(() => document.querySelector('#confirm-send-title') !== null);
    const dialog = document.querySelector('[role="dialog"]');
    expect(dialog?.querySelector('h2')?.textContent).toBe('Send to Claude?');
    expect(dialog?.textContent).toContain('Claude (Anthropic) · Claude Sonnet 4.5');
    expect(dialog?.textContent).toMatch(/\d+ KB in 1 chunk/);
    expect([...(dialog?.querySelectorAll('.pill') ?? [])].map((p) => p.textContent)).toEqual(['Transcript', 'Details', 'Participants', 'Agenda', 'Highlights']);
    await click(dialog?.querySelector('.btn.g'));
    expect((h.callsOf('generation.confirm') as GenerationConfirmParams[]).at(-1)?.approved).toBe(false);
    expect(document.querySelector('#confirm-send-title')).toBeNull();

    await click(button('Generate minutes'));
    await until(() => document.querySelector('#confirm-send-title') !== null);
    expect((h.callsOf('generation.start') as GenerationStartParams[]).at(-1)?.template.providerId).toBe('anthropic');
    await click(button('Send'));
    await until(() => document.querySelector('.gen-card') !== null);
    expect(document.querySelector('.gen-card')?.textContent).toContain('Cancel');
    await until(() => h.store.route.value.name === 'document', 10_000);
    const route = h.store.route.value;
    expect(route.name === 'document' && route.recordingId === DESIGN).toBe(true);
  });

  it('shows the §17 failure card with Try again and Switch to the other ready provider', async () => {
    await open({ m4: { ai: 'ready', gen: 'fail' } });
    await h.bridge.call('ai.setKey', { provider: 'openai', key: 'sk-test-0123456789' });
    await h.bridge.call('settings.set', { ai: { askBeforeSend: false } });
    h.store.settings.value = await h.bridge.call('settings.get');
    await click(button('Generate minutes'));
    await until(() => document.querySelector('.ai-failure') !== null, 10_000);
    const card = document.querySelector('.ai-failure');
    expect(card?.querySelector('.ai-failure-lead')?.textContent).toBe("Claude didn't respond");
    expect(card?.textContent).toContain('the network connection was lost');
    expect(card?.textContent).toContain('Nothing was sent twice and no document was changed.');
    await until(() => card?.textContent.includes('Switch to ChatGPT') === true);
    await click(button('Switch to ChatGPT'));
    expect((h.callsOf('generation.start') as GenerationStartParams[]).at(-1)?.template.providerId).toBe('openai');
    await until(() => h.store.route.value.name === 'document', 10_000);
  });

  it('refuses to start without a transcript, in the failure card with the host’s words', async () => {
    h = await mountApp({ name: 'builder', recordingId: '20260930-130500-onbrd', templateId: null, documentId: null }, { m4: { ai: 'ready' }, stage: 'failed' });
    await until(() => document.querySelector('.mod') !== null);
    await press(document.body, 'Tab');
    await click(button('Generate notes'));
    await until(() => document.querySelector('.ai-failure') !== null);
    expect(document.querySelector('.ai-failure-lead')?.textContent).toBe('The interview notes could not be started');
    expect(document.querySelector('.ai-failure')?.textContent).toContain('has no transcript yet');
  });
});
