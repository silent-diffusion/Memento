// The browser-preview host's module catalog, templates and styles (BRIDGE.md, M4): the 23 modules
// of src/Memento.Documents/Model/Modules/ModuleCatalog.cs, the four built-in templates and three
// built-in styles copied from the JSON resources in src/Memento.Documents/Templates and Styles,
// CRUD with built-in protection, and styles.sampleHtml built from the host's own sample page.
import sampleCorporate from './fixtures/style-sample.corporate.html?raw';
import { articleOpen, extractArticle } from './mockPaper';
import { MockHostError } from './mockSession';
import type {
  ContentShape,
  EventName,
  EventPayload,
  InputSelection,
  ModuleGroup,
  ModuleId,
  ModuleInfo,
  ModuleLength,
  ModuleSettings,
  Style,
  StyleSettings,
  Template,
  TemplateRow,
  TextSize,
} from './types';

const R = {
  claim: 'Each statement that reports a decision, action, quote or conclusion carries a timestamp that cites a real transcript moment.',
  commitment: 'Action items list only commitments actually made in the recording, each with an in-transcript source.',
  owner: 'An owner is given only if one was stated; otherwise the item says no owner was named.',
  deadline: 'A deadline is given only if one was stated; otherwise the item says no date was given.',
  participants: 'Participants come from the recording details and the identified speakers, never from inference.',
  agenda: 'Agenda items that were not reached or not discussed are reported as such instead of inventing coverage.',
  decision: 'A decision is listed only where the moment of agreement can be cited in the transcript.',
  quote: 'Quotes reproduce the transcript’s words exactly, with the speaker and the time.',
  questions: 'Open questions are questions raised in the recording and not answered in it.',
  times: 'Every time shown is a real transcript time.',
  stated: 'The content comes from the recording details or was stated in the recording; otherwise it says not discussed.',
  transcript: 'The transcript as stored, with speakers and times, placed without AI.',
  annotations: 'Chapters, highlights and notes as they are in the project, with their times, placed without AI.',
  details: 'Taken from the recording details as entered, without AI.',
  user: 'The text the user wrote, kept unchanged; no AI is involved.',
} as const;

type CatalogRow = [ModuleId, string, ModuleGroup, ContentShape, boolean, ModuleLength, boolean, string, string];

// id, name, group, shape, generated (AI), default length, link to transcript by default, grounding rule, description
const CATALOG: readonly CatalogRow[] = [
  ['title', 'Title', 'structure', 'text', false, 'short', false, R.details, 'The recording’s title as entered in its details.'],
  ['summary', 'Summary', 'structure', 'paragraph', true, 'medium', true, R.claim, 'A neutral summary of what was said, in the order it was said.'],
  ['executiveSummary', 'Executive summary', 'structure', 'paragraph', true, 'short', true, R.claim, 'Three sentences. Decisions first, then risks.'],
  ['meetingPurpose', 'Meeting purpose', 'structure', 'labelValue', true, 'short', false, R.stated, 'One line, taken from the recording details.'],
  ['participants', 'Participants', 'structure', 'chips', false, 'short', false, R.participants, 'Names from the recording details and the identified speakers.'],
  ['agenda', 'Agenda', 'structure', 'list', true, 'medium', true, R.agenda, 'As imported, with a note on items not reached.'],
  ['discussion', 'Discussion summary', 'structure', 'paragraph', true, 'long', true, R.claim, 'One short paragraph per agenda item, neutral tone.'],
  ['decisions', 'Decisions', 'structure', 'list', true, 'medium', true, R.decision, 'Bulleted. Quote the moment of agreement with a timestamp.'],
  ['actionItems', 'Action items', 'structure', 'table', true, 'medium', true, R.commitment, 'Table: action, owner, deadline. Only commitments actually made.'],
  ['openQuestions', 'Open questions', 'structure', 'list', true, 'short', false, R.questions, 'Questions raised but not answered.'],
  ['nextMeeting', 'Next meeting', 'structure', 'labelValue', true, 'short', false, R.stated, 'Date, time and proposed agenda if mentioned.'],
  ['topic', 'Topic', 'detail', 'paragraph', true, 'medium', true, R.claim, 'What was said about one topic, with who said it.'],
  ['owner', 'Owner', 'detail', 'labelValue', true, 'short', true, R.owner, 'Each named owner and what they committed to.'],
  ['deadline', 'Deadline', 'detail', 'table', true, 'short', true, R.deadline, 'Stated deadlines in date order.'],
  ['quote', 'Quote', 'detail', 'quote', true, 'short', true, R.quote, 'One remark that captures the meeting, word for word.'],
  ['highlight', 'Highlight', 'detail', 'quote', false, 'short', true, R.annotations, 'The highlights marked in the recording, with their notes.'],
  ['chapter', 'Chapter', 'detail', 'timeline', false, 'medium', true, R.annotations, 'The recording’s chapters with their start times.'],
  ['timeline', 'Timeline', 'detail', 'timeline', true, 'medium', true, R.times, 'The key moments in time order.'],
  ['followUpEmail', 'Follow-up email', 'detail', 'paragraph', true, 'medium', false, R.commitment, 'A short email to the participants: decisions, actions and the next meeting.'],
  ['notes', 'Notes', 'detail', 'list', false, 'medium', true, R.annotations, 'The notes written during and after the recording.'],
  ['fullTranscript', 'Full transcript', 'detail', 'transcript', false, 'long', false, R.transcript, 'The entire transcript with speakers and timestamps. No AI is involved.'],
  ['customText', 'Custom text', 'custom', 'text', false, 'medium', false, R.user, 'Your own text, placed as written.'],
  ['customAi', 'Custom AI section', 'custom', 'paragraph', true, 'medium', true, R.claim, 'Describe what this section should contain.'],
];

export const MODULE_CATALOG: readonly ModuleInfo[] = CATALOG.map(([id, name, group, shape, generated, defaultLength, , groundingRule, description]) => ({
  id,
  name,
  group,
  shape,
  generated,
  defaultLength,
  groundingRule,
  description,
}));

const DEFAULT_LINK = new Map<ModuleId, boolean>(CATALOG.map((row) => [row[0], row[6]]));

export function moduleInfo(id: ModuleId): ModuleInfo {
  const info = MODULE_CATALOG.find((m) => m.id === id);
  if (info === undefined) {
    throw new MockHostError('bridge.invalidParams', `The document module "${id}" is not in the catalog. Nothing was changed.`, id);
  }
  return info;
}

/** A module as the palette adds it: the catalog's defaults. */
export function newModuleSettings(id: string, module: ModuleId): ModuleSettings {
  const info = moduleInfo(module);
  return {
    id,
    module,
    instructions: info.generated ? info.description : '',
    length: info.defaultLength,
    textSize: 'normal',
    linkToTranscript: DEFAULT_LINK.get(module) ?? false,
    customTitle: null,
    customText: module === 'customText' ? '' : null,
  };
}

type TemplateModuleRow = [string, ModuleId, string, string, ModuleLength, TextSize, boolean];

function rows(...list: TemplateModuleRow[][]): TemplateRow[] {
  return list.map((row) => ({
    modules: row.map(([id, module, title, instructions, length, textSize, linkToTranscript]) => ({
      id,
      module,
      instructions,
      length,
      textSize,
      linkToTranscript,
      customTitle: title === moduleInfo(module).name ? null : title,
      customText: module === 'customText' ? '' : null,
    })),
  }));
}

const inputs = (i: Partial<InputSelection>): InputSelection => ({
  transcript: true,
  details: false,
  participants: false,
  agenda: false,
  highlights: false,
  attachments: false,
  previousDocuments: false,
  ...i,
});


/** src/Memento.Documents/Templates/BuiltIn/*.json in the bridge's shape (importedDocuments → attachments). */
export function builtInTemplates(): Template[] {
  const output = { alsoExportDocx: false, alsoExportMarkdown: false };
  return [
    {
      id: 'meeting-minutes',
      name: 'Meeting minutes',
      builtIn: true,
      recordingTypes: ['meeting'],
      rows: rows(
        [['m01', 'executiveSummary', 'Executive summary', 'Three sentences. Decisions first, then risks.', 'short', 'larger', true]],
        [
          ['m02', 'meetingPurpose', 'Meeting purpose', 'One line, taken from the recording details.', 'short', 'normal', false],
          ['m03', 'participants', 'Participants', 'Names and roles. Mark who was absent.', 'short', 'normal', false],
        ],
        [['m04', 'agenda', 'Agenda', 'As imported, with a note on items not reached.', 'medium', 'normal', true]],
        [['m05', 'discussion', 'Discussion summary', 'One short paragraph per agenda item, neutral tone.', 'long', 'normal', true]],
        [
          ['m06', 'decisions', 'Decisions', 'Bulleted. Quote the moment of agreement with a timestamp.', 'medium', 'normal', true],
          ['m07', 'actionItems', 'Action items', 'Table: action, owner, deadline. Only commitments actually made.', 'medium', 'normal', true],
        ],
        [
          ['m08', 'openQuestions', 'Open questions', 'Questions raised but not answered.', 'short', 'normal', false],
          ['m09', 'nextMeeting', 'Next meeting', 'Date, time and proposed agenda if mentioned.', 'short', 'normal', false],
        ],
      ),
      inputs: inputs({ details: true, participants: true, agenda: true, highlights: true }),
      providerId: null,
      styleId: 'corporate',
      output,
      modifiedAt: null,
      documentKind: 'Meeting minutes',
    },
    {
      id: 'interview-notes',
      name: 'Interview notes',
      builtIn: true,
      recordingTypes: ['interview'],
      rows: rows(
        [['m01', 'summary', 'Summary', 'A neutral summary of the interview in four or five sentences.', 'medium', 'larger', true]],
        [
          ['m02', 'meetingPurpose', 'Interview purpose', 'One line, taken from the recording details.', 'short', 'normal', false],
          ['m03', 'participants', 'Participants', 'Interviewer and interviewee as named in the details.', 'short', 'normal', false],
        ],
        [['m04', 'topic', 'Main themes', 'One short paragraph per theme the interviewee spoke about, in their own terms.', 'long', 'normal', true]],
        [
          ['m05', 'quote', 'Key quotes', 'Two or three remarks, word for word, with who said them.', 'short', 'normal', true],
          ['m06', 'highlight', 'Highlights', 'The highlights marked during the interview.', 'short', 'normal', true],
        ],
        [['m07', 'openQuestions', 'Follow-up questions', 'Questions left open that are worth asking next time.', 'short', 'normal', false]],
      ),
      inputs: inputs({ details: true, participants: true, highlights: true }),
      providerId: null,
      styleId: 'minimal',
      output,
      modifiedAt: null,
      documentKind: 'Interview notes',
    },
    {
      id: 'lecture-summary',
      name: 'Lecture summary',
      builtIn: true,
      recordingTypes: ['lecture', 'presentation'],
      rows: rows(
        [['m01', 'summary', 'Summary', 'What the lecture covered, in one paragraph.', 'medium', 'larger', true]],
        [['m02', 'chapter', 'Outline', 'The chapters with their start times.', 'medium', 'normal', true]],
        [['m03', 'topic', 'Key concepts', 'Each concept the speaker defined or explained, with the explanation they gave.', 'long', 'normal', true]],
        [
          ['m04', 'quote', 'Worth quoting', 'One or two definitions or remarks, word for word.', 'short', 'normal', true],
          ['m05', 'openQuestions', 'Questions to review', 'Questions raised by the audience or the speaker that were not answered.', 'short', 'normal', false],
        ],
      ),
      inputs: inputs({ highlights: true, attachments: true }),
      providerId: null,
      styleId: 'academic',
      output,
      modifiedAt: null,
      documentKind: 'Lecture summary',
    },
    {
      id: 'dictation-cleanup',
      name: 'Dictation clean-up',
      builtIn: true,
      recordingTypes: ['dictation'],
      rows: rows(
        [
          [
            'm01',
            'customAi',
            'Text',
            'Rewrite the dictation as clean prose: remove fillers, false starts and spoken punctuation, keep the speaker’s words and order, add nothing.',
            'long',
            'normal',
            false,
          ],
        ],
        [['m02', 'actionItems', 'Tasks mentioned', 'Only tasks the speaker said they or someone else would do.', 'short', 'smaller', true]],
      ),
      inputs: inputs({}),
      providerId: null,
      styleId: 'minimal',
      output,
      modifiedAt: null,
      documentKind: 'Dictation clean-up',
    },
  ];
}

/** src/Memento.Documents/Styles/BuiltIn/*.json. */
export function builtInStyleSettings(): Record<'corporate' | 'minimal' | 'academic', StyleSettings> {
  const common = { baseSize: 'normal', paper: 'letter', pageNumbers: true } as const;
  return {
    corporate: {
      ...common,
      headingFace: 'sans',
      bodyFace: 'sans',
      headingCase: 'smallCaps',
      numberedHeadings: false,
      headingColor: 'navy',
      tableHeaderFill: true,
      ruleUnderTitle: true,
      linesBetweenSections: false,
      spacing: 'normal',
      runningHeader: true,
    },
    minimal: {
      ...common,
      headingFace: 'sans',
      bodyFace: 'sans',
      headingCase: 'normal',
      numberedHeadings: false,
      headingColor: 'ink',
      tableHeaderFill: false,
      ruleUnderTitle: false,
      linesBetweenSections: true,
      spacing: 'airy',
      runningHeader: false,
    },
    academic: {
      ...common,
      headingFace: 'serif',
      bodyFace: 'serif',
      headingCase: 'normal',
      numberedHeadings: true,
      headingColor: 'ink',
      tableHeaderFill: false,
      ruleUnderTitle: false,
      linesBetweenSections: false,
      spacing: 'normal',
      runningHeader: true,
    },
  };
}

const BUILT_IN_STYLE_NAMES = { corporate: 'Corporate', minimal: 'Minimal', academic: 'Academic' } as const;

const sameSettings = (a: StyleSettings, b: StyleSettings): boolean =>
  (Object.keys(a) as (keyof StyleSettings)[]).every((key) => a[key] === b[key]);

// The sample page's fixed parts, from the host's own corporate sample (SampleDocument.Minutes).
const SAMPLE = extractArticle(sampleCorporate);
const SAMPLE_RUNHEAD = /<div class="paper-runhead">[\s\S]*?<\/div>\n/.exec(SAMPLE)?.[0] ?? '';
const SAMPLE_TITLE = /<header class="paper-titleblock">[\s\S]*?<\/header>\n/.exec(SAMPLE)?.[0] ?? '';
const SAMPLE_ROWS = SAMPLE.slice(SAMPLE.indexOf(SAMPLE_TITLE) + SAMPLE_TITLE.length).replace(/<div class="paper-pagenum">[\s\S]*$/, '').replace(/<\/article>\s*$/, '');

/** styles.sampleHtml: the fixed sample minutes in these settings (DocumentHtmlRenderer.RenderSample). */
export function sampleHtml(settings: StyleSettings): string {
  const builtIn = Object.entries<StyleSettings>(builtInStyleSettings()).find(([, s]) => sameSettings(s, settings));
  const styleId = builtIn?.[0] ?? 'custom';
  return (
    articleOpen(settings, styleId, 'sample', 'style-sample') +
    (settings.runningHeader ? SAMPLE_RUNHEAD : '') +
    SAMPLE_TITLE +
    SAMPLE_ROWS +
    (settings.pageNumbers ? '<div class="paper-pagenum">1 of 2</div>\n' : '') +
    '</article>\n'
  );
}

export interface TemplateStoreEnvironment {
  emit: <E extends EventName>(event: E, payload: EventPayload<E>) => void;
  now: () => string;
}

export interface MockTemplateStore {
  templates(): Template[];
  template(templateId: string): Template;
  saveTemplate(template: Template): Template;
  duplicateTemplate(templateId: string): Template;
  deleteTemplate(templateId: string): void;
  resetTemplate(templateId: string): Template;
  styles(): Style[];
  style(styleId: string): Style;
  saveStyle(style: Style): Style;
  duplicateStyle(styleId: string): Style;
  deleteStyle(styleId: string): void;
  resetStyle(styleId: string): Style;
  /** The template for a recording type: the first whose types include it, else Meeting minutes. */
  templateFor(type: string): Template;
}

function copyName(name: string, taken: readonly string[]): string {
  const base = `${name} (copy)`;
  if (!taken.includes(base)) {
    return base;
  }
  let n = 2;
  while (taken.includes(`${name} (copy ${n})`)) {
    n += 1;
  }
  return `${name} (copy ${n})`;
}

const clone = <T>(value: T): T => structuredClone(value);

export function createMockTemplateStore(env: TemplateStoreEnvironment): MockTemplateStore {
  const builtInT = builtInTemplates();
  const templates: Template[] = clone(builtInT);
  const presets = builtInStyleSettings();
  const builtInS: Style[] = (['corporate', 'minimal', 'academic'] as const).map((id) => ({
    id,
    name: BUILT_IN_STYLE_NAMES[id],
    builtIn: true,
    settings: presets[id],
    usedByTemplates: 0,
    modifiedAt: null,
  }));
  const styles: Style[] = clone(builtInS);
  let counter = 0;
  const nextId = (prefix: string): string => `${prefix}-${Date.now().toString(36)}${(++counter).toString(36)}`;

  const findTemplate = (templateId: string): Template => {
    const template = templates.find((t) => t.id === templateId);
    if (template === undefined) {
      throw new MockHostError('templates.notFound', 'That template is not in this library any more; it may have been deleted. Nothing was changed.', templateId);
    }
    return template;
  };
  const findStyle = (styleId: string): Style => {
    const style = styles.find((s) => s.id === styleId);
    if (style === undefined) {
      throw new MockHostError('styles.notFound', 'That style is not in this library any more; it may have been deleted. Nothing was changed.', styleId);
    }
    return style;
  };
  const withCount = (style: Style): Style => ({ ...clone(style), usedByTemplates: templates.filter((t) => t.styleId === style.id).length });

  const checkTemplate = (template: Template): void => {
    const name = template.name.trim();
    if (name === '' || name.length > 80) {
      throw new MockHostError('bridge.invalidParams', 'A template needs a name of 1 to 80 characters. Nothing was saved.', template.name);
    }
    for (const row of template.rows) {
      if (row.modules.length > 3) {
        throw new MockHostError('bridge.invalidParams', 'A row holds at most three modules side by side. Nothing was saved.', String(row.modules.length));
      }
      for (const module of row.modules) {
        moduleInfo(module.module);
      }
    }
    findStyle(template.styleId);
  };

  return {
    templates: () => clone(templates),
    template: (templateId) => clone(findTemplate(templateId)),
    saveTemplate(template) {
      checkTemplate(template);
      const existing = templates.find((t) => t.id === template.id);
      const cleanRows = template.rows.filter((r) => r.modules.length > 0);
      if (existing?.builtIn !== false) {
        // New, or a built-in saved over: a copy of its own (built-ins stay as they are), never with a name in use.
        const taken = templates.map((t) => t.name);
        const trimmed = template.name.trim();
        const name = taken.some((t) => t.toLocaleLowerCase() === trimmed.toLocaleLowerCase()) ? copyName(trimmed, taken) : trimmed;
        const saved: Template = { ...clone(template), rows: cleanRows, id: nextId('tpl'), name, builtIn: false, modifiedAt: env.now() };
        templates.push(saved);
        env.emit('templates.changed', {});
        env.emit('styles.changed', {});
        return clone(saved);
      }
      const saved: Template = { ...clone(template), rows: cleanRows, name: template.name.trim(), builtIn: false, modifiedAt: env.now() };
      templates.splice(templates.indexOf(existing), 1, saved);
      env.emit('templates.changed', {});
      env.emit('styles.changed', {});
      return clone(saved);
    },
    duplicateTemplate(templateId) {
      const source = findTemplate(templateId);
      const copy: Template = { ...clone(source), id: nextId('tpl'), name: copyName(source.name, templates.map((t) => t.name)), builtIn: false, modifiedAt: env.now() };
      templates.push(copy);
      env.emit('templates.changed', {});
      return clone(copy);
    },
    deleteTemplate(templateId) {
      const template = findTemplate(templateId);
      if (template.builtIn) {
        throw new MockHostError(
          'templates.builtIn',
          `${template.name} is built in and cannot be deleted. Duplicate it to make your own version. Nothing was deleted.`,
          templateId,
        );
      }
      templates.splice(templates.indexOf(template), 1);
      env.emit('templates.changed', {});
      env.emit('styles.changed', {});
    },
    resetTemplate(templateId) {
      const template = findTemplate(templateId);
      const original = builtInT.find((t) => t.id === templateId);
      if (!template.builtIn || original === undefined) {
        throw new MockHostError('templates.builtIn', `${template.name} is your own template; only built-in templates can be reset. Nothing was changed.`, templateId);
      }
      templates.splice(templates.indexOf(template), 1, clone(original));
      env.emit('templates.changed', {});
      return clone(original);
    },
    styles: () => styles.map(withCount),
    style: (styleId) => withCount(findStyle(styleId)),
    saveStyle(style) {
      const name = style.name.trim();
      if (name === '' || name.length > 60) {
        throw new MockHostError('bridge.invalidParams', 'A style needs a name of 1 to 60 characters. Nothing was saved.', style.name);
      }
      const existing = styles.find((s) => s.id === style.id);
      if (existing?.builtIn !== false) {
        const taken = styles.map((s) => s.name);
        const savedName = name === existing?.name ? copyName(name, taken) : name;
        const saved: Style = { ...clone(style), id: nextId('sty'), name: savedName, builtIn: false, usedByTemplates: 0, modifiedAt: env.now() };
        styles.push(saved);
        env.emit('styles.changed', {});
        return withCount(saved);
      }
      const saved: Style = { ...clone(style), name, builtIn: false, modifiedAt: env.now() };
      styles.splice(styles.indexOf(existing), 1, saved);
      env.emit('styles.changed', {});
      return withCount(saved);
    },
    duplicateStyle(styleId) {
      const source = findStyle(styleId);
      const copy: Style = { ...clone(source), id: nextId('sty'), name: copyName(source.name, styles.map((s) => s.name)), builtIn: false, modifiedAt: env.now() };
      styles.push(copy);
      env.emit('styles.changed', {});
      return withCount(copy);
    },
    deleteStyle(styleId) {
      const style = findStyle(styleId);
      if (style.builtIn) {
        throw new MockHostError('styles.builtIn', `${style.name} is built in and cannot be deleted. Nothing was deleted.`, styleId);
      }
      const users = templates.filter((t) => t.styleId === styleId);
      if (users.length > 0) {
        const names = users.map((t) => t.name).join(', ');
        throw new MockHostError(
          'styles.inUse',
          `${style.name} is the style of ${users.length === 1 ? 'the template' : `${users.length} templates`} ${names}. Choose another style for ${users.length === 1 ? 'it' : 'them'} first. Nothing was deleted.`,
          styleId,
        );
      }
      styles.splice(styles.indexOf(style), 1);
      env.emit('styles.changed', {});
    },
    resetStyle(styleId) {
      const style = findStyle(styleId);
      const original = builtInS.find((s) => s.id === styleId);
      if (!style.builtIn || original === undefined) {
        throw new MockHostError('styles.builtIn', `${style.name} is your own style; only built-in styles can be reset. Nothing was changed.`, styleId);
      }
      styles.splice(styles.indexOf(style), 1, clone(original));
      env.emit('styles.changed', {});
      return withCount(original);
    },
    templateFor(type) {
      return clone(templates.find((t) => t.recordingTypes.includes(type)) ?? findTemplate('meeting-minutes'));
    },
  };
}
