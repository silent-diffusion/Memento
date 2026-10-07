// Wording for one recording: type names, the people part of the meta line, status pills and the
// Library summary line (DESIGN.md §4, §5.4, §17).
import type { BuiltInRecordingType, RecordingSummary, RecordingType, StageName, StageStatus } from '../bridge/types';
import { formatTotalDuration } from './duration';
import { formatWhen, parseIso } from './when';

const TYPE_NAMES: Record<BuiltInRecordingType, string> = {
  meeting: 'Meeting',
  interview: 'Interview',
  presentation: 'Presentation',
  lecture: 'Lecture',
  dictation: 'Dictation',
  research: 'Research',
  general: 'General',
};

/** Chip labels, in the order of DESIGN.md §4. General and custom types are appended after Research. */
export const FILTER_TYPES: readonly { type: BuiltInRecordingType; label: string }[] = [
  { type: 'meeting', label: 'Meetings' },
  { type: 'interview', label: 'Interviews' },
  { type: 'lecture', label: 'Lectures' },
  { type: 'presentation', label: 'Presentations' },
  { type: 'dictation', label: 'Dictation' },
  { type: 'research', label: 'Research' },
];

/** Types offered when choosing one (Change type, Settings › Recording). */
export const CHOOSABLE_TYPES: readonly BuiltInRecordingType[] = [
  'meeting',
  'interview',
  'presentation',
  'lecture',
  'dictation',
  'research',
  'general',
];

export function isBuiltInType(type: RecordingType): type is BuiltInRecordingType {
  return Object.hasOwn(TYPE_NAMES, type);
}

/** "Meeting", or the custom type's own name. */
export function typeName(type: RecordingType): string {
  return isBuiltInType(type) ? TYPE_NAMES[type] : type;
}

/** "5 people", "2 people", "1 speaker", "Just me". `count` is the people listed for the recording (0 = a solo recording). */
export function peopleWording(count: number): string {
  if (!Number.isFinite(count) || count <= 0) {
    return 'Just me';
  }
  if (count === 1) {
    return '1 speaker';
  }
  return `${Math.floor(count)} people`;
}

/** "Meeting · 5 people · 10:00 AM". */
export function metaLine(recording: RecordingSummary, now: Date): string {
  return `${typeName(recording.type)} · ${peopleWording(recording.participantCount)} · ${formatWhen(parseIso(recording.createdAt), now)}`;
}

/** "10 recordings · 8 h 38 min · all on this PC". Reflects whatever the list currently shows. */
export function summaryLine(count: number, totalDurationMs: number): string {
  const noun = count === 1 ? 'recording' : 'recordings';
  return `${count} ${noun} · ${formatTotalDuration(totalDurationMs)} · all on this PC`;
}

export interface Pill {
  kind: StageStatus['state'];
  label: string;
  /** The stage the pill stands for (a failed pill's Retry acts on it). */
  stage: StageName;
}

/**
 * Pipeline stages the UI can name: the host's StageName. Unknown names fall back to a capitalised
 * form, so a newer host never breaks the row.
 */
export type PipelineStage = StageName;

const DONE_NAMES: Record<PipelineStage, string> = {
  stored: 'Stored',
  transcript: 'Transcript',
  speakers: 'Speakers',
  topics: 'Topics',
  minutes: 'Minutes',
  optimize: 'Smaller files',
};

const ACTIVE_NAMES: Record<PipelineStage, string> = {
  stored: 'Storing',
  transcript: 'Transcribing',
  speakers: 'Speakers',
  topics: 'Topics',
  minutes: 'Minutes',
  optimize: 'Making smaller',
};

/** The processing card's column names (DESIGN.md §4). */
export const CARD_STAGE_NAMES: Record<PipelineStage, string> = {
  stored: 'Stored',
  transcript: 'Transcribing',
  speakers: 'Speakers',
  topics: 'Topics',
  minutes: 'Minutes',
  optimize: 'Smaller files',
};

function named(names: Record<PipelineStage, string>, stage: string): string {
  return (names as Record<string, string | undefined>)[stage] ?? `${stage.charAt(0).toLocaleUpperCase()}${stage.slice(1)}`;
}

/** The processing card's column name for any stage the host reports. */
export function cardStageName(stage: string): string {
  return named(CARD_STAGE_NAMES, stage);
}

/** "Transcript", "Speakers": the stage's name in a done or queued pill, and in "… failed". */
export function stageName(stage: string): string {
  return named(DONE_NAMES, stage);
}

/**
 * Stages whose finished state is the normal state, not news: keeping the audio safe or small, and
 * the quick local topics pass after speakers (the host leaves a finished one out of rows too).
 */
const HOUSEKEEPING: ReadonlySet<string> = new Set<StageName>(['stored', 'topics', 'optimize']);

function pillFor(stage: StageStatus): Pill {
  const base = { stage: stage.stage };
  switch (stage.state) {
    case 'done':
      return { ...base, kind: 'done', label: named(DONE_NAMES, stage.stage) };
    case 'active':
      // A stage waiting for a busy PC is not running; it reads as waiting, with its reason in the card.
      if (stage.label !== null && /^paused\b/i.test(stage.label)) {
        return { ...base, kind: 'queued', label: `${named(DONE_NAMES, stage.stage)} paused` };
      }
      return {
        ...base,
        kind: 'active',
        label: stage.percent === null ? named(ACTIVE_NAMES, stage.stage) : `${named(ACTIVE_NAMES, stage.stage)} ${Math.round(stage.percent)}%`,
      };
    case 'queued':
      return { ...base, kind: 'queued', label: named(DONE_NAMES, stage.stage) };
    case 'failed':
      // Waiting for a model to be installed is not a failure; it starts by itself once there is one.
      if (stage.label === 'Waiting for a model') {
        return { ...base, kind: 'queued', label: `${named(DONE_NAMES, stage.stage)} · needs a model` };
      }
      return { ...base, kind: 'failed', label: `${named(DONE_NAMES, stage.stage)} failed · Retry` };
  }
}

/**
 * Row pills. A finished "Stored" or "Smaller files" stage is the normal state of a recording (the
 * host leaves both out of RecordingSummary.stages), so it is shown only beside a failed stage, where
 * it says the recording itself is safe. An empty list means "Audio only".
 */
export function stagePills(stages: readonly StageStatus[]): Pill[] {
  const broken = (s: StageStatus): boolean => s.state === 'failed' && s.label !== 'Waiting for a model';
  const anyFailed = stages.some(broken);
  const failed = stages.filter(broken).map(pillFor);
  const rest = stages
    .filter((s) => !broken(s) && (!HOUSEKEEPING.has(s.stage) || s.state !== 'done' || anyFailed))
    .map(pillFor);
  // A row's stages leave a finished Stored out (BRIDGE.md, Stages); beside a failure it is put back,
  // because "the recording itself is safe" is the point (DESIGN.md §17).
  if (anyFailed && !stages.some((s) => s.stage === 'stored')) {
    rest.unshift({ kind: 'done', label: named(DONE_NAMES, 'stored'), stage: 'stored' });
  }
  return [...failed, ...rest];
}

/** Right-hand status of a processing card column: the host's label, else a plain fallback. */
export function stageStatusText(stage: StageStatus): string {
  if (stage.label !== null && stage.label !== '') {
    return stage.label;
  }
  switch (stage.state) {
    case 'done':
      return 'Done';
    case 'active':
      return stage.percent === null ? 'Running' : `${Math.round(stage.percent)}%`;
    case 'queued':
      return 'Queued';
    case 'failed':
      return 'Failed';
  }
}

/** Width of a processing card progress fill, as a CSS percentage. */
export function stageFill(stage: StageStatus): string {
  switch (stage.state) {
    case 'done':
      return '100%';
    case 'active':
      return `${Math.min(100, Math.max(0, stage.percent ?? 0))}%`;
    case 'queued':
    case 'failed':
      return '0%';
  }
}
