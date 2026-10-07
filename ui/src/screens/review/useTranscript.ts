// Review's transcript state: transcript.get, refetched on transcript.changed; the recording's stages
// from processing.progress (for "Transcribing 64% · local GPU"); and the transcript.* mutations,
// each applied to the local copy from the host's answer.
import { useEffect, useMemo, useRef, useState } from 'preact/hooks';
import type { BridgeClient } from '../../bridge/client';
import type { Speaker, StageName, StageStatus, TranscriptGetResult, TranscriptSearchMatch, TranscriptSegment } from '../../bridge/types';
import { isPausedLabel, stepMatch } from '../../format/transcript';

function messageOf(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

export interface TranscriptApi {
  result: TranscriptGetResult | null;
  error: string | null;
  /** Every stage of the recording, live. */
  stages: StageStatus[];
  /** The host's answer is the new segment; failures come back as the host's message. */
  editSegment: (segmentId: string, text: string) => Promise<string | null>;
  setSpeaker: (segmentId: string, speakerId: string | null, newSpeakerName?: string) => Promise<string | null>;
  renameSpeaker: (speakerId: string, name: string) => Promise<string | null>;
  mergeSpeakers: (fromSpeakerId: string, intoSpeakerId: string) => Promise<string | null>;
  markReviewed: (reviewed: boolean) => Promise<string | null>;
  retry: (stage: StageName, remedyId?: string) => Promise<string | null>;
  transcribe: (modelId?: string, language?: string) => Promise<string | null>;
  /** processing.resume: carries on with paused processing. */
  resume: () => Promise<string | null>;
  reload: () => void;
}

function withSegment(result: TranscriptGetResult, segment: TranscriptSegment, speakers?: Speaker[]): TranscriptGetResult {
  const transcript = result.transcript;
  if (transcript === null) {
    return result;
  }
  return {
    ...result,
    transcript: {
      ...transcript,
      segments: transcript.segments.map((s) => (s.id === segment.id ? segment : s)),
      ...(speakers === undefined ? {} : { speakers }),
    },
  };
}

export function useTranscript(bridge: BridgeClient, recordingId: string, initialStages: StageStatus[] | null): TranscriptApi {
  const [result, setResult] = useState<TranscriptGetResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [stages, setStages] = useState<StageStatus[]>([]);
  const [reload, setReload] = useState(0);
  const sequence = useRef(0);
  const stagesRef = useRef<StageStatus[]>([]);

  useEffect(() => {
    const mine = ++sequence.current;
    bridge
      .call('transcript.get', { recordingId })
      .then((next) => {
        if (mine === sequence.current) {
          setResult(next);
          setError(null);
        }
      })
      .catch((e: unknown) => {
        if (mine === sequence.current) {
          setError(messageOf(e, 'The transcript could not be read.'));
        }
      });
  }, [bridge, recordingId, reload]);

  // The project's row stages until processing.progress reports the whole pipeline.
  useEffect(() => {
    if (initialStages !== null && stagesRef.current.length === 0) {
      stagesRef.current = initialStages;
      setStages(initialStages);
    }
  }, [initialStages]);

  useEffect(() => {
    const offs = [
      bridge.on('transcript.changed', (payload) => {
        if (payload.recordingId === recordingId) {
          setReload((n) => n + 1);
        }
      }),
      bridge.on('processing.progress', (payload) => {
        if (payload.recordingId !== recordingId) {
          return;
        }
        const before = stagesRef.current;
        stagesRef.current = payload.stages;
        setStages(payload.stages);
        // A transcript stage that started, stopped or paused changes what transcript.get answers.
        const was = (s: StageStatus[], name: StageName): string => {
          const stage = s.find((st) => st.stage === name);
          return stage === undefined ? 'none' : `${stage.state}:${isPausedLabel(stage.label) ? 'paused' : ''}`;
        };
        if (was(before, 'transcript') !== was(payload.stages, 'transcript')) {
          setReload((n) => n + 1);
        }
      }),
    ];
    return () => {
      for (const off of offs) {
        off();
      }
    };
  }, [bridge, recordingId]);

  const apply = (change: (r: TranscriptGetResult) => TranscriptGetResult): void => {
    setResult((r) => (r === null ? r : change(r)));
  };

  const attempt = async (run: () => Promise<void>, fallback: string): Promise<string | null> => {
    try {
      await run();
      return null;
    } catch (e) {
      return messageOf(e, fallback);
    }
  };

  return {
    result,
    error,
    stages,
    reload: () => {
      setReload((n) => n + 1);
    },
    editSegment: (segmentId, text) =>
      attempt(async () => {
        const { segment } = await bridge.call('transcript.editSegment', { recordingId, segmentId, text });
        apply((r) => withSegment(r, segment));
      }, 'The line was not saved.'),
    setSpeaker: (segmentId, speakerId, newSpeakerName) =>
      attempt(async () => {
        const { segment, speakers } = await bridge.call('transcript.setSegmentSpeaker', {
          recordingId,
          segmentId,
          speakerId,
          ...(newSpeakerName === undefined ? {} : { newSpeakerName }),
        });
        apply((r) => withSegment(r, segment, speakers));
      }, 'The speaker was not changed.'),
    renameSpeaker: (speakerId, name) =>
      attempt(async () => {
        const { speakers } = await bridge.call('transcript.renameSpeaker', { recordingId, speakerId, name });
        apply((r) => (r.transcript === null ? r : { ...r, transcript: { ...r.transcript, speakers } }));
      }, 'The speaker was not renamed.'),
    mergeSpeakers: (fromSpeakerId, intoSpeakerId) =>
      attempt(async () => {
        await bridge.call('transcript.mergeSpeakers', { recordingId, fromSpeakerId, intoSpeakerId });
        setReload((n) => n + 1);
      }, 'The speakers were not merged.'),
    markReviewed: (reviewed) =>
      attempt(async () => {
        const answer = await bridge.call('transcript.markReviewed', { recordingId, reviewed });
        apply((r) => (r.transcript === null ? r : { ...r, transcript: { ...r.transcript, reviewed: answer.reviewed } }));
      }, 'The transcript was not marked.'),
    retry: (stage, remedyId) =>
      attempt(async () => {
        await bridge.call('processing.retry', { recordingId, stage, ...(remedyId === undefined ? {} : { remedyId }) });
        setReload((n) => n + 1);
      }, 'The stage was not retried.'),
    transcribe: (modelId, language) =>
      attempt(async () => {
        await bridge.call('transcript.retranscribe', {
          recordingId,
          ...(modelId === undefined ? {} : { modelId }),
          ...(language === undefined ? {} : { language }),
        });
        setReload((n) => n + 1);
      }, 'Transcription was not queued.'),
    resume: () =>
      attempt(async () => {
        await bridge.call('processing.resume');
        setReload((n) => n + 1);
      }, 'Processing did not resume.'),
  };
}

export const SEARCH_DEBOUNCE_MS = 150;

export interface SearchApi {
  query: string;
  setQuery: (query: string) => void;
  matches: TranscriptSearchMatch[];
  /** Index into `matches` of the one the search is on, or -1. */
  current: number;
  /** True once the host answered for the current query. */
  answered: boolean;
  step: (direction: 1 | -1) => TranscriptSearchMatch | null;
  clear: () => void;
}

/**
 * The player strip's transcript search: transcript.search after a short pause in typing, the
 * result count, and next / previous with wrap-around. `version` refreshes the matches after an edit.
 */
export function useTranscriptSearch(bridge: BridgeClient, recordingId: string, version: number | null): SearchApi {
  const [query, setQuery] = useState('');
  const [matches, setMatches] = useState<TranscriptSearchMatch[]>([]);
  const [current, setCurrent] = useState(-1);
  const [answeredFor, setAnsweredFor] = useState<string | null>(null);
  const sequence = useRef(0);

  useEffect(() => {
    const needle = query.trim();
    const mine = ++sequence.current;
    if (needle === '') {
      setMatches([]);
      setCurrent(-1);
      setAnsweredFor(null);
      return undefined;
    }
    const timer = setTimeout(() => {
      bridge
        .call('transcript.search', { recordingId, query: needle })
        .then(({ matches: found }) => {
          if (mine === sequence.current) {
            setMatches(found);
            setCurrent((c) => (c >= found.length ? found.length - 1 : c));
            setAnsweredFor(needle);
          }
        })
        .catch((e: unknown) => {
          console.warn('[review] transcript.search failed', e);
          if (mine === sequence.current) {
            setMatches([]);
            setAnsweredFor(needle);
          }
        });
    }, SEARCH_DEBOUNCE_MS);
    return () => {
      clearTimeout(timer);
    };
  }, [bridge, recordingId, query, version]);

  return useMemo<SearchApi>(
    () => ({
      query,
      setQuery: (next) => {
        setQuery(next);
        setCurrent(-1);
      },
      matches,
      current,
      answered: answeredFor !== null && answeredFor === query.trim(),
      step: (direction) => {
        const next = stepMatch(current, matches.length, direction);
        setCurrent(next);
        return matches[next] ?? null;
      },
      clear: () => {
        setQuery('');
        setMatches([]);
        setCurrent(-1);
      },
    }),
    [query, matches, current, answeredFor],
  );
}
