import type { EngineStatus, StorageStatus } from '../bridge/types';
import { formatFreeSpace } from './storage';

export type DotTone = 'ok' | 'neutral';

export interface FooterLine {
  text: string;
}

export interface EngineLine extends FooterLine {
  tone: DotTone;
}

export interface StorageLine extends FooterLine {
  low: boolean;
}

/** Left side of the status footer. `null` means the host has not reported yet. */
export function engineLine(engine: EngineStatus | null): EngineLine {
  if (engine === null) {
    return { text: 'Checking transcription engine', tone: 'neutral' };
  }
  if (engine.ready) {
    return {
      text: engine.device === null ? 'Local transcription ready' : `Local transcription ready · ${engine.device}`,
      tone: 'ok',
    };
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
