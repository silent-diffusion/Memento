// Copy to the clipboard (BRIDGE.md, "Clipboard and transcript text options", after 1.2.0): the host
// formats and writes the Windows clipboard; the page never uses the browser's clipboard. A copy is
// confirmed quietly (DESIGN.md §17: no toast for a success); a refusal gets the host's words in a toast.
import type { BridgeClient } from '../bridge/client';
import type { TranscriptCopyFormat, TranscriptCopyResult, TranscriptTextOptions } from '../bridge/types';
import type { AppStore } from './store';
import { writeCopyFormat } from './uiPrefs';

export const FORMAT_WORDS: Record<TranscriptCopyFormat, string> = { text: 'text', markdown: 'Markdown' };

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

/** "Copied the transcript as text", or for a filtered view "Copied 42 of 318 lines as Markdown". */
export function copiedText(result: Pick<TranscriptCopyResult, 'lines' | 'totalLines'>, format: TranscriptCopyFormat, filtered: boolean): string {
  const as = `as ${FORMAT_WORDS[format]}`;
  if (!filtered) {
    return `Copied the transcript ${as}`;
  }
  return `Copied ${result.lines} of ${result.totalLines} ${result.totalLines === 1 ? 'line' : 'lines'} ${as}`;
}

export interface TranscriptCopyRequest {
  recordingId: string;
  format: TranscriptCopyFormat;
  options: TranscriptTextOptions;
  /** The lines a filtered view shows; null copies every line. */
  segmentIds: readonly string[] | null;
}

/**
 * Copies the transcript and returns the confirmation, or null when the host refused (a warning toast
 * says why). The chosen text form is remembered for the next quick Copy.
 */
export async function copyTranscript(bridge: BridgeClient, store: AppStore, request: TranscriptCopyRequest): Promise<string | null> {
  writeCopyFormat(request.format);
  try {
    const result = await bridge.call('transcript.copy', {
      recordingId: request.recordingId,
      format: request.format,
      options: request.options,
      ...(request.segmentIds === null ? {} : { segmentIds: [...request.segmentIds] }),
    });
    return copiedText(result, request.format, request.segmentIds !== null);
  } catch (e) {
    store.toasts.show({ tone: 'warning', title: 'The transcript was not copied', body: messageOf(e) });
    return null;
  }
}

/** Copies a document as Markdown and formatted; returns "Copied “Minutes”", or null after a warning toast. */
export async function copyDocument(bridge: BridgeClient, store: AppStore, recordingId: string, documentId: string, name: string): Promise<string | null> {
  try {
    await bridge.call('documents.copy', { recordingId, documentId });
    return `Copied “${name}”`;
  } catch (e) {
    store.toasts.show({ tone: 'warning', title: `“${name}” was not copied`, body: messageOf(e) });
    return null;
  }
}
