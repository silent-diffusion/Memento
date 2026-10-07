// Attachments of a recording (BRIDGE.md M3): in the Details sheet below Tags and in Review › Details.
// Each row names the file, its size and kind; Open hands it to its Windows app, Remove asks first.
import type { JSX } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import type { Attachment } from '../../bridge/types';
import { formatSize } from '../../format/storage';
import { useServices } from '../../state/context';
import { CloseIcon, DocumentIcon } from '../icons';

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

const KIND_BY_TYPE: readonly [RegExp, string][] = [
  [/pdf/, 'PDF'],
  [/wordprocessingml|msword/, 'Word document'],
  [/spreadsheetml|ms-excel|csv/, 'Spreadsheet'],
  [/presentationml|powerpoint/, 'Presentation'],
  [/^image\//, 'Image'],
  [/^text\//, 'Text'],
];

/** "Agenda file", "PDF", "Image", or the extension in capitals. */
export function attachmentKind(attachment: Attachment): string {
  if (attachment.kind === 'agenda') {
    return 'Agenda file';
  }
  const type = attachment.contentType ?? '';
  for (const [pattern, name] of KIND_BY_TYPE) {
    if (pattern.test(type)) {
      return name;
    }
  }
  const extension = /\.([a-z0-9]+)$/i.exec(attachment.name)?.[1];
  return extension === undefined ? 'File' : extension.toUpperCase();
}

interface AttachmentsSectionProps {
  /** null before the recording exists: files can be attached once it has started. */
  recordingId: string | null;
  variant: 'sheet' | 'review';
}

export function AttachmentsSection({ recordingId, variant }: AttachmentsSectionProps): JSX.Element {
  const { bridge, store } = useServices();
  const [attachments, setAttachments] = useState<Attachment[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const [reload, setReload] = useState(0);

  useEffect(() => {
    if (recordingId === null) {
      setAttachments([]);
      return undefined;
    }
    let live = true;
    bridge
      .call('attachments.list', { recordingId })
      .then(({ attachments: list }) => {
        if (live) {
          setAttachments(list);
        }
      })
      .catch((e: unknown) => {
        if (live) {
          setAttachments([]);
          setError(`The attachments could not be listed. ${messageOf(e)}`);
        }
      });
    return () => {
      live = false;
    };
  }, [bridge, recordingId, reload]);

  // A removal (or an agenda file applied) writes the project; the host says so.
  useEffect(
    () =>
      bridge.on('library.changed', (payload) => {
        if (recordingId !== null && payload.recordingIds.includes(recordingId)) {
          setReload((n) => n + 1);
        }
      }),
    [bridge, recordingId],
  );

  const add = async (): Promise<void> => {
    if (recordingId === null) {
      return;
    }
    setError(null);
    setAdding(true);
    try {
      const result = await bridge.call('attachments.add', { recordingId });
      const added = result.attachment;
      if (!result.cancelled && added !== null) {
        setAttachments((list) => [...(list ?? []).filter((a) => a.id !== added.id), added]);
      }
    } catch (e) {
      setError(messageOf(e));
    } finally {
      setAdding(false);
    }
  };

  const open = (attachment: Attachment): void => {
    if (recordingId === null) {
      return;
    }
    setError(null);
    bridge.call('attachments.open', { recordingId, attachmentId: attachment.id }).catch((e: unknown) => {
      setError(`${attachment.name} could not be opened. ${messageOf(e)}`);
    });
  };

  const list = attachments ?? [];
  const headingId = `attachments-label-${variant}`;
  return (
    <div class={variant === 'sheet' ? 'sheet-section attachments attachments--sheet' : 'detail-group attachments attachments--review'}>
      <span class="lbl" id={headingId}>
        Attachments
      </span>
      {recordingId === null ? (
        <span class="attachments-empty">Files can be attached once the recording has started.</span>
      ) : attachments === null ? (
        <span class="attachments-empty">Looking for attachments…</span>
      ) : list.length === 0 ? (
        <span class="attachments-empty">No files attached</span>
      ) : (
        <ul class="attachment-list" aria-labelledby={headingId}>
          {list.map((attachment) => (
            <li key={attachment.id} class="attachment">
              <span class="attachment-icon" aria-hidden="true">
                <DocumentIcon size={14} />
              </span>
              <span class="attachment-text">
                <span class="attachment-name" title={attachment.name}>
                  {attachment.name}
                </span>
                <span class="attachment-meta">
                  <span class="mono">{formatSize(attachment.sizeBytes)}</span> · {attachmentKind(attachment)}
                </span>
              </span>
              <button
                class="btn ghost attachment-open"
                type="button"
                aria-label={`Open ${attachment.name}`}
                onClick={() => {
                  open(attachment);
                }}
              >
                Open
              </button>
              <button
                class="icon-btn agenda-remove"
                type="button"
                aria-label={`Remove ${attachment.name}`}
                aria-haspopup="dialog"
                onClick={() => {
                  store.dialog.value = { kind: 'removeAttachment', recordingId, attachment };
                }}
              >
                <CloseIcon size={13} />
              </button>
            </li>
          ))}
        </ul>
      )}
      {error === null ? null : (
        <p class="attachments-error" role="alert">
          {error}
        </p>
      )}
      <button
        class={variant === 'sheet' ? 'btn add-row attachments-add' : 'btn add-pill attachments-add'}
        type="button"
        disabled={recordingId === null || adding}
        onClick={() => {
          void add();
        }}
      >
        {adding ? 'Adding…' : '+ Add a file'}
      </button>
    </div>
  );
}
