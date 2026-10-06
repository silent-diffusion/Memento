// The Tracks card (DESIGN.md §8, §5.11): one lane per source that has a track in this session,
// drawing the loudest level per span of the recording so far from recording.levels, and the ruler.
import { effect, type Signal } from '@preact/signals';
import type { JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import type { RecordingLevelsPayload, Track } from '../../bridge/types';
import { formatRuler, formatTimecode } from '../../format/duration';
import type { ElapsedClock } from '../../format/elapsed';
import { LaneHistory, laneBarHeight } from '../../format/levels';
import { sourceName } from './RecordParts';

export const LANE_BARS = 72;

export interface Lane {
  sourceId: string;
  name: string;
  live: boolean;
}

/** Sources in order of their first track; a source is live while one of its tracks is open. */
export function lanesOf(tracks: readonly Track[]): Lane[] {
  const lanes: Lane[] = [];
  for (const track of tracks) {
    const live = track.endedEarlyAtMs === null;
    const known = lanes.find((l) => l.sourceId === track.sourceId);
    if (known === undefined) {
      lanes.push({ sourceId: track.sourceId, name: sourceName({ kind: track.sourceKind, name: track.name }), live });
    } else if (live) {
      known.live = true;
    }
  }
  return lanes;
}

interface TracksCardProps {
  sessionId: string;
  tracks: readonly Track[];
  levels: Signal<RecordingLevelsPayload | null>;
  clock: ElapsedClock;
  running: boolean;
}

/** Redraws at most ~12 times a second; level events can arrive 30 times a second. */
const REDRAW_MS = 80;

export function TracksCard({ sessionId, tracks, levels, clock, running }: TracksCardProps): JSX.Element {
  const histories = useRef(new Map<string, LaneHistory>());
  const [, setFrame] = useState(0);

  useEffect(() => {
    histories.current = new Map();
  }, [sessionId]);

  useEffect(() => {
    let last = 0;
    let pending: ReturnType<typeof setTimeout> | undefined;
    const redraw = (): void => {
      pending = undefined;
      last = performance.now();
      setFrame((n) => n + 1);
    };
    const stop = effect(() => {
      const payload = levels.value;
      if (payload?.sessionId !== sessionId) {
        return;
      }
      const at = clock.read(performance.now());
      for (const level of payload.levels) {
        let history = histories.current.get(level.sourceId);
        if (history === undefined) {
          history = new LaneHistory(LANE_BARS);
          histories.current.set(level.sourceId, history);
        }
        history.add(at, level.rms);
      }
      pending ??= setTimeout(redraw, Math.max(0, REDRAW_MS - (performance.now() - last)));
    });
    return () => {
      stop();
      clearTimeout(pending);
    };
  }, [levels, sessionId, clock]);

  // While paused nothing arrives; keep the ruler in step with the timer anyway.
  useEffect(() => {
    if (!running) {
      return undefined;
    }
    const timer = setInterval(() => {
      setFrame((n) => n + 1);
    }, 500);
    return () => {
      clearInterval(timer);
    };
  }, [running]);

  const now = clock.read(performance.now());
  const lanes = lanesOf(tracks);
  return (
    <section class="rec-card rec-tracks" aria-label="Tracks">
      <div class="rec-card-head">
        <span class="lbl">Tracks</span>
        <span class="mono rec-tracks-time">{formatTimecode(now)}</span>
      </div>
      <div class="rec-lanes">
        {lanes.map((lane) => {
          const bars = histories.current.get(lane.sourceId)?.bars(now) ?? Array.from({ length: LANE_BARS }, () => null);
          return (
            <div key={lane.sourceId} class="rec-lane-row" data-live={lane.live}>
              <span class="rec-lane-name">{lane.name}</span>
              <div class="rec-lane" aria-hidden="true">
                {bars.map((value, i) => (
                  <div
                    key={i}
                    class={value === null ? 'bar rec-bar--gap' : lane.live ? 'bar live' : 'bar'}
                    style={{ height: `${value === null ? 2 : laneBarHeight(value)}px` }}
                  />
                ))}
              </div>
            </div>
          );
        })}
      </div>
      <div class="rec-lane-row">
        <span />
        <div class="mono rec-ruler" aria-hidden="true">
          <span>00:00</span>
          <span>{formatRuler(now / 4)}</span>
          <span>{formatRuler(now / 2)}</span>
          <span>{formatRuler((now * 3) / 4)}</span>
          <span>{formatRuler(now)}</span>
        </div>
      </div>
    </section>
  );
}
