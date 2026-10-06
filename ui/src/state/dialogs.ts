// Modal dialogs are requested through the store so any screen (or a host event) can open one and
// DialogHost renders it above everything. Add a kind here, then a case in components/DialogHost.tsx.
// Recovered recordings have their own queue (store.recoveryQueue) and show when no other dialog is open.
import type { ProjectDeleteEstimate, RecordingType } from '../bridge/types';

export type DialogRequest =
  | { kind: 'delete'; recordingId: string; estimate: ProjectDeleteEstimate }
  | { kind: 'rename'; recordingId: string; title: string }
  | { kind: 'changeType'; recordingId: string; title: string; type: RecordingType }
  /** A real control whose feature arrives later (Export, Create document in M1): says so plainly. */
  | { kind: 'notice'; title: string; body: string };

export type DialogKind = DialogRequest['kind'];
