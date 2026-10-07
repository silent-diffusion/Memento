import type { JSX } from 'preact';
import type { LibraryProcessingResult } from '../../bridge/types';
import { formatDuration } from '../../format/duration';
import { cardStageName, peopleWording, stageFill, stageStatusText, typeName } from '../../format/recording';
import { formatRecordedAt, parseIso } from '../../format/when';

interface ProcessingCardProps {
  processing: LibraryProcessingResult;
  now: Date;
  onOpen: (recordingId: string) => void;
}

/**
 * "Processing now" (DESIGN.md §4): the most recent recording in the pipeline, one progress track per
 * stage the host reports, live from processing.progress. Several running reads "+n more".
 */
export function ProcessingCard({ processing, now, onOpen }: ProcessingCardProps): JSX.Element | null {
  const current = processing.current;
  if (current === null) {
    return null;
  }
  const meta = current.meta;
  const metaText = `${typeName(meta.type)} · ${peopleWording(meta.participantCount)} · ${formatRecordedAt(parseIso(meta.createdAt), now)} · ${formatDuration(meta.durationMs)}`;
  return (
    <section class="proc-card" aria-label="Processing now">
      <div class="proc-info">
        <div class="proc-label">
          <span class="pulse proc-dot" aria-hidden="true" />
          Processing now
        </div>
        <div class="proc-title">
          {current.title}
          {processing.othersCount > 0 ? <span class="proc-more"> +{processing.othersCount} more</span> : null}
        </div>
        <div class="proc-meta">{metaText}</div>
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
        class="btn ghost proc-open"
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
