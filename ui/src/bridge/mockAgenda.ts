// The browser-preview host's agenda import (BRIDGE.md M3): a loose copy of the local parser's rules
// (numbering, bullets, indentation, headings) that marks the lines it is unsure about and says why,
// a few sample "files" chosen by extension, and agenda.apply / discard / setCovered over the
// in-memory projects. Every name and item here is invented.
import { stripMarkers } from '../format/agenda';
import { isoWithOffset, type MockProject } from './mockData';
import { MockHostError } from './mockSession';
import {
  AGENDA_MAX_ITEM_LENGTH,
  AGENDA_MAX_ITEMS,
  type Agenda,
  type AgendaApplyParams,
  type AgendaImportResult,
  type AgendaParsedItem,
  type AgendaParsePreview,
  type AgendaSourceKind,
  type AgendaWarning,
  type Attachment,
  type Project,
} from './types';

/** `?agenda=ocrmissing`: photos fail as if the Windows OCR language were missing; `nodrop`: drops cannot be resolved. */
export type AgendaFlag = 'ok' | 'ocrMissing' | 'noDrop';

export const MERGED_REASON = 'A heading and its bullet may have been merged.';
export const LONG_REASON = 'This line is long and may hold more than one item.';
export const CONTINUES_REASON = 'This line seems to continue on the next one.';
export const DUPLICATE_REASON = 'The same item appears twice.';
export const OCR_REASON = 'Windows OCR was not sure of some words in this line.';

export interface ParsedText {
  title: string | null;
  items: AgendaParsedItem[];
  warnings: AgendaWarning[];
}

const indentOf = (line: string): number => {
  const leading = /^[\t ]*/.exec(line)?.[0] ?? '';
  return leading.replace(/\t/g, '    ').length;
};

/** Why the parser would be unsure of this line, or null. */
function doubtOf(text: string, seen: ReadonlySet<string>): string | null {
  // "Launch date Options: 2nd week" - a word with a capital and a colon inside the line.
  if (/\S\s+[A-Z][a-z]+:\s+\S/.test(text)) {
    return MERGED_REASON;
  }
  if (text.length > 120) {
    return LONG_REASON;
  }
  if (/(?:,|\band|\bor|-)$/.test(text)) {
    return CONTINUES_REASON;
  }
  if (seen.has(text.toLocaleLowerCase())) {
    return DUPLICATE_REASON;
  }
  return null;
}

/**
 * Plain text, Markdown or pasted text to items: one item per line, markers removed, deeper
 * indentation (or a lettered marker under a numbered one) as level 1, a first line that names the
 * agenda as the title.
 */
export function parseAgendaText(text: string, locationPrefix = ''): ParsedText {
  const lines = text.split(/\r\n|\r|\n/);
  const items: AgendaParsedItem[] = [];
  const warnings: AgendaWarning[] = [];
  const seen = new Set<string>();
  let title: string | null = null;
  let skipped = 0;
  let baseIndent: number | null = null;
  lines.forEach((raw, index) => {
    if (raw.trim() === '') {
      return;
    }
    const text = stripMarkers(raw).replace(/\s+/g, ' ');
    if (text === '') {
      skipped += 1;
      return;
    }
    if (title === null && items.length === 0 && /^agenda\b[:\s-]*/i.test(text)) {
      const rest = text.replace(/^agenda\b[:\s-]*/i, '').trim();
      title = rest === '' ? 'Agenda' : rest;
      return;
    }
    const indent = indentOf(raw);
    baseIndent ??= indent;
    const lettered = /^\s*\(?[a-z][.)]\s+/.test(raw);
    const level = indent > baseIndent + 1 || lettered ? 1 : 0;
    const reason = doubtOf(text, seen);
    seen.add(text.toLocaleLowerCase());
    items.push({
      text,
      uncertain: reason !== null,
      uncertainReason: reason,
      level,
      location: `${locationPrefix}line ${index + 1}`,
    });
  });
  if (skipped > 0) {
    warnings.push({
      code: 'markersOnly',
      message: `${skipped} ${skipped === 1 ? 'line held' : 'lines held'} only a number or a bullet and ${skipped === 1 ? 'was' : 'were'} skipped.`,
    });
  }
  if (items.length > AGENDA_MAX_ITEMS) {
    warnings.push({ code: 'tooManyItems', message: `${items.length} items were found; an agenda keeps up to ${AGENDA_MAX_ITEMS}.` });
  }
  return { title, items, warnings };
}

const item = (text: string, location: string | null, reason: string | null = null, level = 0): AgendaParsedItem => ({
  text,
  uncertain: reason !== null,
  uncertainReason: reason,
  level,
  location,
});

interface SampleFile {
  sourceKind: AgendaSourceKind;
  sizeBytes: number;
  contentType: string;
  parse(name: string): ParsedText & { ocrEngine?: string };
}

const SAMPLE_TEXT = [
  '# Agenda: Product sync',
  '1. Welcome and goals',
  '2. Customer feedback',
  '   - Onboarding survey',
  '   - Support themes',
  '3. Roadmap changes',
  '4. Any other business',
].join('\n');

const SAMPLES: Record<string, SampleFile> = {
  docx: {
    sourceKind: 'docx',
    sizeBytes: 38_912,
    contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    parse: () => ({
      title: 'Q3 planning sync',
      items: [
        item('Q2 recap', 'paragraph 2'),
        item('Hiring plan', 'paragraph 3'),
        item('Launch date Options: 2nd week of Nov, after beta', 'paragraph 4', MERGED_REASON),
        item('Budget asks', 'paragraph 6'),
        item('Open questions', 'paragraph 7'),
      ],
      warnings: [],
    }),
  },
  pdf: {
    sourceKind: 'pdf',
    sizeBytes: 211_456,
    contentType: 'application/pdf',
    parse: () => ({
      title: 'Board prep',
      items: [
        item('Minutes of the last meeting', 'page 1, line 4'),
        item('Finance update: Q3 forecast and', 'page 1, line 9', CONTINUES_REASON),
        item('Hiring plan for the new office', 'page 2, line 2'),
        item('Risks and mitigations', 'page 2, line 6'),
        item('Any other business', 'page 2, line 14'),
      ],
      warnings: [{ code: 'pdf.columns', message: 'Page 2 has two columns; the left column was read first.' }],
    }),
  },
  xlsx: {
    sourceKind: 'xlsx',
    sizeBytes: 15_360,
    contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    parse: () => ({
      title: null,
      items: [
        item('Welcome', 'Agenda!A2'),
        item('Sales pipeline review', 'Agenda!A3'),
        item('Marketing calendar', 'Agenda!A4'),
        item('Marketing calendar', 'Agenda!A5', DUPLICATE_REASON),
        item('Wrap-up', 'Agenda!A6'),
      ],
      warnings: [{ code: 'xlsx.sheets', message: 'Only the first sheet, "Agenda", was read.' }],
    }),
  },
  csv: { sourceKind: 'csv', sizeBytes: 1_024, contentType: 'text/csv', parse: () => parseAgendaText(SAMPLE_TEXT.replace(/^#.*\n/, '')) },
  tsv: { sourceKind: 'tsv', sizeBytes: 1_024, contentType: 'text/tab-separated-values', parse: () => parseAgendaText(SAMPLE_TEXT.replace(/^#.*\n/, '')) },
  md: { sourceKind: 'markdown', sizeBytes: 2_048, contentType: 'text/markdown', parse: () => parseAgendaText(SAMPLE_TEXT) },
  txt: { sourceKind: 'text', sizeBytes: 2_048, contentType: 'text/plain', parse: () => parseAgendaText(SAMPLE_TEXT) },
  image: {
    sourceKind: 'image',
    sizeBytes: 1_843_200,
    contentType: 'image/jpeg',
    parse: () => ({
      title: 'Team offsite',
      items: [
        item('Coffee and introductions', 'line 2'),
        item('Revew of actlon items', 'line 3', OCR_REASON),
        item('Planning the next quarter', 'line 4'),
        item('Lunch', 'line 5'),
      ],
      warnings: [{ code: 'ocr.lowContrast', message: 'Parts of the photo were faint; check the items against the paper.' }],
      ocrEngine: 'Windows OCR',
    }),
  },
};

const EXTENSIONS: Record<string, string> = {
  docx: 'docx',
  pdf: 'pdf',
  xlsx: 'xlsx',
  csv: 'csv',
  tsv: 'tsv',
  md: 'md',
  markdown: 'md',
  txt: 'txt',
  jpg: 'image',
  jpeg: 'image',
  png: 'image',
  heic: 'image',
  tif: 'image',
  tiff: 'image',
};

const PICKER_FILES = ['agenda.docx', 'board-agenda.pdf', 'offsite-agenda.jpg', 'sync-agenda.md'];

export interface PendingOriginal {
  name: string;
  sizeBytes: number;
  contentType: string;
}

export interface MockAgendaEnvironment {
  now(): number;
  find(recordingId: string): MockProject;
  toProject(project: MockProject): Project;
  changed(recordingId: string): void;
  /** Adds the original file as the recording's agenda attachment. */
  attach(recordingId: string, original: PendingOriginal, kind: Attachment['kind']): void;
  flag: AgendaFlag;
}

export interface MockAgenda {
  /** The host's picker: the preview offers a different sample each time. */
  importFile(): AgendaImportResult;
  importDropped(paths: readonly string[]): AgendaImportResult;
  parseText(text: string): AgendaParsePreview;
  apply(params: AgendaApplyParams): Project;
  discard(token: string): void;
  setCovered(recordingId: string, itemId: string, covered: boolean): { agenda: Agenda };
  /** Originals still waiting for apply or discard (tests). */
  pendingCount(): number;
}

function baseName(path: string): string {
  return path.split(/[\\/]/).pop() ?? path;
}

export function createMockAgenda(env: MockAgendaEnvironment): MockAgenda {
  const pending = new Map<string, PendingOriginal>();
  let tokenCounter = 0;
  let pickerIndex = 0;
  let itemCounter = 0;

  const fileError = (name: string): MockHostError | null => {
    if (/protected/i.test(name)) {
      return new MockHostError(
        'agenda.protected',
        `${name} is protected with a password, so Memento cannot read it. Nothing was changed. Remove the password in the app that made it, or paste the agenda as text.`,
        name,
      );
    }
    if (/empty|blank/i.test(name)) {
      return new MockHostError('agenda.noText', `${name} has no text Memento can read. Nothing was changed. Try the original document, or paste the agenda as text.`, name);
    }
    if (/huge|large/i.test(name)) {
      return new MockHostError('agenda.fileTooLarge', `${name} is larger than 50 MB, the most Memento reads for an agenda. Nothing was changed. Save the agenda on its own and try again.`, name);
    }
    return null;
  };

  const previewOf = (name: string): AgendaParsePreview => {
    const extension = (/\.([a-z0-9]+)$/i.exec(name)?.[1] ?? '').toLowerCase();
    const key = EXTENSIONS[extension];
    const sample = key === undefined ? undefined : SAMPLES[key];
    if (sample === undefined) {
      throw new MockHostError(
        'agenda.unsupportedFormat',
        `${name} is not a kind of file Memento can read an agenda from. Nothing was changed. Use Word, PDF, Excel, CSV, Markdown, plain text or a photo of the agenda.`,
        extension === '' ? name : `.${extension}`,
      );
    }
    const problem = fileError(name);
    if (problem !== null) {
      throw problem;
    }
    if (sample.sourceKind === 'image' && env.flag === 'ocrMissing') {
      throw new MockHostError(
        'agenda.ocrUnavailable',
        `${name} is a photo, and reading photos needs the English text recognition that Windows installs with the English language pack. Nothing was changed. Add it in Windows Settings › Time and language › Language and region, or paste the agenda as text.`,
        'ms-settings:regionlanguage',
      );
    }
    const parsed = sample.parse(name);
    const token = `agenda-original-${(++tokenCounter).toString(16)}`;
    pending.set(token, { name, sizeBytes: sample.sizeBytes, contentType: sample.contentType });
    return {
      source: name,
      sourceKind: sample.sourceKind,
      title: parsed.title,
      items: parsed.items,
      warnings: parsed.warnings,
      ocrEngine: parsed.ocrEngine ?? null,
      attachmentToken: token,
    };
  };

  return {
    importFile() {
      const name = PICKER_FILES[pickerIndex % PICKER_FILES.length] ?? 'agenda.docx';
      pickerIndex += 1;
      return { preview: previewOf(name), cancelled: false };
    },
    importDropped(paths) {
      if (env.flag === 'noDrop') {
        throw new MockHostError(
          'agenda.dropUnavailable',
          'Memento could not read the dropped file from here. Choose it in the file picker instead; nothing was changed.',
        );
      }
      const [first, ...rest] = paths;
      if (first === undefined) {
        throw new MockHostError('bridge.invalidParams', 'Nothing was dropped.');
      }
      const preview = previewOf(baseName(first));
      if (rest.length > 0) {
        preview.warnings = [
          { code: 'drop.several', message: `Only ${baseName(first)} was read; drop one agenda at a time.` },
          ...preview.warnings,
        ];
      }
      return { preview, cancelled: false };
    },
    parseText(text) {
      const parsed = parseAgendaText(text);
      if (parsed.items.length === 0) {
        throw new MockHostError(
          'agenda.noItems',
          'No agenda items were found in that text. Nothing was changed. Put one item on each line and try again.',
        );
      }
      return {
        source: 'Pasted text',
        sourceKind: 'pastedText',
        title: parsed.title,
        items: parsed.items,
        warnings: parsed.warnings,
        ocrEngine: null,
        attachmentToken: null,
      };
    },
    apply(params) {
      const project = env.find(params.recordingId);
      if (params.items.length > AGENDA_MAX_ITEMS) {
        throw new MockHostError(
          'agenda.tooManyItems',
          `This agenda has ${params.items.length} items; Memento keeps up to ${AGENDA_MAX_ITEMS}. Nothing was saved. Remove ${params.items.length - AGENDA_MAX_ITEMS} and apply it again.`,
          String(params.items.length),
        );
      }
      const long = params.items.findIndex((i) => i.text.length > AGENDA_MAX_ITEM_LENGTH);
      if (long >= 0) {
        throw new MockHostError(
          'agenda.itemTooLong',
          `Item ${long + 1} is ${params.items[long]?.text.length ?? 0} characters; agenda items can be up to ${AGENDA_MAX_ITEM_LENGTH}. Nothing was saved. Shorten it or split it in two.`,
          String(long + 1),
        );
      }
      const original = params.attachmentToken === null ? undefined : pending.get(params.attachmentToken);
      if (params.attachmentToken !== null) {
        pending.delete(params.attachmentToken);
      }
      project.details = {
        ...project.details,
        agenda: {
          source: params.source,
          parsedLocally: true,
          items: params.items
            .filter((i) => i.text.trim() !== '')
            .map((i) => ({
              id: `a${(++itemCounter).toString(16).padStart(8, '0')}`,
              text: i.text.trim(),
              covered: false,
              uncertain: i.uncertain,
              uncertainReason: i.uncertain ? i.uncertainReason : null,
            })),
        },
      };
      if (original !== undefined) {
        env.attach(project.summary.id, original, 'agenda');
      }
      project.history = [
        ...project.history,
        {
          at: isoWithOffset(new Date(env.now())),
          stage: 'edited',
          event: 'info',
          summary: 'Agenda imported',
          detail: `${project.details.agenda.items.length} items from ${params.source}, parsed on this PC`,
        },
      ];
      env.changed(project.summary.id);
      return env.toProject(project);
    },
    discard(token) {
      pending.delete(token);
    },
    setCovered(recordingId, itemId, covered) {
      const project = env.find(recordingId);
      const { agenda } = project.details;
      if (!agenda.items.some((i) => i.id === itemId)) {
        throw new MockHostError('bridge.invalidParams', 'That agenda item is not in this recording any more. Nothing was changed.', itemId);
      }
      project.details = { ...project.details, agenda: { ...agenda, items: agenda.items.map((i) => (i.id === itemId ? { ...i, covered } : i)) } };
      return { agenda: project.details.agenda };
    },
    pendingCount: () => pending.size,
  };
}
