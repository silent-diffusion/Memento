// Renders the dialog the store asks for (state/dialogs.ts), or the next recovered recording.
import type { JSX } from 'preact';
import { useState } from 'preact/hooks';
import type { RecordingType, RecoveredRecording } from '../bridge/types';
import { deleteCopy, recoveryCopy } from '../format/messages';
import { CHOOSABLE_TYPES, isBuiltInType, typeName } from '../format/recording';
import { changeRecordingType, confirmDelete, renameRecording, resolveRecovery } from '../state/actions';
import { useServices } from '../state/context';
import type { DialogRequest } from '../state/dialogs';
import { Dialog } from './Overlay';
import { RestoreVersionDialog, RetranscribeDialog } from './TranscriptDialogs';
import { RemoveAttachmentDialog } from './attachments/RemoveAttachmentDialog';
import { ExportDialog } from '../screens/export/ExportDialog';
import { AiKeyDialog, LibraryMoveDialog } from '../screens/settings/SettingsDialogs';

function DialogError({ message }: { message: string | null }): JSX.Element | null {
  return message === null ? null : (
    <p class="dialog-error" role="alert">
      {message}
    </p>
  );
}

function DeleteDialog({ request, close }: { request: Extract<DialogRequest, { kind: 'delete' }>; close: () => void }): JSX.Element {
  const services = useServices();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const copy = deleteCopy(request.estimate);
  return (
    <Dialog
      titleId="delete-title"
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
              void confirmDelete(services, request.recordingId).then((message) => {
                setBusy(false);
                if (message === null) {
                  close();
                } else {
                  setError(message);
                }
              });
            }}
          >
            Delete
          </button>
        </>
      }
    >
      <p class="dialog-body">{copy.body}</p>
      <DialogError message={error} />
    </Dialog>
  );
}

function RenameDialog({ request, close }: { request: Extract<DialogRequest, { kind: 'rename' }>; close: () => void }): JSX.Element {
  const services = useServices();
  const [title, setTitle] = useState(request.title);
  const [error, setError] = useState<string | null>(null);
  const trimmed = title.trim();
  const save = (): void => {
    if (trimmed === '' || trimmed === request.title) {
      close();
      return;
    }
    void renameRecording(services, request.recordingId, trimmed).then((message) => {
      if (message === null) {
        close();
      } else {
        setError(message);
      }
    });
  };
  return (
    <Dialog
      titleId="rename-title"
      title="Rename recording"
      onEscape={close}
      actions={
        <>
          <button class="btn g" type="button" onClick={close}>
            Cancel
          </button>
          <button class="btn p" type="submit" form="rename-form" disabled={trimmed === ''}>
            Rename
          </button>
        </>
      }
    >
      <form
        id="rename-form"
        class="dialog-form"
        onSubmit={(event) => {
          event.preventDefault();
          save();
        }}
      >
        <label class="field-label" for="rename-input">
          Title
        </label>
        <input
          id="rename-input"
          class="field dialog-field"
          type="text"
          value={title}
          data-autofocus
          autocomplete="off"
          onInput={(event) => {
            setTitle(event.currentTarget.value);
          }}
          onFocus={(event) => {
            event.currentTarget.select();
          }}
        />
        <p class="field-note">The files on disk keep their names; only the title changes.</p>
      </form>
      <DialogError message={error} />
    </Dialog>
  );
}

function ChangeTypeDialog({ request, close }: { request: Extract<DialogRequest, { kind: 'changeType' }>; close: () => void }): JSX.Element {
  const services = useServices();
  const initialCustom = isBuiltInType(request.type) ? '' : request.type;
  const [choice, setChoice] = useState<string>(isBuiltInType(request.type) ? request.type : 'custom');
  const [custom, setCustom] = useState(initialCustom);
  const [error, setError] = useState<string | null>(null);
  const chosen: RecordingType = choice === 'custom' ? custom.trim() : choice;
  const save = (): void => {
    if (chosen === '' || chosen === request.type) {
      close();
      return;
    }
    void changeRecordingType(services, request.recordingId, chosen).then((message) => {
      if (message === null) {
        close();
      } else {
        setError(message);
      }
    });
  };
  return (
    <Dialog
      titleId="type-title"
      title="Change type"
      onEscape={close}
      actions={
        <>
          <button class="btn g" type="button" onClick={close}>
            Cancel
          </button>
          <button class="btn p" type="submit" form="type-form" disabled={chosen === ''}>
            Change type
          </button>
        </>
      }
    >
      <p class="dialog-body">
        <span class="dialog-strong">{request.title}</span> is filed under the type you choose. It decides which details
        and processing are offered.
      </p>
      <form
        id="type-form"
        class="dialog-form"
        onSubmit={(event) => {
          event.preventDefault();
          save();
        }}
      >
        <fieldset class="type-choices">
          <legend class="sr">Recording type</legend>
          {CHOOSABLE_TYPES.map((type) => (
            <label key={type} class="choice">
              <input
                class="chk"
                type="radio"
                name="recording-type"
                value={type}
                checked={choice === type}
                data-autofocus={choice === type ? true : undefined}
                onChange={() => {
                  setChoice(type);
                }}
              />
              <span>{typeName(type)}</span>
            </label>
          ))}
          <label class="choice choice--custom">
            <input
              class="chk"
              type="radio"
              name="recording-type"
              value="custom"
              checked={choice === 'custom'}
              data-autofocus={choice === 'custom' ? true : undefined}
              onChange={() => {
                setChoice('custom');
              }}
            />
            <span>Custom</span>
            <input
              class="field choice-field"
              type="text"
              aria-label="Custom type name"
              placeholder="Name a type"
              value={custom}
              onFocus={() => {
                setChoice('custom');
              }}
              onInput={(event) => {
                setCustom(event.currentTarget.value);
              }}
            />
          </label>
        </fieldset>
      </form>
      <DialogError message={error} />
    </Dialog>
  );
}

function NoticeDialog({ request, close }: { request: Extract<DialogRequest, { kind: 'notice' }>; close: () => void }): JSX.Element {
  return (
    <Dialog
      titleId="notice-title"
      title={request.title}
      onEscape={close}
      actions={
        <button class="btn p" type="button" data-autofocus onClick={close}>
          OK
        </button>
      }
    >
      <p class="dialog-body">{request.body}</p>
    </Dialog>
  );
}

function RecoveryDialog({ item }: { item: RecoveredRecording }): JSX.Element {
  const services = useServices();
  const copy = recoveryCopy(item);
  return (
    <Dialog
      titleId="recovery-title"
      title={copy.title}
      onEscape={() => {
        resolveRecovery(services, item, false);
      }}
      actions={
        <>
          <button
            class="btn g"
            type="button"
            onClick={() => {
              resolveRecovery(services, item, false);
            }}
          >
            Later
          </button>
          <button
            class="btn p"
            type="button"
            data-autofocus
            onClick={() => {
              resolveRecovery(services, item, true);
            }}
          >
            Open recording
          </button>
        </>
      }
    >
      <p class="dialog-body">
        <span class="dialog-strong">{copy.lead}</span>
        {copy.rest}
      </p>
    </Dialog>
  );
}

export function DialogHost(): JSX.Element | null {
  const { store } = useServices();
  const request = store.dialog.value;
  const close = (): void => {
    store.dialog.value = null;
  };
  if (request !== null) {
    switch (request.kind) {
      case 'delete':
        return <DeleteDialog key={request.recordingId} request={request} close={close} />;
      case 'rename':
        return <RenameDialog key={request.recordingId} request={request} close={close} />;
      case 'changeType':
        return <ChangeTypeDialog key={request.recordingId} request={request} close={close} />;
      case 'notice':
        return <NoticeDialog key={request.title} request={request} close={close} />;
      case 'retranscribe':
        return <RetranscribeDialog key={request.recordingId} request={request} close={close} />;
      case 'restoreVersion':
        return <RestoreVersionDialog key={request.version.id} request={request} close={close} />;
      // M3
      case 'removeAttachment':
        return <RemoveAttachmentDialog key={request.attachment.id} recordingId={request.recordingId} attachment={request.attachment} close={close} />;
      case 'export':
        return <ExportDialog key={`${request.recordingId}-${request.retry?.message ?? ''}`} request={request} close={close} />;
      case 'aiKey':
        return <AiKeyDialog key={request.provider} request={request} close={close} />;
      case 'libraryMove':
        return <LibraryMoveDialog key={request.to} request={request} close={close} />;
    }
  }
  const recovered = store.recoveryQueue.value[0];
  return recovered === undefined ? null : <RecoveryDialog key={recovered.recordingId} item={recovered} />;
}
