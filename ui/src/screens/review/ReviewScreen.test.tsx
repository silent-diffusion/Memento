import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { render } from 'preact';
import { act } from 'preact/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../App';
import { createBridgeClient, type BridgeClient } from '../../bridge/client';
import { silenceGaps, skippedBars } from '../../format/silences';
import { loadInitialData, refreshLibrary } from '../../state/data';
import { connectEvents, createStore, type AppStore } from '../../state/store';

const reviewCss = readFileSync(resolve(__dirname, '../../styles/review.css'), 'utf8').replace(/\r\n/g, '\n');

const quiet = { info: () => undefined, warn: () => undefined };
const NOW = new Date(2026, 9, 6, 15, 0);
const DESIGN_REVIEW = '20261005-160000-dsrev';

describe('Review and transcript (against the browser-preview host)', () => {
  let container: HTMLDivElement;
  let store: AppStore;
  let bridge: BridgeClient;
  let disconnect: () => void;

  const settle = async (ms = 0): Promise<void> => {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, ms));
    });
  };

  const until = async (check: () => boolean, timeoutMs = 8000): Promise<void> => {
    const started = Date.now();
    while (!check()) {
      if (Date.now() - started > timeoutMs) {
        throw new Error('timed out waiting for the screen');
      }
      await settle(20);
    }
  };

  const button = (name: string, root: ParentNode = document.body): HTMLButtonElement => {
    const match = [...root.querySelectorAll('button')].find((b) => (b.getAttribute('aria-label') ?? b.textContent.trim()) === name);
    if (match === undefined) {
      throw new Error(`no button named ${name}`);
    }
    return match;
  };

  const click = async (el: HTMLElement): Promise<void> => {
    await act(async () => {
      el.click();
      await Promise.resolve();
    });
  };

  const typeInto = async (input: HTMLInputElement, text: string): Promise<void> => {
    await act(async () => {
      input.value = text;
      input.dispatchEvent(new Event('input', { bubbles: true }));
      await Promise.resolve();
    });
  };

  const press = async (target: HTMLElement, key: string, init: KeyboardEventInit = {}): Promise<void> => {
    await act(async () => {
      target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init }));
      await Promise.resolve();
    });
  };

  const open = async (recordingId = DESIGN_REVIEW): Promise<void> => {
    bridge = createBridgeClient({ logger: quiet, mock: { live: false, recovery: false, now: () => NOW.getTime() } });
    store = createStore(false);
    disconnect = connectEvents(bridge, store, {
      onLibraryChanged: () => {
        void refreshLibrary(bridge, store);
      },
    });
    await act(async () => {
      await loadInitialData(bridge, store);
    });
    store.route.value = { name: 'review', recordingId };
    await act(async () => {
      render(<App bridge={bridge} store={store} now={() => NOW} />, container);
      await Promise.resolve();
    });
    await until(() => container.querySelector('.review-panes') !== null);
  };

  const chapters = (): string[] =>
    [...container.querySelectorAll('.review-outline .outline-group:first-child .chap')].map((c) => c.textContent.trim());

  beforeEach(() => {
    container = document.createElement('div');
    document.body.append(container);
  });

  afterEach(() => {
    render(null, container);
    disconnect();
    container.remove();
    document.body.innerHTML = '';
  });

  it('shows the header meta, outline, the transcript and the details facts', async () => {
    await open();
    expect(container.querySelector('.spoke-title')?.textContent).toBe('Design review: library screen');
    expect(container.querySelector('.spoke-meta')?.textContent).toBe('Meeting · Yesterday, 4:00 PM · 1:10:02 · 4 people · audio + video');
    expect(chapters()).toEqual([
      '0:00Opening and goals',
      '6:30Walkthrough of mockups',
      '18:10Row density and status pills',
      '34:00Dark theme scope',
      '52:40Empty state',
      '1:01:20Owners and next steps',
    ]);
    expect(container.textContent).toContain('Highlights · 3');
    await until(() => container.querySelector('.segm') !== null);
    expect(container.querySelector('.segm .segm-speaker-name')?.textContent).toBe('Sam Okafor');
    expect(container.querySelector('.segm .segm-at')?.textContent).toBe('00:00:02');
    expect(container.querySelector('.transcript-hint')?.textContent).toBe('Click the time to play a line · click the words to correct them');
    expect(container.querySelector<HTMLInputElement>('#tx-search')?.disabled).toBe(false);
    // The preview host's peaks.json (a blob URL) is fetched and drawn as 160 bars.
    await until(() => container.querySelectorAll('.wave-bar').length === 160);
    const facts = [...container.querySelectorAll('.facts dt')].map((dt) => dt.textContent);
    expect(facts).toEqual(['Type', 'Recorded', 'Duration', 'Platform', 'Tracks', 'Purpose']);
    expect(container.querySelector('.fact-mono')?.textContent).toBe('1:10:02');
    expect(container.querySelector('.detail-caption')?.textContent).toBe('agenda.docx · parsed locally · Replace');
    // People are the transcript's speakers with talk-time shares, then the participant they do not name.
    const people = [...container.querySelectorAll('.person')].map((p) => [p.querySelector('.person-name')?.textContent, p.querySelector('.person-share')?.textContent.trim()]);
    expect(people.map(([name]) => name)).toEqual(['Sam Okafor', 'Aiko Tanaka', 'Lena Fischer', 'Speaker 4', 'Jonah Berg']);
    expect(people.slice(0, 4).every(([, share]) => /^\d+%$/.test(share ?? ''))).toBe(true);
    expect(people[4]?.[1]).toBe('—');
    expect(container.textContent).toContain('Also listed as participants');
  });

  it('fills the window with three panes that each scroll on their own, the player pinned above the transcript', async () => {
    await open();
    await until(() => container.querySelector('.segm') !== null);
    // The shell is fixed to the window height for Review only; other screens scroll the page.
    expect(container.querySelector('.shell')?.classList.contains('shell--panes')).toBe(true);
    const panes = container.querySelector('.review-panes');
    const outline = panes?.querySelector(':scope > .review-outline');
    const centre = panes?.querySelector(':scope > .review-centre');
    const details = panes?.querySelector(':scope > .review-details');
    expect([outline, centre, details].every((pane) => pane !== null && pane !== undefined)).toBe(true);
    // The centre column is the pinned player strip, then the transcript's own scrolling area.
    expect([...(centre?.children ?? [])].map((c) => c.className)).toEqual(['player', 'review-scroll']);
    const scroller = centre?.querySelector(':scope > .review-scroll');
    expect(scroller?.querySelector('.segm-list')).not.toBeNull();
    expect(scroller?.querySelector('.player')).toBeNull();
    // Three separate scroll containers: none sits inside another.
    const containers = [outline, scroller, details];
    for (const a of containers) {
      for (const b of containers) {
        expect(a !== b && a?.contains(b ?? null) === true).toBe(false);
      }
    }
    // Side by side they scroll independently; below 1024 px the panes stack and the page scrolls.
    const wide = /@media \(min-width: 1024px\) \{([\s\S]*?)\n\}/.exec(reviewCss)?.[1] ?? '';
    expect(wide).toMatch(/\.shell--panes \{\s*height: 100vh;/);
    expect(wide).toMatch(/\.review-outline,\s*\.review-details,\s*\.review-scroll \{\s*overflow-y: auto;/);
    const narrow = /@media \(max-width: 1023px\) \{([\s\S]*?)\n\}/.exec(reviewCss)?.[1] ?? '';
    expect(narrow).toContain('flex-direction: column;');
    expect(narrow).not.toContain('overflow');

    await act(async () => {
      store.route.value = { name: 'library' };
      await Promise.resolve();
    });
    await until(() => container.querySelector('.review-panes') === null);
    expect(container.querySelector('.shell')?.classList.contains('shell--panes')).toBe(false);
  });

  it('floats the people menu above the scrolling outline instead of inside it', async () => {
    await open();
    await until(() => container.querySelector('.person .person-merge') !== null);
    const trigger = container.querySelector<HTMLButtonElement>('.review-outline .person-merge');
    if (trigger === null) {
      throw new Error('no merge button');
    }
    await click(trigger);
    const menu = document.querySelector<HTMLElement>('.speaker-menu');
    expect(menu?.closest('.popover-layer')?.parentElement).toBe(document.body);
    expect(container.querySelector('.review-outline .speaker-menu')).toBeNull();
    // The other speakers, in a list that scrolls within the room beside the button; the search field has focus.
    expect([...(menu?.querySelectorAll('[role="option"]') ?? [])].map((o) => o.textContent)).toEqual(['Aiko Tanaka', 'Lena Fischer', 'Speaker 4']);
    expect(menu?.style.maxHeight).toMatch(/^\d+px$/);
    const search = menu?.querySelector<HTMLInputElement>('input[role="combobox"]');
    expect(document.activeElement).toBe(search);
    // Esc closes it and gives focus back to the button in the pane.
    await press(search as HTMLElement, 'Escape');
    expect(document.querySelector('.speaker-menu')).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it('offers Skip silences beside the speed, remembers it, and dims the skipped waveform bars', async () => {
    localStorage.clear();
    await open();
    await until(() => container.querySelectorAll('.wave-bar').length === 160 && container.querySelector('.segm') !== null);
    const chip = (): HTMLButtonElement => button('Skip silences', container);
    expect(chip().previousElementSibling?.classList.contains('speed-menu')).toBe(true);
    expect(chip().disabled).toBe(false);
    expect(chip().getAttribute('aria-pressed')).toBe('false');
    expect(container.querySelectorAll('.wave-bar.skipped')).toHaveLength(0);

    await click(chip());
    expect(chip().getAttribute('aria-pressed')).toBe('true');
    expect(chip().classList.contains('on')).toBe(true);
    expect(localStorage.getItem('memento.ui.review.skipSilences')).toBe('1');
    // The dimmed bars are exactly the ones standing for the transcript's silences.
    const { transcript } = await bridge.call('transcript.get', { recordingId: DESIGN_REVIEW });
    const gaps = silenceGaps(transcript?.segments ?? []);
    const expected = skippedBars(160, store.library.value?.recordings.find((r) => r.id === DESIGN_REVIEW)?.durationMs ?? 0, gaps).filter(Boolean).length;
    expect(gaps.length).toBeGreaterThan(0);
    expect(expected).toBeGreaterThan(0);
    expect(container.querySelectorAll('.wave-bar.skipped')).toHaveLength(expected);

    // Reopened, Review remembers the choice (local UI state on this PC).
    render(null, container);
    disconnect();
    await open();
    await until(() => container.querySelector('.player-silences') !== null);
    expect(chip().getAttribute('aria-pressed')).toBe('true');
    await click(chip());
    expect(localStorage.getItem('memento.ui.review.skipSilences')).toBe('0');
    localStorage.clear();
  });

  it('turns Skip silences off until there is a transcript', async () => {
    localStorage.setItem('memento.ui.review.skipSilences', '1');
    await open('20260909-143000-retro');
    await until(() => container.textContent.includes('Not transcribed yet'));
    const chip = button('Skip silences', container);
    expect(chip.disabled).toBe(true);
    expect(chip.title).toBe('Available once transcribed');
    expect(chip.getAttribute('aria-pressed')).toBe('false');
    localStorage.clear();
  });

  it('says plainly when a recording has no transcript and starts one on request', async () => {
    await open('20260909-143000-retro');
    await until(() => container.textContent.includes('Not transcribed yet'));
    expect(container.querySelector('.segm')).toBeNull();
    expect(container.querySelector<HTMLInputElement>('#tx-search')?.disabled).toBe(true);
    expect(container.querySelector<HTMLInputElement>('#tx-search')?.placeholder).toBe('No transcript to search yet');
    expect([...container.querySelectorAll('.person-share')].map((s) => s.textContent.trim())).toEqual(['—', '—', '—']);
    await click(button('Transcribe now'));
    await until(() => container.textContent.includes('Waiting to transcribe') || container.textContent.includes('Transcribing'));
  });

  it('adds a chapter at the playhead in time order', async () => {
    await open();
    const scrubber = container.querySelector<HTMLElement>('.scrubber');
    if (scrubber === null) {
      throw new Error('no scrubber');
    }
    // 30 s steps with Shift: 36 presses land on 18:00, between "Walkthrough" and "Row density".
    for (let i = 0; i < 36; i++) {
      await press(scrubber, 'ArrowRight', { shiftKey: true });
    }
    expect(scrubber.getAttribute('aria-valuetext')).toBe('18:00 of 1:10:02');
    await press(scrubber, 'ArrowLeft');
    expect(scrubber.getAttribute('aria-valuenow')).toBe('1075');
    await press(scrubber, 'End');
    expect(scrubber.getAttribute('aria-valuenow')).toBe('4202');
    await press(scrubber, 'Home');
    for (let i = 0; i < 36; i++) {
      await press(scrubber, 'ArrowRight', { shiftKey: true });
    }

    await click(button('Add a chapter at 18:00'));
    const input = container.querySelector<HTMLInputElement>('.chap--new input');
    expect(document.activeElement).toBe(input);
    expect(chapters()[2]).toBe('18:00');
    if (input === null) {
      throw new Error('no chapter input');
    }
    await typeInto(input, 'Pill colours');
    await press(input, 'Enter');
    await until(() => chapters().length === 7);
    expect(chapters().slice(1, 4)).toEqual(['6:30Walkthrough of mockups', '18:00Pill colours', '18:10Row density and status pills']);
    expect(container.querySelector('.chap.on')?.textContent).toBe('18:00Pill colours');
  });

  it('seeks from a chapter, adds a highlight and a topic, and renames a person', async () => {
    await open();
    await click(button('Play from 34:00, Dark theme scope'));
    expect(container.querySelector('.scrubber')?.getAttribute('aria-valuetext')).toBe('34:00 of 1:10:02');
    expect(container.querySelector('.transcript-head .lbl')?.textContent).toBe('Dark theme scope');

    await click(button('Highlight'));
    await until(() => container.textContent.includes('Highlights · 4'));

    await click(button('Add a topic'));
    const topic = container.querySelector<HTMLInputElement>('.outline-group--pad input');
    if (topic === null) {
      throw new Error('no topic input');
    }
    await typeInto(topic, 'Accessibility');
    await press(topic, 'Enter');
    await until(() => [...container.querySelectorAll('.outline-group--pad .pill')].some((p) => p.textContent.includes('Accessibility')));
    expect((await bridge.call('project.get', { recordingId: DESIGN_REVIEW })).topics.map((t) => t.label)).toContain('Accessibility');

    await click(button('Rename Jonah Berg'));
    const rename = container.querySelector<HTMLInputElement>('.person-input');
    if (rename === null) {
      throw new Error('no rename input');
    }
    await typeInto(rename, 'Jonah Berg-Larsen');
    await press(rename, 'Enter');
    await until(() => container.textContent.includes('Jonah Berg-Larsen'));
    expect((await bridge.call('project.get', { recordingId: DESIGN_REVIEW })).details.participants).toContain('Jonah Berg-Larsen');
  });

  it('switches Details, Documents and History with the arrow keys and colours history dots', async () => {
    await open('20260930-130500-onbrd');
    const details = container.querySelector<HTMLButtonElement>('#review-tab-details') ?? button('Details');
    details.focus();
    await press(details, 'ArrowRight');
    expect(container.querySelector('[role="tab"][aria-selected="true"]')?.textContent).toBe('Documents');
    await until(() => container.textContent.includes('No documents yet'));
    expect(container.textContent).toContain('Documents are saved inside this recording and can be exported on their own.');
    await press(button('Documents'), 'ArrowRight');
    expect(container.querySelector('[role="tab"][aria-selected="true"]')?.textContent).toBe('History');
    const dots = [...container.querySelectorAll('.history-dot')].map((d) => d.className.replace('history-dot history-dot--', ''));
    expect(dots).toEqual(['ok', 'ok', 'failed']);
    // Retry is live now: it queues the transcript stage again.
    const retry = button('Retry: Transcription failed');
    expect(retry.disabled).toBe(false);
    await click(retry);
    await until(() => !container.textContent.includes('Transcription failed') || container.textContent.includes('Transcribing'));
  });

  it('opens Export copies (M3), and Create document opens the Document builder (M4)', async () => {
    await open();
    await click(button('Export'));
    expect(document.querySelector('[role="dialog"] h2')?.textContent).toBe('Export copies');
    await click(button('Cancel'));
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    await click(button('Create document'));
    expect(store.route.value).toEqual({ name: 'builder', recordingId: DESIGN_REVIEW, templateId: null, documentId: null });
  });

  it('offers Reprocess as a submenu and deletes back to the Library', async () => {
    await open();
    await until(() => container.querySelector('.segm') !== null);
    await click(button('More actions'));
    const group = document.querySelector('[role="group"][aria-label="Reprocess"]');
    expect([...(group?.querySelectorAll('[role="menuitem"]') ?? [])].map((m) => [m.textContent, m.getAttribute('aria-disabled')])).toEqual([
      ['Transcribe again…', null],
      ['Identify speakers again', null],
    ]);
    const remove = [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find((m) => m.textContent === 'Delete');
    if (remove === undefined) {
      throw new Error('no Delete');
    }
    await click(remove);
    await until(() => document.querySelector('[role="dialog"]') !== null);
    expect(document.querySelector('[role="dialog"] h2')?.textContent).toBe('Delete "Design review: library screen"?');
    await click(button('Delete', document.querySelector('[role="dialog"]') ?? document));
    await until(() => store.route.value.name === 'library');
    await expect(bridge.call('project.get', { recordingId: DESIGN_REVIEW })).rejects.toMatchObject({ code: 'project.notFound' });
  });

  it('edits details in the sheet and saves them after the pause in typing', async () => {
    await open();
    const call = vi.spyOn(bridge, 'call');
    await click(button('Edit details'));
    const platform = [...document.querySelectorAll<HTMLInputElement>('.sheet-input')].find((i) => i.value === 'Zoom');
    if (platform === undefined) {
      throw new Error('no platform field');
    }
    await typeInto(platform, 'Teams');
    expect(document.querySelector('.sheet-save')?.textContent).toBe('Saving…');
    await settle(700);
    expect(call.mock.calls.filter(([method]) => method === 'project.updateDetails').map(([, params]) => params)).toEqual([
      { recordingId: DESIGN_REVIEW, details: { platform: 'Teams' } },
    ]);
    expect(document.querySelector('.sheet-save')?.textContent).toBe('Saved');
    await click(button('Done'));
    await until(() => [...container.querySelectorAll('.facts dd')].some((d) => d.textContent === 'Teams'));
  });
});
