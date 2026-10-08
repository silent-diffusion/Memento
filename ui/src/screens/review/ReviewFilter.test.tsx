import { act } from 'preact/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import type { MockOptions } from '../../bridge/mock';
import type { Project, Transcript, TranscriptCopyParams } from '../../bridge/types';
import { button, click, mountApp, settle, type, until, type Harness } from '../../testing/appHarness';
import { undoOf } from '../../state/undo';

const NOW = new Date(2026, 9, 6, 15, 0);

/** The value, or a failed test naming what was missing. */
function must<T>(value: T | null | undefined, what: string): T {
  if (value === null || value === undefined) {
    throw new Error(`no ${what}`);
  }
  return value;
}
const DESIGN_REVIEW = '20261005-160000-dsrev';

describe('transcript filters and Copy in Review (DESIGN.md §9, after 1.2.0)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
    globalThis.localStorage.clear();
  });

  const open = async (mock: MockOptions = {}): Promise<void> => {
    h = await mountApp({ name: 'review', recordingId: DESIGN_REVIEW }, mock, NOW);
    await until(() => h.container.querySelector('.segm') !== null);
  };

  const transcript = async (): Promise<Transcript> => {
    const { transcript: t } = await h.bridge.call('transcript.get', { recordingId: DESIGN_REVIEW });
    if (t === null) {
      throw new Error('no transcript');
    }
    return t;
  };

  const project = (): Promise<Project> => h.bridge.call('project.get', { recordingId: DESIGN_REVIEW });

  const line = (): string => h.container.querySelector('.tx-filter-line-text')?.textContent ?? '';
  const list = (): Element | null => h.container.querySelector('.segm-list');
  const rows = (): HTMLElement[] => [...h.container.querySelectorAll<HTMLElement>('.segm')];
  const status = (): string => h.container.querySelector('.undo-status')?.textContent ?? '';
  const personButton = (name: string): HTMLButtonElement => {
    const match = [...h.container.querySelectorAll<HTMLButtonElement>('.person-filter')].find((b) => b.querySelector('.person-filter-name')?.textContent === name);
    if (match === undefined) {
      throw new Error(`no person ${name}`);
    }
    return match;
  };

  const key = async (k: string, target: Element = document.body, init: KeyboardEventInit = {}): Promise<KeyboardEvent> => {
    const event = new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true, ...init });
    await act(async () => {
      target.dispatchEvent(event);
      await Promise.resolve();
    });
    await settle(10);
    return event;
  };

  const openFilter = async (): Promise<HTMLElement> => {
    await click(h.container.querySelector('.tx-filter-btn'));
    await until(() => document.querySelector('.tx-filter-pop') !== null);
    return must(document.querySelector<HTMLElement>('.tx-filter-pop'), 'filter popover');
  };

  const option = (pop: HTMLElement, name: string): HTMLInputElement => {
    const label = [...pop.querySelectorAll('label')].find((l) => l.querySelector('.tx-filter-option-name')?.textContent === name);
    const input = label?.querySelector('input');
    if (input === null || input === undefined) {
      throw new Error(`no filter option ${name}`);
    }
    return input;
  };

  it('a speaker in People shows only their lines, with a pressed row and a count; again shows everyone', async () => {
    await open();
    const t = await transcript();
    const sarah = t.speakers[0];
    if (sarah === undefined) {
      throw new Error('no speakers');
    }
    const theirs = t.segments.filter((s) => s.speaker === sarah.id).length;

    await click(personButton(sarah.name));

    expect(personButton(sarah.name).getAttribute('aria-pressed')).toBe('true');
    expect(personButton(sarah.name).querySelector('.person-count')?.textContent).toBe(`${theirs} lines`);
    expect(h.container.querySelector(`.person[data-speaker-id="${sarah.id}"]`)?.classList.contains('person--filtered')).toBe(true);
    expect(line()).toBe(`Showing ${theirs} of ${t.segments.length} lines · ${sarah.name}`);
    expect(list()?.getAttribute('aria-label')).toBe(`Transcript, ${theirs} of ${t.segments.length} lines shown`);
    expect(rows().length).toBeGreaterThan(0);
    expect(rows().every((r) => r.querySelector('.segm-speaker-name')?.textContent === sarah.name)).toBe(true);
    expect(rows()[0]?.getAttribute('aria-setsize')).toBe(String(theirs));

    await click(personButton(sarah.name));
    expect(personButton(sarah.name).getAttribute('aria-pressed')).toBe('false');
    expect(h.container.querySelector('.tx-filter-line')).toBeNull();
    expect(list()?.getAttribute('aria-label')).toBe(`Transcript, ${t.segments.length} lines`);
  });

  it('Ctrl+click adds a speaker; Show all and Esc clear the filter', async () => {
    await open();
    const t = await transcript();
    const [a, b] = t.speakers;
    if (a === undefined || b === undefined) {
      throw new Error('needs two speakers');
    }
    await click(personButton(a.name));
    await act(async () => {
      personButton(b.name).dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, ctrlKey: true }));
      await Promise.resolve();
    });
    const both = t.segments.filter((s) => s.speaker === a.id || s.speaker === b.id).length;
    expect(line()).toBe(`Showing ${both} of ${t.segments.length} lines · ${a.name} or ${b.name}`);

    await click(button('Show all'));
    expect(h.container.querySelector('.tx-filter-line')).toBeNull();

    await click(personButton(a.name));
    expect(h.container.querySelector('.tx-filter-line')).not.toBeNull();
    // Esc in a text field belongs to the field.
    await key('Escape', must(h.container.querySelector('#tx-search'), 'search field'));
    expect(h.container.querySelector('.tx-filter-line')).not.toBeNull();
    await key('Escape');
    expect(h.container.querySelector('.tx-filter-line')).toBeNull();
  });

  it('the Filter menu combines highlights, uncertain words, edited lines, a chapter and the search', async () => {
    await open();
    const t = await transcript();
    const p = await project();
    const total = t.segments.length;

    // Highlights: the lines the recording's highlights belong to.
    await h.bridge.call('transcript.editSegment', { recordingId: DESIGN_REVIEW, segmentId: t.segments[3]?.id ?? '', text: 'Corrected by hand.' });
    let pop = await openFilter();
    expect(pop.getAttribute('role')).toBe('dialog');
    expect(document.activeElement?.tagName).toBe('INPUT');
    await click(option(pop, 'Highlights'));
    const highlighted = new Set(p.highlights.map((x) => x.segmentId).filter((id): id is string => id !== null));
    expect(line()).toBe(`Showing ${highlighted.size} of ${total} lines · Highlights`);

    // Edited lines: the sample's corrections and the one just made.
    const edited = (await transcript()).segments.filter((s) => s.edited !== null).length;
    await until(() => option(pop, 'Edited lines').closest('label')?.querySelector('.tx-filter-option-count')?.textContent === String(edited));
    await click(option(pop, 'Highlights'));
    await click(option(pop, 'Edited lines'));
    expect(line()).toBe(`Showing ${edited} of ${total} ${edited === 1 ? 'line' : 'lines'} · Edited lines`.replace(`of ${total} line ·`, `of ${total} lines ·`));
    expect(rows().map((r) => r.querySelector('.segm-text')?.textContent)).toContain('Corrected by hand.');
    expect(rows().every((r) => r.querySelector('.segm-edited') !== null)).toBe(true);
    await click(option(pop, 'Edited lines'));

    // Uncertain words: lines with a word below the threshold.
    const uncertain = (await transcript()).segments.filter((s) => s.words.some((w) => w.c < t.lowConfidenceThreshold)).length;
    await click(option(pop, 'Uncertain words'));
    expect(line()).toBe(`Showing ${uncertain} of ${total} lines · Uncertain words`);
    await click(option(pop, 'Uncertain words'));

    // A chapter: its span, from its time to the next chapter's.
    const chapters = [...p.chapters].sort((x, y) => x.atMs - y.atMs);
    const first = chapters[0];
    if (first !== undefined) {
      const end = chapters[1]?.atMs ?? Number.POSITIVE_INFINITY;
      const inside = t.segments.filter((s) => s.start * 1000 >= first.atMs && s.start * 1000 < end).length;
      const radio = [...pop.querySelectorAll<HTMLInputElement>('input[type="radio"]')][1];
      await click(radio);
      expect(line()).toBe(`Showing ${inside} of ${total} lines · ${first.title}`);
      await click([...pop.querySelectorAll<HTMLInputElement>('input[type="radio"]')][0]);
    }

    // Esc closes the menu (and leaves the filter alone); the search is offered once there is a query.
    await key('Escape', pop);
    expect(document.querySelector('.tx-filter-pop')).toBeNull();
    expect(document.activeElement?.classList.contains('tx-filter-btn')).toBe(true);
    await type(h.container.querySelector('#tx-search'), 'sidebar');
    pop = await openFilter();
    await click(option(pop, 'The search “sidebar”'));
    const matching = t.segments.filter((s) => /\bsidebar\b/i.test(s.text)).length;
    expect(line()).toBe(`Showing ${matching} of ${total} lines · “sidebar”`);
    const speaker = t.speakers[0];
    if (speaker !== undefined) {
      await click(option(pop, speaker.name));
      const both = t.segments.filter((s) => /\bsidebar\b/i.test(s.text) && s.speaker === speaker.id).length;
      expect(line()).toBe(both === 0 ? `No lines match · ${speaker.name} · “sidebar”` : `Showing ${both} of ${total} lines · ${speaker.name} · “sidebar”`);
    }
    await click(button('Show all'));
    expect(h.container.querySelector('.tx-filter-btn')?.classList.contains('on')).toBe(false);
  });

  it('keeps the arrow keys, the playhead and seeking on the lines shown', async () => {
    await open();
    const t = await transcript();
    const speaker = t.speakers[1];
    if (speaker === undefined) {
      throw new Error('needs a second speaker');
    }
    const theirs = t.segments.filter((s) => s.speaker === speaker.id);
    await click(personButton(speaker.name));

    const firstRow = must(rows()[0], 'first row');
    await click(firstRow);
    expect(firstRow.classList.contains('now')).toBe(true);
    firstRow.focus();
    await key('ArrowDown', firstRow);
    await until(() => document.activeElement?.getAttribute('data-index') === '1');
    expect(document.activeElement?.getAttribute('data-segment-id')).toBe(theirs[1]?.id);
    await key('Enter', must(document.activeElement, 'focused row'));
    await until(() => h.container.querySelector('.segm.now')?.getAttribute('data-segment-id') === theirs[1]?.id);
  });

  it('edits a line in place while filtered, and Undo brings it back into the filter', async () => {
    await open();
    const t = await transcript();
    const uncertain = t.segments.filter((s) => s.words.some((w) => w.c < t.lowConfidenceThreshold));
    const pop = await openFilter();
    await click(option(pop, 'Uncertain words'));
    await key('Escape', pop);
    const target = must(rows()[0], 'first row');
    const id = target.getAttribute('data-segment-id');
    expect(id).toBe(uncertain[0]?.id);

    await click(target.querySelector('.segm-text'));
    const editor = h.container.querySelector<HTMLTextAreaElement>('.segm-editor');
    expect(editor).not.toBeNull();
    await type(editor, 'Every word checked now.');
    await key('Enter', editor as Element);
    await until(() => h.callsOf('transcript.editSegment').length === 1);
    // The new words are certain, so the corrected line leaves the filter once saved.
    const after = (await transcript()).segments.filter((s) => s.words.some((w) => w.c < t.lowConfidenceThreshold));
    expect(after.some((s) => s.id === id)).toBe(false);
    await until(() => line() === `Showing ${after.length} of ${t.segments.length} lines · Uncertain words`);
    expect(rows().some((r) => r.getAttribute('data-segment-id') === id)).toBe(false);

    // Undo puts the words back (as certain words: the host re-aligns them); the filter follows the transcript as it is now.
    await act(async () => {
      await undoOf(h.store).undo();
    });
    await until(() => h.callsOf('transcript.editSegment').length === 2);
    const restored = await transcript();
    expect(restored.segments.find((s) => s.id === id)?.text).toBe(t.segments.find((s) => s.id === id)?.text);
    const now = restored.segments.filter((s) => s.words.some((w) => w.c < t.lowConfidenceThreshold)).length;
    await until(() => line() === `Showing ${now} of ${t.segments.length} lines · Uncertain words`);
  });

  it('keeps an edited line shown under a speaker filter, and Undo there restores it in place', async () => {
    await open();
    const t = await transcript();
    const speaker = t.speakers[0];
    if (speaker === undefined) {
      throw new Error('no speakers');
    }
    await click(personButton(speaker.name));
    const row = must(rows()[1], 'second row');
    const id = row.getAttribute('data-segment-id');
    const original = t.segments.find((s) => s.id === id)?.text;
    await click(row.querySelector('.segm-text'));
    const editor = h.container.querySelector<HTMLTextAreaElement>('.segm-editor');
    await type(editor, 'Changed while filtered.');
    await key('Enter', editor as Element);
    await until(() => h.container.querySelector('.segm-editor') === null);
    await until(() => rows()[1]?.querySelector('.segm-text')?.textContent === 'Changed while filtered.');
    expect(rows()[1]?.getAttribute('data-segment-id')).toBe(id);
    // Focus comes back to the corrected line, as without a filter.
    await until(() => document.activeElement?.getAttribute('data-segment-id') === id);
    await act(async () => {
      await undoOf(h.store).undo();
    });
    await until(() => rows()[1]?.querySelector('.segm-text')?.textContent === original);
    expect(line()).toContain(speaker.name);
  });

  it('copies the lines shown through the host, and says so quietly beside Undo', async () => {
    await open();
    const t = await transcript();
    const speaker = t.speakers[0];
    if (speaker === undefined) {
      throw new Error('no speakers');
    }
    await click(personButton(speaker.name));
    const theirs = t.segments.filter((s) => s.speaker === speaker.id).map((s) => s.id);

    await click(button('Copy'));
    await until(() => h.callsOf('transcript.copy').length === 1);
    const params = h.callsOf('transcript.copy')[0] as TranscriptCopyParams;
    expect(params).toEqual({ recordingId: DESIGN_REVIEW, format: 'text', options: { timestamps: true, speakers: true, layout: 'auto' }, segmentIds: theirs });
    await until(() => status() === `Copied ${theirs.length} of ${t.segments.length} lines as text`);
    expect(document.querySelector('.toast')).toBeNull();

    // The More menu offers both forms; Markdown is remembered for the next quick Copy.
    await click(button('More actions'));
    await click(button('As Markdown'));
    await until(() => h.callsOf('transcript.copy').length === 2);
    expect((h.callsOf('transcript.copy')[1] as TranscriptCopyParams).format).toBe('markdown');
    await click(button('Copy'));
    await until(() => h.callsOf('transcript.copy').length === 3);
    expect((h.callsOf('transcript.copy')[2] as TranscriptCopyParams).format).toBe('markdown');

    // Without a filter the whole transcript goes, with no ids.
    await click(button('Show all'));
    await click(button('More actions'));
    await click(button('As text'));
    await until(() => h.callsOf('transcript.copy').length === 4);
    expect((h.callsOf('transcript.copy')[3] as TranscriptCopyParams).segmentIds).toBeUndefined();
    await until(() => status() === 'Copied the transcript as text');
  });

  it('a clipboard another program holds gets the host’s words in a warning', async () => {
    await open({ clipboardBusy: true });
    await click(button('More actions'));
    await click(button('As text'));
    await until(() => document.body.textContent.includes('The transcript was not copied'));
    expect(document.body.textContent).toContain('Windows did not let Memento use the clipboard, so the transcript was not copied. Nothing was changed.');
    expect(status()).toBe('');
  });

  it('forgets the filter when another recording opens', async () => {
    await open();
    const t = await transcript();
    const speaker = t.speakers[0];
    if (speaker === undefined) {
      throw new Error('no speakers');
    }
    await click(personButton(speaker.name));
    expect(h.container.querySelector('.tx-filter-line')).not.toBeNull();
    // The Export dialog over Review keeps it (the recording stays open).
    await click(button('Export'));
    await until(() => document.querySelector('.export-dialog') !== null);
    await click(document.querySelector('.export-close'));
    expect(h.container.querySelector('.tx-filter-line')).not.toBeNull();
    await act(async () => {
      h.store.route.value = { name: 'library' };
      await Promise.resolve();
    });
    await settle(20);
    await act(async () => {
      h.store.route.value = { name: 'review', recordingId: DESIGN_REVIEW };
      await Promise.resolve();
    });
    await until(() => h.container.querySelector('.segm') !== null);
    expect(h.container.querySelector('.tx-filter-line')).toBeNull();
  });
});
