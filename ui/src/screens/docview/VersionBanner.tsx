// A document version open read-only in the viewer (after 1.2.0; from the Versions list or Review's History): the
// banner above its paper (DESIGN.md §5.19) with Restore this version and Back to current, and the restore itself,
// through documents.restoreVersion, as one Undo step.
import type { JSX } from 'preact';
import { useEffect, useRef } from 'preact/hooks';
import type { BridgeClient } from '../../bridge/client';
import { InfoIcon } from '../../components/icons';
import type { UndoManager } from '../../state/undo';

/** The version open read-only: its id, and its paper once read (or why it could not be). */
export interface ViewedVersion {
  versionId: string;
  html: string | null;
  error: string | null;
}

interface Props {
  name: string;
  /** Its number in the Versions list, once that is read. */
  number: number | null;
  /** "Edited by you · today 10:42 AM · 3 changes", once the list is read. */
  meta: string | null;
  error: string | null;
  ready: boolean;
  restoring: boolean;
  onRestore: () => void;
  onBack: () => void;
}

export function VersionBanner({ name, number, meta, error, ready, restoring, onRestore, onBack }: Props): JSX.Element {
  const back = useRef<HTMLButtonElement | null>(null);
  useEffect(() => {
    back.current?.focus({ preventScroll: true });
  }, []);
  const lead = number === null ? `${name}: an earlier version` : `${name}: version ${number}`;
  const rest =
    error ?? (ready ? `Read only${meta === null ? '' : ` · ${meta}`}. Restoring it keeps the text you have now as a version, and Undo takes it back.` : 'Opening this version…');
  return (
    <div
      class="banner doc-version-banner"
      role="region"
      aria-label="Earlier version of the document"
      onKeyDown={(event) => {
        if (event.key === 'Escape') {
          event.preventDefault();
          onBack();
        }
      }}
    >
      <InfoIcon size={18} class="banner-icon" />
      <span class="banner-text" aria-live="polite">
        <span class="banner-lead">{lead}.</span> {rest}
      </span>
      {ready && error === null ? (
        <button class="btn banner-action" type="button" disabled={restoring} onClick={onRestore}>
          {restoring ? 'Restoring…' : 'Restore this version'}
        </button>
      ) : null}
      <button ref={back} class="btn link-btn doc-version-back" type="button" onClick={onBack}>
        Back to current
      </button>
    </div>
  );
}

/**
 * Restores a kept document version. The text it replaces is kept as a version by the host (version history is on,
 * or there would be no version to open); Undo restores that one, Redo this one again.
 */
export async function restoreDocumentVersion(
  bridge: BridgeClient,
  undo: UndoManager,
  recordingId: string,
  documentId: string,
  versionId: string,
  label: string,
): Promise<void> {
  const restore = (id: string) => bridge.call('documents.restoreVersion', { recordingId, documentId, versionId: id });
  const ids = async (): Promise<string[]> => (await bridge.call('documents.versions', { recordingId, documentId })).versions.map((v) => v.id).filter((id) => id !== 'current');
  const before = new Set(await ids());
  await restore(versionId);
  const kept = (await ids()).find((id) => !before.has(id)) ?? null;
  if (kept !== null) {
    undo.push({ label, undo: () => restore(kept), redo: () => restore(versionId) });
  }
}
