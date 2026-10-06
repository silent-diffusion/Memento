// The browser-preview host's library.list and library.processing: the same filtering the real host
// does over its index (titles and people; transcripts arrive in M2).
import type {
  LibraryListParams,
  LibraryListResult,
  LibraryProcessingResult,
  RecordingSummary,
  StageStatus,
} from './types';

function matchesQuery(recording: RecordingSummary, query: string): boolean {
  const needle = query.trim().toLocaleLowerCase();
  if (needle === '') {
    return true;
  }
  return (
    recording.title.toLocaleLowerCase().includes(needle) ||
    recording.people.some((person) => person.toLocaleLowerCase().includes(needle))
  );
}

function matchesType(recording: RecordingSummary, type: LibraryListParams['type']): boolean {
  return type === undefined || type === 'all' || recording.type.toLocaleLowerCase() === type.toLocaleLowerCase();
}

const byNewest = (a: RecordingSummary, b: RecordingSummary): number => Date.parse(b.createdAt) - Date.parse(a.createdAt);

export function queryLibrary(recordings: readonly RecordingSummary[], params: LibraryListParams): LibraryListResult {
  const matching = recordings.filter((r) => matchesType(r, params.type) && matchesQuery(r, params.query ?? ''));
  const sorted = [...matching];
  switch (params.sort ?? 'newest') {
    case 'newest':
      sorted.sort(byNewest);
      break;
    case 'oldest':
      sorted.sort((a, b) => -byNewest(a, b));
      break;
    case 'longest':
      sorted.sort((a, b) => b.durationMs - a.durationMs || byNewest(a, b));
      break;
    case 'title':
      sorted.sort((a, b) => a.title.localeCompare(b.title, 'en', { sensitivity: 'base' }) || byNewest(a, b));
      break;
  }
  return {
    recordings: sorted,
    totalDurationMs: sorted.reduce((sum, r) => sum + r.durationMs, 0),
    totalCount: sorted.length,
  };
}

/** The most recent recording that is still processing, and how many others are. */
export function processingOf(recordings: readonly RecordingSummary[]): LibraryProcessingResult {
  const processing = recordings.filter((r) => r.isProcessing).sort(byNewest);
  const [current] = processing;
  if (current === undefined) {
    return { current: null, othersCount: 0 };
  }
  return {
    current: { recordingId: current.id, title: current.title, meta: current, stages: current.stages },
    othersCount: processing.length - 1,
  };
}

/**
 * One tick of the simulated pipeline: the active stage moves `step` percent; when it finishes, the
 * next queued stage starts. Returns the same array when nothing is running.
 */
export function advanceStages(stages: readonly StageStatus[], step: number, device = 'local GPU'): StageStatus[] {
  const activeIndex = stages.findIndex((st) => st.state === 'active');
  if (activeIndex < 0) {
    const nextQueued = stages.findIndex((st) => st.state === 'queued');
    if (nextQueued < 0) {
      return [...stages];
    }
    return stages.map((st, i) => (i === nextQueued ? { ...st, state: 'active', percent: 0, label: `0% · ${device}` } : st));
  }
  const active = stages[activeIndex];
  if (active === undefined) {
    return [...stages];
  }
  const percent = Math.min(100, (active.percent ?? 0) + step);
  if (percent < 100) {
    return stages.map((st, i) => (i === activeIndex ? { ...st, percent, label: `${percent}% · ${device}` } : st));
  }
  return stages.map((st, i) => (i === activeIndex ? { ...st, state: 'done', percent: null, label: 'Done' } : st));
}
