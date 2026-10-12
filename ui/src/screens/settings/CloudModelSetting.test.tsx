import { afterEach, describe, expect, it } from 'vitest';
import { CLOUD_MODELS, modelIdProblem, providerState } from '../../bridge/cloudModels';
import type { GenerationPreviewResult, SettingsSetParams } from '../../bridge/types';
import { button, click, mountApp, press, type, until, type Harness } from '../../testing/appHarness';

const DESIGN = '20261005-160000-dsrev';

/**
 * Settings › AI and privacy › the model each cloud provider is sent (after 2.1): the list with a note each, the default
 * first, "Other model…" for an id typed by hand (checked, saved as typed, marked "not in Memento's list"), and the
 * Builder naming the chosen model on its provider card, in the preview and the failure card (LiveOutputSheet.test.tsx has the Live output).
 */
describe('Cloud model choice (against the browser-preview host)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const openSettings = async (): Promise<void> => {
    h = await mountApp({ name: 'settings', section: 'ai-privacy' }, { m4: { ai: 'ready' } });
    await until(() => document.querySelector('[role="radiogroup"][aria-label="Claude model"]') !== null);
  };
  const group = (name: string): HTMLElement => {
    const el = document.querySelector<HTMLElement>(`[role="radiogroup"][aria-label="${name}"]`);
    if (el === null) {
      throw new Error(`no ${name} list`);
    }
    return el;
  };
  const rows = (name: string): { id: string; note: string; checked: boolean }[] =>
    [...group(name).querySelectorAll('label')].map((l) => ({
      id: l.querySelector('.model-choice-id')?.textContent ?? '',
      note: l.querySelector('.model-choice-note')?.textContent ?? '',
      checked: l.querySelector('input')?.checked === true,
    }));
  const radio = (name: string, id: string): HTMLInputElement | null => group(name).querySelector<HTMLInputElement>(`input[value="${id}"]`);
  const modelSets = (): SettingsSetParams[] => (h.callsOf('settings.set') as SettingsSetParams[]).filter((p) => p.ai?.providers !== undefined);
  const inUse = (provider: 'anthropic' | 'openai'): string => (document.querySelectorAll('.cloud-model-in-use')[provider === 'anthropic' ? 0 : 1]?.textContent ?? '').replace(/\s+/g, ' ');

  it('lists each provider’s models with a note, the default first and chosen, then Other model…', async () => {
    await openSettings();

    expect(rows('Claude model')).toEqual([
      { id: 'claude-opus-5-5', note: "The most capable for long transcripts. Memento's default.", checked: true },
      {
        id: 'claude-fable-5-1',
        note: "Anthropic's top tier: the newest and most intelligent. Costs more and takes longer; the account needs 30-day data retention.",
        checked: false,
      },
      { id: 'claude-sonnet-5-5', note: 'Fast and strong, at about half the price of Opus.', checked: false },
      { id: 'claude-haiku-5-5', note: 'The fastest and cheapest.', checked: false },
      { id: 'Other model…', note: 'Any other model id Claude’s service offers, typed exactly as it names it.', checked: false },
    ]);
    expect(rows('ChatGPT model').map((r) => r.id)).toEqual(['gpt-6-astra', 'gpt-6.1-sol', 'gpt-6-luna', 'Other model…']);
    expect(rows('ChatGPT model')[0]?.checked).toBe(true);
    expect(inUse('anthropic')).toBe('Requests to Claude go to claude-opus-5-5.');
    expect(document.querySelector('.model-other-field')).toBeNull();
  });

  it('saves a listed model, and the default as null so it follows a later default', async () => {
    await openSettings();

    await click(radio('Claude model', 'claude-haiku-5-5'));
    await until(() => h.store.settings.value?.ai.providers.anthropic.model === 'claude-haiku-5-5');
    expect(modelSets().at(-1)).toEqual({ ai: { providers: { anthropic: { model: 'claude-haiku-5-5' } } } });
    expect(radio('Claude model', 'claude-haiku-5-5')?.checked).toBe(true);
    expect(inUse('anthropic')).toBe('Requests to Claude go to claude-haiku-5-5.');

    await click(radio('Claude model', 'claude-opus-5-5'));
    await until(() => h.store.settings.value?.ai.providers.anthropic.model === 'claude-opus-5-5');
    expect(modelSets().at(-1)).toEqual({ ai: { providers: { anthropic: { model: null } } } });
    // ChatGPT's model is untouched.
    expect(h.store.settings.value?.ai.providers.openai.model).toBe('gpt-6-astra');
  });

  it('Other model… checks the typed id before saving it, then saves it as typed and marks it unlisted', async () => {
    await openSettings();

    await click(radio('Claude model', 'other'));
    const field = document.querySelector<HTMLInputElement>('#anthropic-model-other');
    expect(field).not.toBeNull();
    expect(modelSets()).toHaveLength(0);

    await click(button('Use this model'));
    expect(document.querySelector('.settings-inline[role="alert"]')?.textContent).toBe('Type the model id, for example claude-opus-4-8. Nothing was changed.');
    await type(field, 'claude opus <4>');
    await press(field, 'Enter');
    expect(document.querySelector('.settings-inline[role="alert"]')?.textContent).toBe(
      "'claude opus <4>' is not a model id. Use letters, digits and . _ : / - and start with a letter or digit. Nothing was changed.",
    );
    expect(field?.getAttribute('aria-invalid')).toBe('true');
    expect(modelSets()).toHaveLength(0);

    await type(field, '  claude-opus-4-8 ');
    expect(document.querySelector('.settings-inline[role="alert"]')).toBeNull();
    await click(button('Use this model'));
    await until(() => h.store.settings.value?.ai.providers.anthropic.custom === true);
    expect(modelSets().at(-1)).toEqual({ ai: { providers: { anthropic: { model: 'claude-opus-4-8' } } } });
    expect(radio('Claude model', 'other')?.checked).toBe(true);
    expect(inUse('anthropic')).toBe(
      "Requests to Claude go to claude-opus-4-8. Not in Memento's list. Memento assumes it reads up to 200k tokens a request and sends it no effort setting. If Claude does not offer it, generating says so and nothing is sent to another model.",
    );
    expect(document.querySelector('.cloud-model-in-use')?.getAttribute('data-custom')).toBe('true');
    expect(button('Use this model').disabled).toBe(true);
  });

  it('an id over 100 characters is refused before anything is saved', async () => {
    await openSettings();
    await click(radio('Claude model', 'other'));
    const field = document.querySelector<HTMLInputElement>('#anthropic-model-other');
    await type(field, 'x'.repeat(101));
    await click(button('Use this model'));
    expect(document.querySelector('.settings-inline[role="alert"]')?.textContent).toBe('A model id is at most 100 characters; this one has 101. Nothing was changed.');
    expect(h.store.settings.value?.ai.providers.anthropic.model).toBe('claude-opus-5-5');
  });

  it('the Builder names the chosen model on the provider card and in the preview', async () => {
    h = await mountApp({ name: 'builder', recordingId: DESIGN, templateId: null, documentId: null }, { m4: { ai: 'ready' } });
    await until(() => document.querySelector('.mod') !== null);
    await h.bridge.call('settings.set', { ai: { providers: { anthropic: { model: 'claude-haiku-5-5' } } } });
    h.store.settings.value = await h.bridge.call('settings.get');
    await click(document.querySelector('#builder-tab-inputs'));
    await until(() => document.querySelector('.provider')?.textContent.includes('claude-haiku-5-5') === true);
    expect(document.querySelector('.provider')?.textContent.trim()).toBe('ClaudeAnthropic · claude-haiku-5-5 · key saved');

    await click(button('Preview exactly what will be sent'));
    await until(() => document.querySelector('.preview-inputs') !== null);
    expect(document.querySelector('#preview-sub')?.textContent).toContain('Claude · claude-haiku-5-5');
    const template = await h.bridge.call('templates.get', { templateId: 'meeting-minutes' });
    const preview: GenerationPreviewResult = await h.bridge.call('generation.preview', { recordingId: DESIGN, template: { ...template, providerId: 'anthropic' } });
    expect(preview.provider?.modelLabel).toBe('claude-haiku-5-5');
  });

  it('a typed model the service does not offer fails with its own card and Choose a model', async () => {
    h = await mountApp({ name: 'builder', recordingId: DESIGN, templateId: null, documentId: null }, { m4: { ai: 'ready' } });
    await until(() => document.querySelector('.mod') !== null);
    await h.bridge.call('settings.set', { ai: { askBeforeSend: false, providers: { anthropic: { model: 'claude-nonexistent-9' } } } });
    h.store.settings.value = await h.bridge.call('settings.get');
    await click(document.querySelector('#builder-tab-inputs'));
    await until(() => document.querySelector('.provider')?.textContent.includes('claude-nonexistent-9') === true);
    expect(document.querySelector('.provider')?.textContent.trim()).toBe('ClaudeAnthropic · claude-nonexistent-9 (not in Memento’s list) · key saved');

    await click(button('Generate minutes'));
    await until(() => document.querySelector('.ai-failure') !== null, 10_000);
    const card = document.querySelector('.ai-failure');
    expect(card?.getAttribute('data-code')).toBe('ai.unknownModel');
    expect(card?.querySelector('.ai-failure-lead')?.textContent).toBe('Claude does not offer claude-nonexistent-9');
    expect(card?.querySelector('.ai-failure-body')?.textContent).toBe(
      "Claude does not offer a model called 'claude-nonexistent-9'. Nothing was generated and no document was changed. Choose one from the list in Settings › AI and privacy, or check the id.",
    );
    await click(button('Choose a model'));
    expect(h.store.route.value).toEqual({ name: 'settings', section: 'ai-privacy' });
  });
});

describe('cloudModels (the preview host’s copy of the host lists)', () => {
  it('builds the snapshot entry like the host', () => {
    expect(providerState('anthropic', true, null)).toMatchObject({ hasKey: true, model: 'claude-opus-5-5', custom: false, models: CLOUD_MODELS.anthropic.map((c) => c.id) });
    expect(providerState('openai', false, ' gpt-6-luna ')).toMatchObject({ model: 'gpt-6-luna', custom: false, modelNote: "OpenAI's most efficient: the fastest and cheapest." });
    expect(providerState('openai', false, 'gpt-4o')).toMatchObject({ model: 'gpt-4o', custom: true, modelNote: "Not in Memento's list. Memento assumes it reads up to 128k tokens a request and sends it no effort setting." });
    expect(providerState('anthropic', false, '')).toMatchObject({ model: 'claude-opus-5-5', custom: false });
  });

  it('checks a typed id as the host does', () => {
    expect(modelIdProblem('claude-opus-4-8')).toBeNull();
    expect(modelIdProblem('ft:gpt-6-luna:org:custom:abc123')).toBeNull();
    expect(modelIdProblem('gpt-6.1-sol')).toBeNull();
    expect(modelIdProblem('   ')).not.toBeNull();
    expect(modelIdProblem('-x')).not.toBeNull();
    expect(modelIdProblem('ünicode')).not.toBeNull();
    expect(modelIdProblem('a'.repeat(100))).toBeNull();
    expect(modelIdProblem('a'.repeat(101))).not.toBeNull();
  });
});
