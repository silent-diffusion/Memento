import { act } from 'preact/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import type { Speaker, Transcript } from '../../bridge/types';
import { click, mountApp, settle, type, until, type Harness } from '../../testing/appHarness';
import { undoOf } from '../../state/undo';

const NOW = new Date(2026, 9, 6, 15, 0);
const DESIGN_REVIEW = '20261005-160000-dsrev';
const OTHER = '20260930-130500-onbrd';

describe('Undo in Review (against the browser-preview host)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const open = async (): Promise<void> => {
    h = await mountApp({ name: 'review', recordingId: DESIGN_REVIEW }, {}, NOW);
    await until(() => h.container.querySelector('.segm') !== null);
  };

  const transcript = async (): Promise<Transcript> => {
    const { transcript: t } = await h.bridge.call('transcript.get', { recordingId: DESIGN_REVIEW });
    if (t === null) {
      throw new Error('no transcript');
    }
    return t;
  };

  const shape = (speakers: Speaker[]): [string, string, number, boolean][] => speakers.map((s) => [s.id, s.name, s.color, s.renamed]);

  const undoButton = (): HTMLButtonElement | null => h.container.querySelector<HTMLButtonElement>('.undo-btn');

  const key = async (init: KeyboardEventInit, target: Element = document.body): Promise<KeyboardEvent> => {
    const event = new KeyboardEvent('keydown', { bubbles: true, cancelable: true, ...init });
    await act(async () => {
      target.dispatchEvent(event);
      await Promise.resolve();
    });
    await settle(20);
    return event;
  };

  const press = async (target: Element, k: string): Promise<void> => {
    await act(async () => {
      target.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true }));
      await Promise.resolve();
    });
  };

  const undo = async (): Promise<void> => {
    await act(async () => {
      await undoOf(h.store).undo();
    });
    await settle(20);
  };

  const redo = async (): Promise<void> => {
    await act(async () => {
      await undoOf(h.store).redo();
    });
    await settle(20);
  };

  const segmentEl = (text: string): HTMLElement => {
    const match = [...h.container.querySelectorAll<HTMLElement>('.segm')].find((s) => s.querySelector('.segm-text')?.textContent.startsWith(text));
    if (match === undefined) {
      throw new Error(`no segment starting "${text}"`);
    }
    return match;
  };

  const option = (name: string): HTMLElement => {
    const match = [...document.querySelectorAll<HTMLElement>('.speaker-menu [role="option"]')].find((o) => o.textContent.trim() === name);
    if (match === undefined) {
      throw new Error(`no option ${name}`);
    }
    return match;
  };

  const named = (name: string): HTMLButtonElement => {
    const match = [...document.querySelectorAll<HTMLButtonElement>('button')].find((b) => (b.getAttribute('aria-label') ?? b.textContent.trim()) === name);
    if (match === undefined) {
      throw new Error(`no button ${name}`);
    }
    return match;
  };

  it('undoes and redoes moving a line, adding, renaming and merging speakers, with the button and the keys', async () => {
    await open();
    const before = await transcript();
    const line = before.segments.find((s) => s.text.startsWith('Okay, I think everyone'));
    if (line === undefined) {
      throw new Error('no line');
    }
    expect(undoButton()).toBeNull();

    // Move the line to Lena: the button names the step, Undo puts it back, Ctrl+Y does it again.
    await click(segmentEl('Okay, I think everyone').querySelector('.segm-speaker'));
    await click(option('Lena Fischer'));
    await click(document.querySelector('.speaker-menu--choices [data-choice="line"]'));
    await until(() => undoButton() !== null);
    expect(undoButton()?.getAttribute('aria-label')).toBe('Undo move line to Lena Fischer');
    expect(undoButton()?.title).toBe('Undo move line to Lena Fischer');
    expect(undoButton()?.getAttribute('aria-keyshortcuts')).toBe('Control+Z');
    await click(undoButton());
    await until(() => h.container.querySelector('.undo-status')?.textContent === 'Undone: move line to Lena Fischer');
    expect((await transcript()).segments.find((s) => s.id === line.id)?.speaker).toBe(line.speaker);
    expect((await key({ key: 'y', ctrlKey: true })).defaultPrevented).toBe(true);
    await until(() => segmentEl('Okay, I think everyone').querySelector('.segm-speaker-name')?.textContent === 'Lena Fischer');

    // Add a speaker from the menu: Undo moves the line back and removes the speaker; Redo brings the same speaker back.
    await click(segmentEl('Okay, I think everyone').querySelector('.segm-speaker'));
    const search = document.querySelector<HTMLInputElement>('.speaker-menu input');
    await type(search, 'Jonah Berg');
    await press(search as HTMLElement, 'Enter');
    await until(() => segmentEl('Okay, I think everyone').querySelector('.segm-speaker-name')?.textContent === 'Jonah Berg');
    const jonah = (await transcript()).speakers.find((s) => s.name === 'Jonah Berg');
    expect(undoButton()?.getAttribute('aria-label')).toBe('Undo add speaker Jonah Berg');
    await key({ key: 'z', ctrlKey: true });
    await until(() => segmentEl('Okay, I think everyone').querySelector('.segm-speaker-name')?.textContent === 'Lena Fischer');
    expect((await transcript()).speakers.some((s) => s.name === 'Jonah Berg')).toBe(false);
    await key({ key: 'z', ctrlKey: true, shiftKey: true });
    await until(() => segmentEl('Okay, I think everyone').querySelector('.segm-speaker-name')?.textContent === 'Jonah Berg');
    expect((await transcript()).speakers.find((s) => s.name === 'Jonah Berg')).toMatchObject({ id: jonah?.id, color: jonah?.color });
    await undo();
    await undo();
    expect((await transcript()).segments.find((s) => s.id === line.id)?.speaker).toBe(line.speaker);

    // Rename, then undo: the name and its "renamed" flag are as before.
    await click(named('Rename Speaker 4'));
    const rename = h.container.querySelector<HTMLInputElement>('.person-input');
    await type(rename, 'Dana Whitfield');
    await press(rename as HTMLElement, 'Enter');
    await until(() => (undoButton()?.getAttribute('aria-label') ?? '') === 'Undo rename speaker');
    await undo();
    expect(shape((await transcript()).speakers)).toEqual(shape(before.speakers));

    // Merge Speaker 4 into Aiko: Undo re-creates Speaker 4 with its id, name and colour, and gives its lines back.
    await click(named('Merge Speaker 4 into someone else'));
    await click(option('Aiko Tanaka'));
    await until(() => ![...h.container.querySelectorAll('.person-name')].some((p) => p.textContent === 'Speaker 4'));
    expect(undoButton()?.getAttribute('aria-label')).toBe('Undo merge speakers');
    await click(undoButton());
    await until(() => [...h.container.querySelectorAll('.person-name')].some((p) => p.textContent === 'Speaker 4'));
    const restored = await transcript();
    expect(shape(restored.speakers)).toEqual(shape(before.speakers));
    expect(restored.segments.map((s) => s.speaker)).toEqual(before.segments.map((s) => s.speaker));
    expect(restored.speakers.map((s) => s.talkTimeMs)).toEqual(before.speakers.map((s) => s.talkTimeMs));
    await redo();
    expect((await transcript()).speakers.some((s) => s.id === 'sp4')).toBe(false);
  });

  it('undoes and redoes a line edit, and highlight and chapter adds, renames and removals', async () => {
    await open();
    const before = await transcript();
    const great = before.segments.find((s) => s.text.startsWith('Great. Lena'));
    if (great === undefined) {
      throw new Error('no line');
    }
    // Edit a line in place.
    await click(segmentEl('Great. Lena').querySelector('.segm-text'));
    const editor = h.container.querySelector<HTMLTextAreaElement>('.segm-editor');
    await type(editor, 'Great. Lena, any questions?');
    await press(editor as HTMLElement, 'Enter');
    await until(() => h.container.querySelector('.segm-editor') === null);
    expect(undoButton()?.getAttribute('aria-label')).toBe('Undo edit line');
    await undo();
    const back = (await transcript()).segments.find((s) => s.id === great.id);
    expect(back?.text).toBe(great.text);
    expect(back?.edited).toBeNull();
    await redo();
    expect((await transcript()).segments.find((s) => s.id === great.id)?.text).toBe('Great. Lena, any questions?');

    const project = async (): Promise<{ highlights: string[]; chapters: string[] }> => {
      const p = await h.bridge.call('project.get', { recordingId: DESIGN_REVIEW });
      return { highlights: p.highlights.map((x) => `${x.atMs}:${x.note}`), chapters: p.chapters.map((c) => `${c.atMs}:${c.title}`) };
    };
    const start = await project();

    // Highlight: add, rename, remove; each undone and redone.
    await click(named('Highlight'));
    await until(() => h.container.textContent.includes('Highlights · 4'));
    expect(undoButton()?.getAttribute('aria-label')).toBe('Undo add highlight');
    await undo();
    expect((await project()).highlights).toEqual(start.highlights);
    await redo();
    expect((await project()).highlights).toHaveLength(4);
    await undo();

    const first = (await h.bridge.call('project.get', { recordingId: DESIGN_REVIEW })).highlights[0];
    if (first === undefined) {
      throw new Error('no highlight');
    }
    await click(named(first.note === '' ? 'Name the highlight at 0:00' : `Rename ${first.note}`));
    const field = h.container.querySelector<HTMLInputElement>('.chap-row .inline-input');
    await type(field, 'Agreed on 68 px rows');
    await press(field as HTMLElement, 'Enter');
    await until(() => h.container.querySelector('.undo-status')?.textContent === 'Saved');
    expect(undoButton()?.getAttribute('aria-label')).toBe('Undo rename highlight');
    await undo();
    expect((await project()).highlights).toEqual(start.highlights);
    await redo();
    expect((await project()).highlights[0]).toBe(`${first.atMs}:Agreed on 68 px rows`);
    await undo();

    await click(h.container.querySelectorAll<HTMLElement>('.review-outline .outline-group')[1]?.querySelector('.chap-remove'));
    await until(() => h.container.textContent.includes('Highlights · 2'));
    expect(undoButton()?.getAttribute('aria-label')).toBe('Undo remove highlight');
    await undo();
    expect((await project()).highlights).toEqual(start.highlights);
    // A step after the re-created highlight still finds it under its new id.
    await redo();
    expect((await project()).highlights).toHaveLength(2);
    await undo();
    expect((await project()).highlights).toEqual(start.highlights);

    // Chapter: rename and remove, undone and redone.
    await click(named('Rename Dark theme scope'));
    const chapter = h.container.querySelector<HTMLInputElement>('.chap-row .inline-input');
    await type(chapter, 'Dark theme');
    await press(chapter as HTMLElement, 'Enter');
    await until(() => (undoButton()?.getAttribute('aria-label') ?? '') === 'Undo rename chapter');
    await click(named('Remove the chapter at 34:00, Dark theme'));
    await until(() => (undoButton()?.getAttribute('aria-label') ?? '') === 'Undo remove chapter');
    await undo();
    expect((await project()).chapters).toContain(`${34 * 60_000}:Dark theme`);
    await undo();
    expect((await project()).chapters).toEqual(start.chapters);
    await redo();
    await redo();
    expect((await project()).chapters.some((c) => c.endsWith(':Dark theme'))).toBe(false);
    await undo();
    await undo();
    expect((await project()).chapters).toEqual(start.chapters);
  });

  it('leaves Ctrl+Z to a text field with changes, keeps a refused step, and starts afresh for another recording', async () => {
    await open();
    await click(named('Rename Opening and goals'));
    const chapter = h.container.querySelector<HTMLInputElement>('.chap-row .inline-input');
    await type(chapter, 'Welcome');
    await press(chapter as HTMLElement, 'Enter');
    await until(() => (undoButton()?.getAttribute('aria-label') ?? '') === 'Undo rename chapter');

    // Typing in the transcript search: Ctrl+Z belongs to the field.
    const search = h.container.querySelector<HTMLInputElement>('#tx-search');
    if (search === null) {
      throw new Error('no search');
    }
    await act(async () => {
      search.focus();
      await Promise.resolve();
    });
    await type(search, 'dark');
    expect((await key({ key: 'z', ctrlKey: true }, search)).defaultPrevented).toBe(false);
    expect(undoButton()?.getAttribute('aria-label')).toBe('Undo rename chapter');

    // The chapter goes away behind Review's back: the undo is refused, said so, and kept.
    const { chapters } = await h.bridge.call('project.get', { recordingId: DESIGN_REVIEW });
    const welcome = chapters.find((c) => c.title === 'Welcome');
    await act(async () => {
      await h.bridge.call('annotations.removeChapter', { recordingId: DESIGN_REVIEW, chapterId: welcome?.id ?? '' });
    });
    await undo();
    expect(h.store.toasts.items.value.at(-1)?.title).toBe('Rename chapter was not undone');
    expect(h.container.querySelector('.undo-status')?.textContent).toBe('Not undone: rename chapter');
    expect(undoButton()?.getAttribute('aria-label')).toBe('Undo rename chapter');

    // Another recording: an empty stack.
    await act(async () => {
      h.store.route.value = { name: 'review', recordingId: OTHER };
      await Promise.resolve();
    });
    await until(() => h.container.querySelector('.review-panes') !== null && h.container.querySelector('.spoke-title')?.textContent !== 'Design review: library screen');
    expect(undoButton()).toBeNull();
    expect(undoOf(h.store).depth.value).toBe(0);
  });
});
