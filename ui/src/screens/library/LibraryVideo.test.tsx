// 2.0 video in the Library (DESIGN.md §18.6, §18.7) against the browser-preview host: the Video
// column of the processing card, the failed video stage card and its remedies, rows and grid cards
// with video or a removed video, and the recovered video sentence.
import { afterEach, describe, expect, it } from 'vitest';
import type { MockOptions } from '../../bridge/mock';
import { button, click, mountApp, until, type Harness } from '../../testing/appHarness';

// Tuesday 6 October 2026, 3 PM, like LibraryScreen.test.tsx.
const NOW = new Date(2026, 9, 6, 15, 0);
const Q3 = '20261006-100000-q3plan';

describe('Library with video', () => {
  let h: Harness | null = null;

  const start = async (mock: MockOptions = {}): Promise<Harness> => {
    h = await mountApp({ name: 'library' }, mock, NOW);
    await until(() => h?.container.querySelector('.lib-row') !== null);
    return h;
  };
  const texts = (selector: string): string[] => [...(h?.container.querySelectorAll(selector) ?? [])].map((el) => el.textContent.trim());
  const rowOf = (title: string): HTMLElement => {
    const row = [...(h?.container.querySelectorAll<HTMLElement>('.lib-row') ?? [])].find((r) => r.querySelector('.row-title')?.textContent === title);
    if (row === undefined) {
      throw new Error(`no row ${title}`);
    }
    return row;
  };
  const cardOf = (title: string): HTMLElement => {
    const card = [...(h?.container.querySelectorAll<HTMLElement>('.lib-card') ?? [])].find((c) => c.querySelector('.card-title')?.textContent === title);
    if (card === undefined) {
      throw new Error(`no card ${title}`);
    }
    return card;
  };
  const pillsOf = (el: HTMLElement): string[] => [...el.querySelectorAll('.pill')].map((p) => `${p.classList[1] ?? ''}:${p.textContent.trim()}`);

  afterEach(() => {
    h?.unmount();
    h = null;
  });

  it('puts the Video column between Stored and Transcribing, with its sub-step on one line', async () => {
    await start({ video: ['encoding'] });
    expect(texts('.proc-stage-name')).toEqual(['Stored', 'Video', 'Transcribing', 'Speakers']);
    const video = h?.container.querySelectorAll('.proc-stage')[1];
    expect(video?.querySelector('.proc-stage-status')?.textContent).toBe('Encoding 42% · GPU');
    const track = video?.querySelector('[role="progressbar"]');
    expect(track?.getAttribute('aria-label')).toBe('Video');
    expect(track?.getAttribute('aria-valuenow')).toBe('42');
    expect(track?.getAttribute('aria-valuetext')).toBe('Encoding 42% · GPU');
    expect(texts('.proc-meta')).toEqual(['Meeting · screen + camera · 5 people · 1:02:14']);
    expect(pillsOf(rowOf('Q3 planning sync'))).toContain('active:Video · Encoding 42%');
  });

  it('keeps the meta line without video parts for an audio recording', async () => {
    await start();
    expect(texts('.proc-stage-name')).toEqual(['Stored', 'Transcribing', 'Speakers']);
    expect(texts('.proc-meta')).toEqual(['Meeting · 5 people · recorded today at 10:00 AM · 1:02:14']);
  });

  it('turns the card into "Video encoding failed" with the host remedies, first primary', async () => {
    const app = await start({ video: ['stagefail'] });
    const card = app.container.querySelector<HTMLElement>('.proc-card');
    expect(card?.getAttribute('aria-label')).toBe('Video encoding failed');
    expect(card?.classList.contains('proc-card--failed')).toBe(true);
    expect(card?.querySelector('.proc-label')?.textContent).toBe('Video encoding failed');
    expect(card?.querySelector('.proc-title')?.textContent).toBe('Q3 planning sync');
    expect(card?.querySelector('.proc-meta')?.textContent).toBe(
      'Finalising the video file stopped at 71%. The raw frames and all audio tracks are safe, and the transcript was not affected.',
    );
    expect(card?.querySelector('.proc-stages')).toBeNull();
    const buttons = [...(card?.querySelectorAll('button') ?? [])];
    expect(buttons.map((b) => b.textContent)).toEqual(['Retry with software encoder', 'Keep raw frames', 'Details']);
    expect(buttons.map((b) => b.className)).toEqual(['btn p proc-remedy', 'btn g proc-remedy', 'btn g proc-remedy']);
    expect(buttons[2]?.getAttribute('aria-label')).toBe('Details of Q3 planning sync');
    // Keyboard: every action is a focusable button.
    for (const b of buttons) {
      b.focus();
      expect(document.activeElement).toBe(b);
    }
    expect(pillsOf(rowOf('Q3 planning sync'))).toContain('failed:Video failed · Retry');
  });

  it('retries with the software encoder and the card returns to its stages', async () => {
    const app = await start({ video: ['stagefail'] });
    await click(button('Retry with software encoder'));
    expect(app.callsOf('processing.retry')).toEqual([{ recordingId: Q3, stage: 'video', remedyId: 'software' }]);
    await until(() => app.container.querySelector('.proc-card--failed') === null);
    expect(texts('.proc-stage-name')).toContain('Video');
    expect(texts('.proc-stage-status')[1]).toBe('Encoding 0% · CPU');
  });

  it('keeps the raw frames and the card shows the finished Video stage', async () => {
    const app = await start({ video: ['stagefail'] });
    await click(button('Keep raw frames'));
    expect(app.callsOf('processing.retry')).toEqual([{ recordingId: Q3, stage: 'video', remedyId: 'keepRaw' }]);
    await until(() => app.container.querySelector('.proc-card--failed') === null);
    expect(app.container.querySelector('.proc-card')?.getAttribute('aria-label')).toBe('Processing now');
    expect(texts('.proc-stage-status')[1]).toBe('Done');
    expect(pillsOf(rowOf('Q3 planning sync'))).toContain('done:Video');
  });

  it('says what is safe when a remedy is refused', async () => {
    const app = await start({ video: ['stagefail'] });
    const original = app.bridge.call.bind(app.bridge);
    (app.bridge as { call: unknown }).call = (method: string, params?: unknown) =>
      method === 'processing.retry' ? Promise.reject(new Error('The software encoder is not installed.')) : (original as (m: string, p?: unknown) => Promise<unknown>)(method, params);
    await click(button('Retry with software encoder'));
    await until(() => app.store.toasts.items.value.length > 0);
    expect(app.store.toasts.items.value[0]).toMatchObject({
      tone: 'warning',
      title: 'The video was not encoded again',
      body: 'The software encoder is not installed. The raw frames, the audio and the transcript are safe.',
    });
    expect(app.container.querySelector('.proc-card--failed')).not.toBeNull();
    expect(button('Retry with software encoder').disabled).toBe(false);
  });

  it('opens the recording in Review from Details', async () => {
    const app = await start({ video: ['stagefail'] });
    await click(button('Details of Q3 planning sync'));
    expect(app.store.route.value).toEqual({ name: 'review', recordingId: Q3 });
  });

  it('marks rows with video, and a removed video with the crossed camera and "video removed"', async () => {
    await start();
    const design = rowOf('Design review: library screen');
    expect(design.querySelector('.row-meta [aria-label="Includes video"]')).not.toBeNull();
    expect(design.querySelector('.row-meta')?.textContent).toBe('Meeting · 4 people · 4:00 PM');

    const town = rowOf('Town hall Q&A');
    expect(pillsOf(town)).toContain('done:Video');

    const board = rowOf('Board prep walkthrough');
    expect(board.querySelector('[aria-label="Includes video"]')).toBeNull();
    expect(board.querySelector('.row-meta-removed svg')?.getAttribute('aria-hidden')).toBe('true');
    expect(board.querySelector('.row-meta')?.textContent).toMatch(/^Presentation · 3 people · .+ · video removed$/);

    const audio = rowOf('Weekly 1:1 with Sam');
    expect(audio.querySelector('.row-meta svg')).toBeNull();
  });

  it('gives grid cards with video the video strip, its caption and the running sub-step', async () => {
    const app = await start({ video: ['encoding'] });
    await click(button('Grid view'));
    const q3 = cardOf('Q3 planning sync');
    const strip = q3.querySelector('.video-strip');
    expect(strip?.getAttribute('role')).toBe('img');
    expect(strip?.getAttribute('aria-label')).toBe('Includes video · Screen + camera · 1440p');
    expect(strip?.querySelector('.video-strip-pill')?.textContent).toBe('Encoding 42%');
    expect(strip?.querySelector('.video-strip-caption')?.textContent).toBe('Screen + camera · 1440p');
    expect(strip?.querySelector('.video-strip-win')).not.toBeNull();
    expect(strip?.querySelector('.video-strip-cam')).not.toBeNull();
    expect(q3.querySelector('.wave-strip')).toBeNull();
    // The strip names the sub-step, so the card's own pill is just "Video".
    expect(pillsOf(q3.querySelector<HTMLElement>('.card-pills') ?? q3)).toContain('active:Video');

    const cs = cardOf('CS 301, lecture 12: consensus protocols');
    expect(cs.querySelector('.video-strip-caption')?.textContent).toBe('Screen · 1080p');
    expect(cs.querySelector('.video-strip-cam')).toBeNull();
    expect(cs.querySelector('.video-strip-pill')).toBeNull();

    const board = cardOf('Board prep walkthrough');
    const removed = board.querySelector('.video-strip--removed');
    expect(removed?.getAttribute('aria-label')).toBe('Video removed · audio kept');
    expect(removed?.textContent).toBe('Video removed · audio kept');

    expect(cardOf('Weekly 1:1 with Sam').querySelector('.wave-strip')).not.toBeNull();
    expect(app.container.querySelector('.proc-card')).toBeNull();
  });

  it('says in the recovery dialog how much video came back', async () => {
    const app = await start({ video: ['recovered'], recovery: true });
    await until(() => app.container.querySelector('.dialog-body') !== null || document.querySelector('.dialog-body') !== null);
    const body = document.querySelector('.dialog-body')?.textContent ?? '';
    expect(body).toMatch(/^Weekly 1:1 with Sam was saved up to [\d:]+ before the recording was interrupted\. All 2 tracks are intact\. The camera video was recovered up to [\d:]+\. The last 20 seconds may be missing\.$/);
  });
});
