// Sample library for the browser preview (npm run dev). Every name is invented. Dates are relative to
// "now" so the Today / Yesterday / Earlier this week / month buckets are always populated.
import { calendarDaysBetween } from '../format/when';
import { visibleStages } from './mockLibrary';
import type {
  AudioSource,
  AudioSourceKind,
  Chapter,
  Highlight,
  HistoryEntry,
  RecordingDetails,
  RecordingSummary,
  StageStatus,
  Topic,
  Track,
} from './types';

/** "2026-10-06T10:00:00+02:00": local time with its offset, as the host writes it. */
export function isoWithOffset(date: Date): string {
  const pad = (n: number): string => String(Math.floor(Math.abs(n))).padStart(2, '0');
  const offset = -date.getTimezoneOffset();
  const sign = offset >= 0 ? '+' : '-';
  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
    `T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}` +
    `${sign}${pad(offset / 60)}:${pad(offset % 60)}`
  );
}

export const SAMPLE_SOURCES: readonly AudioSource[] = [
  { id: 'mic:usb-mv7', kind: 'microphone', name: 'Shure MV7', detail: 'USB', isDefault: true, processId: null },
  {
    id: 'system:default-output',
    kind: 'system',
    name: 'Everything this PC plays',
    detail: 'Default output',
    isDefault: true,
    processId: null,
  },
  { id: 'app:4120', kind: 'application', name: 'Zoom', detail: 'Only this app', isDefault: false, processId: 4120 },
];

/** A sample recording plus what project.get needs beyond the summary. */
export interface MockProject {
  /** The row; its `stages` follow the host's rule (visibleStages). */
  summary: RecordingSummary;
  /** Every stage of the pipeline, finished stored and optimize included (library.processing, processing.progress). */
  stages: StageStatus[];
  details: RecordingDetails;
  trackSources: AudioSourceKind[];
  /** The real tracks of a recording made in the preview; sample recordings derive theirs. */
  tracks?: Track[];
  chapters: Chapter[];
  highlights: Highlight[];
  topics: Topic[];
  history: HistoryEntry[];
}

const s = (h: number, m: number, sec: number): number => ((h * 60 + m) * 60 + sec) * 1000;

const done = (stage: StageStatus['stage']): StageStatus => ({ stage, state: 'done', percent: null, label: 'Done' });
const queued = (stage: StageStatus['stage']): StageStatus => ({ stage, state: 'queued', percent: null, label: 'Queued' });

interface Seed {
  id: string;
  title: string;
  type: string;
  /** Calendar days before today, or a month offset with a day of that month. */
  when: { daysAgo: number; hour: number; minute: number } | { monthsAgo: number; day: number; hour: number; minute: number };
  durationMs: number;
  people: string[];
  hasVideo?: boolean;
  stages: StageStatus[];
  state?: RecordingSummary['state'];
  tracks: AudioSourceKind[];
  platform?: string;
  purpose?: string;
  tags?: string[];
  /** Hand-written annotations as [h, m, s, text]; otherwise a generic set is derived. */
  chapters?: [number, number, number, string][];
  highlights?: [number, number, number, string][];
  topics?: string[];
  agenda?: { source: string; items: string[]; covered?: number };
}

const SEEDS: readonly Seed[] = [
  {
    id: '20261006-100000-q3plan',
    title: 'Q3 planning sync',
    type: 'meeting',
    when: { daysAgo: 0, hour: 10, minute: 0 },
    durationMs: s(1, 2, 14),
    people: ['Priya Natarajan', 'Sam Okafor', 'Lena Fischer', 'Marcus Lee', 'Dana Whitfield'],
    stages: [
      done('stored'),
      { stage: 'transcript', state: 'active', percent: 64, label: '64% · local GPU' },
      queued('speakers'),
    ],
    tracks: ['microphone', 'system', 'application'],
    platform: 'Zoom',
    purpose: 'Lock the launch date and agree Q3 budget asks.',
    tags: ['planning', 'q3'],
    agenda: {
      source: 'agenda.docx',
      items: ['Q2 recap', 'Hiring plan', 'Launch date', 'Budget asks', 'Open questions'],
      covered: 2,
    },
  },
  {
    id: '20261006-141500-pnint',
    title: 'Interview — Priya Natarajan, product research',
    type: 'interview',
    when: { daysAgo: 0, hour: 14, minute: 15 },
    durationMs: s(0, 48, 30),
    people: ['Priya Natarajan', 'Tomás Rivera'],
    stages: [done('stored'), done('transcript'), done('speakers')],
    tracks: ['microphone', 'system'],
    platform: 'Teams',
    purpose: 'How research teams share findings today.',
    tags: ['research'],
  },
  {
    id: '20261005-160000-dsrev',
    title: 'Design review: library screen',
    type: 'meeting',
    when: { daysAgo: 1, hour: 16, minute: 0 },
    durationMs: s(1, 10, 2),
    people: ['Aiko Tanaka', 'Jonah Berg', 'Lena Fischer', 'Sam Okafor'],
    hasVideo: true,
    stages: [done('stored'), done('transcript'), done('speakers'), done('minutes')],
    tracks: ['microphone', 'system', 'application'],
    platform: 'Zoom',
    purpose: 'Agree the library layout before build starts.',
    tags: ['design', 'memento'],
    chapters: [
      [0, 0, 0, 'Opening and goals'],
      [0, 6, 30, 'Walkthrough of mockups'],
      [0, 18, 10, 'Row density and status pills'],
      [0, 34, 0, 'Dark theme scope'],
      [0, 52, 40, 'Empty state'],
      [1, 1, 20, 'Owners and next steps'],
    ],
    highlights: [
      [0, 18, 42, 'Row height agreed at 68 px'],
      [0, 41, 10, 'Dark theme ships with v1'],
      [1, 2, 5, 'Sam owns the empty state'],
    ],
    topics: ['Library layout', 'Status pills', 'Dark theme', 'Empty state', 'Next steps'],
    agenda: {
      source: 'agenda.docx',
      items: ['Review mockups', 'Row density and status pills', 'Dark theme scope', 'Owners and next steps'],
      covered: 4,
    },
  },
  {
    id: '20261005-091200-readme',
    title: 'Notes for the README',
    type: 'dictation',
    when: { daysAgo: 1, hour: 9, minute: 12 },
    durationMs: s(0, 6, 41),
    people: [],
    stages: [done('stored'), done('transcript')],
    tracks: ['microphone'],
  },
  {
    id: '20261003-110000-cs301',
    title: 'CS 301, lecture 12: consensus protocols',
    type: 'lecture',
    when: { daysAgo: 3, hour: 11, minute: 0 },
    durationMs: s(1, 28, 45),
    people: ['Ada Ruiz'],
    hasVideo: true,
    stages: [done('stored'), done('transcript')],
    tracks: ['microphone'],
    tags: ['cs301'],
  },
  {
    id: '20261002-153000-sam11',
    title: 'Weekly 1:1 with Sam',
    type: 'meeting',
    when: { daysAgo: 4, hour: 15, minute: 30 },
    durationMs: s(0, 29, 8),
    people: ['Sam Okafor', 'Jonah Berg'],
    stages: [],
    state: 'recovered',
    tracks: ['microphone', 'system'],
    platform: 'In person',
  },
  {
    id: '20261001-174000-field',
    title: 'Field notes, site visit',
    type: 'research',
    when: { daysAgo: 5, hour: 17, minute: 40 },
    durationMs: s(0, 14, 22),
    people: [],
    stages: [done('stored'), done('transcript')],
    tracks: ['microphone'],
  },
  {
    id: '20260930-130500-onbrd',
    title: 'Customer call: onboarding feedback',
    type: 'interview',
    when: { daysAgo: 6, hour: 13, minute: 5 },
    durationMs: s(0, 37, 50),
    people: ['Mira Kowalski', 'Omar Haddad'],
    stages: [done('stored'), { stage: 'transcript', state: 'failed', percent: null, label: 'Transcript failed' }],
    tracks: ['microphone', 'application'],
    platform: 'Google Meet',
  },
  {
    id: '20260929-090000-board',
    title: 'Board prep walkthrough',
    type: 'presentation',
    when: { monthsAgo: 1, day: 29, hour: 9, minute: 0 },
    durationMs: s(0, 52, 10),
    people: ['Felix Andersen', 'Dana Whitfield', 'Marcus Lee'],
    stages: [done('stored'), done('transcript'), done('speakers'), done('minutes')],
    tracks: ['microphone', 'system'],
  },
  {
    id: '20260924-101500-mlcan',
    title: 'Interview — Marcus Lee, candidate',
    type: 'interview',
    when: { monthsAgo: 1, day: 24, hour: 10, minute: 15 },
    durationMs: s(0, 41, 5),
    people: ['Marcus Lee', 'Jonah Berg'],
    stages: [done('stored'), done('transcript'), done('speakers')],
    tracks: ['microphone', 'system'],
    platform: 'Teams',
  },
  {
    id: '20260918-160000-thall',
    title: 'Town hall Q&A',
    type: 'meeting',
    when: { monthsAgo: 1, day: 18, hour: 16, minute: 0 },
    durationMs: s(1, 45, 30),
    people: [
      'Felix Andersen',
      'Priya Natarajan',
      'Sam Okafor',
      'Lena Fischer',
      'Marcus Lee',
      'Dana Whitfield',
      'Aiko Tanaka',
      'Jonah Berg',
      'Mira Kowalski',
      'Omar Haddad',
      'Tomás Rivera',
      'Ada Ruiz',
    ],
    stages: [done('stored'), done('transcript'), done('speakers'), done('minutes')],
    tracks: ['microphone', 'system', 'application'],
    platform: 'Zoom',
  },
  {
    id: '20260909-143000-retro',
    title: 'Sprint retrospective',
    type: 'meeting',
    when: { monthsAgo: 1, day: 9, hour: 14, minute: 30 },
    durationMs: s(0, 55, 12),
    people: ['Aiko Tanaka', 'Jonah Berg', 'Sam Okafor'],
    stages: [],
    tracks: ['microphone', 'system'],
  },
  {
    id: '20260827-200000-books',
    title: 'Reading notes: designing data-intensive systems',
    type: 'Book notes',
    when: { monthsAgo: 2, day: 27, hour: 20, minute: 0 },
    durationMs: s(0, 22, 40),
    people: [],
    stages: [done('stored'), done('transcript')],
    tracks: ['microphone'],
  },
  {
    id: '20260814-081000-garden',
    title: 'Voice memo: garden plan',
    type: 'dictation',
    when: { monthsAgo: 2, day: 14, hour: 8, minute: 10 },
    durationMs: s(0, 3, 26),
    people: [],
    stages: [],
    tracks: ['microphone'],
  },
];

function seedDate(when: Seed['when'], durationMs: number, now: Date): Date {
  if ('daysAgo' in when) {
    const date = new Date(now.getFullYear(), now.getMonth(), now.getDate() - when.daysAgo, when.hour, when.minute);
    // A "today" recording must have finished already.
    const latest = now.getTime() - durationMs - 5 * 60_000;
    return date.getTime() > latest ? new Date(latest) : date;
  }
  const lastDay = new Date(now.getFullYear(), now.getMonth() - when.monthsAgo + 1, 0).getDate();
  let date = new Date(now.getFullYear(), now.getMonth() - when.monthsAgo, Math.min(when.day, lastDay), when.hour, when.minute);
  // Early in a month the end of the last one is still "this week": keep month samples older than that.
  while (calendarDaysBetween(date, now) < 7) {
    date = new Date(date.getFullYear(), date.getMonth(), date.getDate() - 7, date.getHours(), date.getMinutes());
  }
  return date;
}

const TRACK_NAMES: Record<AudioSourceKind, string> = {
  microphone: 'Shure MV7',
  system: 'Everything this PC plays',
  application: 'Zoom',
};

const TRACK_SOURCE_IDS: Record<AudioSourceKind, string> = {
  microphone: 'mic:usb-mv7',
  system: 'system:default-output',
  application: 'app:4120',
};

export function mockTracks(recordingId: string, kinds: readonly AudioSourceKind[], durationMs: number): Track[] {
  return kinds.map((kind, index) => ({
    id: `${recordingId}-t${index + 1}`,
    sourceId: TRACK_SOURCE_IDS[kind],
    sourceKind: kind,
    name: TRACK_NAMES[kind],
    file: `tracks/${String(index + 1).padStart(2, '0')}-${kind}.flac`,
    sampleRate: 48_000,
    channels: kind === 'microphone' ? 1 : 2,
    durationMs,
    sha256: null,
    startOffsetMs: 0,
    endedEarlyAtMs: null,
  }));
}

/** About 96 KB per second per FLAC track, plus video when there is any. */
export function estimateSizeBytes(summary: RecordingSummary, trackCount: number): number {
  const seconds = summary.durationMs / 1000;
  return Math.round(seconds * trackCount * 96_000 + (summary.hasVideo ? seconds * 450_000 : 0));
}

function sampleAnnotations(seed: Seed): Pick<MockProject, 'chapters' | 'highlights' | 'topics'> {
  const length = seed.durationMs;
  if (seed.chapters !== undefined || seed.highlights !== undefined) {
    return {
      chapters: (seed.chapters ?? []).map(([h, m, sec, title], i) => ({ id: `${seed.id}-c${i + 1}`, atMs: s(h, m, sec), title, origin: 'user' })),
      highlights: (seed.highlights ?? []).map(([h, m, sec, note], i) => ({
        id: `${seed.id}-h${i + 1}`,
        atMs: s(h, m, sec),
        note,
        origin: 'user',
        segmentId: null,
      })),
      topics: (seed.topics ?? []).map((label, i) => ({ id: `${seed.id}-p${i}`, label, origin: 'local' })),
    };
  }
  const chapters: Chapter[] =
    length > s(0, 20, 0)
      ? [
          { id: `${seed.id}-c1`, atMs: 0, title: 'Introductions', origin: 'user' },
          { id: `${seed.id}-c2`, atMs: Math.round(length * 0.3), title: 'Main discussion', origin: 'user' },
          { id: `${seed.id}-c3`, atMs: Math.round(length * 0.8), title: 'Next steps', origin: 'user' },
        ]
      : [];
  const highlights: Highlight[] = [
    { id: `${seed.id}-h1`, atMs: Math.round(length * 0.42), note: 'Decision to revisit', origin: 'user', segmentId: null },
  ];
  const topics: Topic[] = (seed.tags ?? []).map((tag, i) => ({ id: `${seed.id}-p${i}`, label: tag, origin: 'user' }));
  return { chapters, highlights, topics };
}

function sampleHistory(seed: Seed, createdAt: Date): HistoryEntry[] {
  const at = (offsetMs: number): string => isoWithOffset(new Date(createdAt.getTime() + offsetMs));
  const history: HistoryEntry[] = [
    {
      at: at(0),
      stage: 'recorded',
      event: 'completed',
      summary: `Recorded ${seed.tracks.length} ${seed.tracks.length === 1 ? 'track' : 'tracks'}`,
      detail: seed.tracks.map((k) => TRACK_NAMES[k]).join(', '),
    },
  ];
  if (seed.state === 'recovered') {
    history.push({
      at: at(seed.durationMs + 60_000),
      stage: 'recovered',
      event: 'info',
      summary: 'Recovered after an interruption',
      detail: 'The last 20 seconds may be missing.',
    });
  }
  const wording: Record<StageStatus['stage'], { done: string; failed: string; active: string; detail: string }> = {
    stored: { done: 'Stored', failed: 'Storing failed', active: 'Storing', detail: 'Lossless FLAC tracks on this PC' },
    transcript: { done: 'Transcribed locally', failed: 'Transcription failed', active: 'Transcribing locally', detail: 'Local engine · GPU · English' },
    speakers: { done: 'Speakers identified', failed: 'Speaker identification failed', active: 'Identifying speakers', detail: 'Local engine · CPU' },
    topics: { done: 'Topics found locally', failed: 'Topics could not be saved', active: 'Finding topics', detail: 'On this PC' },
    minutes: { done: 'Minutes generated', failed: 'Minutes failed', active: 'Generating minutes', detail: 'Sent the transcript, agenda and participants' },
    optimize: { done: 'Saved smaller files', failed: 'Making smaller files failed', active: 'Making smaller files', detail: 'AAC 128 kbps' },
  };
  seed.stages.forEach((stage, index) => {
    if (stage.state === 'queued') {
      return;
    }
    const words = wording[stage.stage];
    history.push({
      at: at(seed.durationMs + 30_000 * (index + 1)),
      stage: stage.stage,
      event: stage.state === 'done' ? 'completed' : stage.state === 'failed' ? 'failed' : 'started',
      summary: stage.state === 'done' ? words.done : stage.state === 'failed' ? words.failed : words.active,
      detail:
        stage.state === 'failed'
          ? 'The GPU ran out of memory at 64%. The recording is safe and the partial transcript was kept.'
          : stage.state === 'active'
            ? (stage.label ?? words.detail)
            : words.detail,
    });
  });
  if (seed.stages.length === 0) {
    history.push({
      at: at(seed.durationMs + 30_000),
      stage: 'stored',
      event: 'completed',
      summary: 'Stored',
      detail: 'Lossless FLAC tracks on this PC',
    });
  }
  return history;
}

export function sampleProjects(now: Date): MockProject[] {
  return SEEDS.map((seed) => {
    const createdAt = seedDate(seed.when, seed.durationMs, now);
    const summary: RecordingSummary = {
      id: seed.id,
      title: seed.title,
      type: seed.type,
      createdAt: isoWithOffset(createdAt),
      durationMs: seed.durationMs,
      participantCount: seed.people.length,
      hasVideo: seed.hasVideo ?? false,
      stages: visibleStages(seed.stages),
      people: seed.people,
      isProcessing: seed.stages.some((st) => st.state === 'active' || st.state === 'queued'),
      state: seed.state ?? 'ready',
      sizeBytes: 0,
      matchSnippet: null,
    };
    summary.sizeBytes = estimateSizeBytes(summary, seed.tracks.length);
    const details: RecordingDetails = {
      title: seed.title,
      type: seed.type,
      participants: seed.people,
      purpose: seed.purpose ?? '',
      platform: seed.platform ?? '',
      organization: '',
      location: '',
      notes: '',
      tags: seed.tags ?? [],
      agenda:
        seed.agenda === undefined
          ? { source: null, parsedLocally: true, items: [] }
          : {
              source: seed.agenda.source,
              parsedLocally: true,
              items: seed.agenda.items.map((text, i) => ({
                id: `${seed.id}-a${i + 1}`,
                text,
                covered: i < (seed.agenda?.covered ?? 0),
                uncertain: false,
                uncertainReason: null,
              })),
            },
    };
    return {
      summary,
      stages: [...seed.stages],
      details,
      trackSources: seed.tracks,
      ...sampleAnnotations(seed),
      history: sampleHistory(seed, createdAt),
    };
  });
}
