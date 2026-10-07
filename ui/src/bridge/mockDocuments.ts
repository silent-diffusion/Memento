// The browser-preview host's documents (BRIDGE-M4.md): the documents saved inside each recording,
// rendered in the engine's viewer markup, light edits with versions, duplicate, delete, make
// template and single-document export. The sample "Design review" recording starts with the
// engine's own corporate viewer snapshot (tests/Memento.Documents.Tests/fixtures/expected) as its
// minutes; everything the preview generates is written here in that markup, from the recording's
// own details, chapters and highlights. Nothing is sent anywhere.
import viewerFixture from './fixtures/meeting-minutes.corporate.viewer.html?raw';
import { isoWithOffset, type MockProject } from './mockData';
import { articleOpen, chipHtml, chipTime, cssNumber, escapeHtml, extractArticle, metaLine, moduleOpen, restyle, titleBlock } from './mockPaper';
import { MockHostError } from './mockSession';
import { moduleInfo, type MockTemplateStore } from './mockTemplates';
import type {
  Block,
  DocumentContent,
  DocumentExportFormat,
  DocumentSummary,
  DocumentVersion,
  DocumentVersionReason,
  EventName,
  EventPayload,
  GenerationRecord,
  ModuleSettings,
  ProviderId,
  Run,
  SettingsSnapshot,
  StyleSettings,
  Template,
} from './types';

export const SAMPLE_RECORDING_ID = '20261005-160000-dsrev';

/** Tags the viewer markup may hold after light edits (HtmlToBlocks reads these back). */
export const ALLOWED_TAGS: ReadonlySet<string> = new Set([
  'article', 'header', 'section', 'div', 'p', 'span', 'a', 'strong', 'em', 'b', 'i', 'u', 'br', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
  'ul', 'ol', 'li', 'table', 'colgroup', 'col', 'thead', 'tbody', 'tr', 'th', 'td', 'dl', 'dt', 'dd', 'blockquote', 'footer', 'cite', 'sup', 'hr',
]);

/** The first tag or attribute the host would refuse with documents.unsupportedEdit, or null. */
export function unsupportedMarkup(html: string): string | null {
  for (const match of html.matchAll(/<\/?([a-zA-Z][a-zA-Z0-9-]*)([^>]*)>/g)) {
    const tag = (match[1] ?? '').toLowerCase();
    if (!ALLOWED_TAGS.has(tag)) {
      return `<${tag}>`;
    }
    const attributes = match[2] ?? '';
    const handler = /\son[a-z]+\s*=/i.exec(attributes);
    if (handler !== null) {
      return handler[0].trim().replace(/\s*=$/, '');
    }
    if (/javascript:/i.test(attributes)) {
      return 'a javascript: link';
    }
  }
  return null;
}

interface MockVersion {
  id: string;
  at: string;
  reason: DocumentVersionReason;
  changes: number;
  html: string;
}

interface MockDocument {
  summary: DocumentSummary;
  /** The current viewer article (its own style is applied again on every render). */
  html: string;
  record: GenerationRecord | null;
  /** The template it was generated from (Regenerate, Make template). */
  template: Template | null;
  /** Oldest first; the last is the current version. */
  versions: MockVersion[];
}

export interface TranscriptLine {
  t: number;
  speaker: string;
  text: string;
}

export interface MockDocumentsEnvironment {
  emit: <E extends EventName>(event: E, payload: EventPayload<E>) => void;
  now: () => number;
  find: (recordingId: string) => MockProject;
  settings: () => SettingsSnapshot;
  store: MockTemplateStore;
  /** The recording's transcript as speaker lines, [] when it has none. */
  transcript: (recordingId: string) => TranscriptLine[];
}

export interface WriteRequest {
  recordingId: string;
  template: Template;
  providerId: ProviderId;
  modelLabel: string;
  startedAt: string;
  durationMs: number;
  inputs: GenerationRecord['inputs'];
  chunks: number;
  /** Regenerate into this document. */
  documentId: string | null;
}

export interface MockDocuments {
  list(recordingId: string): DocumentSummary[];
  get(recordingId: string, documentId: string): { document: DocumentContent; summary: DocumentSummary };
  renderHtml(recordingId: string, documentId: string, mode: 'view' | 'print'): string;
  create(recordingId: string, name: string, styleId: string): DocumentSummary;
  saveEdit(recordingId: string, documentId: string, html: string): { document: DocumentContent; version: number };
  rename(recordingId: string, documentId: string, name: string): DocumentSummary;
  duplicate(recordingId: string, documentId: string): DocumentSummary;
  remove(recordingId: string, documentId: string): void;
  makeTemplate(recordingId: string, documentId: string, name: string): Template;
  versions(recordingId: string, documentId: string): DocumentVersion[];
  restoreVersion(recordingId: string, documentId: string, versionId: string): DocumentContent;
  exportOne(recordingId: string, documentId: string, format: DocumentExportFormat, path: string | undefined): { path: string; bytes: number; sha256: string };
  /** Writes a generated document (new, or a new version of `documentId`) and returns its id. */
  writeGenerated(request: WriteRequest): string;
  /** The documents a recording holds, for the Export dialog's Documents row. */
  exportable(recordingId: string): { id: string; name: string; sizeBytes: number }[];
}

const MINUTE = 60_000;

/** A stable fake SHA-256: the preview has no file bytes to hash. */
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

const plainRun = (text: string): Run => ({ kind: 'text', text });

/** A rough reading of the markup into blocks, as documents.get reports them (the UI never draws these). */
function contentOf(id: string, html: string, record: GenerationRecord | null): DocumentContent {
  const text = (fragment: string): string =>
    fragment
      .replace(/<a class="ts"[^>]*>[^<]*<\/a>/g, '')
      .replace(/<[^>]+>/g, '')
      .replace(/&quot;/g, '"')
      .replace(/&#39;/g, "'")
      .replace(/&lt;/g, '<')
      .replace(/&gt;/g, '>')
      .replace(/&amp;/g, '&')
      .trim();
  const title = text(/<h1 class="paper-title">([\s\S]*?)<\/h1>/.exec(html)?.[1] ?? '');
  const meta = text(/<p class="paper-meta"[^>]*>([\s\S]*?)<\/p>/.exec(html)?.[1] ?? '');
  const rows = [...html.matchAll(/<div class="paper-row"[^>]*>([\s\S]*?)<\/div>\n(?=<div class="paper-row"|<\/article>)/g)].map((row) => {
    const blocks: Block[] = [];
    for (const section of (row[1] ?? '').matchAll(/<section[^>]*>([\s\S]*?)<\/section>/g)) {
      const body = (section[1] ?? '').replace(/<h2 class="paper-h">[\s\S]*?<\/h2>/, '');
      const items = [...body.matchAll(/<li[^>]*>([\s\S]*?)<\/li>/g)].map((li) => ({ runs: [plainRun(text(li[1] ?? ''))], items: [] }));
      if (body.includes('class="chips"')) {
        blocks.push({ type: 'chips', items: [...body.matchAll(/<span class="chip">([^<]*)<\/span>/g)].map((m) => text(m[1] ?? '')) });
      } else if (items.length > 0) {
        blocks.push({ type: 'list', style: body.includes('<ol') ? 'numbered' : 'bulleted', items });
      } else if (text(body) !== '') {
        blocks.push({ type: 'paragraph', runs: [plainRun(text(body))] });
      }
    }
    return { blocks };
  });
  return { schemaVersion: 1, id, title, meta, rows, record };
}

interface WriteContext {
  project: MockProject;
  lines: TranscriptLine[];
  link: boolean;
}

const sentence = (text: string): string => {
  const trimmed = text.trim();
  if (trimmed === '') {
    return trimmed;
  }
  const first = trimmed.charAt(0).toUpperCase() + trimmed.slice(1);
  return /[.!?]$/.test(first) ? first : `${first}.`;
};

const NOT_DISCUSSED = '<p><span class="note">Not discussed in this recording.</span></p>\n';

/** The content of one module, written from what the recording holds (the preview's stand-in for the AI). */
function writeModule(module: ModuleSettings, ctx: WriteContext): string {
  const { project, lines } = ctx;
  const { details, highlights, chapters } = project;
  const ts = (seconds: number): string => (ctx.link ? chipHtml(seconds) : '');
  const named = highlights.filter((h) => h.note.trim() !== '');
  const firstName = (full: string): string => full.split(' ')[0] ?? full;
  switch (module.module) {
    case 'title':
      return `<p>${escapeHtml(details.title)}</p>\n`;
    case 'participants':
      return details.participants.length === 0
        ? '<p><span class="note">Nobody is listed in the recording details.</span></p>\n'
        : `<div class="chips" contenteditable="false">${details.participants.map((p) => `<span class="chip">${escapeHtml(p)}</span>`).join('')}</div>\n`;
    case 'meetingPurpose':
      return details.purpose.trim() === '' ? NOT_DISCUSSED : `<p>${escapeHtml(sentence(details.purpose))}</p>\n`;
    case 'agenda': {
      const items = details.agenda.items;
      if (items.length === 0) {
        return '<p><span class="note">No agenda was imported for this recording.</span></p>\n';
      }
      return `<ul>${items
        .map((item) => `<li>${escapeHtml(item.text)}${item.covered ? '' : ' <span class="note">(not reached)</span>'}</li>`)
        .join('')}</ul>\n`;
    }
    case 'decisions':
    case 'notes': {
      if (named.length === 0) {
        return NOT_DISCUSSED;
      }
      return `<ul>${named.map((h) => `<li>${escapeHtml(sentence(h.note))}${ts(h.atMs / 1000)}</li>`).join('')}</ul>\n`;
    }
    case 'actionItems':
    case 'deadline': {
      const owned = named
        .map((h) => ({ h, match: /^(\w+) owns (.+)$/i.exec(h.note.trim()) }))
        .filter((x) => x.match !== null);
      if (owned.length === 0) {
        return '<p><span class="note">No commitments were made in this recording.</span></p>\n';
      }
      if (module.module === 'deadline') {
        return `<table class="paper-table" data-widths="1,3"><colgroup><col style="width:25%"><col style="width:75%"></colgroup><thead><tr><th>Due</th><th>Action</th></tr></thead><tbody>${owned
          .map(({ h, match }) => `<tr><td><span class="note">No date was given</span></td><td>${escapeHtml(sentence(match?.[2] ?? h.note))}${ts(h.atMs / 1000)}</td></tr>`)
          .join('')}</tbody></table>\n`;
      }
      return `<table class="paper-table" data-widths="2,1,1"><colgroup><col style="width:50%"><col style="width:25%"><col style="width:25%"></colgroup><thead><tr><th>Action</th><th>Owner</th><th>Due</th></tr></thead><tbody>${owned
        .map(
          ({ h, match }) =>
            `<tr><td>${escapeHtml(sentence(`Take on ${match?.[2] ?? ''}`))}${ts(h.atMs / 1000)}</td><td>${escapeHtml(match?.[1] ?? '')}</td><td><span class="note">No date was given</span></td></tr>`,
        )
        .join('')}</tbody></table>\n`;
    }
    case 'owner': {
      const owners = named.map((h) => /^(\w+) owns (.+)$/i.exec(h.note.trim())).filter((m): m is RegExpExecArray => m !== null);
      return owners.length === 0
        ? '<p><span class="note">No owner was named in this recording.</span></p>\n'
        : `<dl class="kv">${owners.map((m) => `<dt>${escapeHtml(m[1] ?? '')}</dt><dd>${escapeHtml(sentence(m[2] ?? ''))}</dd>`).join('')}</dl>\n`;
    }
    case 'openQuestions': {
      const asked = lines.filter((l) => l.text.trim().endsWith('?')).slice(0, 3);
      return asked.length === 0
        ? '<p><span class="note">No questions were left open.</span></p>\n'
        : `<ul>${asked.map((l) => `<li>${escapeHtml(l.text.trim())}${ts(l.t)}</li>`).join('')}</ul>\n`;
    }
    case 'nextMeeting':
      return '<dl class="kv"><dt>When</dt><dd><span class="note">Not discussed</span></dd><dt>Agenda</dt><dd><span class="note">Not discussed</span></dd></dl>\n';
    case 'quote':
    case 'highlight': {
      const pick = module.module === 'quote' ? lines.filter((l) => l.text.length > 40).slice(0, 1) : [];
      if (module.module === 'quote' && pick.length > 0) {
        return pick
          .map(
            (l) =>
              `<blockquote class="paper-quote" data-t="${cssNumber(l.t)}"><p>${escapeHtml(l.text)}</p><footer contenteditable="false">— <cite>${escapeHtml(l.speaker)}</cite> · <a class="q-t" href="#t=${cssNumber(l.t)}" data-t="${cssNumber(l.t)}" contenteditable="false">${chipTime(l.t)}</a></footer></blockquote>\n`,
          )
          .join('');
      }
      if (highlights.length === 0) {
        return NOT_DISCUSSED;
      }
      return highlights
        .map((h) => {
          const t = h.atMs / 1000;
          return `<blockquote class="paper-quote" data-t="${cssNumber(t)}"><p>${escapeHtml(h.note.trim() === '' ? 'Marked without a note.' : sentence(h.note))}</p><footer contenteditable="false"><a class="q-t" href="#t=${cssNumber(t)}" data-t="${cssNumber(t)}" contenteditable="false">${chipTime(t)}</a></footer></blockquote>\n`;
        })
        .join('');
    }
    case 'chapter':
    case 'timeline': {
      const entries = module.module === 'chapter' ? chapters.map((c) => ({ t: c.atMs / 1000, text: c.title })) : highlights.map((h) => ({ t: h.atMs / 1000, text: sentence(h.note) }));
      if (entries.length === 0) {
        return NOT_DISCUSSED;
      }
      return `<ol class="timeline">${entries
        .map((e) => `<li data-t="${cssNumber(e.t)}"><a class="tl-t" href="#t=${cssNumber(e.t)}" data-t="${cssNumber(e.t)}" contenteditable="false">${chipTime(e.t)}</a><span class="tl-x">${escapeHtml(e.text)}</span></li>`)
        .join('')}</ol>\n`;
    }
    case 'fullTranscript': {
      if (lines.length === 0) {
        return '<p><span class="note">This recording has no transcript yet.</span></p>\n';
      }
      return `<table class="paper-transcript" contenteditable="false"><tbody>${lines
        .map((l, i) => `<tr data-t="${cssNumber(l.t)}" data-seg="s${String(i + 1).padStart(4, '0')}"><td class="tr-t">${chipTime(l.t)}</td><td class="tr-sp">${escapeHtml(l.speaker)}</td><td class="tr-x">${escapeHtml(l.text)}</td></tr>`)
        .join('')}</tbody></table>\n`;
    }
    case 'customText':
      return module.customText === null || module.customText.trim() === ''
        ? '<p><br></p>\n'
        : `${module.customText
            .split(/\n{2,}/)
            .map((para) => `<p>${escapeHtml(para.trim()).replace(/\n/g, '<br>')}</p>`)
            .join('\n')}\n`;
    case 'followUpEmail': {
      const people = details.participants.map(firstName);
      const greeting = people.length === 0 ? 'Hello,' : `Hello ${people.join(', ')},`;
      const agreed = named.slice(0, 2).map((h) => `${sentence(h.note)}${ts(h.atMs / 1000)}`);
      return `<p>${escapeHtml(greeting)}</p>\n<p>${escapeHtml(`Thank you for joining "${details.title}".`)} ${agreed.length === 0 ? 'Nothing was agreed that needs following up.' : `What we agreed: ${agreed.join(' ')}`}</p>\n`;
    }
    case 'summary':
    case 'executiveSummary':
    case 'discussion':
    case 'topic':
    case 'customAi': {
      if (lines.length === 0 && named.length === 0) {
        return NOT_DISCUSSED;
      }
      const opening = chapters.length > 1 ? `The recording moves through ${chapters.length} parts, from ${chapters[0]?.title.toLowerCase() ?? 'the opening'} to ${chapters[chapters.length - 1]?.title.toLowerCase() ?? 'the close'}.` : `The recording runs ${Math.max(1, Math.round(project.summary.durationMs / MINUTE))} minutes.`;
      const points = named.map((h) => `${sentence(h.note)}${ts(h.atMs / 1000)}`).slice(0, module.module === 'executiveSummary' ? 2 : 4);
      const said = lines.slice(0, module.module === 'discussion' ? 2 : 1).map((l) => `${escapeHtml(firstName(l.speaker))} said: \u201c${escapeHtml(l.text)}\u201d${ts(l.t)}`);
      return `<p>${escapeHtml(opening)} ${[...points, ...said].join(' ')}</p>\n`;
    }
  }
}

export function createMockDocuments(env: MockDocumentsEnvironment): MockDocuments {
  const byRecording = new Map<string, MockDocument[]>();
  let counter = 0;
  const nextId = (prefix: string): string => `${prefix}-${env.now().toString(36)}${(++counter).toString(36)}`;
  const nowIso = (): string => isoWithOffset(new Date(env.now()));
  const historyOn = (): boolean => env.settings().history.keepVersions;

  const styleSettings = (styleId: string): StyleSettings => {
    try {
      return env.store.style(styleId).settings;
    } catch {
      return env.store.style('corporate').settings;
    }
  };

  const documentsOf = (recordingId: string): MockDocument[] => {
    const project = env.find(recordingId);
    let list = byRecording.get(recordingId);
    if (list === undefined) {
      list = recordingId === SAMPLE_RECORDING_ID ? seed(project) : [];
      byRecording.set(recordingId, list);
    }
    return list;
  };

  const findDoc = (recordingId: string, documentId: string): MockDocument => {
    const doc = documentsOf(recordingId).find((d) => d.summary.id === documentId);
    if (doc === undefined) {
      throw new MockHostError(
        'documents.notFound',
        'That document is not in this recording any more; it may have been deleted. Nothing was changed. Go back to the recording to see its documents.',
        documentId,
      );
    }
    return doc;
  };

  const meta = (project: MockProject, kind: string): string =>
    metaLine({
      kind,
      recordedAt: project.summary.createdAt,
      durationMs: project.summary.durationMs,
      platform: project.details.platform,
      participantCount: project.details.platform.trim() === '' ? project.details.participants.length : 0,
    });

  const summarise = (doc: MockDocument): DocumentSummary => ({
    ...doc.summary,
    version: doc.versions.length,
    versions: doc.versions.length,
    sizeBytes: doc.html.length,
  });

  const render = (project: MockProject, template: Template, documentId: string, styleId: string): string => {
    const lines = env.transcript(project.summary.id);
    const body = template.rows
      .filter((row) => row.modules.length > 0)
      .map(
        (row) =>
          `<div class="paper-row" data-cols="${row.modules.length}">\n${row.modules
            .map(
              (m) =>
                moduleOpen(m.id, m.module, m.textSize, m.linkToTranscript, m.customTitle ?? moduleInfo(m.module).name) +
                writeModule(m, { project, lines, link: m.linkToTranscript }) +
                '</section>\n',
            )
            .join('')}</div>\n`,
      )
      .join('');
    return articleOpen(styleSettings(styleId), styleId, 'viewer', documentId) + titleBlock(project.details.title, meta(project, template.name), 'viewer') + body + '</article>\n';
  };

  function seed(project: MockProject): MockDocument[] {
    const created = Date.parse(project.summary.createdAt);
    const at = (minutesAfter: number): string => isoWithOffset(new Date(created + minutesAfter * MINUTE));
    const minutesTemplate = env.store.template('meeting-minutes');
    const generatedAt = at(74);
    const v1 = extractArticle(viewerFixture);
    // The edit that made version 2: a clarified agenda note.
    const v2 = v1.replace('(reached with 8 minutes left)', '(reached with 8 minutes left; owners agreed)');
    const inputs = { ...minutesTemplate.inputs };
    const record = (templateId: string, templateName: string, styleId: string, startedAt: string, durationMs: number): GenerationRecord => ({
      templateId,
      templateName,
      styleId,
      providerId: 'anthropic',
      modelLabel: 'Claude Sonnet 4.5',
      startedAt,
      durationMs,
      inputs,
      payloadHash: fakeHash(`${templateId}${startedAt}`),
      payloadKept: true,
      chunks: 1,
      modules: [],
    });
    const minutes: MockDocument = {
      summary: {
        id: 'doc-20261005-minutes',
        name: 'Meeting minutes',
        kind: 'generated',
        templateName: 'Meeting minutes',
        styleId: 'corporate',
        providerId: 'anthropic',
        generatedAt,
        version: 2,
        versions: 2,
        modifiedAt: at(91),
        sizeBytes: v2.length,
      },
      html: v2,
      record: record('meeting-minutes', 'Meeting minutes', 'corporate', generatedAt, 38_000),
      template: minutesTemplate,
      versions: [
        { id: 'v1', at: generatedAt, reason: 'generated', changes: 0, html: v1 },
        { id: 'v2', at: at(91), reason: 'edited', changes: 3, html: v2 },
      ],
    };
    const actionTemplate: Template = {
      ...minutesTemplate,
      id: 'meeting-minutes',
      name: 'Action items',
      rows: minutesTemplate.rows.flatMap((r) => r.modules).filter((m) => m.module === 'actionItems').map((m) => ({ modules: [{ ...m, id: 'm01' }] })),
      styleId: 'minimal',
    };
    const actionsHtml = render(project, actionTemplate, 'doc-20261005-actions', 'minimal');
    const actionsAt = at(76);
    const actions: MockDocument = {
      summary: {
        id: 'doc-20261005-actions',
        name: 'Action items',
        kind: 'generated',
        templateName: 'Action items',
        styleId: 'minimal',
        providerId: 'anthropic',
        generatedAt: actionsAt,
        version: 1,
        versions: 1,
        modifiedAt: actionsAt,
        sizeBytes: actionsHtml.length,
      },
      html: actionsHtml,
      record: record('meeting-minutes', 'Action items', 'minimal', actionsAt, 21_000),
      template: actionTemplate,
      versions: [{ id: 'v1', at: actionsAt, reason: 'generated', changes: 0, html: actionsHtml }],
    };
    const notesAt = at(40);
    const notesHtml =
      articleOpen(styleSettings('minimal'), 'minimal', 'viewer', 'doc-20261005-notes') +
      titleBlock('My notes', meta(project, 'Notes'), 'viewer') +
      '<div class="paper-row" data-cols="1">\n' +
      moduleOpen('m01', 'customText', 'normal', false, 'Notes') +
      '<p>Ask Lena for the icon set before Wednesday.</p>\n<p>Check the 68 px rows on the small laptop screen.</p>\n</section>\n</div>\n</article>\n';
    const notes: MockDocument = {
      summary: {
        id: 'doc-20261005-notes',
        name: 'My notes',
        kind: 'written',
        templateName: null,
        styleId: 'minimal',
        providerId: null,
        generatedAt: null,
        version: 1,
        versions: 1,
        modifiedAt: notesAt,
        sizeBytes: notesHtml.length,
      },
      html: notesHtml,
      record: null,
      template: null,
      versions: [{ id: 'v1', at: notesAt, reason: 'edited', changes: 0, html: notesHtml }],
    };
    return [minutes, actions, notes];
  }

  const changed = (recordingId: string, documentId: string, reason: EventPayload<'documents.changed'>['reason']): void => {
    env.emit('documents.changed', { recordingId, documentId, reason });
  };

  const addVersion = (doc: MockDocument, reason: DocumentVersionReason, html: string): void => {
    const at = nowIso();
    const last = doc.versions[doc.versions.length - 1];
    if (!historyOn()) {
      // Version history off: one version, replaced.
      doc.versions = [{ id: last?.id ?? 'v1', at, reason, changes: reason === 'edited' ? (last?.changes ?? 0) + 1 : 0, html }];
      return;
    }
    // Edits a few minutes apart belong to one version, as typing does.
    if (reason === 'edited' && last?.reason === 'edited' && env.now() - Date.parse(last.at) < 10 * MINUTE) {
      doc.versions[doc.versions.length - 1] = { ...last, at, changes: last.changes + 1, html };
      return;
    }
    doc.versions.push({ id: `v${doc.versions.length + 1}`, at, reason, changes: reason === 'edited' ? 1 : 0, html });
  };

  return {
    list: (recordingId) => documentsOf(recordingId).map(summarise),
    get(recordingId, documentId) {
      const doc = findDoc(recordingId, documentId);
      return { document: contentOf(doc.summary.id, doc.html, doc.record), summary: summarise(doc) };
    },
    renderHtml(recordingId, documentId, mode) {
      const doc = findDoc(recordingId, documentId);
      const styleId = doc.summary.styleId;
      const article = restyle(doc.html, styleSettings(styleId), styleId, 'viewer');
      if (mode === 'view') {
        return article;
      }
      // The print page (PDF): the same article in a page of its own.
      const print = article.replace('paper paper-viewer', 'paper paper-print');
      return `<!doctype html>\n<html lang="en">\n<head>\n<meta charset="utf-8">\n<title>${escapeHtml(doc.summary.name)}</title>\n</head>\n<body>\n${print}</body>\n</html>\n`;
    },
    create(recordingId, name, styleId) {
      const project = env.find(recordingId);
      const trimmed = name.trim();
      if (trimmed === '' || trimmed.length > 120) {
        throw new MockHostError('bridge.invalidParams', 'A document needs a name of 1 to 120 characters. Nothing was created.', name);
      }
      env.store.style(styleId);
      const id = nextId('doc');
      const html =
        articleOpen(styleSettings(styleId), styleId, 'viewer', id) +
        titleBlock(trimmed, meta(project, 'Notes'), 'viewer') +
        '<div class="paper-row" data-cols="1">\n' +
        moduleOpen('m01', 'customText', 'normal', false, 'Notes') +
        '<p><br></p>\n</section>\n</div>\n</article>\n';
      const at = nowIso();
      const doc: MockDocument = {
        summary: { id, name: trimmed, kind: 'written', templateName: null, styleId, providerId: null, generatedAt: null, version: 1, versions: 1, modifiedAt: at, sizeBytes: html.length },
        html,
        record: null,
        template: null,
        versions: [{ id: 'v1', at, reason: 'edited', changes: 0, html }],
      };
      documentsOf(recordingId).push(doc);
      changed(recordingId, id, 'created');
      return summarise(doc);
    },
    saveEdit(recordingId, documentId, html) {
      const doc = findDoc(recordingId, documentId);
      const refused = unsupportedMarkup(html);
      if (refused !== null) {
        throw new MockHostError(
          'documents.unsupportedEdit',
          `The last edit was not saved: a document cannot hold ${refused}. The version saved before it is unchanged. Undo the last change (Ctrl+Z), then keep editing.`,
          refused,
        );
      }
      if (!/<article[^>]*class="paper[\s"]/.test(html)) {
        throw new MockHostError('documents.unsupportedEdit', 'The last edit was not saved: the page is not a document paper. The version saved before it is unchanged.', null);
      }
      const article = extractArticle(html);
      addVersion(doc, 'edited', article);
      doc.html = article;
      doc.summary = { ...doc.summary, modifiedAt: nowIso() };
      changed(recordingId, documentId, 'edited');
      return { document: contentOf(doc.summary.id, doc.html, doc.record), version: doc.versions.length };
    },
    rename(recordingId, documentId, name) {
      const doc = findDoc(recordingId, documentId);
      const trimmed = name.trim();
      if (trimmed === '' || trimmed.length > 120) {
        throw new MockHostError('bridge.invalidParams', 'A document needs a name of 1 to 120 characters. The old name was kept.', name);
      }
      doc.summary = { ...doc.summary, name: trimmed, modifiedAt: nowIso() };
      changed(recordingId, documentId, 'edited');
      return summarise(doc);
    },
    duplicate(recordingId, documentId) {
      const doc = findDoc(recordingId, documentId);
      const id = nextId('doc');
      const names = documentsOf(recordingId).map((d) => d.summary.name);
      let name = `${doc.summary.name} (copy)`;
      for (let n = 2; names.includes(name); n++) {
        name = `${doc.summary.name} (copy ${n})`;
      }
      const html = doc.html.replace(/data-doc="[^"]*"/, `data-doc="${id}"`);
      const at = nowIso();
      const copy: MockDocument = {
        ...structuredClone(doc),
        summary: { ...doc.summary, id, name, modifiedAt: at },
        html,
        versions: [{ id: 'v1', at, reason: doc.summary.kind === 'generated' ? 'generated' : 'edited', changes: 0, html }],
      };
      documentsOf(recordingId).push(copy);
      changed(recordingId, id, 'created');
      return summarise(copy);
    },
    remove(recordingId, documentId) {
      const list = documentsOf(recordingId);
      const doc = findDoc(recordingId, documentId);
      list.splice(list.indexOf(doc), 1);
      changed(recordingId, documentId, 'deleted');
    },
    makeTemplate(recordingId, documentId, name) {
      const doc = findDoc(recordingId, documentId);
      if (doc.template === null) {
        throw new MockHostError(
          'bridge.invalidParams',
          `${doc.summary.name} was written by hand, so there is no structure to keep as a template. Nothing was saved.`,
          documentId,
        );
      }
      return env.store.saveTemplate({ ...doc.template, id: '', builtIn: false, name, styleId: doc.summary.styleId });
    },
    versions(recordingId, documentId) {
      const doc = findDoc(recordingId, documentId);
      return [...doc.versions].reverse().map(({ id, at, reason, changes }) => ({ id, at, reason, changes }));
    },
    restoreVersion(recordingId, documentId, versionId) {
      const doc = findDoc(recordingId, documentId);
      const version = doc.versions.find((v) => v.id === versionId);
      if (version === undefined) {
        throw new MockHostError(
          'documents.versionNotFound',
          'That version is not kept any more; versions older than the history setting are removed. Nothing was changed.',
          versionId,
        );
      }
      doc.html = version.html;
      addVersion(doc, 'restored', version.html);
      doc.summary = { ...doc.summary, modifiedAt: nowIso() };
      changed(recordingId, documentId, 'restored');
      return contentOf(doc.summary.id, doc.html, doc.record);
    },
    exportOne(recordingId, documentId, format, path) {
      const doc = findDoc(recordingId, documentId);
      const extension = format === 'markdown' ? 'md' : format;
      const target = path ?? `${env.settings().export.defaultFolder ?? 'D:\\Exports'}\\${doc.summary.name}.${extension}`;
      const bytes = format === 'markdown' ? Math.round(doc.html.length / 3) : doc.html.length + (format === 'pdf' ? 60_000 : 9_000);
      return { path: target, bytes, sha256: fakeHash(`${target}${doc.html}`) };
    },
    writeGenerated(request) {
      const project = env.find(request.recordingId);
      const list = documentsOf(request.recordingId);
      const into = request.documentId === null ? undefined : list.find((d) => d.summary.id === request.documentId);
      const id = into?.summary.id ?? nextId('doc');
      const styleId = request.template.styleId;
      const html = render(project, request.template, id, styleId);
      const record: GenerationRecord = {
        templateId: request.template.id,
        templateName: request.template.name,
        styleId,
        providerId: request.providerId,
        modelLabel: request.modelLabel,
        startedAt: request.startedAt,
        durationMs: request.durationMs,
        inputs: request.inputs,
        payloadHash: fakeHash(`${id}${request.startedAt}`),
        payloadKept: env.settings().ai.keepRecord,
        chunks: request.chunks,
        modules: request.template.rows.flatMap((r) =>
          r.modules.filter((m) => moduleInfo(m.module).generated).map((m) => ({ moduleId: m.id, claims: 3, verified: 3, dropped: 0, notDiscussed: false })),
        ),
      };
      const at = nowIso();
      if (into !== undefined) {
        into.html = html;
        into.record = record;
        into.template = structuredClone(request.template);
        into.summary = { ...into.summary, styleId, providerId: request.providerId, templateName: request.template.name, generatedAt: at, modifiedAt: at, kind: 'generated' };
        addVersion(into, 'regenerated', html);
        changed(request.recordingId, id, 'generated');
        return id;
      }
      const names = list.map((d) => d.summary.name);
      let name = request.template.name;
      for (let n = 2; names.includes(name); n++) {
        name = `${request.template.name} ${n}`;
      }
      list.push({
        summary: { id, name, kind: 'generated', templateName: request.template.name, styleId, providerId: request.providerId, generatedAt: at, version: 1, versions: 1, modifiedAt: at, sizeBytes: html.length },
        html,
        record,
        template: structuredClone(request.template),
        versions: [{ id: 'v1', at, reason: 'generated', changes: 0, html }],
      });
      changed(request.recordingId, id, 'generated');
      return id;
    },
    exportable: (recordingId) => {
      try {
        return documentsOf(recordingId).map((d) => ({ id: d.summary.id, name: d.summary.name, sizeBytes: d.html.length }));
      } catch {
        return [];
      }
    },
  };
}
