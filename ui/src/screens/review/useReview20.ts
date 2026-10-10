// What Review shows from the 2.0 suggestions (DESIGN.md §19): the known voices the unnamed speakers sound like
// (voices.matches, while Remember speakers by voice is on) and the suggested chapters (annotations.suggestChapters).
// Both are read again when the transcript or the chapters change, and when an action asks.
import { useEffect, useRef, useState } from 'preact/hooks';
import type { BridgeClient } from '../../bridge/client';
import type { ChapterSuggestion, VoiceMatch } from '../../bridge/types';

export interface Review20Suggestions {
  matches: VoiceMatch[];
  suggestions: ChapterSuggestion[];
  refreshMatches: () => void;
  refreshSuggestions: () => void;
}

export function useReview20(
  bridge: BridgeClient,
  recordingId: string,
  /** The transcript's version, or null without one: nothing is suggested then. */
  transcriptVersion: number | null,
  /** Changes whenever the chapters do (an accepted suggestion becomes one). */
  chaptersKey: string,
  remember: boolean,
): Review20Suggestions {
  const [matches, setMatches] = useState<VoiceMatch[]>([]);
  const [suggestions, setSuggestions] = useState<ChapterSuggestion[]>([]);
  const [matchTick, setMatchTick] = useState(0);
  const [suggestionTick, setSuggestionTick] = useState(0);
  const matchCall = useRef(0);
  const suggestionCall = useRef(0);
  const hasTranscript = transcriptVersion !== null;

  useEffect(() => {
    const mine = ++matchCall.current;
    if (!hasTranscript || !remember) {
      setMatches([]);
      return;
    }
    bridge
      .call('voices.matches', { recordingId })
      .then(({ matches: found }) => {
        if (mine === matchCall.current) {
          setMatches(found);
        }
      })
      .catch((e: unknown) => {
        console.warn('[review] voices.matches failed', e);
        if (mine === matchCall.current) {
          setMatches([]);
        }
      });
  }, [bridge, recordingId, transcriptVersion, remember, matchTick]);

  useEffect(() => {
    const mine = ++suggestionCall.current;
    if (!hasTranscript) {
      setSuggestions([]);
      return;
    }
    bridge
      .call('annotations.suggestChapters', { recordingId })
      .then(({ suggestions: found }) => {
        if (mine === suggestionCall.current) {
          setSuggestions(found);
        }
      })
      .catch((e: unknown) => {
        console.warn('[review] annotations.suggestChapters failed', e);
        if (mine === suggestionCall.current) {
          setSuggestions([]);
        }
      });
  }, [bridge, recordingId, transcriptVersion, chaptersKey, suggestionTick]);

  return {
    matches,
    suggestions,
    refreshMatches: () => {
      setMatchTick((n) => n + 1);
    },
    refreshSuggestions: () => {
      setSuggestionTick((n) => n + 1);
    },
  };
}
