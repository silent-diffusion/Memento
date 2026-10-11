// 2.1, the Library's Folders view (DESIGN.md §6.1): the folder tree beside the list. "Library" is
// the root (recordings in no folder); each folder shows how many recordings it holds, opens and
// closes, and has a ⋯ menu (New folder inside, Rename, Move to, Delete). Recordings and folders are
// moved by dragging them onto a folder, or with Move to folder… from a menu.
import type { JSX } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import type { FoldersListResult } from '../../bridge/types';
import { ActionMenu, type MenuAction } from '../../components/Menus';
import { ChevronRightIcon, FolderIcon, MoreIcon, PlusIcon } from '../../components/icons';
import { ancestorsOf, folderCountLabel, folderTree, ROOT_LABEL, type FolderNode } from '../../format/folders';
import { changeFolder, moveRecordings, showFolder } from '../../state/folderActions';
import { useServices, type AppServices } from '../../state/context';

/** Drag payloads: a recording row or a folder. */
export const DRAG_RECORDING = 'application/x-memento-recording';
export const DRAG_FOLDER = 'application/x-memento-folder';

/** Reads folders.list again whenever the Library changes (library.changed refetches the list). */
export function useFolders(services: AppServices): { folders: FoldersListResult | null; error: string | null } {
  const { bridge, store } = services;
  const library = store.library.value;
  const [folders, setFolders] = useState<FoldersListResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let live = true;
    bridge
      .call('folders.list', {})
      .then((result) => {
        if (live) {
          setFolders(result);
          setError(null);
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
  }, [bridge, library]);
  return { folders, error };
}

function dropAction(services: AppServices, target: string | null, data: DataTransfer): void {
  const recording = data.getData(DRAG_RECORDING);
  const folder = data.getData(DRAG_FOLDER);
  const toast = (message: string | null, what: string): void => {
    if (message !== null) {
      services.store.toasts.show({ tone: 'warning', title: `${what} was not moved`, body: message });
    }
  };
  if (recording !== '') {
    void moveRecordings(services, [recording], target).then((m) => {
      toast(m, 'The recording');
    });
  } else if (folder !== '' && folder !== target) {
    void changeFolder(services, folder, { parent: target }).then((m) => {
      toast(m, 'The folder');
    });
  }
}

/** Drop handling shared by the root and every folder row. */
interface DropProps {
  onDragOver: (event: DragEvent) => void;
  onDragLeave: () => void;
  onDrop: (event: DragEvent) => void;
}

function useDropTarget(services: AppServices, target: string | null): { over: boolean; props: DropProps } {
  const [over, setOver] = useState(false);
  const accepts = (event: DragEvent): boolean => {
    const types = event.dataTransfer?.types ?? [];
    return types.includes(DRAG_RECORDING) || types.includes(DRAG_FOLDER);
  };
  return {
    over,
    props: {
      onDragOver: (event: DragEvent) => {
        if (accepts(event)) {
          event.preventDefault();
          setOver(true);
        }
      },
      onDragLeave: () => {
        setOver(false);
      },
      onDrop: (event: DragEvent) => {
        setOver(false);
        if (event.dataTransfer !== null && accepts(event)) {
          event.preventDefault();
          dropAction(services, target, event.dataTransfer);
        }
      },
    },
  };
}

function FolderItem({ node, depth, selected, open, toggle }: { node: FolderNode; depth: number; selected: string | null; open: ReadonlySet<string>; toggle: (path: string) => void }): JSX.Element {
  const services = useServices();
  const { folder, children } = node;
  const isOpen = open.has(folder.path.toLocaleLowerCase());
  const isSelected = selected !== null && selected.toLocaleLowerCase() === folder.path.toLocaleLowerCase();
  const drop = useDropTarget(services, folder.path);
  const actions: MenuAction[] = [
    {
      label: 'New folder inside…',
      run: () => {
        services.store.dialog.value = { kind: 'folderName', mode: 'create', parent: folder.path };
      },
    },
    {
      label: 'Rename…',
      run: () => {
        services.store.dialog.value = { kind: 'folderName', mode: 'rename', path: folder.path };
      },
    },
    {
      label: 'Move to…',
      run: () => {
        services.store.dialog.value = { kind: 'moveToFolder', recordingIds: [], title: folder.name, current: folder.parent, folderPath: folder.path };
      },
    },
    {
      label: 'Delete…',
      run: () => {
        services.store.dialog.value = { kind: 'deleteFolder', folder, childFolders: children.length };
      },
    },
  ];
  return (
    <li class="folder-item" role="none">
      <div class={`folder-row${isSelected ? ' on' : ''}${drop.over ? ' drop' : ''}`} style={{ paddingLeft: `${8 + depth * 16}px` }} {...drop.props}>
        {children.length > 0 ? (
          <button
            class={isOpen ? 'folder-disclosure open' : 'folder-disclosure'}
            type="button"
            aria-label={isOpen ? `Close ${folder.name}` : `Open ${folder.name}`}
            aria-expanded={isOpen}
            onClick={() => {
              toggle(folder.path);
            }}
          >
            <ChevronRightIcon size={14} />
          </button>
        ) : (
          <span class="folder-disclosure-space" aria-hidden="true" />
        )}
        <button
          class="folder-select"
          type="button"
          draggable
          aria-current={isSelected ? 'true' : undefined}
          aria-label={`${folder.name}, ${folderCountLabel(folder)}`}
          onDragStart={(event) => {
            event.dataTransfer?.setData(DRAG_FOLDER, folder.path);
          }}
          onClick={() => {
            showFolder(services, folder.path);
          }}
        >
          <FolderIcon size={16} />
          <span class="folder-name">{folder.name}</span>
          <span class="folder-count mono" aria-hidden="true">
            {folder.totalCount}
          </span>
        </button>
        <ActionMenu label={`More actions for the folder ${folder.name}`} triggerClass="icon-btn folder-more" actions={actions}>
          <MoreIcon size={16} />
        </ActionMenu>
      </div>
      {isOpen && children.length > 0 ? (
        <ul class="folder-children" role="group">
          {children.map((child) => (
            <FolderItem key={child.folder.path} node={child} depth={depth + 1} selected={selected} open={open} toggle={toggle} />
          ))}
        </ul>
      ) : null}
    </li>
  );
}

export function FolderTree({ selected, folders, error }: { selected: string | null; folders: FoldersListResult | null; error: string | null }): JSX.Element {
  const services = useServices();
  const [open, setOpen] = useState<ReadonlySet<string>>(() => new Set(selected === null ? [] : ancestorsOf(selected).slice(0, -1).map((p) => p.toLocaleLowerCase())));
  const toggle = (path: string): void => {
    const key = path.toLocaleLowerCase();
    setOpen((before) => {
      const next = new Set(before);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }
      return next;
    });
  };
  const root = useDropTarget(services, null);
  const nodes = folderTree(folders?.folders ?? []);
  return (
    <nav class="folder-pane" aria-label="Folders">
      <div class="folder-pane-head">
        <span class="lbl">Folders</span>
        <button
          class="icon-btn folder-new"
          type="button"
          aria-label="New folder"
          title="New folder"
          onClick={() => {
            services.store.dialog.value = { kind: 'folderName', mode: 'create', parent: null };
          }}
        >
          <PlusIcon size={16} />
        </button>
      </div>
      <ul class="folder-tree">
        <li class="folder-item" role="none">
          <div class={`folder-row${selected === null ? ' on' : ''}${root.over ? ' drop' : ''}`} style={{ paddingLeft: '8px' }} {...root.props}>
            <span class="folder-disclosure-space" aria-hidden="true" />
            <button
              class="folder-select"
              type="button"
              aria-current={selected === null ? 'true' : undefined}
              aria-label={`${ROOT_LABEL}, recordings in no folder: ${folders?.rootCount ?? 0}`}
              onClick={() => {
                showFolder(services, null);
              }}
            >
              <span class="folder-name">{ROOT_LABEL}</span>
              <span class="folder-count mono" aria-hidden="true">
                {folders?.rootCount ?? ''}
              </span>
            </button>
          </div>
        </li>
        {nodes.map((node) => (
          <FolderItem key={node.folder.path} node={node} depth={0} selected={selected} open={open} toggle={toggle} />
        ))}
      </ul>
      {error === null ? null : (
        <p class="folder-error" role="alert">
          The folders could not be shown. {error}
        </p>
      )}
      {folders !== null && folders.folders.length === 0 ? <p class="folder-hint">Make a folder to sort recordings by client, course or project. Nothing moves on disk.</p> : null}
    </nav>
  );
}
