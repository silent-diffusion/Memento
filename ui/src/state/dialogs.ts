// Modal dialogs are requested through the store so any screen (or a host event) can open one and
// DialogHost renders it above everything. Add a kind here, then a case in components/DialogHost.tsx.
// Recovered recordings have their own queue (store.recoveryQueue) and show when no other dialog is open.
import type {
  AiKeyProvider,
  Attachment,
  ExportDestination,
  ExportSelection,
  FolderInfo,
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
  | { kind: 'export'; recordingId: string; retry?: ExportRetry; /** M4: the Document viewer's Export ticks Documents with these. */ documentIds?: string[] }
  /** Settings › AI and privacy › Add or Replace a provider key. */
  | { kind: 'aiKey'; provider: AiKeyProvider; replacing: boolean }
  /** Settings › General › Library location › Change: copy, verify, then delete the old folder. */
  | { kind: 'libraryMove'; from: string; to: string }
  // 2.1 (DESIGN.md §6.1, §6.2)
  /** Library › New folder… (in `parent`, null: the top level) or a folder's Rename…. */
  | { kind: 'folderName'; mode: 'create'; parent: string | null }
  | { kind: 'folderName'; mode: 'rename'; path: string }
  /** A folder's Delete…: names it and where its contents go. */
  | { kind: 'deleteFolder'; folder: FolderInfo; childFolders: number }
  /** Move to folder… for recordings (`folderPath` unset) or for a folder (`folderPath` = the folder). `current`: where it is now. */
  | { kind: 'moveToFolder'; recordingIds: string[]; title: string; current: string | null; folderPath?: string; onMoved?: () => void }
  /** Delete audio…: the recording, its length and the size freed; `onDeleted` runs after it succeeded (Review reloads). */
  | { kind: 'deleteAudio'; recordingId: string; title: string; durationMs: number; bytes: number; hasVideo: boolean; onDeleted?: () => void };

/** What a failed export leaves for Try again and Choose another folder. */
export interface ExportRetry {
  selection: ExportSelection;
  destination: ExportDestination;
  message: string;
  /** Choose another folder: the picker opens with the dialog. */
  pickFolder: boolean;
}

export type DialogKind = DialogRequest['kind'];
