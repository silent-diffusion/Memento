// Cards of the Recording session (DESIGN.md §8), transcribed from renders/Record.dc.html: sources,
// the stage, highlights, live transcript and agenda, and the footer.
import type { JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import type { AgendaItem, AudioSource, FooterStatusPayload, Highlight, LiveTranscriptSegment, RecordingDetails, TranscriptionTiming } from '../../bridge/types';
import { Toggle } from '../../components/Controls';
import { CheckIcon, FlagIcon, PauseIcon, PlayIcon, StopIcon } from '../../components/icons';
import { agendaMarks } from '../../format/agenda';
import { formatTimecode } from '../../format/duration';
import type { ElapsedClock } from '../../format/elapsed';
import type { TrackFormat } from '../../format/estimate';
import { meterPercent } from '../../format/levels';
import { recordStatusLine, recordStorageLine, type RecordPhase } from '../../format/recordFooter';

// ---------------------------------------------------------------------------------------------
// Sources
// ---------------------------------------------------------------------------------------------

export interface SourceRow {
  source: AudioSource;
  on: boolean;
  /** A switch is waiting for the host. */
  busy: boolean;
  /** 0..1 RMS from recording.levels, or null outside a recording. */
  rms: number | null;
  /** "Stopped at 00:41:12" for a source lost in this session. */
  lostAt: number | null;
}

/** The render's sub-lines: "Shure MV7 · USB", "Everything this PC plays", "Only this app". */
function sourceSubline(source: AudioSource): string {
  switch (source.kind) {
    case 'microphone':
      return source.detail === '' ? source.name : `${source.name} · ${source.detail}`;
    case 'system':
      return source.name;
    case 'application':
      return source.detail;
  }
}

/** "Microphone", "System audio", or the app's own name ("Zoom"). */
export function sourceName(source: Pick<AudioSource, 'kind' | 'name'>): string {
  switch (source.kind) {
    case 'microphone':
      return 'Microphone';
    case 'system':
      return 'System audio';
    case 'application':
      return source.name;
  }
}

interface SourcesCardProps {
  rows: SourceRow[] | null;
  error: string | null;
  onToggle: (source: AudioSource, on: boolean) => void;
  onRescan: () => void;
}

export function SourcesCard({ rows, error, onToggle, onRescan }: SourcesCardProps): JSX.Element {
  return (
    <section class="rec-card rec-sources" aria-label="Sources">
      <div class="rec-card-head">
        <span class="lbl" id="rec-audio-label">
          Audio sources
        </span>
        <button class="btn ghost rec-add" type="button" aria-label="Add a source: look again for microphones and apps" onClick={onRescan}>
          Add
        </button>
      </div>
      <div class="rec-source-list" role="group" aria-labelledby="rec-audio-label">
        {error !== null ? (
          <p class="rec-note rec-note--error" role="alert">
            {error}
          </p>
        ) : rows === null ? (
          <p class="rec-note">Looking for microphones and apps…</p>
        ) : rows.length === 0 ? (
          <p class="rec-note">No audio sources were found. Connect a microphone or start the app you want to record, then press Add.</p>
        ) : (
          rows.map((row) => (
            <div key={row.source.id} class={row.on ? 'src' : 'src off'}>
              <div class="src-row">
                <Toggle
                  label={sourceName(row.source)}
                  checked={row.on}
                  disabled={row.busy}
                  onChange={(on) => {
                    onToggle(row.source, on);
                  }}
                />
                <div class="src-text">
                  <span class="src-name">{sourceName(row.source)}</span>
                  {row.lostAt !== null && !row.on ? (
                    <span class="src-sub src-sub--lost">Stopped at {formatTimecode(row.lostAt)}</span>
                  ) : (
                    <span class="src-sub">{sourceSubline(row.source)}</span>
                  )}
                </div>
              </div>
              <div class="src-meter" aria-hidden="true">
                <div class="src-meter-fill" style={{ width: `${row.on && row.rms !== null ? meterPercent(row.rms) : 0}%` }} />
              </div>
            </div>
          ))
        )}
      </div>
      <div class="rec-divider" />
      <div class="rec-card-head">
        <span class="lbl" id="rec-video-label">
          Video
        </span>
      </div>
      <div class="rec-source-list" role="group" aria-labelledby="rec-video-label">
        {['Screen', 'Camera'].map((name) => (
          <div key={name} class="src off">
            <div class="src-row">
              <Toggle label={`${name} (available in a later version)`} checked={false} disabled />
              <div class="src-text">
                <span class="src-name">{name}</span>
                <span class="src-sub">Available in a later version</span>
              </div>
            </div>
          </div>
        ))}
      </div>
      <p class="rec-foot-note">Each source is saved as its own synchronized track.</p>
    </section>
  );
}

// ---------------------------------------------------------------------------------------------
// Stage
// ---------------------------------------------------------------------------------------------

/** The 64 px timer: redraws four times a second from the interpolating clock. */
export function Timer({ clock, phase }: { clock: ElapsedClock; phase: RecordPhase }): JSX.Element {
  const [, setTick] = useState(0);
  useEffect(() => {
    if (phase !== 'recording') {
      return undefined;
    }
    const timer = setInterval(() => {
      setTick((n) => n + 1);
    }, 100);
    return () => {
      clearInterval(timer);
    };
  }, [phase]);
  const ms = phase === 'ready' ? 0 : clock.read(performance.now());
  return (
    <div class={phase === 'ready' ? 'mono rec-timer rec-timer--ready' : 'mono rec-timer'} role="timer" aria-live="off">
      {formatTimecode(ms)}
    </div>
  );
}

interface StageCardProps {
  phase: RecordPhase;
  clock: ElapsedClock;
  subline: string;
  canStart: boolean;
  busy: boolean;
  error: string | null;
  onStart: () => void;
  onTogglePause: () => void;
  onStop: () => void;
  onMark: () => void;
  onOpenRecording: () => void;
}

export function StageCard(props: StageCardProps): JSX.Element {
  const { phase } = props;
  const live = phase === 'recording' || phase === 'paused';
  return (
    <div class="rec-card rec-stage">
      {phase === 'ready' ? (
        <div class="lbl">Ready to record</div>
      ) : phase === 'finalizing' ? (
        <div class="lbl">Finalizing…</div>
      ) : phase === 'stopped' ? (
        <div class="rec-status rec-status--stopped">
          <span class="rec-status-dot" aria-hidden="true" />
          Stopped
        </div>
      ) : (
        <div class="rec-status">
          <span class={phase === 'recording' ? 'rec-status-dot pulse' : 'rec-status-dot'} aria-hidden="true" />
          {phase === 'recording' ? 'Recording' : 'Paused'}
        </div>
      )}

      <Timer clock={props.clock} phase={phase} />
      <div class="rec-subline">{props.subline}</div>

      <div class="rec-controls">
        {live ? (
          <>
            <button
              class="btn ghost rec-round"
              type="button"
              aria-label={phase === 'paused' ? 'Resume' : 'Pause'}
              title={phase === 'paused' ? 'Resume (Space)' : 'Pause (Space)'}
              disabled={props.busy}
              onClick={props.onTogglePause}
            >
              {phase === 'paused' ? <PlayIcon size={22} /> : <PauseIcon size={22} />}
            </button>
            <button class="btn primary rec-stop" type="button" aria-label="Stop and open review" disabled={props.busy} onClick={props.onStop}>
              <StopIcon size={26} />
            </button>
            <button class="btn ghost rec-mark" type="button" title="Mark highlight (Ctrl+M)" onClick={props.onMark}>
              <FlagIcon size={18} />
              Mark highlight
            </button>
          </>
        ) : phase === 'ready' ? (
          <button class="btn primary rec-start" type="button" disabled={!props.canStart || props.busy} onClick={props.onStart}>
            <span class="rec-start-dot" aria-hidden="true" />
            Start recording
          </button>
        ) : phase === 'stopped' ? (
          <button class="btn primary rec-open" type="button" onClick={props.onOpenRecording}>
            Open recording
          </button>
        ) : null}
      </div>

      {phase === 'ready' ? (
        <p class="rec-start-note">
          {props.canStart
            ? 'Starts the moment you press. Everything is saved to this PC as it records; transcription runs locally afterwards.'
            : 'Turn on at least one audio source to record.'}
        </p>
      ) : null}
      {props.error === null ? null : (
        <p class="rec-error" role="alert">
          {props.error}
        </p>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------------------------
// Highlights
// ---------------------------------------------------------------------------------------------

interface HighlightsCardProps {
  highlights: Highlight[];
  focusId: string | null;
  onFocused: () => void;
  onNote: (highlight: Highlight, note: string) => void;
}

function HighlightRow({ highlight, focus, onFocused, onNote }: { highlight: Highlight; focus: boolean; onFocused: () => void; onNote: HighlightsCardProps['onNote'] }): JSX.Element {
  const input = useRef<HTMLInputElement | null>(null);
  const [note, setNote] = useState(highlight.note);
  useEffect(() => {
    if (focus) {
      input.current?.focus();
      onFocused();
    }
  }, [focus, onFocused]);
  return (
    <div class="rec-mark-row">
      <span class="mono rec-mark-at">{formatTimecode(highlight.atMs)}</span>
      <input
        ref={input}
        class="field rec-mark-note"
        type="text"
        value={note}
        placeholder="Add a note (optional)"
        aria-label={`Note for the highlight at ${formatTimecode(highlight.atMs)}`}
        onInput={(event) => {
          setNote(event.currentTarget.value);
        }}
        onKeyDown={(event) => {
          if (event.key === 'Enter') {
            event.currentTarget.blur();
          }
        }}
        onBlur={() => {
          if (note.trim() !== highlight.note) {
            onNote(highlight, note.trim());
          }
        }}
      />
    </div>
  );
}

export function HighlightsCard({ highlights, focusId, onFocused, onNote }: HighlightsCardProps): JSX.Element {
  return (
    <section class="rec-card rec-highlights" aria-label="Highlights">
      <span class="lbl">Highlights</span>
      <div class="rec-mark-list">
        {highlights.map((h) => (
          <HighlightRow key={h.id} highlight={h} focus={h.id === focusId} onFocused={onFocused} onNote={onNote} />
        ))}
      </div>
    </section>
  );
}

// ---------------------------------------------------------------------------------------------
// Right column
// ---------------------------------------------------------------------------------------------

/** How long a recording runs without a draft before the card says live transcription is unavailable. */
export const LIVE_DRAFT_GRACE_MS = 8_000;

/** The newest rough segments shown in the card. */
const LIVE_LINES = 4;

interface LiveTranscriptCardProps {
  engine: FooterStatusPayload['engine'] | null;
  /** The draft for this session, or null when none has arrived. */
  segments: readonly LiveTranscriptSegment[] | null;
  /** Settings › Transcription › Timing; null until settings are read. */
  timing: TranscriptionTiming | null;
  phase: RecordPhase;
  elapsedMs: number;
}

/** What the card says when there is no draft to show (DESIGN.md §8). */
export function liveTranscriptCopy(timing: TranscriptionTiming | null, phase: RecordPhase, elapsedMs: number): string {
  const live = phase === 'recording' || phase === 'paused';
  if (timing !== 'during') {
    return 'Live transcription is off. The full transcript is made on this PC after you stop. For a rough draft here while recording, set Timing to During recording in Settings › Transcription.';
  }
  if (!live) {
    return 'Words appear here a few seconds behind the recording. Turn this off in Settings if you prefer to transcribe afterwards.';
  }
  if (elapsedMs < LIVE_DRAFT_GRACE_MS) {
    return 'Listening. Words appear here a few seconds behind the recording.';
  }
  return 'Live transcription is not available in this version of Memento. Every track is kept in full, and the transcript is made on this PC after you stop.';
}

export function LiveTranscriptCard({ engine, segments, timing, phase, elapsedMs }: LiveTranscriptCardProps): JSX.Element {
  const device = engine?.detail.device ?? engine?.device ?? null;
  const shown = (segments ?? []).slice(-LIVE_LINES);
  return (
    <section class="rec-card rec-side-card" aria-label="Live transcript">
      <div class="rec-card-head">
        <span class="lbl">Live transcript</span>
        {engine?.ready === true && timing === 'during' ? <span class="pill done">{device === null ? 'Local' : `Local · ${device}`}</span> : null}
      </div>
      {shown.length === 0 ? (
        <p class="rec-side-text">{liveTranscriptCopy(timing, phase, elapsedMs)}</p>
      ) : (
        <>
          <ol class="rec-live" aria-live="off">
            {shown.map((segment, i) => (
              <li key={`${segment.start}-${i}`} class="rec-live-line">
                <span class="mono rec-live-at">{formatTimecode(segment.start * 1000)}</span>
                <span class="rec-live-text">
                  {segment.text}
                  {i === shown.length - 1 && (phase === 'recording' || phase === 'paused') ? <span class="rec-live-more"> …</span> : null}
                </span>
              </li>
            ))}
          </ol>
          <p class="rec-side-text rec-live-foot">Rough draft. Speaker names and corrections happen in Review.</p>
        </>
      )}
    </section>
  );
}

interface AgendaCardProps {
  details: RecordingDetails;
  onToggle: (item: AgendaItem) => void;
  onOpenDetails: () => void;
}

export function AgendaCard({ details, onToggle, onOpenDetails }: AgendaCardProps): JSX.Element {
  const { agenda } = details;
  const marks = agendaMarks(agenda.items);
  return (
    <section class="rec-card rec-side-card rec-agenda" aria-label="Agenda">
      <div class="rec-card-head">
        <span class="lbl">Agenda</span>
        {agenda.items.length === 0 ? null : (
          <span class="rec-agenda-source">{agenda.source === null ? 'Pasted text' : `From ${agenda.source}`}</span>
        )}
      </div>
      {agenda.items.length === 0 ? (
        <>
          <p class="rec-side-text">
            Add an agenda in Details and agenda to tick items off here as you cover them. Nothing about it needs AI.
          </p>
          <button class="btn ghost rec-agenda-add" type="button" onClick={onOpenDetails}>
            Add an agenda
          </button>
        </>
      ) : (
        <ol class="rec-agenda-list">
          {agenda.items.map((item, i) => {
            const mark = marks[i] ?? 'upcoming';
            return (
              <li key={item.id}>
                <button
                  class={`rec-agenda-item rec-agenda-item--${mark}`}
                  type="button"
                  aria-pressed={item.covered}
                  aria-label={`${item.text}${mark === 'covered' ? ', covered' : mark === 'current' ? ', current item' : ''}. ${item.covered ? 'Mark as not covered' : 'Mark as covered'}`}
                  onClick={() => {
                    onToggle(item);
                  }}
                >
                  {mark === 'covered' ? (
                    <CheckIcon size={16} class="rec-agenda-check" strokeWidth={2.5} />
                  ) : (
                    <span class="rec-agenda-ring" aria-hidden="true" />
                  )}
                  <span class="rec-agenda-text">{item.text === '' ? 'Untitled item' : item.text}</span>
                </button>
              </li>
            );
          })}
        </ol>
      )}
    </section>
  );
}

// ---------------------------------------------------------------------------------------------
// Footer
// ---------------------------------------------------------------------------------------------

interface RecordFooterProps {
  phase: RecordPhase;
  status: FooterStatusPayload | null;
  lastCheckpointAt: string | null;
  lostAtMs: number | null;
  formats: TrackFormat[];
  now: () => Date;
}

export function RecordFooter({ phase, status, lastCheckpointAt, lostAtMs, formats, now }: RecordFooterProps): JSX.Element {
  const [, setTick] = useState(0);
  useEffect(() => {
    if (phase !== 'recording') {
      return undefined;
    }
    const timer = setInterval(() => {
      setTick((n) => n + 1);
    }, 1000);
    return () => {
      clearInterval(timer);
    };
  }, [phase]);
  const left = recordStatusLine(phase, status, lastCheckpointAt, lostAtMs, now().getTime());
  const right = recordStorageLine(status, formats);
  return (
    <footer class="app-footer">
      <span class="footer-engine">
        <span class={`status-dot status-dot--${left.tone}`} aria-hidden="true" />
        <span>
          {left.strong === undefined ? null : <span class="footer-strong">{left.strong}</span>}
          {left.text}
        </span>
      </span>
      <span class={right.low ? 'footer-storage footer-storage--low' : 'footer-storage'}>{right.text}</span>
    </footer>
  );
}
