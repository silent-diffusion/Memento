// The 2.0 suggestions in Review's outline (DESIGN.md §19, renders/Features20.dc.html): the known-voice match
// prompt under an unnamed speaker in People ("Sounds like Priya Natarajan · 91% match · 4 past recordings", Use name,
// Not Priya) and the "Suggested chapters · n" block under the Chapters (mono time, dotted title, accept and dismiss,
// Accept all). Nothing is applied until the person says so; every choice is one Undo step (reviewActions20.ts).
import type { JSX } from 'preact';
import type { ChapterSuggestion, VoiceMatch } from '../../bridge/types';
import { CheckIcon, CloseIcon, PersonIcon } from '../../components/icons';
import { formatDuration } from '../../format/duration';
import './review20.css';

/** "Priya" from "Priya Natarajan", for "Not Priya". */
export function firstName(name: string): string {
  return name.trim().split(/\s+/)[0] ?? name;
}

/** "91% match · 4 past recordings". */
export function matchWording(match: VoiceMatch): string {
  const percent = `${Math.round(Math.max(0, Math.min(1, match.similarity)) * 100)}% match`;
  return `${percent} · ${match.recordings} past ${match.recordings === 1 ? 'recording' : 'recordings'}`;
}

export function VoiceMatchPrompt({
  match,
  speakerName,
  busy,
  onUse,
  onDecline,
}: {
  match: VoiceMatch;
  speakerName: string;
  busy: boolean;
  onUse: () => void;
  onDecline: () => void;
}): JSX.Element {
  return (
    <div class="sug voice-match" role="group" aria-label={`${speakerName} sounds like ${match.name}`} data-voice-id={match.voiceId}>
      <PersonIcon size={16} class="sug-icon" />
      <span class="sug-text">
        Sounds like <strong class="sug-name">{match.name}</strong> <span class="sug-meta">· {matchWording(match)}</span>
      </span>
      <span class="sug-actions">
        <button
          class="btn p sug-btn"
          type="button"
          disabled={busy}
          aria-label={`Use the name ${match.name} for ${speakerName}`}
          title={`Every line of ${speakerName} gets the name ${match.name}. Undo takes it back.`}
          onClick={onUse}
        >
          Use name
        </button>
        <button
          class="btn g sug-btn"
          type="button"
          disabled={busy}
          aria-label={`${speakerName} is not ${match.name}`}
          title="The suggestion goes away for this recording"
          onClick={onDecline}
        >
          Not {firstName(match.name)}
        </button>
      </span>
    </div>
  );
}

export function SuggestedChapters({
  suggestions,
  busy,
  onSeek,
  onAccept,
  onAcceptAll,
  onDismiss,
}: {
  suggestions: readonly ChapterSuggestion[];
  busy: boolean;
  onSeek: (ms: number) => void;
  onAccept: (suggestion: ChapterSuggestion) => void;
  onAcceptAll: () => void;
  onDismiss: (suggestion: ChapterSuggestion) => void;
}): JSX.Element | null {
  if (suggestions.length === 0) {
    return null;
  }
  return (
    <section class="sug-chapters" aria-labelledby="sug-chapters-label">
      <div class="sug-chapters-head">
        <span id="sug-chapters-label" class="lbl">
          Suggested chapters · {suggestions.length}
        </span>
        <button class="btn link sug-accept-all" type="button" disabled={busy} onClick={onAcceptAll}>
          Accept all
        </button>
      </div>
      <ul class="sug-chapter-list">
        {suggestions.map((s) => {
          const time = formatDuration(s.atMs);
          return (
            <li key={s.id} class="chap sug-chapter" data-suggestion-id={s.id}>
              <button
                class="chap-time"
                type="button"
                aria-label={`Play from ${time}, suggested chapter ${s.title}`}
                onClick={() => {
                  onSeek(s.atMs);
                }}
              >
                <span class="mono chap-at">{time}</span>
              </button>
              <span class="sug-chapter-title" title={`Suggested: ${s.basis}`}>
                {s.title}
                <span class="sr"> (suggested, not accepted yet)</span>
              </span>
              <button
                class="icon-btn sug-icon-btn sug-icon-btn--ok"
                type="button"
                disabled={busy}
                aria-label={`Accept the chapter ${s.title} at ${time}`}
                onClick={() => {
                  onAccept(s);
                }}
              >
                <CheckIcon size={13} />
              </button>
              <button
                class="icon-btn sug-icon-btn"
                type="button"
                disabled={busy}
                aria-label={`Dismiss the chapter ${s.title} at ${time}`}
                onClick={() => {
                  onDismiss(s);
                }}
              >
                <CloseIcon size={13} />
              </button>
            </li>
          );
        })}
      </ul>
      <p class="sug-note">Found on this PC from topic shifts, pauses and speaker turns. Dotted titles are suggestions until accepted; accepted chapters join the list above.</p>
    </section>
  );
}
