import type { EngineStatus, EngineStatusDetail, FooterExportStatus, FooterStatusPayload, StorageStatus } from '../bridge/types';
import { formatTimecode } from './duration';
import { formatFreeSpace } from './storage';

/** DESIGN.md §17 footer variants: ok = normal, accent = paused or warning, danger = a source lost. */
export type DotTone = 'ok' | 'neutral' | 'accent' | 'danger';

export interface FooterLine {
  text: string;
}

export interface EngineLine extends FooterLine {
  tone: DotTone;
  /** Shown before `text` in the dot's colour and bold (the lost-source variant). */
  strong?: string;
}

export interface StorageLine extends FooterLine {
  low: boolean;
}

/** "GPU (NVIDIA GeForce RTX 4070)", "CPU", or null when nothing runs. */
export function deviceWording(detail: Pick<EngineStatusDetail, 'device' | 'gpuName'>): string | null {
  if (detail.device === null) {
    return null;
  }
  return detail.gpuName !== null && /gpu/i.test(detail.device) ? `${detail.device} (${detail.gpuName})` : detail.device;
}

/**
 * Left side of the status footer from the engine alone (BRIDGE.md M2 status.footer): "Local
 * transcription ready · GPU (RTX 4070)", "Transcription paused · PC is busy", "No transcription model
 * installed". `null` means the host has not reported yet.
 */
export function engineLine(engine: EngineStatus | null): EngineLine {
  if (engine === null) {
    return { text: 'Checking transcription engine', tone: 'neutral' };
  }
  // An M1 host sends no detail; its two fields still give the plain line.
  const detail = (engine as Partial<EngineStatus>).detail ?? null;
  if (detail?.paused != null) {
    return { text: `Transcription paused · ${detail.paused}`, tone: 'accent' };
  }
  if (engine.ready) {
    const device = detail === null ? engine.device : (deviceWording(detail) ?? engine.device);
    return { text: device === null ? 'Local transcription ready' : `Local transcription ready · ${device}`, tone: 'ok' };
  }
  if (detail !== null && detail.model === null) {
    return { text: 'No transcription model installed', tone: 'neutral' };
  }
  return { text: 'Local transcription is not set up yet', tone: 'neutral' };
}

/** Right side of the status footer. Never shows a number the host did not measure. */
export function storageLine(storage: StorageStatus | null): StorageLine {
  const unknown: StorageLine = { text: 'Everything is stored on this PC', low: false };
  if (storage === null) {
    return unknown;
  }
  if (storage.freeBytes === null) {
    return unknown;
  }
  const free = formatFreeSpace(storage.freeBytes);
  if (storage.lowSpace) {
    return { text: `Low disk space · ${free} free`, low: true };
  }
  return { text: `Everything is stored on this PC · ${free} free`, low: false };
}

/**
 * Left side with every variant: a lost source outranks paused processing, which outranks the
 * engine state. `lostAtMs` comes from the matching recording.sourceLost event when there was one.
 */
export function statusLine(status: FooterStatusPayload | null, lostAtMs: number | null = null): EngineLine {
  if (status === null) {
    return engineLine(null);
  }
  const lost = status.recording.lostSource;
  if (status.recording.active && lost !== null) {
    return {
      tone: 'danger',
      strong: lostAtMs === null ? `${lost} lost` : `${lost} lost at ${formatTimecode(lostAtMs)}`,
      text: ' · other tracks recording',
    };
  }
  if (status.processingPaused !== null) {
    return { tone: 'accent', text: `Transcription paused · ${status.processingPaused}` };
  }
  return engineLine(status.engine);
}

/** Right side with the recording variant: while recording, say it is saving; storage warnings still win. */
export function footerStorageLine(status: FooterStatusPayload | null): StorageLine {
  const storage = storageLine(status?.storage ?? null);
  if (status === null || storage.low || !status.recording.active) {
    return storage;
  }
  return {
    text: status.storage.freeBytes === null ? 'Saving continuously' : `Saving continuously · ${formatFreeSpace(status.storage.freeBytes)} free`,
    low: false,
  };
}

/** M3: "Exporting Q3 planning sync · 42%" while an export runs (status.footer `export`). */
export function exportFooterLine(status: FooterExportStatus): string {
  const title = status.title ?? 'a recording';
  return status.percent === null ? `Exporting ${title}` : `Exporting ${title} · ${Math.round(status.percent)}%`;
}
