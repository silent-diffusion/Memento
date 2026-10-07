// Modal dialogs are requested through the store so any screen (or a host event) can open one and
// DialogHost renders it above everything. Add a kind here, then a case in components/DialogHost.tsx.
// Recovered recordings have their own queue (store.recoveryQueue) and show when no other dialog is open.
import type { ProjectDeleteEstimate, RecordingType, TranscriptVersion } from '../bridge/types';

export type DialogRequest =
  | { kind: 'delete'; recordingId: string; estimate: ProjectDeleteEstimate }
  | { kind: 'rename'; recordingId: string; title: string }
  | { kind: 'changeType'; recordingId: string; title: string; type: RecordingType }
  /** Review › More › Reprocess › Transcribe again…: model and language for a new pass. */
  | { kind: 'retranscribe'; recordingId: string; title: string; hasTranscript: boolean }
  /** Details › Transcript versions › Restore. `when` is the version's time in words. */
  | { kind: 'restoreVersion'; recordingId: string; version: TranscriptVersion; when: string }
  /** A real control whose feature arrives later (Export, Create document in M1): says so plainly. */
  | { kind: 'notice'; title: string; body: string };

export type DialogKind = DialogRequest['kind'];
