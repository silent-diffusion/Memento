import { act } from 'preact/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import type { MockOptions } from '../../bridge/mock';
import type { GenerationOutput } from '../../bridge/types';
import { button, click, mountApp, press, until, type Harness } from '../../testing/appHarness';
import { applyOutput, liveOutputOf, type LiveOutput, type LivePass } from './liveOutput';
import { passMeta, passStats } from './LiveOutputSheet';

function at(live: LiveOutput, index: number): LivePass {
  const pass = live.passes[index];
  if (pass === undefined) {
    throw new Error(`no pass ${String(index)}`);
  }
  return pass;
}

function present(el: HTMLElement | null): HTMLElement {
  if (el === null) {
    throw new Error('not on screen');
  }
  return el;
}

const DESIGN = '20261005-160000-dsrev';

const sheet = (): HTMLElement | null => document.querySelector<HTMLElement>('.live-sheet');
const reply = (): HTMLElement | null => document.querySelector<HTMLElement>('.live-reply');
const groups = (): string[] => [...document.querySelectorAll('.live-group-label')].map((g) => g.firstChild?.textContent ?? '');
const current = (): string | null => document.querySelector('.live-pass[aria-current="true"] .live-pass-title')?.textContent ?? null;

/** jsdom has no layout: gives the reply well a height and a scroll position the test can move. */
function scrollable(el: HTMLElement): { top: () => number; scrollUp: () => Promise<void> } {
  let top = 0;
  Object.defineProperty(el, 'scrollHeight', { configurable: true, get: () => 2000 });
  Object.defineProperty(el, 'clientHeight', { configurable: true, get: () => 300 });
  Object.defineProperty(el, 'scrollTop', {
    configurable: true,
    get: () => top,
    set: (v: number) => {
      top = Math.min(v, 1700);
    },
  });
  return {
    top: () => top,
    scrollUp: async () => {
      top = 200;
      await act(async () => {
        el.dispatchEvent(new Event('scroll'));
        await Promise.resolve();
      });
    },
  };
}

describe('Live output (DESIGN.md §10, §11, against the browser-preview host)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const generate = async (mock: MockOptions): Promise<void> => {
    h = await mountApp({ name: 'builder', recordingId: DESIGN, templateId: null, documentId: null }, { stepMs: 250, ...mock });
    await until(() => document.querySelector('.builder-paper article.paper') !== null && document.querySelector('.mod') !== null);
    await click(button('Generate minutes'));
  };

  it('opens beside Cancel while the local model writes, streams the reply, and stays readable in the viewer once finished', async () => {
    // Slow steps, so the generation is still running when the sheet is closed and opened again, even on a busy machine.
    await generate({ m4: { ai: 'local' }, stepMs: 600 });
    await until(() => document.querySelector('.gen-card') !== null);
    const actions = [...document.querySelectorAll('.gen-card .gen-actions button')].map((b) => b.textContent);
    expect(actions).toEqual(['Cancel', 'Show live output']);
    await click(button('Show live output'));

    const dialog = document.querySelector('[role="dialog"]');
    expect(dialog?.getAttribute('aria-modal')).toBe('true');
    expect(document.getElementById(dialog?.getAttribute('aria-labelledby') ?? '')?.textContent).toBe('Live output');
    expect(document.querySelector('.live-sub')?.textContent).toMatch(/on this PC · Writing$/);

    // A map pass streams: its reply grows while it runs, with a caret and the token counters.
    await until(() => document.querySelector('.live-pass .live-pass-title')?.textContent === 'Segment the transcript');
    await until(() => (reply()?.textContent.length ?? 0) > 10 && document.querySelector('.live-caret') !== null, 10_000);
    const before = reply()?.textContent.length ?? 0;
    await until(() => (reply()?.textContent.length ?? 0) > before || document.querySelector('.live-caret') === null);
    expect(document.querySelector('.live-request')?.textContent).toMatch(/^System\n/);
    expect(document.querySelector('.live-request')?.textContent).toContain('[Transcript]');
    expect(document.querySelector('.live-stats')?.textContent).toMatch(/tokens/);
    expect(document.querySelector('.live-follow-note')?.textContent).toBe('Following the newest output');
    expect(document.querySelector('.live-whole')).toBeNull();

    // Closed and opened again while it runs: everything so far is still there.
    const passes = document.querySelectorAll('.live-pass').length;
    await press(sheet(), 'Escape');
    expect(sheet()).toBeNull();
    expect(h.store.route.value.name).toBe('builder');
    await click(button('Show live output'));
    expect(document.querySelectorAll('.live-pass').length).toBeGreaterThanOrEqual(passes);

    // The Builder opens the document; the sheet stays, read-only, with every step in pipeline order.
    await until(() => h.store.route.value.name === 'document', 30_000);
    await until(() => document.querySelector('.live-sub')?.textContent.endsWith('Finished · read only') === true);
    expect(groups()).toEqual(['Segment', 'Map', 'Reduce', 'Verify', 'Grounding']);
    expect(document.querySelector('.live-caret')).toBeNull();
    expect(document.querySelector('.live-follow-note')).toBeNull();
    expect(document.querySelectorAll('.live-dot--running')).toHaveLength(0);
    expect(document.querySelector('.sheet-footer-note')?.textContent).toContain('nothing leaves it');
    await click(button('Done'));
    expect(sheet()).toBeNull();

    // The viewer offers it until it is left; then it is gone.
    await until(() => document.querySelector('.doc-live') !== null);
    await click(button('Show live output'));
    expect(sheet()).not.toBeNull();
    await click(button('Done'));
    expect(liveOutputOf(h.store).value).not.toBeNull();
    await act(async () => {
      h.store.route.value = { name: 'review', recordingId: DESIGN };
      await Promise.resolve();
    });
    expect(liveOutputOf(h.store).value).toBeNull();
  }, 60_000);

  it('follows the newest output, pauses when the person scrolls up or picks a pass, and follows again on request', async () => {
    await generate({ m4: { ai: 'local' } });
    await until(() => document.querySelector('.gen-card') !== null);
    await click(button('Show live output'));
    await until(() => document.querySelector('.live-request') !== null && document.querySelector('.live-caret') !== null, 10_000);

    // Following moves the reply to its end as text arrives.
    const well = scrollable(present(reply()));
    await until(() => well.top() === 1700, 10_000);
    await well.scrollUp();
    expect(button('Follow live output')).toBeTruthy();
    expect(document.querySelector('.live-follow-note')).toBeNull();
    const paused = current();
    await until(() => document.querySelectorAll('.live-pass').length >= 5, 10_000);
    expect(current()).toBe(paused);

    await click(button('Follow live output'));
    expect(document.querySelector('.live-follow-note')).not.toBeNull();

    // Choosing an earlier pass pauses following too; arrow keys move between passes.
    const first = document.querySelector<HTMLElement>('.live-pass');
    await click(first);
    expect(current()).toBe('Segment the transcript');
    expect(button('Follow live output')).toBeTruthy();
    expect(document.querySelector('.live-reply')?.textContent).toMatch(/segment/);
    first?.focus();
    await press(first, 'ArrowDown');
    expect(current()).not.toBe('Segment the transcript');
    expect(document.activeElement?.getAttribute('aria-current')).toBe('true');
  }, 60_000);

  it('shows a cloud provider’s replies whole, with the request sent', async () => {
    h = await mountApp({ name: 'builder', recordingId: DESIGN, templateId: null, documentId: null }, { stepMs: 250, m4: { ai: 'ready' } });
    await until(() => document.querySelector('.mod') !== null);
    await click(button('Generate minutes'));
    await until(() => document.querySelector('#confirm-send-title') !== null);
    await click(button('Send'));
    await until(() => document.querySelector('.gen-card') !== null);
    await click(button('Show live output'));
    await until(() => document.querySelector('.live-request') !== null, 10_000);

    expect(document.querySelector('.live-sub')?.textContent).toBe('Claude · Sending and receiving');
    expect(document.querySelector('.live-whole')?.textContent).toBe('Replies from Claude arrive whole');
    expect(document.querySelector('#live-request-label')?.textContent).toBe('Request · sent to Claude');
    expect(document.querySelector('.live-caret')).toBeNull();
    await until(() => [...document.querySelectorAll('.live-pass-meta')].some((m) => m.textContent.includes('tokens')), 10_000);
    expect(document.querySelector('.sheet-footer-note')?.textContent).toContain('Only what was sent to Claude and what it answered');
  }, 60_000);

  it('applies token, reply and done events to the exchange, and words its counters', () => {
    const base: LiveOutput = { jobId: 'g1', recordingId: 'r', provider: { id: 'local', name: 'Local model', kind: 'local', modelLabel: 'Qwen3.5 4B' }, documentId: null, passes: [], ended: false, outcome: 'running' };
    const event = (e: Partial<GenerationOutput> & Pick<GenerationOutput, 'kind' | 'passId' | 'text'>): GenerationOutput => ({
      jobId: 'g1',
      step: null,
      title: null,
      streamed: null,
      outputTokens: null,
      promptTokens: null,
      tokensPerSecond: null,
      elapsedMs: null,
      stopReason: null,
      ...e,
    });
    let live = applyOutput(base, event({ kind: 'request', passId: 'p1', step: 'map', title: 'Decisions · segment 1 of 2', text: 'System\nx', streamed: true }));
    expect(passMeta(at(live, 0), live)).toBe('Reading the request');
    live = applyOutput(live, event({ kind: 'token', passId: 'p1', text: '{"a"', outputTokens: 3, tokensPerSecond: 50.25, elapsedMs: 40 }));
    live = applyOutput(live, event({ kind: 'token', passId: 'p1', text: ': 1}', outputTokens: 6 }));
    expect(live.passes[0]?.reply).toBe('{"a": 1}');
    expect(passMeta(at(live, 0), live)).toBe('Writing · 6 tokens');
    live = applyOutput(live, event({ kind: 'reply', passId: 'p1', text: '{"a": 1}', outputTokens: 7, promptTokens: 2931, tokensPerSecond: 51.5, elapsedMs: 1400, stopReason: 'eog' }));
    expect(passStats(at(live, 0))).toEqual(['7 tokens', '51.5 tokens/s', '1.4 s', 'request 2,931 tokens']);
    expect(applyOutput(live, event({ kind: 'token', passId: 'p1', text: 'late' })).passes[0]?.reply).toBe('{"a": 1}');
    live = applyOutput(live, event({ kind: 'request', passId: 'p2', step: 'verify', title: 'Check', text: 'System\ny', streamed: true }));
    live = applyOutput(live, event({ kind: 'done', passId: '', text: '' }));
    expect(live.ended).toBe(true);
    expect(live.passes.map((p) => p.status)).toEqual(['done', 'stopped']);
    expect(passMeta(at(live, 1), live)).toBe('Stopped');
  });
});
