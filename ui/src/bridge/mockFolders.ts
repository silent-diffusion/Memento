// 2.1 in the browser preview: Library folders and Delete audio (BRIDGE.md "2.1 — Folders, dates and
// Delete audio"). Folders are data on each recording's summary (`folder`) plus the list of folders,
// exactly as the host keeps them in project.json and folders.json; no file ever moves.
import { folderNameProblem } from '../format/folders';
import { formatSize } from '../format/storage';
import { isoWithOffset, type MockProject } from './mockData';
import { MockHostError } from './mockSession';
import type { FolderInfo, LibraryListParams, MethodParams, MethodResult, RecordingSummary, SettingsSnapshot } from './types';

export const FOLDER_METHODS = [
  'folders.list',
  'folders.create',
  'folders.rename',
  'folders.move',
  'folders.delete',
  'library.moveToFolder',
  'audio.remove',
  'audio.removeEstimate',
] as const;

type FolderMethod = (typeof FOLDER_METHODS)[number];
type FolderHandlers = { [M in FolderMethod]: (params: MethodParams<M>) => MethodResult<M> };

export interface MockFoldersEnvironment {
  now(): number;
  projects: Map<string, MockProject>;
  find(recordingId: string): MockProject;
  changed(...recordingIds: string[]): void;
  settings(): SettingsSnapshot;
  /** The recording being recorded now, if any. */
  recordingId(): string | null;
}

/** The preview's sample folders (2.1), and which samples are filed in them. */
export const SAMPLE_FOLDERS: readonly string[] = ['Clients', 'Clients/Acme', 'Hiring', 'Team', 'Team/Retros'];

const SAMPLE_FILING: Readonly<Record<string, string>> = {
  '20260930-130500-onbrd': 'Clients/Acme',
  '20260929-090000-board': 'Clients',
  '20261006-141500-pnint': 'Hiring',
  '20260924-101500-mlcan': 'Hiring',
  '20261002-153000-sam11': 'Team',
  '20260909-143000-retro': 'Team/Retros',
};

export function sampleFolderOf(recordingId: string): string | null {
  return SAMPLE_FILING[recordingId] ?? null;
}

const MAX_DEPTH = 8;
const same = (a: string | null, b: string | null): boolean => (a === null || b === null ? a === b : a.toLocaleLowerCase() === b.toLocaleLowerCase());
const within = (path: string | null, folder: string): boolean =>
  path !== null && (same(path, folder) || path.toLocaleLowerCase().startsWith(`${folder.toLocaleLowerCase()}/`));
const parentOf = (path: string): string | null => (path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : null);
const nameOf = (path: string): string => path.slice(path.lastIndexOf('/') + 1);
const depthOf = (path: string): number => path.split('/').length;
const rebase = (path: string, from: string, to: string | null): string | null => {
  const rest = path.length === from.length ? '' : path.slice(from.length + 1);
  if (to === null) {
    return rest === '' ? null : rest;
  }
  return rest === '' ? to : `${to}/${rest}`;
};

/** library.list's folder filter: left out = all, null = the root, a path = that folder only. */
export function filterByFolder(recordings: readonly RecordingSummary[], params: LibraryListParams): RecordingSummary[] {
  if (params.folder === undefined) {
    return [...recordings];
  }
  const folder = params.folder;
  return recordings.filter((r) => same(r.folder, folder));
}

/** The audio a recording's tracks, mix and peaks take: what Delete audio frees. */
function audioBytes(project: MockProject): number {
  return Math.max(0, project.summary.sizeBytes - (project.video?.sizeBytes ?? 0) - 40_000);
}

export function createMockFolders(env: MockFoldersEnvironment, seed: readonly string[] = []): { handlers: FolderHandlers } {
  let saved: string[] = [...seed];

  const listed = (): MockProject[] => [...env.projects.values()].filter((p) => p.summary.state !== 'recording' && p.summary.state !== 'finalizing');

  /** folders.json and every folder a recording names, with their parents, each once. */
  const tree = (): string[] => {
    const all = new Map<string, string>();
    for (const folder of [...saved, ...listed().map((p) => p.summary.folder).filter((f): f is string => f !== null)]) {
      const names = folder.split('/');
      for (let i = 1; i <= names.length; i++) {
        const path = names.slice(0, i).join('/');
        if (!all.has(path.toLocaleLowerCase())) {
          all.set(path.toLocaleLowerCase(), path);
        }
      }
    }
    return [...all.values()].sort((a, b) => a.localeCompare(b, 'en', { sensitivity: 'base' }));
  };

  const existing = (path: string): string => {
    const found = tree().find((f) => same(f, path.trim()));
    if (found === undefined) {
      throw new MockHostError('folder.notFound', `There is no folder "${path}" in the Library any more. Nothing was changed; the Library shows the folders there are now.`, path);
    }
    return found;
  };

  const validName = (name: string): string => {
    const problem = folderNameProblem(name);
    if (problem !== null) {
      throw new MockHostError('folder.invalid', `${problem} Nothing was changed.`, name);
    }
    return name.trim();
  };

  const taken = (name: string, parent: string | null): MockHostError =>
    new MockHostError(
      'folder.nameTaken',
      parent === null
        ? `There is already a folder called "${name}" in the Library. Nothing was changed; choose another name.`
        : `There is already a folder called "${name}" in "${nameOf(parent)}". Nothing was changed; choose another name.`,
      name,
    );

  const refile = (folder: string, map: (path: string) => string | null): number => {
    let count = 0;
    for (const project of env.projects.values()) {
      const current = project.summary.folder;
      if (current !== null && within(current, folder)) {
        project.summary = { ...project.summary, folder: map(current) };
        count++;
      }
    }
    return count;
  };

  const moveTree = (from: string, to: string): void => {
    const all = tree();
    refile(from, (f) => rebase(f, from, to));
    saved = [...all.map((f) => (within(f, from) ? (rebase(f, from, to) ?? f) : f)), to];
    env.changed();
  };

  const handlers: FolderHandlers = {
    'folders.list': () => {
      const direct = new Map<string, number>();
      let rootCount = 0;
      for (const project of listed()) {
        const folder = project.summary.folder;
        if (folder === null) {
          rootCount++;
        } else {
          direct.set(folder.toLocaleLowerCase(), (direct.get(folder.toLocaleLowerCase()) ?? 0) + 1);
        }
      }
      const folders: FolderInfo[] = tree().map((path) => ({
        path,
        name: nameOf(path),
        parent: parentOf(path),
        count: direct.get(path.toLocaleLowerCase()) ?? 0,
        totalCount: [...direct.entries()].filter(([f]) => within(f, path)).reduce((sum, [, n]) => sum + n, 0),
      }));
      return { folders, rootCount };
    },
    'folders.create': (params) => {
      const parent = params.parent === null ? null : existing(params.parent);
      const name = validName(params.name);
      const path = parent === null ? name : `${parent}/${name}`;
      if (depthOf(path) > MAX_DEPTH) {
        throw new MockHostError('folder.invalid', `Folders can be nested ${MAX_DEPTH} deep at most. Nothing was created; choose a folder higher up.`, parent);
      }
      if (tree().some((f) => same(f, path))) {
        throw taken(name, parent);
      }
      saved = [...tree(), path];
      env.changed();
      return { path };
    },
    'folders.rename': (params) => {
      const from = existing(params.path);
      const name = validName(params.name);
      const parent = parentOf(from);
      const to = parent === null ? name : `${parent}/${name}`;
      if (!same(from, to) && tree().some((f) => same(f, to))) {
        throw taken(name, parent);
      }
      moveTree(from, to);
      return { path: to };
    },
    'folders.move': (params) => {
      const from = existing(params.path);
      const parent = params.parent === null ? null : existing(params.parent);
      if (parent !== null && within(parent, from)) {
        throw new MockHostError('folder.invalid', `"${nameOf(from)}" can’t be moved into itself or one of its own folders. Nothing was moved.`, parent);
      }
      const to = parent === null ? nameOf(from) : `${parent}/${nameOf(from)}`;
      if (same(from, to)) {
        return { path: from };
      }
      if (tree().some((f) => same(f, to))) {
        throw taken(nameOf(from), parent);
      }
      moveTree(from, to);
      return { path: to };
    },
    'folders.delete': (params) => {
      const folder = existing(params.path);
      const parent = parentOf(folder);
      const all = tree();
      const recordingsMoved = refile(folder, (f) => (same(f, folder) ? parent : rebase(f, folder, parent)));
      const foldersMoved = all.filter((f) => { const p = parentOf(f); return p !== null && same(p, folder); }).length;
      saved = all.filter((f) => !same(f, folder)).map((f) => (within(f, folder) ? (rebase(f, folder, parent) ?? f) : f));
      env.changed();
      return { recordingsMoved, foldersMoved };
    },
    'library.moveToFolder': (params) => {
      if (params.recordingIds.length === 0) {
        throw new MockHostError('bridge.invalidParams', 'Name 1 to 1000 recordings to move.');
      }
      const target = params.folder === null ? null : existing(params.folder);
      const projects = params.recordingIds.map((id) => env.find(id));
      const busy = projects.find((p) => p.summary.id === env.recordingId() || p.summary.state === 'recording' || p.summary.state === 'finalizing');
      if (busy !== undefined) {
        throw new MockHostError('project.recording', `"${busy.summary.title}" is still being recorded or saved. Nothing was moved; file it once it is in the Library.`, busy.summary.id);
      }
      let moved = 0;
      for (const project of projects) {
        if (!same(project.summary.folder, target)) {
          project.summary = { ...project.summary, folder: target };
          moved++;
        }
      }
      if (moved > 0) {
        env.changed(...projects.map((p) => p.summary.id));
      }
      return { moved };
    },
    'audio.remove': (params) => {
      const project = env.find(params.recordingId);
      const { title } = project.summary;
      if (project.summary.id === env.recordingId() || project.summary.state === 'recording' || project.summary.state === 'finalizing') {
        throw new MockHostError('project.recording', `"${title}" is still being recorded or saved, so its audio can’t be deleted yet. Nothing was changed; try again once it is in the Library.`, params.recordingId);
      }
      if (project.stages.some((s) => s.state === 'active' || s.state === 'queued')) {
        throw new MockHostError('library.busy', `"${title}" is being processed, and processing reads its audio. Nothing was changed; try again when it is done.`, 'audio');
      }
      if (project.summary.audioRemoved !== null) {
        throw new MockHostError('bridge.invalidParams', `The audio of "${title}" was already deleted. Nothing was changed.`, params.recordingId);
      }
      const freed = audioBytes(project);
      const at = isoWithOffset(new Date(env.now()));
      project.summary = { ...project.summary, audioRemoved: { at, bytesFreed: freed }, sizeBytes: Math.max(0, project.summary.sizeBytes - freed) };
      const hasVideo = (project.video?.tracks.length ?? 0) > 0;
      project.history = [
        ...project.history,
        {
          at,
          stage: 'stored',
          event: 'completed',
          summary: `Deleted the audio · ${formatSize(freed)} freed`,
          detail: `${project.trackSources.length} audio ${project.trackSources.length === 1 ? 'track' : 'tracks'} and the mix removed. The transcript${hasVideo ? ', the video' : ''} and documents are kept; the audio can’t be brought back.`,
        },
      ];
      env.changed(project.summary.id);
      return { freedBytes: freed };
    },
    'audio.removeEstimate': (params) => {
      if (params.olderThanDays !== null && !(params.olderThanDays >= 1 && params.olderThanDays <= 3650)) {
        throw new MockHostError('bridge.invalidParams', 'olderThanDays is 1 to 3650, or null.');
      }
      const cutoff = params.olderThanDays === null ? Number.POSITIVE_INFINITY : env.now() - params.olderThanDays * 86_400_000;
      const chosen = listed().filter((p) => {
        if (p.summary.audioRemoved !== null) {
          return false;
        }
        return params.recordingIds !== null ? params.recordingIds.includes(p.summary.id) : Date.parse(p.summary.createdAt) < cutoff;
      });
      return { recordings: chosen.length, bytes: chosen.reduce((sum, p) => sum + audioBytes(p), 0) };
    },
  };

  return { handlers };
}
