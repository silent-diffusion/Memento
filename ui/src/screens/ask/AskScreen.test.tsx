import { afterEach, describe, expect, it } from 'vitest';
import type { AskSendParams } from '../../bridge/types';
import { button, click, mountApp, press, type, until, type Harness } from '../../testing/appHarness';

const DESIGN = '20261005-160000-dsrev';

/** Ask (2.9.0) against the browser-preview host: setup, streaming, citations to Review, stop, confirmation and clear. */
describe('Ask', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const field = (): HTMLTextAreaElement | null => document.querySelector<HTMLTextAreaElement>('#ask-question');
  const question = (): HTMLTextAreaElement => {
    const el = field();
    if (el === null) {
      throw new Error('no question field');
    }
    return el;
  };
  const answer = (index: number): Element => {
    const el = answers()[index];
    if (el === undefined) {
      throw new Error(`no answer ${index}`);
    }
    return el;
  };
  const answers = (): Element[] => [...document.querySelectorAll('.ask-answer:not(.ask-answer--live)')];

  it('shows how to set AI up when no provider is ready, and nothing else', async () => {
    h = await mountApp({ name: 'ask', recordingId: DESIGN }, { m4: { ai: 'off' } });
    await until(() => document.querySelector('.ask-setup') !== null);

    expect(document.querySelector('#ask-setup-title')?.textContent).toBe('Ask needs an AI provider');
    expect(document.querySelector('#ask-question')).toBeNull();
    await click(button('Open AI and privacy settings'));
    expect(h.store.route.value).toEqual({ name: 'settings', section: 'ai-privacy' });
    expect(h.callsOf('ask.send')).toHaveLength(0);
  });

  it('opens from Review and streams an answer from the local model with citations that open Review at the line', async () => {
    h = await mountApp({ name: 'review', recordingId: DESIGN }, { m4: { ai: 'local' }, stepMs: 20 });
    await until(() => document.querySelector('.spoke-actions') !== null);
    await click(button('Ask'));
    await until(() => field() !== null);
    expect(h.store.route.value).toEqual({ name: 'ask', recordingId: DESIGN });
    expect(document.querySelector('.ask-composer .field-note')?.textContent).toContain('The local model answers on this PC: nothing leaves it.');

    await type(question(), 'What did we agree about the launch?');
    await press(question(), 'Enter');
    await until(() => answers().length === 1);

    const sent = h.callsOf('ask.send') as AskSendParams[];
    expect(sent).toEqual([{ recordingId: DESIGN, question: 'What did we agree about the launch?', providerId: 'local' }]);
    expect(document.querySelector('.ask-question')?.textContent).toBe('What did we agree about the launch?');
    expect(question().value).toBe('');
    const chips = [...answer(0).querySelectorAll('button.ask-cite')];
    expect(chips.length).toBeGreaterThan(0);
    expect(answer(0).querySelector('.ask-sections summary')?.textContent).toMatch(/^Answered from /);
    expect(h.calls.filter(([m]) => m === 'ask.confirm')).toHaveLength(0);

    await click(chips[0]);
    expect(h.store.route.value.name).toBe('review');
    expect(h.store.route.value).toMatchObject({ recordingId: DESIGN });
    expect((h.store.route.value as { atMs?: number }).atMs).toBeGreaterThanOrEqual(0);
  });

  it('stops an answer mid-stream and keeps what arrived', async () => {
    h = await mountApp({ name: 'ask', recordingId: DESIGN }, { m4: { ai: 'local' }, stepMs: 150 });
    await until(() => field() !== null);
    await type(question(), 'Who spoke first?');
    await click(button('Ask'));
    await until(() => (document.querySelector('.ask-answer--live .ask-text')?.textContent ?? '').startsWith('From what was said'));

    await click(button('Stop'));
    await until(() => answers().length === 1);

    expect(answer(0).textContent).toContain('stopped before it finished');
    expect(document.querySelector('[role="status"]')?.textContent).toBe('Stopped. The part of the answer that had arrived was saved.');
    expect(h.callsOf('ask.cancel')).toHaveLength(1);
  });

  it('asks before sending to an external service, and a decline sends nothing', async () => {
    h = await mountApp({ name: 'ask', recordingId: DESIGN }, { m4: { ai: 'ready' }, stepMs: 20 });
    await until(() => field() !== null);
    expect(document.querySelector('.ask-composer .field-note')?.textContent).toContain('go to Claude when you press Ask');

    await type(question(), 'Summarise the decisions.');
    await click(button('Ask'));
    await until(() => document.querySelector('#ask-confirm-title') !== null);
    expect(document.querySelector('#ask-confirm-title')?.textContent).toBe('Send this question to Claude?');

    await click(button("Don't send"));
    await until(() => document.querySelector('#ask-confirm-title') === null);
    expect(document.querySelector('.ask-question')).toBeNull();
    expect(document.querySelector('[role="status"]')?.textContent).toBe('Nothing was sent.');
    expect(answers()).toHaveLength(0);
    expect(h.callsOf('ask.confirm')).toEqual([{ jobId: expect.any(String) as unknown, approved: false }]);

    await type(question(), 'Summarise the decisions.');
    await click(button('Ask'));
    await until(() => document.querySelector('#ask-confirm-title') !== null);
    await click(button('Send'));
    await until(() => answers().length === 1);
    expect(answer(0).textContent).not.toContain('stayed on this PC');
  });

  it('clears the conversation after asking first', async () => {
    h = await mountApp({ name: 'ask', recordingId: DESIGN }, { m4: { ai: 'local' }, stepMs: 10 });
    await until(() => field() !== null);
    await type(question(), 'Anything open?');
    await click(button('Ask'));
    await until(() => answers().length === 1);

    await click(button('Clear conversation'));
    await click(button('Keep'));
    expect(answers()).toHaveLength(1);
    await click(button('Clear conversation'));
    await click(button('Clear'));
    await until(() => answers().length === 0);
    expect(h.callsOf('ask.clear')).toHaveLength(1);
    expect(document.querySelector('.ask-empty')).not.toBeNull();
  });
});
