// Hear a speaker (DESIGN.md §9, 2.6.0): the People list's play button plays a few seconds of that
// speaker's clearest line through Review's player, so "Speaker 3" can be named by ear. One sample at a
// time; it stops at the sample's end (from the playhead, with a timer behind it for when the clock does
// not report), when the person pauses or goes elsewhere, when the button is pressed again, and when the
// recording changes.
import { useEffect, useMemo, useRef, useState } from 'preact/hooks';
import type { Speaker, TranscriptSegment } from '../../bridge/types';
import { clearestLine, sampleEndMs } from '../../format/voiceSample';
import type { PlayerApi } from './Player';

/** What the People list needs to offer Hear {name}. */
export interface HearProps {
  /** The speaker whose sample is playing, or null. */
  playingId: string | null;
  /** Speakers with a line to hear. */
  hearable: ReadonlySet<string>;
  /** Plays the speaker's sample, or stops it when it is the one playing. */
  toggle: (speaker: Speaker) => void;
}

interface Sample {
  speakerId: string;
  startMs: number;
  endMs: number;
  /** Playback has reported itself playing since the sample started. */
  started: boolean;
}

/** A seek this far outside the sample is the person going elsewhere, not the sample ending. */
const ELSEWHERE_MS = 1000;

/** Added to the timer so the playhead usually ends the sample first, at the exact place. */
const TIMER_SLACK_MS = 150;

export function useHearSpeaker(player: PlayerApi, segments: readonly TranscriptSegment[], recordingId: string, resumeFollow: () => void): HearProps {
  const [sample, setSample] = useState<Sample | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const latest = useRef({ player, sample });
  latest.current = { player, sample };

  const clearTimer = (): void => {
    if (timer.current !== null) {
      clearTimeout(timer.current);
      timer.current = null;
    }
  };

  /** Ends the sample; `pause` stops playback too (the sample reached its end, or the button was pressed again). */
  const stop = (pause: boolean): void => {
    clearTimer();
    setSample(null);
    const { player: p } = latest.current;
    if (pause && p.playing) {
      p.toggle();
    }
  };
  const stopRef = useRef(stop);
  stopRef.current = stop;

  // Another recording, or leaving Review: nothing is left to fire.
  useEffect(
    () => () => {
      clearTimer();
      setSample(null);
    },
    [recordingId],
  );

  // The playhead decides: the sample ends at its end; a pause by the person, or a jump elsewhere, ends it quietly.
  useEffect(() => {
    if (sample === null) {
      return;
    }
    const at = player.positionMs;
    if (player.playing && !sample.started) {
      setSample({ ...sample, started: true });
      return;
    }
    if (sample.started && !player.playing) {
      clearTimer();
      setSample(null);
      return;
    }
    if (at < sample.startMs - ELSEWHERE_MS || at > sample.endMs + ELSEWHERE_MS) {
      clearTimer();
      setSample(null);
      return;
    }
    if (at >= sample.endMs) {
      stop(true);
    }
  }, [player.positionMs, player.playing, sample]);

  const hearable = useMemo(() => {
    const ids = new Set<string>();
    for (const line of segments) {
      if (line.speaker !== null) {
        ids.add(line.speaker);
      }
    }
    return ids;
  }, [segments]);

  return {
    playingId: sample?.speakerId ?? null,
    hearable,
    toggle: (speaker) => {
      if (sample?.speakerId === speaker.id) {
        stop(true);
        return;
      }
      const line = clearestLine(segments, speaker.id);
      if (line === null) {
        return;
      }
      clearTimer();
      const startMs = line.start * 1000;
      const endMs = sampleEndMs(line);
      resumeFollow();
      player.seek(startMs);
      if (!player.playing) {
        player.toggle();
      }
      setSample({ speakerId: speaker.id, startMs, endMs, started: player.playing });
      timer.current = setTimeout(
        () => {
          timer.current = null;
          stopRef.current(true);
        },
        (endMs - startMs) / Math.max(0.25, player.rate) + TIMER_SLACK_MS,
      );
    },
  };
}
