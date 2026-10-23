// The boundary between two consecutive transcript lines of different speakers (DESIGN.md §9, 2.6.0): most
// remaining speaker errors are one line off at a speaker change, so the boundary shows, on hover or focus, a
// pill with two buttons that move it a line up or down: the line above takes the next speaker, or the line
// below takes the previous one. Each is the single-line move (one Undo step), and the boundary follows the
// lines, so repeated presses walk it. It takes no room in the list and never shows inside a turn.
import type { JSX } from 'preact';
import type { Speaker, TranscriptSegment } from '../../bridge/types';
import { ChevronDownIcon, ChevronUpIcon } from '../../components/icons';
import { formatDuration } from '../../format/duration';

interface TurnBoundaryProps {
  above: TranscriptSegment;
  below: TranscriptSegment;
  aboveSpeaker: Speaker;
  belowSpeaker: Speaker;
  /** The line above takes the speaker of the line below. */
  onGiveAbove: () => void;
  /** The line below takes the speaker of the line above. */
  onGiveBelow: () => void;
}

export function TurnBoundary({ above, below, aboveSpeaker, belowSpeaker, onGiveAbove, onGiveBelow }: TurnBoundaryProps): JSX.Element {
  return (
    <div
      class="turn-boundary"
      role="group"
      aria-label={`Speaker change from ${aboveSpeaker.name} to ${belowSpeaker.name} at ${formatDuration(below.start * 1000)}`}
      data-boundary={`${above.id}|${below.id}`}
      onClick={(event) => {
        event.stopPropagation();
      }}
      onDblClick={(event) => {
        event.stopPropagation();
      }}
    >
      <span class="turn-boundary-rule" aria-hidden="true" />
      <span class="turn-boundary-pill">
        <button
          class="icon-btn turn-boundary-btn"
          type="button"
          aria-label={`Give the line above to ${belowSpeaker.name}`}
          title={`Give the line above to ${belowSpeaker.name}`}
          onClick={onGiveAbove}
        >
          <ChevronUpIcon size={12} />
        </button>
        <button
          class="icon-btn turn-boundary-btn"
          type="button"
          aria-label={`Give the line below to ${aboveSpeaker.name}`}
          title={`Give the line below to ${aboveSpeaker.name}`}
          onClick={onGiveBelow}
        >
          <ChevronDownIcon size={12} />
        </button>
      </span>
    </div>
  );
}
