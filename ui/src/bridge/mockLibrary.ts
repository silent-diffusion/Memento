// The browser-preview host's library.list and library.processing: the same filtering the real host
// does over its index (titles and people, and from M2 transcript text with a snippet).
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

/** A snippet of the recording's transcript around the first match of `query`, or null. */
export type TranscriptSnippet = (recordingId: string, query: string) => string | null;

/**
 * library.list over the index. A recording matches by title or people (matchSnippet null) or, when
 * `transcriptSnippet` finds the query in its transcript, by transcript text (matchSnippet set).
 */
export function queryLibrary(
  recordings: readonly RecordingSummary[],
  params: LibraryListParams,
  transcriptSnippet: TranscriptSnippet = () => null,
): LibraryListResult {
  const query = params.query ?? '';
  const matching: RecordingSummary[] = [];
  for (const r of recordings) {
    if (!matchesType(r, params.type)) {
      continue;
    }
    if (matchesQuery(r, query)) {
      matching.push({ ...r, matchSnippet: null });
      continue;
    }
    const snippet = query.trim() === '' ? null : transcriptSnippet(r.id, query.trim());
    if (snippet !== null) {
      matching.push({ ...r, matchSnippet: snippet });
    }
  }
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
    case 'size':
      sorted.sort((a, b) => b.sizeBytes - a.sizeBytes || byNewest(a, b));
      break;
  }
  return {
    recordings: sorted,
    totalDurationMs: sorted.reduce((sum, r) => sum + r.durationMs, 0),
    totalCount: sorted.length,
  };
}

/**
 * RecordingSummary.stages from the full pipeline, as the host's ProjectMapper.VisibleStages does it:
 * a finished `stored` or `optimize` stage is left out, so a recording with nothing else run reads
 * "Audio only". Running, queued and failed stages stay.
 */
export function visibleStages(stages: readonly StageStatus[]): StageStatus[] {
  return stages.filter((st) => !((st.stage === 'stored' || st.stage === 'optimize') && st.state === 'done'));
}

/** A recording as the preview host keeps it: the Library row and every stage of its pipeline. */
export interface MockPipeline {
  summary: RecordingSummary;
  stages: StageStatus[];
}

/**
 * The most recent recording that is still processing, and how many others are. The card's `stages`
 * are the whole pipeline; `meta` is the row, which leaves finished stored and optimize stages out.
 */
export function processingOf(recordings: readonly MockPipeline[]): LibraryProcessingResult {
  const processing = recordings.filter((r) => r.summary.isProcessing).sort((a, b) => byNewest(a.summary, b.summary));
  const [current] = processing;
  if (current === undefined) {
    return { current: null, othersCount: 0 };
  }
  return {
    current: { recordingId: current.summary.id, title: current.summary.title, meta: current.summary, stages: current.stages },
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
