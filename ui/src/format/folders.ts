// 2.1: Library folders and Delete audio, as words and as a tree (DESIGN.md §6.1, §6.2, §17).
import type { FolderInfo } from '../bridge/types';
import { formatDuration } from './duration';
import { formatSize } from './storage';

export interface FolderNode {
  folder: FolderInfo;
  children: FolderNode[];
}

/** The flat, path-sorted folders.list answer as a tree of top-level folders. */
export function folderTree(folders: readonly FolderInfo[]): FolderNode[] {
  const byPath = new Map<string, FolderNode>();
  const roots: FolderNode[] = [];
  for (const folder of [...folders].sort((a, b) => a.path.localeCompare(b.path, 'en', { sensitivity: 'base' }))) {
    const node: FolderNode = { folder, children: [] };
    byPath.set(folder.path.toLocaleLowerCase(), node);
    const parent = folder.parent === null ? undefined : byPath.get(folder.parent.toLocaleLowerCase());
    (parent?.children ?? roots).push(node);
  }
  return roots;
}

/** The folder and every folder above it, outermost first: what the tree opens to show it. */
export function ancestorsOf(path: string): string[] {
  const names = path.split('/');
  return names.map((_, i) => names.slice(0, i + 1).join('/'));
}

export const ROOT_LABEL = 'Library';

const MAX_NAME = 80;

/** What is wrong with a folder name, in the host's words (BRIDGE.md 2.1), or null. */
export function folderNameProblem(name: string): string | null {
  const trimmed = name.trim();
  if (trimmed === '') {
    return 'A folder needs a name.';
  }
  if (trimmed.length > MAX_NAME) {
    return `A folder name can be at most ${MAX_NAME} characters; this one has ${trimmed.length}.`;
  }
  if (/[/\\]/.test(trimmed) || Array.from({ length: trimmed.length }, (_, i) => trimmed.charCodeAt(i)).some((c) => c < 32)) {
    return 'A folder name can’t contain / or \\.';
  }
  if (trimmed === '.' || trimmed === '..') {
    return 'A folder can’t be called . or ..';
  }
  return null;
}

/** "Acme" for "Clients/Acme"; the root is "Library". */
export function folderName(path: string | null): string {
  return path === null ? ROOT_LABEL : path.slice(path.lastIndexOf('/') + 1);
}

/** "Clients › Acme" for headings and the move list. */
export function folderTrail(path: string | null): string {
  return path === null ? ROOT_LABEL : path.split('/').join(' › ');
}

const recordingsWord = (n: number): string => `${n} ${n === 1 ? 'recording' : 'recordings'}`;
const foldersWord = (n: number): string => `${n} ${n === 1 ? 'folder' : 'folders'}`;

/** The count a tree item shows: what is in it and below it. */
export function folderCountLabel(folder: FolderInfo): string {
  return folder.totalCount === folder.count ? recordingsWord(folder.count) : `${recordingsWord(folder.totalCount)}, ${folder.count} here`;
}

export interface ConfirmCopy {
  title: string;
  body: string;
  action: string;
}

/**
 * §17 Delete folder: names the folder and says exactly where its contents go; nothing is deleted
 * but the folder itself.
 */
export function deleteFolderCopy(folder: FolderInfo, childFolders: number): ConfirmCopy {
  const parent = folderName(folder.parent);
  const contents: string[] = [];
  if (folder.totalCount > 0) {
    contents.push(recordingsWord(folder.totalCount));
  }
  if (childFolders > 0) {
    contents.push(foldersWord(childFolders));
  }
  const body =
    contents.length === 0
      ? `It is empty. No recording is affected, and nothing changes on disk.`
      : `Its ${contents.join(' and ')} move to ${parent === ROOT_LABEL ? 'the Library itself' : `“${parent}”`}. No recording is deleted, and nothing changes on disk.`;
  return { title: `Delete the folder “${folder.name}”?`, body, action: 'Delete folder' };
}

/**
 * §17 Delete audio: names the recording, its length and the size freed, and what remains (the
 * transcript, documents and any video), then that it cannot be undone. The button says what it does.
 */
export function deleteAudioCopy(title: string, durationMs: number, bytes: number, hasVideo: boolean): ConfirmCopy {
  const kept = hasVideo ? 'The transcript, documents and video stay' : 'The transcript and documents stay';
  return {
    title: `Delete the audio of “${title}”?`,
    body: `This deletes its audio tracks and the mix: ${formatDuration(durationMs)} of audio, ${formatSize(bytes)}. ${kept}, and the recording stays in the Library to read. The audio can’t be brought back, so it can’t be played, exported or transcribed again.`,
    action: 'Delete audio',
  };
}

/** Review's line in place of the player once the audio was deleted. */
export function audioRemovedLine(removedAt: string, bytesFreed: number, when: (iso: string) => string): string {
  return `The audio was deleted on ${when(removedAt)} to free ${formatSize(bytesFreed)}. The transcript and documents are kept.`;
}
