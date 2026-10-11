import { signal, type Signal } from '@preact/signals';
import { render } from 'preact';
import { act } from 'preact/test-utils';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { AudioSource, RecordingLevelsPayload } from '../../bridge/types';
import { quietHint, quietSources, SpeechWindow } from '../../format/levels';
import { SourcesCard, type SourceRow } from './RecordParts';
import { QUIET_CHECK_MS, useQuietSources } from './useQuietSources';

const fromDb = (db: number): number => 10 ** (db / 20);

/** Ten seconds of readings at 30 a second: speech peaks at `speechDb` a third of the time, `floorDb` between. */
function speaking(window: SpeechWindow, speechDb: number, floorDb: number, startMs = 0): void {
  for (let i = 0; i < 300; i++) {
    window.add(startMs + i * 33, fromDb(i % 3 === 0 ? speechDb : floorDb));
  }
}

describe('quiet sources (after 2.0.0-beta.1, ENGINE-NOTES.md §Q)', () => {
  it('finds a microphone that speaks under -30 dBFS, or 20 dB under the system audio', () => {
    const mic = new SpeechWindow();
    const system = new SpeechWindow();
    speaking(mic, -38, -58);
    speaking(system, -12, -80);

    const quiet = quietSources(
      new Map([
        ['mic', mic],
        ['system', system],
      ]),
      new Map(),
    );

    expect([...quiet.keys()]).toEqual(['mic']);
    expect(quiet.get('mic')).toBeCloseTo(-38, 0);
  });

  it('leaves a normal microphone, a silent one and too few readings alone', () => {
    const normal = new SpeechWindow();
    speaking(normal, -14, -50);
    const silent = new SpeechWindow();
    speaking(silent, -60, -60);
    const short = new SpeechWindow();
    for (let i = 0; i < 20; i++) {
      short.add(i * 33, fromDb(-45));
    }
    const system = new SpeechWindow();
    speaking(system, -12, -80);

    const quiet = quietSources(
      new Map([
        ['normal', normal],
        ['silent', silent],
        ['short', short],
        ['system', system],
      ]),
      new Map(),
    );

    expect(quiet.size).toBe(0);
  });

  it('keeps the hint while nobody speaks and lets it go only once the speech is clearly louder', () => {
    const mic = new SpeechWindow();
    speaking(mic, -60, -60);
    expect(quietSources(new Map([['mic', mic]]), new Map([['mic', -38]])).get('mic')).toBe(-38);

    const louder = new SpeechWindow();
    speaking(louder, -28, -55); // over -30, but not yet -26: stays
    expect(quietSources(new Map([['mic', louder]]), new Map([['mic', -38]])).has('mic')).toBe(true);
    const loud = new SpeechWindow();
    speaking(loud, -18, -55);
    expect(quietSources(new Map([['mic', loud]]), new Map([['mic', -38]])).has('mic')).toBe(false);
  });

  it('only looks at the last ten seconds', () => {
    const mic = new SpeechWindow();
    speaking(mic, -38, -58, 0);
    speaking(mic, -14, -50, 20_000);
    expect(quietSources(new Map([['mic', mic]]), new Map()).size).toBe(0);
  });

  it('words the hint as §17 asks, for a microphone and for other sources', () => {
    expect(quietHint('microphone', 'Microphone', -38.4)).toBe('Microphone is quiet (−38 dB). Move closer or raise its level in Windows; the transcript may miss you.');
    expect(quietHint('system', 'System audio', -41)).toBe('System audio is quiet (−41 dB). Raise its volume in Windows or in the app; the transcript may miss it.');
  });
});

describe('the hint under a meter', () => {
  const host = document.createElement('div');
  const mic: AudioSource = { id: 'mic:1', kind: 'microphone', name: 'USB microphone', detail: 'USB', isDefault: true, processId: null };
  const row = (over: Partial<SourceRow>): SourceRow => ({ source: mic, on: true, busy: false, rms: 0.01, lostAt: null, quietDb: null, ...over });

  afterEach(() => {
    render(null, host);
  });

  it('shows one status line under the quiet source only while it records', () => {
    render(<SourcesCard rows={[row({ quietDb: -38 })]} error={null} onToggle={() => undefined} onRescan={() => undefined} video={null} />, host);
    const hint = host.querySelector('.src-hint');
    expect(hint?.getAttribute('role')).toBe('status');
    expect(hint?.textContent).toBe('Microphone is quiet (−38 dB). Move closer or raise its level in Windows; the transcript may miss you.');

    render(<SourcesCard rows={[row({ quietDb: -38, on: false })]} error={null} onToggle={() => undefined} onRescan={() => undefined} video={null} />, host);
    expect(host.querySelector('.src-hint')).toBeNull();
    render(<SourcesCard rows={[row({ quietDb: null })]} error={null} onToggle={() => undefined} onRescan={() => undefined} video={null} />, host);
    expect(host.querySelector('.src-hint')).toBeNull();
  });
});

describe('useQuietSources', () => {
  const host = document.createElement('div');
  let clock = 0;
  let seen: ReadonlyMap<string, number> = new Map();

  beforeEach(() => {

    clock = 0;
  });

  afterEach(() => {
    render(null, host);

  });

  function Probe({ levels, sessionId }: { levels: Signal<RecordingLevelsPayload | null>; sessionId: string | null }): null {
    seen = useQuietSources(levels, sessionId, () => clock);
    return null;
  }

  it('judges once a second from the session’s level events, and forgets them with the session', async () => {
    const levels = signal<RecordingLevelsPayload | null>(null);
    await act(async () => {
      render(<Probe levels={levels} sessionId="s1" />, host);
      await Promise.resolve();
    });
    await act(async () => {
      for (let i = 0; i < 300; i++) {
        clock = i * 33;
        const speech = i % 3 === 0;
        levels.value = {
          sessionId: 's1',
          levels: [
            { sourceId: 'mic', rms: 0, peak: fromDb(speech ? -38 : -58) },
            { sourceId: 'system', rms: 0, peak: fromDb(speech ? -12 : -80) },
          ],
        };
      }
      // Another session's levels count for nothing.
      levels.value = { sessionId: 'other', levels: [{ sourceId: 'mic', rms: 0, peak: 1 }] };
      await Promise.resolve();
    });
    expect(seen.size).toBe(0);

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, QUIET_CHECK_MS + 150));
      await Promise.resolve();
    });
    expect([...seen.keys()]).toEqual(['mic']);

    await act(async () => {
      render(<Probe levels={levels} sessionId={null} />, host);
      await Promise.resolve();
    });
    expect(seen.size).toBe(0);
  });
});
