// Review's 2.0 parts (DESIGN.md §19) against the browser-preview host: the known-voice match prompt, suggested
// chapters, selection mode and the line context menu, each undoable.
import { act } from 'preact/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import type { Transcript } from '../../bridge/types';
import { click, mountApp, press, settle, type, until, type Harness } from '../../testing/appHarness';
import { undoOf } from '../../state/undo';

const NOW = new Date(2026, 9, 6, 15, 0);
const DESIGN_REVIEW = '20261005-160000-dsrev';
const TOWN_HALL = '20260918-160000-thall';
const INTERVIEW = '20261006-141500-pnint';

describe('Review 2.0: known voices, suggested chapters, selection mode', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const open = async (recordingId: string, rememberVoices = false): Promise<void> => {
    h = await mountApp({ name: 'review', recordingId }, {}, NOW);
    if (rememberVoices) {
      await act(async () => {
        h.store.settings.value = await h.bridge.call('settings.set', { speakers: { rememberVoices: true } });
      });
    }
    await until(() => h.container.querySelector('.segm') !== null);
  };

  const transcript = async (recordingId: string): Promise<Transcript> => {
    const { transcript: t } = await h.bridge.call('transcript.get', { recordingId });
    if (t === null) {
      throw new Error('no transcript');
    }
    return t;
  };

  const named = (name: string): HTMLElement => {
    const match = [...document.querySelectorAll<HTMLElement>('button, [role="menuitem"], [role="option"]')].find(
      (b) => (b.getAttribute('aria-label') ?? b.textContent.trim()) === name || b.textContent.trim() === name,
    );
    if (match === undefined) {
      throw new Error(`no button ${name}`);
    }
    return match;
  };

  const undo = async (): Promise<void> => {
    await act(async () => {
      await undoOf(h.store).undo();
    });
    await settle(30);
  };

  const redo = async (): Promise<void> => {
    await act(async () => {
      await undoOf(h.store).redo();
    });
    await settle(30);
  };

  const rows = (): HTMLElement[] => [...h.container.querySelectorAll<HTMLElement>('.segm')];

  const clickWith = async (el: Element | undefined, init: MouseEventInit): Promise<void> => {
    await act(async () => {
      el?.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, ...init }));
      await Promise.resolve();
    });
  };

  it('learns a named voice, then offers it in another recording: Use name and Not are undoable', async () => {
    await open(DESIGN_REVIEW, true);
    await click(named('Rename Speaker 4'));
    const field = h.container.querySelector<HTMLInputElement>('.person-input');
    await type(field, 'Kai Moreno');
    await press(field, 'Enter');
    await until(() => h.callsOf('voices.remember').length === 1);
    expect(h.callsOf('voices.remember')[0]).toEqual({ recordingId: DESIGN_REVIEW, speakerId: 'sp4' });
    await until(() => (h.container.querySelector('.undo-btn')?.getAttribute('aria-label') ?? '') === 'Undo rename speaker');
    expect((await h.bridge.call('voices.list')).voices.map((v) => v.name)).toEqual(['Kai Moreno']);

    // Undo takes the name and the learned voice back; Redo learns it again.
    await undo();
    expect(h.callsOf('voices.revert')).toHaveLength(1);
    expect((await h.bridge.call('voices.list')).voices).toEqual([]);
    await redo();
    expect((await h.bridge.call('voices.list')).voices.map((v) => v.name)).toEqual(['Kai Moreno']);
  });

  it('shows the match prompt, renames with Use name, hides it with Not, and undoes both', async () => {
    await open(DESIGN_REVIEW, true);
    // Kai is learned in the design review (sp4), then the town hall's unnamed sp4 sounds like Kai.
    await click(named('Rename Speaker 4'));
    const field = h.container.querySelector<HTMLInputElement>('.person-input');
    await type(field, 'Kai Moreno');
    await press(field, 'Enter');
    await until(() => h.callsOf('voices.remember').length === 1);
    await act(async () => {
      h.store.route.value = { name: 'library' };
      await Promise.resolve();
    });
    await settle(20);
    await act(async () => {
      h.store.route.value = { name: 'review', recordingId: TOWN_HALL };
      await Promise.resolve();
    });
    await until(() => h.container.querySelector('.voice-match') !== null);
    const prompt = h.container.querySelector<HTMLElement>('.voice-match');
    expect(prompt?.textContent).toContain('Sounds like Kai Moreno · 86% match · 1 past recording');
    expect(prompt?.getAttribute('aria-label')).toBe('Speaker 4 sounds like Kai Moreno');

    await click(named('Use the name Kai Moreno for Speaker 4'));
    await until(() => h.container.querySelector('.voice-match') === null);
    expect((await transcript(TOWN_HALL)).speakers.find((s) => s.id === 'sp4')).toMatchObject({ name: 'Kai Moreno', renamed: true });
    expect((await h.bridge.call('voices.list')).voices[0]?.recordings).toBe(2);
    expect(h.container.querySelector('.undo-btn')?.getAttribute('aria-label')).toBe('Undo use the name Kai Moreno');

    await undo();
    await until(() => h.container.querySelector('.voice-match') !== null);
    expect((await transcript(TOWN_HALL)).speakers.find((s) => s.id === 'sp4')).toMatchObject({ name: 'Speaker 4', renamed: false });
    expect((await h.bridge.call('voices.list')).voices[0]?.recordings).toBe(1);

    await click(named('Speaker 4 is not Kai Moreno'));
    await until(() => h.container.querySelector('.voice-match') === null);
    expect(h.callsOf('voices.decline').at(-1)).toMatchObject({ recordingId: TOWN_HALL, declined: true });
    await undo();
    await until(() => h.container.querySelector('.voice-match') !== null);
  });

  it('shows no prompt while Remember speakers by voice is off', async () => {
    await open(DESIGN_REVIEW);
    await settle(50);
    expect(h.callsOf('voices.matches')).toEqual([]);
    expect(h.container.querySelector('.voice-match')).toBeNull();
  });

  it('accepts a suggested chapter, accepts all, dismisses, and undoes each', async () => {
    await open(INTERVIEW);
    await until(() => h.container.querySelectorAll('.sug-chapter').length === 3);
    expect(h.container.querySelector('.sug-chapters .lbl')?.textContent).toBe('Suggested chapters · 3');
    const chaptersBefore = (await h.bridge.call('project.get', { recordingId: INTERVIEW })).chapters.length;
    const first = h.container.querySelector<HTMLElement>('.sug-chapter');
    const title = first?.querySelector('.sug-chapter-title')?.firstChild?.textContent ?? '';

    await click(first?.querySelector('button[aria-label^="Accept the chapter"]'));
    await until(() => h.container.querySelectorAll('.sug-chapter').length === 2);
    const chapters = (await h.bridge.call('project.get', { recordingId: INTERVIEW })).chapters;
    expect(chapters).toHaveLength(chaptersBefore + 1);
    expect(chapters.find((c) => c.title === title)?.origin).toBe('local');
    await undo();
    await until(() => h.container.querySelectorAll('.sug-chapter').length === 3);

    await click(named('Accept all'));
    await until(() => h.container.querySelector('.sug-chapters') === null);
    expect((await h.bridge.call('project.get', { recordingId: INTERVIEW })).chapters).toHaveLength(chaptersBefore + 3);
    expect(h.container.querySelector('.undo-btn')?.getAttribute('aria-label')).toBe('Undo accept 3 suggested chapters');
    await undo();
    await until(() => h.container.querySelectorAll('.sug-chapter').length === 3);
    expect((await h.bridge.call('project.get', { recordingId: INTERVIEW })).chapters).toHaveLength(chaptersBefore);

    await click(h.container.querySelector('.sug-chapter button[aria-label^="Dismiss the chapter"]'));
    await until(() => h.container.querySelectorAll('.sug-chapter').length === 2);
    await undo();
    await until(() => h.container.querySelectorAll('.sug-chapter').length === 3);
  });

  it('selects lines with Ctrl+click and Shift+click, assigns a speaker to them, and undoes it', async () => {
    await open(DESIGN_REVIEW);
    const before = await transcript(DESIGN_REVIEW);
    const [a, , c] = rows();
    await clickWith(a, { ctrlKey: true });
    expect(h.container.querySelector('.sel-bar')).not.toBeNull();
    await clickWith(c, { shiftKey: true });
    expect(h.container.querySelector('.sel-count')?.textContent).toMatch(/^3 lines selected · /);
    expect(rows().slice(0, 3).every((r) => r.classList.contains('segm--selected'))).toBe(true);
    // A plain click toggles while selecting, and does not seek or edit.
    await clickWith(rows()[1], {});
    expect(h.container.querySelector('.sel-count')?.textContent).toMatch(/^2 lines selected/);

    await click(named('Assign speaker…'));
    await click([...document.querySelectorAll('.speaker-menu [role="option"]')].find((o) => o.textContent.trim() === 'Lena Fischer'));
    await until(() => document.querySelector('.speaker-menu') === null || document.querySelector('.speaker-menu--choices') !== null);
    const choice = document.querySelector<HTMLElement>('.speaker-menu--choices [data-choice="lines"]');
    if (choice !== null) {
      await click(choice);
    }
    const lena = before.speakers.find((s) => s.name === 'Lena Fischer')?.id;
    const ids = [before.segments[0]?.id, before.segments[2]?.id];
    await until(() => h.callsOf('transcript.setSegmentsSpeaker').length === 1);
    expect(h.callsOf('transcript.setSegmentsSpeaker')[0]).toEqual({ recordingId: DESIGN_REVIEW, segmentIds: ids, speakerId: lena });
    let after = await transcript(DESIGN_REVIEW);
    expect(after.segments.filter((s) => ids.includes(s.id)).every((s) => s.speaker === lena)).toBe(true);
    expect(h.container.querySelector('.undo-btn')?.getAttribute('aria-label')).toBe('Undo move 2 lines to Lena Fischer');

    await undo();
    after = await transcript(DESIGN_REVIEW);
    expect(after.segments.slice(0, 3).map((s) => s.speaker)).toEqual(before.segments.slice(0, 3).map((s) => s.speaker));

    // Esc leaves selection mode.
    await press(document.body, 'Escape');
    await until(() => h.container.querySelector('.sel-bar') === null);
  });

  it('selects every line shown with Ctrl+A and extends with Shift+arrows', async () => {
    await open(DESIGN_REVIEW);
    const first = rows()[0];
    await press(first ?? null, 'a');
    expect(h.container.querySelector('.sel-bar')).toBeNull();
    await act(async () => {
      first?.dispatchEvent(new KeyboardEvent('keydown', { key: 'a', ctrlKey: true, bubbles: true, cancelable: true }));
      await Promise.resolve();
    });
    expect(h.container.querySelector('.sel-count')?.textContent).toMatch(/^130 lines selected/);
    await click(h.container.querySelector('.sel-bar input[type="checkbox"]'));
    expect(h.container.querySelector('.sel-count')?.textContent).toMatch(/^0 lines selected/);
    await act(async () => {
      rows()[0]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', shiftKey: true, bubbles: true, cancelable: true }));
      await Promise.resolve();
    });
    expect(h.container.querySelector('.sel-count')?.textContent).toMatch(/^2 lines selected/);
    await act(async () => {
      rows()[1]?.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true }));
      await Promise.resolve();
    });
    expect(h.container.querySelector('.sel-count')?.textContent).toMatch(/^1 line selected/);
  });

  it('opens the line menu on right-click and Shift+F10: highlight, chapter start and remove, each undoable', async () => {
    await open(DESIGN_REVIEW);
    const before = await h.bridge.call('project.get', { recordingId: DESIGN_REVIEW });
    const line = (await transcript(DESIGN_REVIEW)).segments.find((s) => !before.highlights.some((hl) => hl.segmentId === s.id));
    const row = h.container.querySelector<HTMLElement>(`[data-segment-id="${line?.id ?? ''}"]`) ?? rows()[5];
    if (row === undefined || line === undefined) {
      throw new Error('no line without a highlight');
    }
    await act(async () => {
      row.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: 300, clientY: 200 }));
      await Promise.resolve();
    });
    const menu = document.querySelector<HTMLElement>('.line-menu');
    expect(menu?.getAttribute('role')).toBe('menu');
    expect([...(menu?.querySelectorAll('[role="menuitem"]') ?? [])].map((m) => m.textContent)).toEqual([
      expect.stringMatching(/^Play from /),
      'Select',
      'Assign speaker…',
      'Highlight',
      'Mark as chapter start',
      'Copy as text',
      'Copy as Markdown',
    ]);
    expect(document.activeElement?.textContent).toMatch(/^Play from /);

    await click(menu?.querySelector('[data-item="highlight"]'));
    await until(() => h.callsOf('annotations.addHighlight').length === 1);
    await until(() => (h.container.querySelector('.undo-btn')?.getAttribute('aria-label') ?? '') === 'Undo add highlight');
    let project = await h.bridge.call('project.get', { recordingId: DESIGN_REVIEW });
    expect(project.highlights.some((hl) => hl.segmentId === line.id)).toBe(true);

    // Shift+F10 opens it from the keyboard; it now offers Remove highlight.
    await act(async () => {
      row.dispatchEvent(new KeyboardEvent('keydown', { key: 'F10', shiftKey: true, bubbles: true, cancelable: true }));
      await Promise.resolve();
    });
    const again = document.querySelector<HTMLElement>('.line-menu');
    expect(again?.querySelector('[data-item="unhighlight"]')?.textContent).toBe('Remove highlight');
    await press(again, 'Escape');
    expect(document.querySelector('.line-menu')).toBeNull();

    await undo();
    project = await h.bridge.call('project.get', { recordingId: DESIGN_REVIEW });
    expect(project.highlights.some((hl) => hl.segmentId === line.id)).toBe(false);

    await act(async () => {
      row.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: 300, clientY: 200 }));
      await Promise.resolve();
    });
    await click(document.querySelector('.line-menu [data-item="chapter"]'));
    await until(() => h.callsOf('annotations.addChapter').length === 1);
    expect(h.callsOf('annotations.addChapter')[0]).toMatchObject({ chapter: { atMs: Math.round(line.start * 1000), origin: 'user' } });
    await until(() => (h.container.querySelector('.undo-btn')?.getAttribute('aria-label') ?? '') === 'Undo add chapter');
    await undo();
    expect((await h.bridge.call('project.get', { recordingId: DESIGN_REVIEW })).chapters).toHaveLength(before.chapters.length);
  });
});
