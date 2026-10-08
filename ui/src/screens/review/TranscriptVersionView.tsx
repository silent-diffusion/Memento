// A transcript version opened from Review's History (after 1.2.0): read-only, in place of the live transcript,
// under a banner (DESIGN.md §5.19) naming when it is from and what had just happened ("Transcript as of
// 10:42 AM, after speakers were identified"), with Restore this version and Back to current. The player keeps
// playing; a line's time plays it and the line under the playhead is marked, as in the live transcript.
import type { JSX } from 'preact';
import { useEffect, useMemo, useRef } from 'preact/hooks';
import type { HistoryEntry, HistoryLink, Transcript } from '../../bridge/types';
import { InfoIcon } from '../../components/icons';
import { versionTitle } from '../../format/history';
import { segmentIndexAt, segmentTimecode, speakerColourVar } from '../../format/transcript';

export interface OpenTranscriptVersion {
  link: HistoryLink;
  entry: HistoryEntry;
  /** Null while it is read. */
  transcript: Transcript | null;
  /** Why it could not be read (§17), or null. */
  error: string | null;
}

interface Props {
  open: OpenTranscriptVersion;
  now: Date;
  positionMs: number;
  onSeek: (ms: number) => void;
  onRestore: () => void;
  onBack: () => void;
  restoring: boolean;
}

export function TranscriptVersionView({ open, now, positionMs, onSeek, onRestore, onBack, restoring }: Props): JSX.Element {
  const { link, entry, transcript, error } = open;
  const title = versionTitle(link, null, entry.at, now);
  const isCurrent = link.versionId === 'current';
  const segments = transcript?.segments ?? [];
  const speakers = useMemo(() => new Map((transcript?.speakers ?? []).map((s) => [s.id, s])), [transcript]);
  const currentIndex = segmentIndexAt(segments, positionMs / 1000);
  const listRef = useRef<HTMLDivElement | null>(null);
  const backRef = useRef<HTMLButtonElement | null>(null);

  // Opening a version shows the line under the playhead and puts the keyboard on the banner.
  useEffect(() => {
    backRef.current?.focus({ preventScroll: true });
  }, [link.index]);
  useEffect(() => {
    if (transcript === null) {
      return;
    }
    const row = listRef.current?.querySelector<HTMLElement>(`[data-index="${Math.max(0, currentIndex)}"]`);
    // Only when the version arrives; afterwards the person scrolls (jsdom has no scrollIntoView).
    if (row !== null && row !== undefined && typeof Element.prototype.scrollIntoView === 'function') {
      row.scrollIntoView({ block: 'center' });
    }
  }, [transcript]);

  const sub =
    error ??
    (transcript === null
      ? 'Reading this version…'
      : isCurrent
        ? 'This is the transcript you have now.'
        : `Read only · ${transcript.segments.length.toLocaleString('en-US')} lines. Restoring it keeps the transcript you have now as a version, and Undo takes it back.`);

  return (
    <div class="transcript tx-version">
      <div
        class="banner tx-version-banner"
        role="region"
        aria-label="Earlier version of the transcript"
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            event.preventDefault();
            onBack();
          }
        }}
      >
        <InfoIcon size={18} class="banner-icon" />
        <span class="banner-text" aria-live="polite">
          <span class="banner-lead">{title}.</span> {sub}
        </span>
        {isCurrent || transcript === null ? null : (
          <button class="btn banner-action" type="button" disabled={restoring} onClick={onRestore}>
            {restoring ? 'Restoring…' : 'Restore this version'}
          </button>
        )}
        <button ref={backRef} class="btn link-btn tx-version-back" type="button" onClick={onBack}>
          Back to current
        </button>
      </div>
      {transcript === null ? null : (
        <div ref={listRef} class="segm-list tx-version-list" role="list" aria-label={title}>
          {segments.map((segment, i) => {
            const speaker = segment.speaker === null ? null : (speakers.get(segment.speaker) ?? null);
            return (
              <div
                key={segment.id}
                class={i === currentIndex ? 'segm now' : 'segm'}
                role="listitem"
                tabIndex={0}
                data-index={i}
                aria-current={i === currentIndex ? 'true' : undefined}
                onClick={() => {
                  onSeek(segment.start * 1000);
                }}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') {
                    event.preventDefault();
                    onSeek(segment.start * 1000);
                  }
                }}
              >
                <span class="segm-side">
                  {speaker === null ? null : (
                    <span class="segm-speaker tx-version-speaker">
                      <span class="segm-dot" aria-hidden="true" style={{ background: speakerColourVar(speaker.color) }} />
                      <span class="segm-speaker-name">{speaker.name}</span>
                    </span>
                  )}
                  <span class="mono segm-at">{segmentTimecode(segment.start)}</span>
                </span>
                <span class="segm-body">
                  <span class="segm-text">{segment.text}</span>
                </span>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
