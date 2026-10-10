import { render } from 'preact';
import { afterEach, describe, expect, it } from 'vitest';
import type { RecordingLiveTranscriptPayload } from '../../bridge/types';
import { LiveTranscriptCard } from './RecordParts';

const draft = (over: Partial<RecordingLiveTranscriptPayload>): RecordingLiveTranscriptPayload => ({
  sessionId: 's1',
  segments: [
    { start: 1, end: 3, text: 'We start with the budget.' },
    { start: 11, end: 14, text: 'Then the launch date.' },
  ],
  state: 'listening',
  engine: 'Local · CPU · Small',
  note: null,
  ...over,
});

describe('Live transcript card (DESIGN.md §8, 2.0)', () => {
  const host = document.createElement('div');

  afterEach(() => {
    render(null, host);
  });

  const card = (live: RecordingLiveTranscriptPayload | null, phase: 'recording' | 'paused' = 'recording', timing: 'during' | 'after' = 'during'): HTMLElement => {
    render(<LiveTranscriptCard live={live} timing={timing} phase={phase} elapsedMs={20_000} />, host);
    const el = host.querySelector<HTMLElement>('[aria-label="Live transcript"]');
    if (el === null) {
      throw new Error('no live transcript card');
    }
    return el;
  };

  it('shows the lines as a provisional draft with where it runs, the newest still being heard', () => {
    const el = card(draft({}));
    expect(el.querySelector('[data-provisional="true"]')?.getAttribute('aria-label')).toBe('Provisional lines');
    expect([...el.querySelectorAll('.rec-live-at')].map((t) => t.textContent)).toEqual(['00:00:01', '00:00:11']);
    expect(el.querySelector('.pill.done')?.textContent).toBe('Local · CPU · Small');
    expect(el.querySelectorAll('.rec-live-more')).toHaveLength(1);
    expect(el.textContent).toContain('Rough draft, not saved.');
  });

  it('keeps the lines and says why while it waits for a full pass', () => {
    const note = 'Waiting while Memento transcribes another recording, so the live draft never slows a full transcript. It carries on when that is done.';
    const el = card(draft({ state: 'paused', note }));
    expect(el.querySelectorAll('.rec-live-line')).toHaveLength(2);
    expect(el.querySelector('.rec-live-note')?.textContent).toBe(note);
    expect(el.querySelector('.pill.queued')?.textContent).toBe('Local · CPU · Small');
    expect(el.querySelectorAll('.rec-live-more')).toHaveLength(0);
  });

  it('says what to install when no model can make the draft', () => {
    const el = card(draft({ segments: [], state: 'unavailable', engine: null, note: 'The live transcript needs the Small or Base transcription model.' }));
    expect(el.textContent).toContain('needs the Small or Base transcription model');
    expect(el.querySelector('.pill')).toBeNull();
  });

  it('shows nothing of a draft when the setting is off', () => {
    const el = card(draft({}), 'recording', 'after');
    expect(el.querySelectorAll('.rec-live-line')).toHaveLength(0);
    expect(el.textContent).toContain('Live transcription is off.');
  });
});
