// The browser-preview host's export (BRIDGE.md M3): sizes per component and format, a simulated job
// that reports export.progress and the footer, and the manifest it would write beside the files.
// `?export=fail` breaks the job part-way; `?export=unwritable` refuses the destination up front.
import { exportFolderName, joinWindowsPath, MP3_EXPORT_KBPS } from '../format/export';
import { isoWithOffset, type MockProject } from './mockData';
import { MockHostError } from './mockSession';
import type {
  Attachment,
  AudioExportFormat,
  EventName,
  EventPayload,
  ExportComponent,
  ExportDestination,
  ExportEstimate,
  ExportEstimateItem,
  ExportRunParams,
  ExportSelection,
  ExportUnavailable,
  FooterExportStatus,
  JobState,
  SettingsSnapshot,
  Track,
  TranscriptExportFormat,
} from './types';

export type ExportFlag = 'ok' | 'fail' | 'unwritable';

/** Bytes per second of one stereo 48 kHz channel pair, roughly as the host's encoders write them. */
const AUDIO_BYTES_PER_SECOND: Record<Exclude<AudioExportFormat, 'mp3'>, number> = { flac: 110_000, wav: 192_000 };
const TRANSCRIPT_BYTES_PER_SEGMENT: Record<TranscriptExportFormat, number> = { json: 420, markdown: 120, text: 96, srt: 140 };
const TRANSCRIPT_EXTENSIONS: Record<TranscriptExportFormat, string> = { json: 'json', markdown: 'md', text: 'txt', srt: 'srt' };

export interface ExportManifest {
  schemaVersion: 1;
  mementoVersion: string;
  recordingId: string;
  exportedAt: string;
  files: { path: string; bytes: number; sha256: string }[];
}

export interface MockExportEnvironment {
  emit<E extends EventName>(event: E, payload: EventPayload<E>): void;
  now(): number;
  find(recordingId: string): MockProject;
  tracks(project: MockProject): Track[];
  /** Segments in the recording's transcript, or null when it has none. */
  transcriptSegments(recordingId: string): number | null;
  attachments(recordingId: string): Attachment[];
  settings(): SettingsSnapshot;
  setSettings(next: SettingsSnapshot): void;
  /** The footer's export part changed: the host sends status.footer. */
  setFooterExport(status: FooterExportStatus): void;
  version: string;
  flag: ExportFlag;
  stepMs: number;
}

interface Job {
  jobId: string;
  recordingId: string;
  title: string;
  items: ExportEstimateItem[];
  folder: string;
  percent: number;
  timer: ReturnType<typeof setInterval> | null;
  state: JobState;
}

export interface MockExport {
  estimate(recordingId: string, selection: ExportSelection): ExportEstimate;
  run(params: ExportRunParams): { jobId: string };
  cancel(jobId: string): void;
  openFolder(jobId: string): string;
  /** The manifest of a finished job (tests and the console). */
  manifest(jobId: string): ExportManifest | null;
}

/** A stable fake SHA-256 for the manifest: the preview has no file bytes to hash. */
function fakeHash(text: string): string {
  let h = 2166136261;
  let out = '';
  for (let round = 0; round < 8; round++) {
    for (let i = 0; i < text.length; i++) {
      h ^= text.charCodeAt(i) + round;
      h = Math.imul(h, 16777619) >>> 0;
    }
    out += h.toString(16).padStart(8, '0');
  }
  return out;
}

function audioBytes(seconds: number, format: AudioExportFormat, bitrateKbps: number | null, channels: number): number {
  if (format === 'mp3') {
    return Math.round((seconds * (bitrateKbps ?? MP3_EXPORT_KBPS) * 1000) / 8);
  }
  return Math.round(seconds * AUDIO_BYTES_PER_SECOND[format] * (channels / 2));
}

export function createMockExport(env: MockExportEnvironment): MockExport {
  const jobs = new Map<string, Job>();
  const manifests = new Map<string, ExportManifest>();
  let jobCounter = 0;

  /** Every file the selection writes (ticked components only) and what cannot be exported. */
  const plan = (project: MockProject, selection: ExportSelection): { items: ExportEstimateItem[]; unavailable: ExportUnavailable[] } => {
    const { summary } = project;
    const seconds = summary.durationMs / 1000;
    const base = exportFolderName(summary.title, summary.createdAt).replace(/ \d{4}-\d{2}-\d{2}$/, '');
    const items: ExportEstimateItem[] = [];
    const unavailable: ExportUnavailable[] = [];
    const add = (component: ExportComponent, name: string, bytes: number): void => {
      items.push({ component, name, bytes });
    };
    const storing = project.stages.some((st) => st.stage === 'stored' && st.state !== 'done');
    const tracks = env.tracks(project);
    if (storing) {
      unavailable.push({ component: 'audioMixed', reason: 'Still being stored' });
      unavailable.push({ component: 'tracks', reason: 'Still being stored' });
    } else {
      if (selection.audioMixed.on) {
        add('audioMixed', `${base}.${selection.audioMixed.format}`, audioBytes(seconds, selection.audioMixed.format, selection.audioMixed.bitrateKbps, 2));
      }
      if (selection.tracks.on) {
        tracks.forEach((track, index) => {
          add(
            'tracks',
            `Tracks\\${String(index + 1).padStart(2, '0')} ${track.name}.${selection.tracks.format}`,
            audioBytes(track.durationMs / 1000, selection.tracks.format, selection.tracks.bitrateKbps, Math.max(1, track.channels)),
          );
        });
      }
    }
    const segments = env.transcriptSegments(summary.id);
    if (segments === null) {
      unavailable.push({ component: 'transcript', reason: 'Not transcribed yet' });
    } else if (selection.transcript.on) {
      for (const format of selection.transcript.formats) {
        add('transcript', `${base} transcript.${TRANSCRIPT_EXTENSIONS[format]}`, Math.max(512, segments * TRANSCRIPT_BYTES_PER_SEGMENT[format]));
      }
    }
    unavailable.push({ component: 'documents', reason: 'Documents arrive in a later version' });
    if (selection.details.on) {
      add('details', 'Recording details.json', 1_800 + project.details.agenda.items.length * 90 + project.details.participants.length * 40);
    }
    const attachments = env.attachments(summary.id);
    if (attachments.length === 0) {
      unavailable.push({ component: 'attachments', reason: 'No attachments' });
    } else if (selection.attachments.on) {
      for (const attachment of attachments) {
        add('attachments', `Attachments\\${attachment.name}`, attachment.sizeBytes);
      }
    }
    return { items, unavailable };
  };

  const report = (job: Job, currentFile: string | null, message: string | null): void => {
    const done = job.state === 'done';
    env.emit('export.progress', {
      jobId: job.jobId,
      recordingId: job.recordingId,
      percent: job.percent,
      currentFile,
      state: job.state,
      message,
      outputFolder: done ? job.folder : null,
      files: done ? job.items.length : Math.floor((job.items.length * job.percent) / 100),
      bytes: done ? job.items.reduce((sum, i) => sum + i.bytes, 0) : 0,
    });
    env.setFooterExport(
      job.state === 'running' ? { active: true, percent: job.percent, title: job.title } : { active: false, percent: null, title: null },
    );
  };

  const findJob = (jobId: string): Job => {
    const job = jobs.get(jobId);
    if (job === undefined) {
      throw new MockHostError('export.notFound', 'That export is not running any more; it may have finished. Nothing was changed.', jobId);
    }
    return job;
  };

  return {
    estimate(recordingId, selection) {
      const { items, unavailable } = plan(env.find(recordingId), selection);
      return { files: items.length, bytes: items.reduce((sum, i) => sum + i.bytes, 0), items, unavailable };
    },
    run(params) {
      const project = env.find(params.recordingId);
      const { items } = plan(project, params.selection);
      if (items.length === 0) {
        throw new MockHostError('export.nothingSelected', 'Nothing is ticked that can be exported. Tick at least one item; nothing was written.');
      }
      const destination: ExportDestination = params.destination;
      if (env.flag === 'unwritable' || /read-?only/i.test(destination.folder)) {
        throw new MockHostError(
          'export.destinationUnwritable',
          `Memento cannot write to ${destination.folder}: the folder is read-only. Nothing was written, and nothing inside Memento was changed. Choose another folder.`,
          'read-only',
        );
      }
      const folder = destination.createSubfolder
        ? joinWindowsPath(destination.folder, exportFolderName(project.summary.title, project.summary.createdAt))
        : destination.folder;
      if (params.remember) {
        const settings = env.settings();
        env.setSettings({
          ...settings,
          export: { ...settings.export, defaultFolder: destination.folder, createSubfolder: destination.createSubfolder, defaults: params.selection },
        });
      }
      const job: Job = {
        jobId: `export-${(++jobCounter).toString(16)}`,
        recordingId: project.summary.id,
        title: project.summary.title,
        items,
        folder,
        percent: 0,
        timer: null,
        state: 'running',
      };
      jobs.set(job.jobId, job);
      const step = Math.max(8, Math.ceil(100 / Math.max(4, items.length * 2)));
      job.timer = setInterval(() => {
        job.percent = Math.min(100, job.percent + step);
        const index = Math.min(items.length - 1, Math.floor((job.percent / 100) * items.length));
        const current = items[index]?.name ?? null;
        if (env.flag === 'fail' && job.percent >= 40) {
          job.state = 'failed';
          if (job.timer !== null) {
            clearInterval(job.timer);
          }
          report(
            job,
            current,
            `${current ?? 'A file'} could not be written to ${destination.folder}: the drive was disconnected. The files already written were removed. Nothing inside Memento was changed.`,
          );
          return;
        }
        if (job.percent >= 100) {
          job.state = 'done';
          if (job.timer !== null) {
            clearInterval(job.timer);
          }
          manifests.set(job.jobId, {
            schemaVersion: 1,
            mementoVersion: env.version,
            recordingId: job.recordingId,
            exportedAt: isoWithOffset(new Date(env.now())),
            files: items.map((i) => ({ path: i.name, bytes: i.bytes, sha256: fakeHash(`${job.recordingId}/${i.name}`) })),
          });
          report(job, null, null);
          return;
        }
        report(job, current, null);
      }, env.stepMs);
      report(job, items[0]?.name ?? null, null);
      return { jobId: job.jobId };
    },
    cancel(jobId) {
      const job = findJob(jobId);
      if (job.state !== 'running') {
        return;
      }
      if (job.timer !== null) {
        clearInterval(job.timer);
      }
      job.state = 'cancelled';
      report(job, null, 'The export was cancelled. The files already written were removed.');
    },
    openFolder(jobId) {
      return findJob(jobId).folder;
    },
    manifest: (jobId) => manifests.get(jobId) ?? null,
  };
}
