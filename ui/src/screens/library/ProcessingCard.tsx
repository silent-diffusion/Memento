import type { JSX } from 'preact';
import type { LibraryProcessingResult, ProcessingCurrent, StageFailure } from '../../bridge/types';
import { formatDuration } from '../../format/duration';
import { cardStageName, peopleWording, stageFill, stageStatusText, typeName } from '../../format/recording';
import { rolesWording } from '../../format/video';
import { formatRecordedAt, parseIso } from '../../format/when';

interface ProcessingCardProps {
  processing: LibraryProcessingResult;
  now: Date;
  onOpen: (recordingId: string) => void;
  /** processing.retry for the failed video stage with one of its remedies (DESIGN.md §18.6). */
  onRemedy: (recordingId: string, remedyId: string) => void;
  /** The remedy whose processing.retry is in flight; its buttons are disabled meanwhile. */
  pendingRemedy: string | null;
}

/**
 * "Meeting · 5 people · recorded today at 10:00 AM · 1:02:14"; with video "Meeting · screen + camera ·
 * 5 people · 1:02:14". The video parts go right after the type, where renders/ProcessingVideo puts
 * them; to keep the line to one line beside the extra Video column, "when" gives way (the row below
 * the card still says it).
 */
export function processingMeta(current: ProcessingCurrent, now: Date): string {
  const meta = current.meta;
  const roles = meta.hasVideo && meta.video !== null ? rolesWording(meta.video.roles) : '';
  return [
    typeName(meta.type),
    ...(roles === '' ? [] : [roles]),
    peopleWording(meta.participantCount),
    ...(roles === '' ? [formatRecordedAt(parseIso(meta.createdAt), now)] : []),
    formatDuration(meta.durationMs),
  ].join(' · ');
}

/** The video stage failed and is still failed (a retry moves the stage on before the next refetch). */
function videoFailure(current: ProcessingCurrent): StageFailure | null {
  const failure = current.failure;
  if (failure?.stage !== 'video') {
    return null;
  }
  return current.stages.some((s) => s.stage === 'video' && s.state === 'failed') ? failure : null;
}

/**
 * "Processing now" (DESIGN.md §4): the most recent recording in the pipeline, one progress track per
 * stage the host reports, live from processing.progress. Several running reads "+n more". A failed
 * video stage turns it into the `danger` card of §18.6 with the host's remedies.
 */
export function ProcessingCard({ processing, now, onOpen, onRemedy, pendingRemedy }: ProcessingCardProps): JSX.Element | null {
  const current = processing.current;
  if (current === null) {
    return null;
  }
  const more = processing.othersCount > 0 ? <span class="proc-more"> +{processing.othersCount} more</span> : null;
  const failure = videoFailure(current);
  if (failure !== null) {
    return (
      <section class="proc-card proc-card--failed" aria-label="Video encoding failed">
        <div class="proc-info proc-info--failed">
          <div class="proc-label proc-label--failed">
            <span class="proc-dot proc-dot--failed" aria-hidden="true" />
            Video encoding failed
          </div>
          <div class="proc-title proc-title--failed">
            {current.title}
            {more}
          </div>
          <div class="proc-meta">
            {failure.message} {failure.kept}
          </div>
        </div>
        <div class="proc-remedies">
          {failure.remedies.map((remedy, index) => (
            <button
              key={remedy.id}
              class={index === 0 ? 'btn p proc-remedy' : 'btn g proc-remedy'}
              type="button"
              disabled={pendingRemedy !== null}
              aria-busy={pendingRemedy === remedy.id ? 'true' : undefined}
              onClick={() => {
                onRemedy(current.recordingId, remedy.id);
              }}
            >
              {remedy.label}
            </button>
          ))}
          <button
            class="btn g proc-remedy"
            type="button"
            aria-label={`Details of ${current.title}`}
            onClick={() => {
              onOpen(current.recordingId);
            }}
          >
            Details
          </button>
        </div>
      </section>
    );
  }
  return (
    <section class="proc-card" aria-label="Processing now">
      <div class="proc-info">
        <div class="proc-label">
          <span class="pulse proc-dot" aria-hidden="true" />
          Processing now
        </div>
        <div class="proc-title">
          {current.title}
          {more}
        </div>
        <div class="proc-meta">{processingMeta(current, now)}</div>
      </div>
      <div class="proc-stages">
        {current.stages.map((stage) => (
          <div key={stage.stage} class="proc-stage">
            <div class="proc-stage-line">
              <span class="proc-stage-name">{cardStageName(stage.stage)}</span>
              <span class={stage.state === 'failed' ? 'proc-stage-status proc-stage-status--failed' : 'proc-stage-status'}>
                {stageStatusText(stage)}
              </span>
            </div>
            <div
              class="proc-track"
              role="progressbar"
              aria-label={cardStageName(stage.stage)}
              aria-valuemin={0}
              aria-valuemax={100}
              aria-valuenow={stage.state === 'done' ? 100 : stage.state === 'active' ? (stage.percent ?? undefined) : 0}
              aria-valuetext={stageStatusText(stage)}
            >
              <div class={`bar ${stage.state}`} style={{ width: stageFill(stage) }} />
            </div>
          </div>
        ))}
      </div>
      <button
        class="btn ghost"
        type="button"
        onClick={() => {
          onOpen(current.recordingId);
        }}
      >
        Open
      </button>
    </section>
  );
}
