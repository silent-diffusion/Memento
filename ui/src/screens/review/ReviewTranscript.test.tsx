import { render } from 'preact';
import { act } from 'preact/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../App';
import { createBridgeClient, type BridgeClient } from '../../bridge/client';
import type { MockOptions } from '../../bridge/mock';
import { loadInitialData, refreshLibrary } from '../../state/data';
import { connectEvents, createStore, type AppStore } from '../../state/store';

const quiet = { info: () => undefined, warn: () => undefined };
const NOW = new Date(2026, 9, 6, 15, 0);
const DESIGN_REVIEW = '20261005-160000-dsrev';

describe('Review transcript (M2, against the browser-preview host)', () => {
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

  const click = async (el: Element): Promise<void> => {
    await act(async () => {
      (el as HTMLElement).click();
      await Promise.resolve();
    });
  };

  const dblclick = async (el: Element): Promise<void> => {
    await act(async () => {
      el.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true }));
      await Promise.resolve();
    });
  };

  const typeInto = async (input: HTMLInputElement | HTMLTextAreaElement, text: string): Promise<void> => {
    await act(async () => {
      input.value = text;
      input.dispatchEvent(new Event('input', { bubbles: true }));
      await Promise.resolve();
    });
  };

  const press = async (target: Element, key: string, init: KeyboardEventInit = {}): Promise<void> => {
    await act(async () => {
      target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init }));
      await Promise.resolve();
    });
  };

  const segment = (text: string): HTMLElement => {
    const match = [...container.querySelectorAll<HTMLElement>('.segm')].find((s) => s.querySelector('.segm-text')?.textContent.startsWith(text));
    if (match === undefined) {
      throw new Error(`no segment starting "${text}"`);
    }
    return match;
  };

  const open = async (recordingId = DESIGN_REVIEW, mock: MockOptions = {}): Promise<void> => {
    bridge = createBridgeClient({ logger: quiet, mock: { live: false, recovery: false, now: () => NOW.getTime(), stepMs: 30, ...mock } });
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

  const openWithTranscript = async (): Promise<void> => {
    await open();
    await until(() => container.querySelector('.segm') !== null);
  };

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

  it('marks low-confidence words and uncertain speakers accessibly', async () => {
    await openWithTranscript();
    const prototype = segment('And it keeps the sidebar out');
    const low = prototype.querySelector('.lowc');
    expect(low?.getAttribute('title')).toBe('Low confidence');
    expect(low?.textContent).toBe('prototype (low confidence)');
    const uncertain = segment('Mm-hm.');
    expect(uncertain.querySelector('.segm-speaker')?.getAttribute('title')).toBe('Speaker uncertain');
    expect(uncertain.querySelector('.segm-dot--uncertain')).not.toBeNull();
    expect(segment('Okay, I think everyone').querySelector('.segm-dot--uncertain')).toBeNull();
    // The list is windowed: 130 lines (the sample's turbo pass dropped one), only those near the viewport rendered.
    const list = container.querySelector('.segm-list');
    expect(list?.getAttribute('aria-label')).toBe('Transcript, 130 lines');
    expect(container.querySelectorAll('.segm').length).toBeLessThan(40);
    expect(container.querySelector('.segm')?.getAttribute('aria-setsize')).toBe('130');
  });

  it('seeks on click and on Enter, and follows the playhead with the current segment', async () => {
    await openWithTranscript();
    const lena = segment('A few');
    await click(lena);
    expect(container.querySelector('.scrubber')?.getAttribute('aria-valuetext')).toBe('1:33 of 1:10:02');
    expect(lena.classList.contains('now')).toBe(true);
    expect(lena.getAttribute('aria-current')).toBe('true');
    const sam = segment('Great. Lena');
    await press(sam, 'Enter');
    expect(container.querySelector('.scrubber')?.getAttribute('aria-valuetext')).toBe('1:10 of 1:10:02');
    expect(sam.classList.contains('now')).toBe(true);
    // Arrow keys move between segments.
    sam.focus();
    await press(sam, 'ArrowDown');
    await settle();
    expect(document.activeElement?.querySelector('.segm-text')?.textContent).toMatch(/^A few/);
  });

  it('edits a line in place: Enter saves, the original shows on hover, Esc cancels', async () => {
    await openWithTranscript();
    await dblclick(segment('Great. Lena'));
    const editor = container.querySelector<HTMLTextAreaElement>('.segm-editor');
    if (editor === null) {
      throw new Error('no editor');
    }
    expect(editor.value).toBe('Great. Lena, you had a list of questions from the research sessions?');
    await typeInto(editor, 'Great. Lena, you had questions from the research sessions?');
    await press(editor, 'Enter');
    await until(() => container.querySelector('.segm-editor') === null);
    const edited = segment('Great. Lena, you had questions');
    expect(edited.querySelector('.segm-edited')?.getAttribute('title')).toBe('Original: Great. Lena, you had a list of questions from the research sessions?');
    expect(document.activeElement).toBe(edited);
    const saved = await bridge.call('transcript.get', { recordingId: DESIGN_REVIEW });
    expect(saved.transcript?.segments.find((s) => s.text.startsWith('Great. Lena'))?.edited?.original).toBe(
      'Great. Lena, you had a list of questions from the research sessions?',
    );

    // F2 edits too; Esc leaves the text as it was.
    const other = segment('Let us keep those');
    await press(other, 'F2');
    const second = container.querySelector<HTMLTextAreaElement>('.segm-editor');
    if (second === null) {
      throw new Error('no editor');
    }
    await typeInto(second, 'Something else entirely');
    await press(second, 'Escape');
    expect(container.querySelector('.segm-editor')).toBeNull();
    expect(segment('Let us keep those')).toBeDefined();
  });

  const option = (name: string): HTMLElement => {
    const match = [...document.querySelectorAll<HTMLElement>('.speaker-menu [role="option"]')].find((o) => o.textContent.trim() === name);
    if (match === undefined) {
      throw new Error(`no option named ${name}`);
    }
    return match;
  };

  const openSpeakerMenu = async (text: string): Promise<HTMLInputElement> => {
    const line = segment(text);
    await click(line.querySelector('.segm-speaker') ?? line);
    const search = document.querySelector<HTMLInputElement>('.speaker-menu input[role="combobox"]');
    if (search === null) {
      throw new Error('no speaker search');
    }
    return search;
  };

  it('reassigns a line, finds and adds a speaker, and renames one from the speaker menu', async () => {
    await openWithTranscript();
    const search = await openSpeakerMenu('Okay, I think everyone');
    expect(search.placeholder).toBe('Find or add a speaker');
    expect(document.activeElement).toBe(search);
    const menu = document.querySelector('.speaker-menu');
    expect([...(menu?.querySelectorAll('[role="option"]') ?? [])].map((m) => [m.textContent, m.getAttribute('aria-selected')])).toEqual([
      ['Sam Okafor (current)', 'true'],
      ['Aiko Tanaka', 'false'],
      ['Lena Fischer', 'false'],
      ['Speaker 4', 'false'],
      ['Rename Sam Okafor…', 'false'],
    ]);
    // The current speaker is where the arrow keys start.
    expect(search.getAttribute('aria-activedescendant')).toBe(menu?.querySelector('[aria-selected="true"]')?.id);
    await click(option('Lena Fischer'));
    await until(() => segment('Okay, I think everyone').querySelector('.segm-speaker-name')?.textContent === 'Lena Fischer');
    expect(document.querySelector('.speaker-menu')).toBeNull();

    // Typing filters (case and accents ignored); a name nobody has can be added, and Enter on it adds it.
    const again = await openSpeakerMenu('Okay, I think everyone');
    await typeInto(again, 'LENA');
    // Add stays offered unless a speaker has exactly that name (two people may share a first name).
    expect([...document.querySelectorAll('.speaker-menu [role="option"]')].map((o) => o.textContent)).toEqual(['Lena Fischer (current)', 'Add “LENA” as a new speaker']);
    await typeInto(again, 'lena fischer');
    expect([...document.querySelectorAll('.speaker-menu [role="option"]')].map((o) => o.textContent)).toEqual(['Lena Fischer (current)']);
    await typeInto(again, 'Jonah Berg');
    expect([...document.querySelectorAll('.speaker-menu [role="option"]')].map((o) => o.textContent)).toEqual(['Add “Jonah Berg” as a new speaker']);
    await press(again, 'Enter');
    await until(() => segment('Okay, I think everyone').querySelector('.segm-speaker-name')?.textContent === 'Jonah Berg');
    // Jonah is a speaker now, so the participants list no longer repeats him.
    await until(() => !container.textContent.includes('Also listed as participants'));

    // Rename… renames everywhere.
    await openSpeakerMenu('Mm-hm.');
    await click(option('Rename Speaker 4…'));
    const rename = document.querySelector<HTMLInputElement>('.segm-menu-input');
    if (rename === null) {
      throw new Error('no rename field');
    }
    expect(rename.value).toBe('Speaker 4');
    await typeInto(rename, 'Dana Whitfield');
    await click(button('Rename'));
    await until(() => [...container.querySelectorAll('.person-name')].some((p) => p.textContent === 'Dana Whitfield'));
    expect(segment('No objection.').querySelector('.segm-speaker-name')?.textContent).toBe('Dana Whitfield');
  });

  it('moves through the speaker menu with the arrow keys, ranks names that start with the text first, and closes with Esc', async () => {
    await openWithTranscript();
    // Many speakers: the menu stays inside the window and its list scrolls.
    const first = (await bridge.call('transcript.get', { recordingId: DESIGN_REVIEW })).transcript?.segments.find((s) => s.text.startsWith('Okay, I think everyone'))?.id ?? '';
    for (const name of ['Ana Lima', 'Bea Novak', 'Cai Wen', 'Dario Rossi', 'Elif Kaya', 'Femi Ade', 'Göran Berg', 'Hana Sato', 'Iris Lund', 'Jonas Ek']) {
      await act(async () => {
        await bridge.call('transcript.setSegmentSpeaker', { recordingId: DESIGN_REVIEW, segmentId: first, speakerId: null, newSpeakerName: name });
      });
    }
    await until(() => segment('Okay, I think everyone').querySelector('.segm-speaker-name')?.textContent === 'Jonas Ek');
    const search = await openSpeakerMenu('Okay, I think everyone');
    const menu = document.querySelector<HTMLElement>('.speaker-menu');
    expect(menu?.querySelectorAll('[role="option"]')).toHaveLength(15);
    expect(menu?.style.maxHeight).toMatch(/^\d+px$/);
    expect(Number.parseInt(menu?.style.maxHeight ?? '0', 10)).toBeLessThanOrEqual(420);
    const active = (): string | undefined => document.getElementById(search.getAttribute('aria-activedescendant') ?? '')?.textContent;
    expect(active()).toBe('Jonas Ek (current)');
    await press(search, 'ArrowDown');
    expect(active()).toBe('Rename Jonas Ek…');
    await press(search, 'ArrowDown');
    expect(active()).toBe('Sam Okafor');
    await press(search, 'ArrowUp');
    await press(search, 'ArrowUp');
    expect(active()).toBe('Jonas Ek (current)');
    // "be": Bea starts with it, Berg has a word that does, Lena Fischer... does not hold it; "goran" ignores the accent.
    await typeInto(search, 'be');
    expect([...document.querySelectorAll('.speaker-menu [role="option"]')].map((o) => o.textContent)).toEqual(['Bea Novak', 'Göran Berg', 'Add “be” as a new speaker']);
    await typeInto(search, 'goran');
    await press(search, 'Enter');
    await until(() => segment('Okay, I think everyone').querySelector('.segm-speaker-name')?.textContent === 'Göran Berg');
    const reopened = await openSpeakerMenu('Okay, I think everyone');
    await press(reopened, 'Escape');
    expect(document.querySelector('.speaker-menu')).toBeNull();
    expect(document.activeElement).toBe(segment('Okay, I think everyone').querySelector('.segm-speaker'));
  });

  it('edits a line in place with the caret where the text was clicked, keeps the line while playing, and saves on leaving', async () => {
    await openWithTranscript();
    const line = segment('Great. Lena');
    const text = line.querySelector<HTMLElement>('.segm-text');
    const words = text?.firstChild;
    if (text === null || words === null || words === undefined) {
      throw new Error('no text');
    }
    // The browser reports the caret under the pointer; jsdom has none, so the test gives one.
    Object.defineProperty(document, 'caretPositionFromPoint', { value: () => ({ offsetNode: words, offset: 6 }), configurable: true });
    const scrubber = container.querySelector('.scrubber')?.getAttribute('aria-valuetext');
    try {
      await click(text);
    } finally {
      Reflect.deleteProperty(document, 'caretPositionFromPoint');
    }
    const editor = container.querySelector<HTMLTextAreaElement>('.segm-editor');
    if (editor === null) {
      throw new Error('no editor');
    }
    expect(document.activeElement).toBe(editor);
    expect([editor.selectionStart, editor.selectionEnd]).toEqual([6, 6]);
    // Clicking the words corrects them; it does not move the player.
    expect(container.querySelector('.scrubber')?.getAttribute('aria-valuetext')).toBe(scrubber);

    // Playback moving on does not take the line away while it is edited.
    await click(button('Play from 1:01:20, Owners and next steps'));
    await settle();
    expect(container.querySelector('.scrubber')?.getAttribute('aria-valuetext')).toBe('1:01:20 of 1:10:02');
    expect(container.querySelector('.segm-editor')).toBe(editor);
    // The list did not follow the playhead to the end of the recording.
    const rendered = [...container.querySelectorAll<HTMLElement>('.segm')].map((el) => Number(el.dataset.index));
    expect(Math.max(...rendered)).toBeLessThan(60);

    await typeInto(editor, 'Great. Lena, you had questions?');
    await act(async () => {
      editor.blur();
      editor.dispatchEvent(new FocusEvent('blur'));
      await Promise.resolve();
    });
    await until(() => container.querySelector('.segm-editor') === null);
    expect(segment('Great. Lena, you had questions?').querySelector('.segm-edited')).not.toBeNull();
    await until(() => container.querySelector('.undo-status')?.textContent === 'Saved');
  });

  it('renames a highlight from its note under the line', async () => {
    await openWithTranscript();
    // Play from the first highlight in the outline: the transcript follows to its line.
    const time = container.querySelectorAll<HTMLElement>('.review-outline .outline-group')[1]?.querySelector<HTMLElement>('.chap-time');
    if (time === null || time === undefined) {
      throw new Error('no highlight in the outline');
    }
    await click(time);
    const note = await (async (): Promise<HTMLButtonElement> => {
      await until(() => container.querySelector('.segm-note-edit') !== null);
      const found = container.querySelector<HTMLButtonElement>('.segm-note-edit');
      if (found === null) {
        throw new Error('no note');
      }
      return found;
    })();
    const before = note.textContent;
    await click(note);
    const input = container.querySelector<HTMLInputElement>('.segm-note-input');
    if (input === null) {
      throw new Error('no note field');
    }
    expect(input.getAttribute('aria-label')).toBe('Name of the highlight at 18:42. Enter saves, Esc cancels.');
    await typeInto(input, 'Decision: keep 68 px rows');
    await press(input, 'Enter');
    await until(() => [...container.querySelectorAll('.segm-note-words')].some((w) => w.textContent === 'Decision: keep 68 px rows'));
    expect([...container.querySelectorAll('.chap-note')].map((n) => n.textContent)).toContain('Decision: keep 68 px rows');
    expect(before).not.toBe('Decision: keep 68 px rows');
  });

  it('searches live, steps with Enter and Shift+Enter, highlights matches and clears with Esc', async () => {
    await openWithTranscript();
    const field = container.querySelector<HTMLInputElement>('#tx-search');
    if (field === null) {
      throw new Error('no search field');
    }
    await typeInto(field, 'dark theme');
    await until(() => container.querySelector('#tx-search-count')?.textContent === '4 matches');
    await press(field, 'Enter');
    expect(container.querySelector('#tx-search-count')?.textContent).toBe('1 of 4');
    await until(() => container.querySelector('.hl-current') !== null);
    expect(container.querySelector('.hl-current')?.textContent.toLocaleLowerCase()).toBe('dark theme');
    const seekTo = container.querySelector('.scrubber')?.getAttribute('aria-valuetext');
    await press(field, 'Enter');
    expect(container.querySelector('#tx-search-count')?.textContent).toBe('2 of 4');
    expect(container.querySelector('.scrubber')?.getAttribute('aria-valuetext')).not.toBe(seekTo);
    await press(field, 'Enter', { shiftKey: true });
    await press(field, 'Enter', { shiftKey: true });
    expect(container.querySelector('#tx-search-count')?.textContent).toBe('4 of 4');
    await press(field, 'Escape');
    expect(field.value).toBe('');
    expect(container.querySelector('#tx-search-count')).toBeNull();
    expect(container.querySelector('.hl')).toBeNull();
  });

  it('shows a coverage notice where speech was not transcribed, offering another model', async () => {
    await openWithTranscript();
    const { transcript } = await bridge.call('transcript.get', { recordingId: DESIGN_REVIEW });
    const gap = transcript?.coverageGaps[0];
    const after = transcript?.segments.find((s) => gap !== undefined && s.start >= gap.start);
    if (gap === undefined || after === undefined) {
      throw new Error('the sample has no coverage gap');
    }
    // Jump to the line after the gap with the transcript search (the list only renders rows near the view).
    const field = container.querySelector<HTMLInputElement>('#tx-search');
    if (field === null) {
      throw new Error('no search field');
    }
    await typeInto(field, after.text.split(' ').slice(0, 4).join(' ').replace(/[.,?!]+$/, ''));
    await until(() => (container.querySelector('#tx-search-count')?.textContent ?? '').includes('match'));
    await press(field, 'Enter');
    await until(() => container.querySelector('.tx-gap') !== null);

    const notice = container.querySelector('.tx-gap');
    expect(notice?.getAttribute('role')).toBe('note');
    expect(notice?.querySelector('.tx-gap-text')?.textContent).toMatch(/^Nothing was transcribed between \d+:\d\d and \d+:\d\d, although there was speech\.$/);
    // It sits right before the line that follows the gap.
    expect(notice?.parentElement?.querySelector('.segm')?.getAttribute('data-segment-id')).toBe(after.id);
    const call = vi.spyOn(bridge, 'call');
    await click(button('Transcribe again with Small', notice ?? document));
    expect(call).toHaveBeenCalledWith('transcript.retranscribe', { recordingId: DESIGN_REVIEW, modelId: 'whisper-small' });
    await until(() => container.querySelector('.tx-running') !== null);
    // Small kept the passage: once its pass is done, the notice is gone.
    await until(() => container.querySelector('.tx-gap') === null && container.querySelector('.tx-running') === null, 8000);
    expect((await bridge.call('transcript.get', { recordingId: DESIGN_REVIEW })).transcript?.engine.model).toBe('whisper-small');
  });

  it('shows a failed speakers stage with its remedy while the transcript itself is done', async () => {
    await openWithTranscript();
    await act(async () => {
      await bridge.call('processing.retry', { recordingId: DESIGN_REVIEW, stage: 'speakers' });
      await bridge.call('processing.cancel', { recordingId: DESIGN_REVIEW, stage: 'speakers' });
    });
    await until(() => container.querySelector('.tx-failed') !== null);
    const card = container.querySelector('.tx-failed');
    expect(card?.querySelector('.tx-failed-label')?.textContent).toBe('Speaker identification failed');
    expect(card?.textContent).toContain('The transcript is kept without speakers.');
    // The transcript stays on screen under it.
    expect(container.querySelector('.segm')).not.toBeNull();
    const call = vi.spyOn(bridge, 'call');
    await click(button('Identify speakers', card ?? document));
    expect(call).toHaveBeenCalledWith('processing.retry', { recordingId: DESIGN_REVIEW, stage: 'speakers', remedyId: 'retry' });
    await until(() => container.querySelector('.tx-failed') === null);
  });

  it('marks the transcript as reviewed and shows it in the header', async () => {
    await openWithTranscript();
    const chip = button('Mark as reviewed');
    expect(chip.getAttribute('aria-pressed')).toBe('false');
    await click(chip);
    await until(() => container.querySelector('.spoke-meta-pill')?.textContent === 'Reviewed');
    expect(button('Reviewed').getAttribute('aria-pressed')).toBe('true');
    expect((await bridge.call('transcript.get', { recordingId: DESIGN_REVIEW })).transcript?.reviewed).toBe(true);
  });

  it('lists transcript versions and restores one after confirming', async () => {
    await openWithTranscript();
    await until(() => container.querySelectorAll('.version-restore').length === 2);
    expect([...container.querySelectorAll('.version-card .version-title')].map((t) => t.textContent)).toEqual([
      'Current · version 4',
      'Transcribed again',
      'First transcript',
    ]);
    await click(container.querySelectorAll('.version-restore')[0] ?? container);
    const dialog = document.querySelector('[role="dialog"]');
    expect(dialog?.querySelector('h2')?.textContent).toBe('Restore this transcript version?');
    expect(dialog?.textContent).toContain('The current transcript is kept as a version, so nothing is lost.');
    await click(button('Restore', dialog ?? document));
    await until(() => container.querySelectorAll('.version-restore').length === 3);
    expect(container.querySelector('.segm-edited')).toBeNull();
    expect([...container.querySelectorAll('.version-card .version-title')][1]?.textContent).toBe('Edited by you');
  });

  it('transcribes again from More › Reprocess with a model and language', async () => {
    await openWithTranscript();
    await click(button('More actions'));
    await click(button('Transcribe again…'));
    const dialog = document.querySelector('[role="dialog"]');
    expect(dialog?.querySelector('h2')?.textContent).toBe('Transcribe again');
    await until(() => dialog?.querySelector('[aria-label^="Model:"]') !== null);
    expect(dialog?.querySelector('[aria-label^="Model:"]')?.getAttribute('aria-label')).toMatch(/^Model: Large v3/);
    expect(dialog?.textContent).toContain('kept as a version you can restore from Details');
    await click(button('Transcribe again', dialog ?? document));
    await until(() => container.querySelector('.tx-running') !== null);
    // The current transcript stays readable while the new pass runs, then is replaced.
    expect(container.querySelector('.segm')).not.toBeNull();
    await until(() => container.querySelector('.tx-running') === null && container.querySelector('.segm-edited') === null, 6000);
  });

  it('shows the failed-stage card with its remedies and the partial transcript', async () => {
    await open(DESIGN_REVIEW, { stage: 'failed' });
    await until(() => container.querySelector('.tx-failed') !== null);
    const card = container.querySelector('.tx-failed');
    expect(card?.querySelector('.tx-failed-label')?.textContent).toBe('Transcription failed');
    expect(card?.textContent).toContain('The GPU ran out of memory at 64%. The recording is safe and the partial transcript was kept.');
    expect([...(card?.querySelectorAll('button') ?? [])].map((b) => b.textContent)).toEqual(['Retry on CPU', 'Use the Small model', 'Try again', 'Details']);
    expect(container.querySelector('.tx-partial')?.textContent).toMatch(/^The partial transcript, up to /);
    expect(container.querySelector('.segm')).not.toBeNull();
    await click(button('Details', card ?? document));
    expect(container.querySelector('[role="tab"][aria-selected="true"]')?.textContent).toBe('History');
    const call = vi.spyOn(bridge, 'call');
    await click(button('Retry on CPU'));
    expect(call).toHaveBeenCalledWith('processing.retry', { recordingId: DESIGN_REVIEW, stage: 'transcript', remedyId: 'cpu' });
    await until(() => container.querySelector('.tx-failed') === null);
  });

  it('shows the running, queued and paused states', async () => {
    await open(DESIGN_REVIEW, { stage: 'running' });
    await until(() => container.textContent.includes('Transcribing 64% · local GPU'));
    expect(container.querySelector('.tx-progress')?.getAttribute('aria-valuenow')).toBe('64');
    render(null, container);
    disconnect();

    await open(DESIGN_REVIEW, { stage: 'paused' });
    await until(() => container.textContent.includes('Paused · PC is busy'));
    render(null, container);
    disconnect();

    await open(DESIGN_REVIEW, { stage: 'queued' });
    await until(() => container.textContent.includes('Waiting to transcribe'));
  });

  it('renames and merges speakers from the People list', async () => {
    await openWithTranscript();
    await click(button('Rename Speaker 4'));
    const input = container.querySelector<HTMLInputElement>('.person-input');
    if (input === null) {
      throw new Error('no rename input');
    }
    await typeInto(input, 'Dana Whitfield');
    await press(input, 'Enter');
    await until(() => segment('No objection.').querySelector('.segm-speaker-name')?.textContent === 'Dana Whitfield');

    await click(button('Merge Dana Whitfield into someone else'));
    // The menu floats on <body> above the scrolling outline (components/Floating.tsx).
    const mergeMenu = document.querySelector<HTMLElement>('.popover-layer .speaker-menu');
    expect(mergeMenu).not.toBeNull();
    const search = mergeMenu?.querySelector<HTMLInputElement>('input');
    if (search === null || search === undefined) {
      throw new Error('no search');
    }
    await typeInto(search, 'fisch');
    await press(search, 'Enter');
    await until(() => ![...container.querySelectorAll('.person-name')].some((p) => p.textContent === 'Dana Whitfield'));
    expect(segment('No objection.').querySelector('.segm-speaker-name')?.textContent).toBe('Lena Fischer');
  });
});
