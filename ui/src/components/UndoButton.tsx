// The header's Undo (DESIGN.md §9, §10, §12): a ghost button shown while there is something to
// undo, its tooltip and accessible name naming the step ("Undo merge speakers"), and beside it a
// quiet live region for "Undone: merge speakers" and "Saved" (no toast for either).
import type { JSX } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import { useServices } from '../state/context';
import { STATUS_MS, undoOf } from '../state/undo';
import './editing.css';
import { UndoIcon } from './icons';

export function UndoButton(): JSX.Element {
  const { store } = useServices();
  const undo = undoOf(store);
  const label = undo.undoLabel.value;
  const status = undo.status.value;
  const [shown, setShown] = useState<number | null>(null);

  // The status shows for a few seconds after each change, then the region empties (it stays in the page so it announces).
  useEffect(() => {
    if (status === null) {
      setShown(null);
      return undefined;
    }
    setShown(status.serial);
    const timer = setTimeout(() => {
      setShown(null);
    }, STATUS_MS);
    return () => {
      clearTimeout(timer);
    };
  }, [status?.serial]);

  const visible = status !== null && shown === status.serial ? status : null;
  return (
    <>
      <span class={visible?.tone === 'failed' ? 'undo-status undo-status--failed' : 'undo-status'} role="status" aria-live="polite">
        {visible?.text ?? ''}
      </span>
      {label === null ? null : (
        <button
          class="btn ghost spoke-ghost undo-btn"
          type="button"
          title={`Undo ${label}`}
          aria-label={`Undo ${label}`}
          aria-keyshortcuts="Control+Z"
          disabled={undo.busy.value}
          onClick={() => {
            void undo.undo();
          }}
        >
          <UndoIcon size={16} />
          Undo
        </button>
      )}
    </>
  );
}
