// Settings › Speakers: Remember speakers by voice and the Known voices list (DESIGN.md §19).
import { act } from 'preact/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import { button, click, mountApp, settle, until, type Harness } from '../../testing/appHarness';
import { voiceSubline } from './KnownVoices';

const NOW = new Date(2026, 9, 6, 15, 0);
const DESIGN_REVIEW = '20261005-160000-dsrev';
const TOWN_HALL = '20260918-160000-thall';

describe('Settings › Speakers › Known voices', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  /** Learns two voices through the preview host: Kai (design review) and Ana (town hall). */
  const learn = async (): Promise<void> => {
    await h.bridge.call('settings.set', { speakers: { rememberVoices: true } });
    await h.bridge.call('transcript.renameSpeaker', { recordingId: DESIGN_REVIEW, speakerId: 'sp4', name: 'Kai Moreno' });
    await h.bridge.call('voices.remember', { recordingId: DESIGN_REVIEW, speakerId: 'sp4' });
    await h.bridge.call('transcript.renameSpeaker', { recordingId: TOWN_HALL, speakerId: 'sp4', name: 'Ana Ruiz' });
    await h.bridge.call('voices.remember', { recordingId: TOWN_HALL, speakerId: 'sp4' });
  };

  const open = async (seed: boolean): Promise<void> => {
    h = await mountApp({ name: 'settings', section: 'speakers' }, {}, NOW);
    if (seed) {
      await act(async () => {
        await learn();
        h.store.settings.value = await h.bridge.call('settings.get');
      });
    }
    await until(() => h.container.querySelector('.known-voices') !== null);
    await settle(30);
  };

  const names = (): string[] => [...h.container.querySelectorAll('.known-name')].map((n) => n.textContent);

  it('says the switch is off by default, what it does, and that nothing is learned yet', async () => {
    await open(false);
    const toggle = h.container.querySelector<HTMLButtonElement>('[role="switch"][aria-label="Remember speakers by voice"]');
    expect(toggle?.getAttribute('aria-checked')).toBe('false');
    expect(h.container.textContent).toContain('Voice profiles are signatures, not audio, stay on this PC and can be deleted below at any time.');
    expect(h.container.querySelector('.known-empty')?.textContent).toBe('No voices yet. Turn on Remember speakers by voice, then name a speaker in Review.');

    await click(toggle);
    await until(() => h.store.settings.value?.speakers.rememberVoices === true);
    expect(h.callsOf('settings.set').at(-1)).toEqual({ speakers: { rememberVoices: true } });
  });

  it('lists the voices with their recordings, suggests or not, forgets one and then all', async () => {
    await open(true);
    await until(() => names().length === 2);
    expect(names()).toEqual(['Ana Ruiz', 'Kai Moreno']);
    expect(h.container.querySelector('.known-sub')?.textContent).toMatch(/^Confirmed in 1 recording · last /);

    await click(button("Suggest Kai Moreno's voice in other recordings"));
    await until(() => button("Suggest Kai Moreno's voice in other recordings").getAttribute('aria-checked') === 'false');
    expect((await h.bridge.call('voices.list')).voices.find((v) => v.name === 'Kai Moreno')?.suggest).toBe(false);

    await click(button("Forget Ana Ruiz's voice"));
    await until(() => names().length === 1);
    expect(names()).toEqual(['Kai Moreno']);

    // With one voice left, Forget suffices: Forget all is offered from two.
    expect(h.container.querySelector('.known-forget-all')).toBeNull();
  });

  it('asks before forgetting every voice', async () => {
    await open(true);
    await until(() => names().length === 2);
    await click(button('Forget all voices…'));
    expect(h.container.querySelector('.known-confirm')?.textContent).toContain('Forget all 2 voices? Their signatures are removed from this PC at once and cannot be brought back');
    await click(button('Cancel'));
    expect(h.container.querySelector('.known-confirm')).toBeNull();
    expect(h.callsOf('voices.forgetAll')).toEqual([]);

    await click(button('Forget all voices…'));
    await click(button('Forget all'));
    await until(() => names().length === 0);
    expect(h.callsOf('voices.forgetAll')).toHaveLength(1);
  });

  it('words the line under a voice', () => {
    expect(voiceSubline({ id: 'v1', name: 'Priya', recordings: 4, lastConfirmedAt: '2026-10-05T10:00:00+01:00', suggest: true }, NOW)).toBe('Confirmed in 4 recordings · last Oct 5');
  });
});
