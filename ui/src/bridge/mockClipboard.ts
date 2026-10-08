// The browser-preview host's clipboard (BRIDGE.md, "Clipboard and transcript text options"): transcript.copy and
// documents.copy. The real host formats with Memento.Core.Export.TranscriptText (one formatter for files and copies)
// and writes the Windows clipboard; this stand-in follows the same rules closely enough for the preview and the
// tests, and keeps the copy in memory (the page's own clipboard is never touched).
import { MockHostError } from './mockSession';
import type {
  DocumentCopyResult,
  DocumentIdParams,
  RecordingSummary,
  Transcript,
  TranscriptCopyFormat,
  TranscriptCopyParams,
  TranscriptCopyResult,
  TranscriptLayout,
  TranscriptTextOptions,
} from './types';

export const DEFAULT_TEXT_OPTIONS: TranscriptTextOptions = { timestamps: true, speakers: true, layout: 'auto' };

const LAYOUTS: readonly TranscriptLayout[] = ['auto', 'turns', 'lines'];
const NL = '\r\n';

export interface MockClipboardEnvironment {
  /** The recording's row; throws project.notFound. */
  summary: (recordingId: string) => RecordingSummary;
  transcript: (recordingId: string) => Transcript | null;
  /** The document's title and Markdown-ish text and its paper HTML; throws documents.notFound. */
  document: (recordingId: string, documentId: string) => { title: string; markdown: string; html: string };
  /** True while "another program" holds the clipboard (tests). */
  busy: () => boolean;
}

export interface MockClipboard {
  handlers: {
    'transcript.copy': (params: TranscriptCopyParams) => TranscriptCopyResult;
    'documents.copy': (params: DocumentIdParams) => DocumentCopyResult;
  };
  /** What the last copy put on the clipboard. */
  current: () => { text: string; html: string | null } | null;
}

function marker(seconds: number): string {
  const total = Math.max(0, Math.floor(seconds));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  return `[${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}]`;
}

function escapeMarkdown(text: string): string {
  return text.replace(/[\\*_`[\]<>#|~]/g, (c) => `\\${c}`);
}

function clock(ms: number): string {
  const total = Math.floor(ms / 1000);
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}` : `${m}:${String(s).padStart(2, '0')}`;
}

/** The readable transcript as the host writes it (TranscriptText), for the preview. */
export function mockTranscriptText(
  format: TranscriptCopyFormat,
  transcript: Transcript,
  summary: Pick<RecordingSummary, 'title' | 'createdAt' | 'durationMs'>,
  options: TranscriptTextOptions = DEFAULT_TEXT_OPTIONS,
  only: ReadonlySet<string> | null = null,
): { text: string; lines: number } {
  const names = new Map(transcript.speakers.map((s) => [s.id, s.name]));
  const lines = transcript.segments
    .map((seg) => ({ seg, body: seg.text.split(/\s+/).filter((w) => w !== '').join(' ') }))
    .filter(({ seg, body }) => body !== '' && (only === null || only.has(seg.id)))
    .map(({ seg, body }) => ({
      speakerId: seg.speaker,
      speaker: options.speakers && seg.speaker !== null ? (names.get(seg.speaker) ?? seg.speaker) : null,
      marker: options.timestamps ? marker(seg.start) : null,
      body,
    }));
  const layout = options.layout === 'auto' ? (format === 'markdown' ? 'turns' : 'lines') : options.layout;
  const groups: (typeof lines)[] = [];
  for (const line of lines) {
    const last = groups.at(-1);
    if (layout === 'turns' && last !== undefined && line.speakerId !== null && last.at(-1)?.speakerId === line.speakerId) {
      last.push(line);
    } else {
      groups.push([line]);
    }
  }
  const speakers = options.speakers ? [...new Set(lines.map((l) => l.speaker).filter((n): n is string => n !== null))] : [];
  const heading = [summary.createdAt.slice(0, 16).replace('T', ' '), clock(summary.durationMs), ...(speakers.length > 0 ? [`Speakers: ${speakers.join(', ')}`] : [])].join(' · ');
  const item = (l: (typeof lines)[number], md: boolean): string => `${l.marker === null ? '' : `${l.marker} `}${md ? escapeMarkdown(l.body) : l.body}`;
  if (format === 'markdown') {
    const paragraphs = groups.map((g) => `${g[0]?.speaker == null ? '' : `**${escapeMarkdown(g[0].speaker)}:** `}${g.map((l) => item(l, true)).join(' ')}`);
    return { text: `# ${escapeMarkdown(summary.title)}${NL}${NL}${heading}${NL}${paragraphs.length > 0 ? NL + paragraphs.join(NL + NL) + NL : ''}`, lines: lines.length };
  }
  if (layout === 'lines') {
    const body = lines.map((l) => `${l.marker === null ? '' : `${l.marker} `}${l.speaker === null ? '' : `${l.speaker}: `}${l.body}${NL}`).join('');
    return { text: `${summary.title}${NL}${heading}${NL}${body}`, lines: lines.length };
  }
  const body = groups.map((g) => `${NL}${g[0]?.speaker == null ? '' : `${g[0].speaker}: `}${g.map((l) => item(l, false)).join(' ')}${NL}`).join('');
  return { text: `${summary.title}${NL}${heading}${NL}${body}`, lines: lines.length };
}

export function createMockClipboard(env: MockClipboardEnvironment): MockClipboard {
  let current: { text: string; html: string | null } | null = null;
  const write = (text: string, html: string | null, what: string): void => {
    if (env.busy()) {
      throw new MockHostError(
        'clipboard.unavailable',
        `Windows did not let Memento use the clipboard, so ${what} was not copied. Nothing was changed. Try again in a moment; if another program keeps the clipboard open, close it first.`,
        'Windows answered 0x800401D0.',
      );
    }
    current = { text, html };
  };
  return {
    current: () => current,
    handlers: {
      'transcript.copy': (params) => {
        const format: string = params.format;
        if (format !== 'text' && format !== 'markdown') {
          throw new MockHostError('bridge.invalidParams', `format: '${format}' cannot be copied. Choose text or markdown.`);
        }
        const options = { ...DEFAULT_TEXT_OPTIONS, ...(params.options ?? {}) };
        if (!LAYOUTS.includes(options.layout)) {
          throw new MockHostError('bridge.invalidParams', `Transcript layout '${options.layout}' is not available. Choose auto, turns, lines.`);
        }
        const ids = params.segmentIds ?? null;
        if (ids !== null && ids.length === 0) {
          throw new MockHostError('bridge.invalidParams', 'No lines were chosen, so there is nothing to copy. Show all lines, or choose a filter that shows some.');
        }
        const summary = env.summary(params.recordingId);
        const transcript = env.transcript(params.recordingId);
        if (transcript === null) {
          throw new MockHostError('transcript.none', 'This recording has no transcript yet, so there is nothing to copy. Nothing was changed. It appears when transcription finishes.', params.recordingId);
        }
        const missing = ids?.find((id) => !transcript.segments.some((s) => s.id === id));
        if (missing !== undefined) {
          throw new MockHostError('transcript.segmentNotFound', 'A line in the view is not in the transcript any more; it may have been transcribed again. Nothing was copied. Reopen the transcript and copy again.', missing);
        }
        const { text, lines } = mockTranscriptText(params.format, transcript, summary, options, ids === null ? null : new Set(ids));
        const total = ids === null ? lines : mockTranscriptText(params.format, transcript, summary, options).lines;
        write(text, null, 'the transcript');
        return { lines, totalLines: total, characters: text.length };
      },
      'documents.copy': (params) => {
        const doc = env.document(params.recordingId, params.documentId);
        write(doc.markdown, doc.html, `“${doc.title}”`);
        return { characters: doc.markdown.length, formatted: true };
      },
    },
  };
}
