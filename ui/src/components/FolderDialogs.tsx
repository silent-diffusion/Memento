// 2.1 dialogs (DESIGN.md §6.1, §6.2, §17): name a folder, delete a folder, move recordings or a
// folder, and Delete audio. Each confirmation names what it acts on; Cancel comes first and is the
// default; the destructive button says what it does.
import type { JSX } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import type { FolderInfo } from '../bridge/types';
import { deleteAudioCopy, deleteFolderCopy, folderName, folderNameProblem, folderTrail, ROOT_LABEL } from '../format/folders';
import { changeFolder, confirmDeleteAudio, createFolder, deleteFolder, moveRecordings } from '../state/folderActions';
import { useServices } from '../state/context';
import type { DialogRequest } from '../state/dialogs';
import { Dialog } from './Overlay';

function DialogError({ message }: { message: string | null }): JSX.Element | null {
  return message === null ? null : (
    <p class="dialog-error" role="alert">
      {message}
    </p>
  );
}

/** New folder… and Rename folder…: one field, checked as the host checks it before anything is sent. */
export function FolderNameDialog({ request, close }: { request: Extract<DialogRequest, { kind: 'folderName' }>; close: () => void }): JSX.Element {
  const services = useServices();
  const [name, setName] = useState(request.mode === 'rename' ? folderName(request.path) : '');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const problem = name.trim() === '' ? null : folderNameProblem(name);
  const save = (): void => {
    const trimmed = name.trim();
    if (trimmed === '' || problem !== null) {
      return;
    }
    if (request.mode === 'rename' && trimmed === folderName(request.path)) {
      close();
      return;
    }
    setBusy(true);
    const done = request.mode === 'rename' ? changeFolder(services, request.path, { name: trimmed }) : createFolder(services, request.parent, trimmed);
    void done.then((message) => {
      setBusy(false);
      if (message === null) {
        close();
      } else {
        setError(message);
      }
    });
  };
  const title = request.mode === 'rename' ? `Rename the folder “${folderName(request.path)}”` : request.parent === null ? 'New folder' : `New folder in “${folderName(request.parent)}”`;
  return (
    <Dialog
      titleId="folder-name-title"
      title={title}
      onEscape={close}
      actions={
        <>
          <button class="btn g" type="button" onClick={close}>
            Cancel
          </button>
          <button class="btn p" type="submit" form="folder-name-form" disabled={busy || name.trim() === '' || problem !== null}>
            {request.mode === 'rename' ? 'Rename' : 'Create folder'}
          </button>
        </>
      }
    >
      <form
        id="folder-name-form"
        class="dialog-form"
        onSubmit={(event) => {
          event.preventDefault();
          save();
        }}
      >
        <label class="field-label" for="folder-name-input">
          Name
        </label>
        <input
          id="folder-name-input"
          class="field dialog-field"
          type="text"
          value={name}
          maxLength={120}
          data-autofocus
          autocomplete="off"
          onInput={(event) => {
            setName(event.currentTarget.value);
            setError(null);
          }}
        />
        <p class="field-note">{problem ?? 'Folders only sort the Library. The recordings’ files stay where they are on disk.'}</p>
      </form>
      <DialogError message={error} />
    </Dialog>
  );
}

export function DeleteFolderDialog({ request, close }: { request: Extract<DialogRequest, { kind: 'deleteFolder' }>; close: () => void }): JSX.Element {
  const services = useServices();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const copy = deleteFolderCopy(request.folder, request.childFolders);
  return (
    <Dialog
      titleId="delete-folder-title"
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
              void deleteFolder(services, request.folder.path, request.folder.parent).then((message) => {
                setBusy(false);
                if (message === null) {
                  close();
                } else {
                  setError(message);
                }
              });
            }}
          >
            {copy.action}
          </button>
        </>
      }
    >
      <p class="dialog-body">{copy.body}</p>
      <DialogError message={error} />
    </Dialog>
  );
}

/**
 * Move to folder…: the Library root and every folder, indented by depth. For a folder being moved,
 * the folder itself and the folders inside it are not offered (the host refuses them too).
 */
export function MoveToFolderDialog({ request, close }: { request: Extract<DialogRequest, { kind: 'moveToFolder' }>; close: () => void }): JSX.Element {
  const services = useServices();
  const [folders, setFolders] = useState<FolderInfo[] | null>(null);
  const [choice, setChoice] = useState<string | null>(request.current);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    let live = true;
    services.bridge
      .call('folders.list', {})
      .then((result) => {
        if (live) {
          setFolders(result.folders);
        }
      })
      .catch((e: unknown) => {
        if (live) {
          setError(e instanceof Error ? e.message : 'Memento did not answer.');
        }
      });
    return () => {
      live = false;
    };
  }, [services]);

  const moving = request.folderPath;
  const offered = (folders ?? []).filter(
    (f) => moving === undefined || !(f.path.toLocaleLowerCase() === moving.toLocaleLowerCase() || f.path.toLocaleLowerCase().startsWith(`${moving.toLocaleLowerCase()}/`)),
  );
  const unchanged = choice === request.current;
  const move = (): void => {
    if (unchanged) {
      close();
      return;
    }
    setBusy(true);
    const done = moving === undefined ? moveRecordings(services, request.recordingIds, choice) : changeFolder(services, moving, { parent: choice });
    void done.then((message) => {
      setBusy(false);
      if (message === null) {
        close();
        request.onMoved?.();
      } else {
        setError(message);
      }
    });
  };
  const option = (path: string | null, depth: number, label: string): JSX.Element => (
    <label key={path ?? '(root)'} class="move-option" style={{ paddingLeft: `${12 + depth * 18}px` }}>
      <input
        type="radio"
        name="move-target"
        checked={choice === path}
        onChange={() => {
          setChoice(path);
        }}
      />
      <span>{label}</span>
      {path === request.current ? <span class="move-note">now here</span> : null}
    </label>
  );
  return (
    <Dialog
      titleId="move-folder-title"
      title={`Move “${request.title}” to a folder`}
      onEscape={close}
      actions={
        <>
          <button class="btn g" type="button" onClick={close}>
            Cancel
          </button>
          <button class="btn p" type="button" disabled={busy || folders === null || unchanged} onClick={move}>
            {choice === null ? `Move to ${ROOT_LABEL}` : `Move to “${folderName(choice)}”`}
          </button>
        </>
      }
    >
      <fieldset class="move-list" aria-label="Folders">
        {option(null, 0, `${ROOT_LABEL} (no folder)`)}
        {offered.map((f) => option(f.path, f.path.split('/').length, f.name))}
      </fieldset>
      <p class="field-note">
        {moving === undefined
          ? 'Only where it is listed changes; its files stay where they are on disk.'
          : `“${folderTrail(moving)}” moves with everything in it; no file moves on disk.`}
      </p>
      <DialogError message={error} />
    </Dialog>
  );
}

export function DeleteAudioDialog({ request, close }: { request: Extract<DialogRequest, { kind: 'deleteAudio' }>; close: () => void }): JSX.Element {
  const services = useServices();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const copy = deleteAudioCopy(request.title, request.durationMs, request.bytes, request.hasVideo);
  return (
    <Dialog
      titleId="delete-audio-title"
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
              void confirmDeleteAudio(services, request.recordingId, request.title).then((message) => {
                setBusy(false);
                if (message === null) {
                  close();
                  request.onDeleted?.();
                } else {
                  setError(message);
                }
              });
            }}
          >
            {copy.action}
          </button>
        </>
      }
    >
      <p class="dialog-body">{copy.body}</p>
      <DialogError message={error} />
    </Dialog>
  );
}
