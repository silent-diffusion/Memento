// The quiet-source hint on the Recording session (after 2.0.0-beta.1, ENGINE-NOTES.md §Q): each source's last 10 s of
// recording.levels, judged once a second (format/levels.ts quietSources), so the hint under a meter appears and goes
// calmly instead of with every level event.
import { effect, type Signal } from '@preact/signals';
import { useEffect, useRef, useState } from 'preact/hooks';
import type { RecordingLevelsPayload } from '../../bridge/types';
import { quietSources, SpeechWindow } from '../../format/levels';

/** How often the hint is judged. */
export const QUIET_CHECK_MS = 1_000;

const NONE: ReadonlyMap<string, number> = new Map();
const performanceNow = (): number => performance.now();

function same(a: ReadonlyMap<string, number>, b: ReadonlyMap<string, number>): boolean {
  return a.size === b.size && [...a].every(([id, db]) => b.has(id) && Math.round(b.get(id) ?? 0) === Math.round(db));
}

/** The quiet sources of the session in progress, with each one's speech level in dBFS; empty outside a recording. */
export function useQuietSources(levels: Signal<RecordingLevelsPayload | null>, sessionId: string | null, now: () => number = performanceNow): ReadonlyMap<string, number> {
  const windows = useRef(new Map<string, SpeechWindow>());
  // The clock is read through a ref, so a caller passing a new function each render does not start the window over.
  const clock = useRef(now);
  clock.current = now;
  const [quiet, setQuiet] = useState<ReadonlyMap<string, number>>(NONE);

  useEffect(() => {
    windows.current = new Map();
    setQuiet(NONE);
    if (sessionId === null) {
      return undefined;
    }
    const stop = effect(() => {
      const payload = levels.value;
      if (payload?.sessionId !== sessionId) {
        return;
      }
      const at = clock.current();
      for (const level of payload.levels) {
        let window = windows.current.get(level.sourceId);
        if (window === undefined) {
          window = new SpeechWindow();
          windows.current.set(level.sourceId, window);
        }
        window.add(at, level.peak);
      }
    });
    const timer = setInterval(() => {
      setQuiet((before) => {
        const next = quietSources(windows.current, before);
        return same(before, next) ? before : next;
      });
    }, QUIET_CHECK_MS);
    return () => {
      stop();
      clearInterval(timer);
    };
  }, [levels, sessionId]);

  return quiet;
}
