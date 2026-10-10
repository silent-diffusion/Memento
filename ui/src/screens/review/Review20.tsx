// Review's 2.0 wiring (DESIGN.md §19), kept out of ReviewScreen so the screen only passes it on: the suggestions
// (useReview20), their actions with Undo (reviewActions20.ts), the match prompt under each unnamed speaker, the
// suggested chapters under the Chapters list, and what selection mode and the line context menu do.
import type { JSX } from 'preact';
import { useMemo, useRef, useState } from 'preact/hooks';
import type { BridgeClient } from '../../bridge/client';
import type { Project, Speaker, Transcript, TranscriptCopyFormat } from '../../bridge/types';
import type { UndoManager } from '../../state/undo';
import type { LineActions } from './LineSelection';
import type { ReviewActions } from './reviewActions';
import { createReview20Actions, type Review20Deps } from './reviewActions20';
import { SuggestedChapters, VoiceMatchPrompt } from './ReviewSuggestions';
import { useReview20 } from './useReview20';
import type { TranscriptApi } from './useTranscript';

export interface Review20Wiring {
  speakerExtra: (speaker: Speaker) => JSX.Element | null;
  chaptersExtra: JSX.Element | null;
  lineActions: LineActions;
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

export function useReview20Wiring({
  bridge,
  recordingId,
  undo,
  api,
  actions,
  project,
  transcript,
  patch,
  rememberVoices,
  seek,
  onError,
  copyLines,
}: {
  bridge: BridgeClient;
  recordingId: string;
  undo: UndoManager;
  api: TranscriptApi;
  actions: ReviewActions;
  project: Project | null;
  transcript: Transcript | null;
  patch: (changes: Partial<Project>) => void;
  rememberVoices: boolean;
  seek: (ms: number) => void;
  onError: (title: string, message: string) => void;
  copyLines: (segmentIds: string[], format: TranscriptCopyFormat) => void;
}): Review20Wiring {
  const chaptersKey = (project?.chapters ?? []).map((c) => `${c.id}@${c.atMs}`).join(',');
  const suggestions = useReview20(bridge, recordingId, transcript === null ? null : transcript.version, chaptersKey, rememberVoices);
  const [busy, setBusy] = useState(false);

  const depsRef = useRef<Review20Deps | null>(null);
  depsRef.current = {
    bridge,
    recordingId,
    undo,
    api,
    actions,
    project,
    segments: transcript?.segments ?? [],
    speakers: transcript?.speakers ?? [],
    patch,
    refreshMatches: suggestions.refreshMatches,
    refreshSuggestions: suggestions.refreshSuggestions,
  };
  const actions20 = useMemo(
    () =>
      createReview20Actions(() => {
        const deps = depsRef.current;
        if (deps === null) {
          throw new Error('Review is not open.');
        }
        return deps;
      }),
    [],
  );

  const run = (title: string, work: () => Promise<unknown>): void => {
    setBusy(true);
    work()
      .catch((e: unknown) => {
        onError(title, messageOf(e));
      })
      .finally(() => {
        setBusy(false);
      });
  };

  const matchBySpeaker = new Map(suggestions.matches.map((m) => [m.speakerId, m]));
  const speakerExtra = (speaker: Speaker): JSX.Element | null => {
    const match = matchBySpeaker.get(speaker.id);
    if (match === undefined || speaker.renamed) {
      return null;
    }
    return (
      <VoiceMatchPrompt
        match={match}
        speakerName={speaker.name}
        busy={busy}
        onUse={() => {
          run(`${speaker.name} was not renamed`, () =>
            actions20.acceptMatch(match, speaker).then(() => {
              undo.announce(`${speaker.name} is now ${match.name}`);
            }),
          );
        }}
        onDecline={() => {
          run('The suggestion was not dismissed', () => actions20.declineMatch(match));
        }}
      />
    );
  };

  const chaptersExtra = (
    <SuggestedChapters
      suggestions={suggestions.suggestions}
      busy={busy}
      onSeek={seek}
      onAccept={(s) => {
        run('The chapter was not added', () => actions20.acceptSuggestion(s));
      }}
      onAcceptAll={() => {
        const all = suggestions.suggestions;
        run('The chapters were not added', () =>
          actions20.acceptAllSuggestions(all).then(() => {
            undo.announce(`Added ${all.length} ${all.length === 1 ? 'chapter' : 'chapters'}`);
          }),
        );
      }}
      onDismiss={(s) => {
        run('The suggestion was not dismissed', () => actions20.dismissSuggestion(s));
      }}
    />
  );

  const lineActions: LineActions = {
    assign: (lines, speaker) => actions20.assignLines(lines, speaker),
    addSpeaker: (lines, name) => actions20.addSpeakerToLines(lines, name),
    merge: (from, into) => actions.mergeSpeakers(from, into),
    highlight: (lines) => actions20.highlightLines(lines),
    removeHighlights: (highlights) => actions20.removeLineHighlights(highlights),
    chapterAt: (line) => actions20.chapterAtLine(line),
    copy: (lines, format) => {
      copyLines(
        lines.map((l) => l.id),
        format,
      );
    },
    play: (line) => {
      seek(line.start * 1000);
    },
    onError,
    announce: (text) => {
      undo.announce(text);
    },
  };

  return { speakerExtra, chaptersExtra, lineActions };
}
