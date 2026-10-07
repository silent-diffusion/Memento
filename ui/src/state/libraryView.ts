// What the Library shows: search, type filter, sort and layout live here (not in components) so a
// spoke can open and close without losing them (DESIGN.md §7, ARCHITECTURE.md §3).
import type { LibraryListParams, LibrarySort, RecordingSummary, RecordingType } from '../bridge/types';
import { FILTER_TYPES, isBuiltInType, typeName } from '../format/recording';
import { dateBucket, parseIso } from '../format/when';

export type LibraryLayout = 'list' | 'grid';

export interface LibraryViewState {
  query: string;
  type: RecordingType | 'all';
  sort: LibrarySort;
  layout: LibraryLayout;
  /** Window scroll offset saved when a spoke opened. */
  scrollY: number;
  /** The row last opened or acted on; drawn as selected and focused again on return. */
  selectedId: string | null;
}

export const INITIAL_LIBRARY_VIEW: LibraryViewState = {
  query: '',
  type: 'all',
  sort: 'newest',
  layout: 'list',
  scrollY: 0,
  selectedId: null,
};

export type LibraryViewAction =
  | { type: 'search'; query: string }
  | { type: 'filter'; recordingType: RecordingType | 'all' }
  | { type: 'sort'; sort: LibrarySort }
  | { type: 'layout'; layout: LibraryLayout }
  | { type: 'scroll'; y: number }
  | { type: 'select'; id: string | null }
  | { type: 'clear' };

export function libraryViewReducer(state: LibraryViewState, action: LibraryViewAction): LibraryViewState {
  switch (action.type) {
    case 'search':
      return action.query === state.query ? state : { ...state, query: action.query };
    case 'filter':
      return action.recordingType === state.type ? state : { ...state, type: action.recordingType };
    case 'sort':
      return action.sort === state.sort ? state : { ...state, sort: action.sort };
    case 'layout':
      return action.layout === state.layout ? state : { ...state, layout: action.layout };
    case 'scroll':
      return { ...state, scrollY: Math.max(0, action.y) };
    case 'select':
      return { ...state, selectedId: action.id };
    case 'clear':
      return { ...state, query: '', type: 'all' };
  }
}

/** True while a type filter or a search narrows the list (the processing card hides then). */
export function isNarrowed(view: Pick<LibraryViewState, 'query' | 'type'>): boolean {
  return view.type !== 'all' || view.query.trim() !== '';
}

/** The library.list parameters for the current view. */
export function listParams(view: LibraryViewState): LibraryListParams {
  const params: LibraryListParams = { sort: view.sort };
  const query = view.query.trim();
  if (query !== '') {
    params.query = query;
  }
  if (view.type !== 'all') {
    params.type = view.type;
  }
  return params;
}

export interface FilterChip {
  value: RecordingType | 'all';
  label: string;
}

/** All, the built-in types in design order, then General and custom types found in the library. */
export function filterChips(recordings: readonly RecordingSummary[]): FilterChip[] {
  const extra: string[] = [];
  for (const recording of recordings) {
    const shown = FILTER_TYPES.some((t) => t.type === recording.type);
    if (!shown && !extra.includes(recording.type)) {
      extra.push(recording.type);
    }
  }
  // General first (it is built in), then custom names alphabetically so the order is stable.
  extra.sort((a, b) => {
    if (a === 'general') return -1;
    if (b === 'general') return 1;
    return a.localeCompare(b);
  });
  return [
    { value: 'all', label: 'All' },
    ...FILTER_TYPES.map((t) => ({ value: t.type, label: t.label })),
    ...extra.map((t) => ({ value: t, label: isBuiltInType(t) ? typeName(t) : t })),
  ];
}

export interface RecordingGroup {
  key: string;
  label: string;
  items: RecordingSummary[];
}

const SORT_GROUP_LABELS: Record<'longest' | 'title' | 'size', string> = {
  longest: 'Longest first',
  title: 'A to Z',
  size: 'Largest first',
};

/**
 * Date buckets for the date sorts (newest: Today first; oldest: the reverse), keeping the host's
 * order inside each bucket and omitting empty ones. Longest and title read as one ranked list.
 */
export function groupRecordings(
  recordings: readonly RecordingSummary[],
  sort: LibrarySort,
  now: Date,
): RecordingGroup[] {
  if (recordings.length === 0) {
    return [];
  }
  if (sort === 'longest' || sort === 'title' || sort === 'size') {
    return [{ key: sort, label: SORT_GROUP_LABELS[sort], items: [...recordings] }];
  }
  const groups: RecordingGroup[] = [];
  const byKey = new Map<string, RecordingGroup>();
  for (const recording of recordings) {
    const bucket = dateBucket(parseIso(recording.createdAt), now);
    let group = byKey.get(bucket.key);
    if (group === undefined) {
      group = { key: bucket.key, label: bucket.label, items: [] };
      byKey.set(bucket.key, group);
      groups.push(group);
    }
    group.items.push(recording);
  }
  return groups;
}

/** Sort button wording. */
export const SORT_LABELS: Record<LibrarySort, string> = {
  newest: 'Newest first',
  oldest: 'Oldest first',
  longest: 'Longest first',
  title: 'By title',
  size: 'Largest first',
};

/** Sort menu options in order (the menu names them in short; the button reads SORT_LABELS). */
export const SORT_OPTIONS: readonly { value: LibrarySort; label: string }[] = [
  { value: 'newest', label: 'Newest' },
  { value: 'oldest', label: 'Oldest' },
  { value: 'longest', label: 'Longest' },
  { value: 'title', label: 'Title' },
  { value: 'size', label: 'Largest' },
];
