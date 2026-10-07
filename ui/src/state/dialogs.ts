// Modal dialogs are requested through the store so any screen (or a host event) can open one and
// DialogHost renders it above everything. Add a kind here, then a case in components/DialogHost.tsx.
// Recovered recordings have their own queue (store.recoveryQueue) and show when no other dialog is open.
import type {
  AiProvider,
  Attachment,
  ExportDestination,
  ExportSelection,
  ProjectDeleteEstimate,
  RecordingType,
  TranscriptVersion,
} from '../bridge/types';

export type DialogRequest =
  | { kind: 'delete'; recordingId: string; estimate: ProjectDeleteEstimate }
  | { kind: 'rename'; recordingId: string; title: string }
  | { kind: 'changeType'; recordingId: string; title: string; type: RecordingType }
  /** Review › More › Reprocess › Transcribe again…: model and language for a new pass. */
  | { kind: 'retranscribe'; recordingId: string; title: string; hasTranscript: boolean }
  /** Details › Transcript versions › Restore. `when` is the version's time in words. */
  | { kind: 'restoreVersion'; recordingId: string; version: TranscriptVersion; when: string }
  /** A real control whose feature arrives later (Export, Create document in M1): says so plainly. */
  | { kind: 'notice'; title: string; body: string }
  // M3
  /** Details sheet or Review › Details › Remove an attachment: names the file. */
  | { kind: 'removeAttachment'; recordingId: string; attachment: Attachment }
  /** Export copies (DESIGN.md §15). `retry` reopens it after a failed job with the same choices and the failure card. */
  | { kind: 'export'; recordingId: string; retry?: ExportRetry }
  /** Settings › AI and privacy › Add or Replace a provider key. */
  | { kind: 'aiKey'; provider: AiProvider; replacing: boolean }
  /** Settings › General › Library location › Change: copy, verify, then delete the old folder. */
  | { kind: 'libraryMove'; from: string; to: string };

/** What a failed export leaves for Try again and Choose another folder. */
export interface ExportRetry {
  selection: ExportSelection;
  destination: ExportDestination;
  message: string;
  /** Choose another folder: the picker opens with the dialog. */
  pickFolder: boolean;
}

export type DialogKind = DialogRequest['kind'];
