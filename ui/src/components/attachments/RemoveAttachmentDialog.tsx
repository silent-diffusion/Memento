// "Remove {file}?" (DESIGN.md §5.19, §17 delete pattern): names the file and its size, says what is
// and is not touched, then Cancel or the destructive Remove.
import type { JSX } from 'preact';
import { useState } from 'preact/hooks';
import type { Attachment } from '../../bridge/types';
import { formatSize } from '../../format/storage';
import { useServices } from '../../state/context';
import { Dialog } from '../Overlay';

export function removeAttachmentCopy(attachment: Attachment): { title: string; body: string } {
  return {
    title: `Remove ${attachment.name}?`,
    body: `${attachment.name} (${formatSize(attachment.sizeBytes)}) is removed from this recording's attachments. The file you added it from is not touched, and copies you already exported keep it. This cannot be undone.`,
  };
}

interface RemoveAttachmentDialogProps {
  recordingId: string;
  attachment: Attachment;
  close: () => void;
}

export function RemoveAttachmentDialog({ recordingId, attachment, close }: RemoveAttachmentDialogProps): JSX.Element {
  const { bridge } = useServices();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const copy = removeAttachmentCopy(attachment);
  return (
    <Dialog
      titleId="remove-attachment-title"
      title={copy.title}
      onEscape={close}
      actions={
        <>
          <button class="btn g" type="button" data-autofocus onClick={close}>
            Cancel
          </button>
          <button
            class="btn d"
            type="button"
            disabled={busy}
            onClick={() => {
              setBusy(true);
              bridge
                .call('attachments.remove', { recordingId, attachmentId: attachment.id })
                .then(() => {
                  close();
                })
                .catch((e: unknown) => {
                  setBusy(false);
                  setError(`${e instanceof Error ? e.message : 'Memento did not answer.'} The file is still attached.`);
                });
            }}
          >
            Remove
          </button>
        </>
      }
    >
      <p class="dialog-body">{copy.body}</p>
      {error === null ? null : (
        <p class="dialog-error" role="alert">
          {error}
        </p>
      )}
    </Dialog>
  );
}
