import type { Signal } from '@preact/signals';
import type { RecordingStatePayload } from '../bridge/types';

/**
 * Applies `session`, an answer to recording.current asked when the store held `before`, unless a `recording.state`
 * event changed the store while the question was out. An answer computed just before finalize finished could
 * otherwise arrive after the "ready" event and put the Record screen back to Finalizing, waiting for an event that
 * had already been sent.
 */
export function adoptCurrentSession(
  recording: Signal<RecordingStatePayload | null>,
  before: RecordingStatePayload | null,
  session: RecordingStatePayload | null,
): boolean {
  if (recording.value !== before) {
    return false;
  }
  recording.value = session;
  return true;
}
