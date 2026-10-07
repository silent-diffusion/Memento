// The browser-preview host's paper markup: a small port of the document engine's renderer
// (src/Memento.Documents/Render: PaperCss.Classes and Variables, SkeletonHtml, the title block and
// module sections, MetaLine) so the mock answers documents.renderHtml, generation.previewHtml and
// styles.sampleHtml with the same markup the host writes. The fixture tests compare it with the
// C# snapshots in tests/Memento.Documents.Tests/fixtures/expected.
import type { ContentShape, ModuleId, StyleSettings, TextSize } from './types';

export type PaperMode = 'viewer' | 'skeleton' | 'sample';

const SANS = "'Segoe UI', system-ui, -apple-system, 'Helvetica Neue', Arial, sans-serif";
const SERIF = "Georgia, Cambria, 'Times New Roman', serif";

/** As HtmlText.Escape: & < > " ' only. */
export function escapeHtml(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

/** As HtmlText.Number: at most three decimals, no trailing zeros. */
export function cssNumber(value: number): string {
  return String(Math.round(value * 1000) / 1000);
}

export const HEADING_HEX: Record<StyleSettings['headingColor'], string> = {
  navy: '#1F3A5F',
  ink: '#1D1C1A',
  forest: '#2F6B4F',
  burgundy: '#7A2E2E',
};

export const TINT_HEX: Record<StyleSettings['headingColor'], string> = {
  navy: '#D9E1EC',
  ink: '#E8E6E0',
  forest: '#DCE9E1',
  burgundy: '#EEDCDC',
};

const BASE_PX: Record<StyleSettings['baseSize'], number> = { small: 11, normal: 12, large: 13.5 };
const SPACING_PX: Record<StyleSettings['spacing'], number> = { tight: 10, normal: 18, airy: 28 };

export function paperClasses(settings: StyleSettings, mode: PaperMode): string {
  const classes = ['paper', `paper-${mode}`];
  if (settings.headingCase === 'smallCaps') {
    classes.push('caps');
  }
  if (settings.numberedHeadings) {
    classes.push('numbered');
  }
  if (settings.ruleUnderTitle) {
    classes.push('title-rule');
  }
  if (settings.linesBetweenSections) {
    classes.push('lines');
  }
  if (settings.tableHeaderFill) {
    classes.push('th-fill');
  }
  return classes.join(' ');
}

export function paperVariables(settings: StyleSettings, mode: PaperMode): string {
  const base = BASE_PX[settings.baseSize];
  const spacing = SPACING_PX[settings.spacing];
  const [baseSize, gap, columnGap] =
    mode === 'viewer'
      ? [base * (14 / 12), spacing * (14 / 12), '28px']
      : mode === 'skeleton'
        ? [base * (11 / 12), spacing, '18px']
        : [base, spacing, '24px'];
  const face = (f: 'sans' | 'serif'): string => (f === 'serif' ? SERIF : SANS);
  return (
    `--paper-base:${cssNumber(baseSize)}px;--paper-gap:${cssNumber(gap)}px;--paper-col-gap:${columnGap};` +
    `--paper-head:${HEADING_HEX[settings.headingColor]};--paper-tint:${TINT_HEX[settings.headingColor]};` +
    `--paper-head-font:${face(settings.headingFace)};--paper-body-font:${face(settings.bodyFace)}`
  );
}

/** `<article class="paper …">` for a style in a mode. */
export function articleOpen(settings: StyleSettings, styleId: string, mode: PaperMode, documentId: string | null): string {
  const doc = documentId === null || documentId === '' ? '' : ` data-doc="${escapeHtml(documentId)}"`;
  return (
    `<article class="${paperClasses(settings, mode)}"${doc} data-style="${escapeHtml(styleId)}" data-paper="${settings.paper}"` +
    ` style="${escapeHtml(paperVariables(settings, mode))}">\n`
  );
}

export function titleBlock(title: string, meta: string, mode: PaperMode): string {
  const editable = mode === 'viewer' ? ' contenteditable="false"' : '';
  const metaHtml = meta.length > 0 ? `<p class="paper-meta"${editable}>${escapeHtml(meta)}</p>` : '';
  return `<header class="paper-titleblock"><h1 class="paper-title">${escapeHtml(title)}</h1>${metaHtml}</header>\n`;
}

export function moduleOpen(id: string, type: string, size: TextSize, link: boolean, title: string): string {
  return (
    `<section class="paper-module size-${size}" data-id="${escapeHtml(id)}" data-module="${escapeHtml(type)}" data-size="${size}"` +
    ` data-link="${link ? 'true' : 'false'}">\n<h2 class="paper-h">${escapeHtml(title)}</h2>\n`
  );
}

/** Table modules' column widths (ModuleCatalog.cs); others draw 2:1:1. */
const TABLE_WIDTHS: Partial<Record<ModuleId, number[]>> = { actionItems: [2, 1, 1], deadline: [1, 3] };

/** SkeletonHtml.Write: the grey bars per content shape. */
export function skeletonHtml(shape: ContentShape, module: ModuleId): string {
  switch (shape) {
    case 'list':
      return (
        '<div class="sk sk-list" aria-hidden="true">' +
        '<div class="sk-li"><span class="bar d sk-dot"></span><div class="bar sk-fill"></div></div>' +
        '<div class="sk-li"><span class="bar d sk-dot"></span><div class="bar sk-fill" style="max-width:80%"></div></div>' +
        '<div class="sk-li"><span class="bar d sk-dot"></span><div class="bar sk-fill" style="max-width:64%"></div></div>' +
        '</div>\n'
      );
    case 'table': {
      const widths = TABLE_WIDTHS[module] ?? [2, 1, 1];
      const cells = [0, 1, 2].map((row) => widths.map(() => (row === 0 ? '<div class="cell h"></div>' : '<div class="cell"></div>')).join('')).join('');
      return `<div class="sk sk-table" aria-hidden="true" style="grid-template-columns:${widths.map((w) => `${cssNumber(w)}fr`).join(' ')}">${cells}</div>\n`;
    }
    case 'chips':
      return (
        '<div class="sk sk-chips" aria-hidden="true">' +
        '<span class="cell" style="width:64px"></span><span class="cell" style="width:78px"></span>' +
        '<span class="cell" style="width:52px"></span><span class="cell" style="width:40px"></span>' +
        '</div>\n'
      );
    case 'labelValue':
      return (
        '<div class="sk sk-kv" aria-hidden="true">' +
        '<div class="bar d" style="width:40px"></div><div class="bar"></div>' +
        '<div class="bar d" style="width:48px"></div><div class="bar" style="width:70%"></div>' +
        '</div>\n'
      );
    case 'quote':
      return (
        '<div class="sk sk-quote" aria-hidden="true"><span class="sk-quote-bar"></span><div class="sk-col">' +
        '<div class="bar"></div><div class="bar" style="width:72%"></div><div class="bar d" style="width:28%;height:5px"></div>' +
        '</div></div>\n'
      );
    case 'timeline':
      return (
        '<div class="sk sk-tl-list" aria-hidden="true">' +
        '<div class="sk-tl"><span class="bar d sk-tl-t"></span><span class="bar d sk-node"></span><div class="bar sk-fill"></div></div>' +
        '<div class="sk-tl"><span class="bar d sk-tl-t"></span><span class="bar d sk-node"></span><div class="bar sk-fill" style="max-width:70%"></div></div>' +
        '</div>\n'
      );
    case 'transcript':
      return (
        '<div class="sk sk-tr-list" aria-hidden="true">' +
        '<div class="sk-tr"><span class="bar d sk-tl-t"></span><span class="bar d" style="width:44px"></span><div class="bar sk-fill"></div></div>' +
        '<div class="sk-tr"><span class="bar d sk-tl-t"></span><span class="bar d" style="width:36px"></span><div class="bar sk-fill" style="max-width:76%"></div></div>' +
        '<div class="sk-tr"><span class="bar d sk-tl-t"></span><span class="bar d" style="width:44px"></span><div class="bar sk-fill" style="max-width:58%"></div></div>' +
        '</div>\n'
      );
    case 'text':
      return '<div class="sk sk-text" aria-hidden="true"><div class="bar" style="width:90%"></div><div class="bar" style="width:60%"></div></div>\n';
    case 'paragraph':
      return (
        '<div class="sk sk-para" aria-hidden="true">' +
        '<div class="bar"></div><div class="bar" style="width:94%"></div><div class="bar" style="width:88%"></div><div class="bar" style="width:56%"></div>' +
        '</div>\n'
      );
  }
}

const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

interface WallTime {
  year: number;
  month: number;
  day: number;
  hour: number;
  minute: number;
}

/** The clock time written in an ISO string, in its own offset (as MetaLine uses the recording's offset). */
function wallTime(iso: string): WallTime | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(iso);
  if (match === null) {
    return null;
  }
  const [, y, mo, d, h, mi] = match.map(Number) as [number, number, number, number, number, number];
  return { year: y, month: mo, day: d, hour: h, minute: mi };
}

/** "Monday 5 October 2026, 4:00 PM". */
export function longDate(iso: string): string {
  const t = wallTime(iso);
  if (t === null) {
    return '';
  }
  const weekday = WEEKDAYS[new Date(Date.UTC(t.year, t.month - 1, t.day)).getUTCDay()] ?? '';
  const hour12 = t.hour % 12 === 0 ? 12 : t.hour % 12;
  return `${weekday} ${t.day} ${MONTHS[t.month - 1] ?? ''} ${t.year}, ${hour12}:${String(t.minute).padStart(2, '0')} ${t.hour < 12 ? 'AM' : 'PM'}`;
}

/** "5 October 2026" for the running header. */
export function shortDate(iso: string): string {
  const t = wallTime(iso);
  return t === null ? '' : `${t.day} ${MONTHS[t.month - 1] ?? ''} ${t.year}`;
}

/** "1 h 10 min", "42 min", "2 h", "35 s". */
export function durationWords(ms: number): string {
  const totalSeconds = Math.floor(ms / 1000);
  if (totalSeconds < 60) {
    return `${totalSeconds} s`;
  }
  const totalMinutes = Math.floor(totalSeconds / 60);
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  return hours === 0 ? `${minutes} min` : minutes === 0 ? `${hours} h` : `${hours} h ${minutes} min`;
}

export interface MetaParts {
  kind: string;
  recordedAt: string | null;
  durationMs: number | null;
  platform: string;
  participantCount: number;
}

/** MetaLine.Format: "Meeting minutes · Monday 5 October 2026, 4:00 PM · 1 h 10 min · Zoom · 4 participants". */
export function metaLine(parts: MetaParts): string {
  const out: string[] = [];
  if (parts.kind.trim() !== '') {
    out.push(parts.kind.trim());
  }
  if (parts.recordedAt !== null) {
    out.push(longDate(parts.recordedAt));
  }
  if (parts.durationMs !== null && parts.durationMs > 0) {
    out.push(durationWords(parts.durationMs));
  }
  if (parts.platform.trim() !== '') {
    out.push(parts.platform.trim());
  }
  if (parts.participantCount > 0) {
    out.push(parts.participantCount === 1 ? '1 participant' : `${parts.participantCount} participants`);
  }
  return out.join(' · ');
}

/** The `<article>` element of a page or fragment the renderer wrote. */
export function extractArticle(html: string): string {
  const match = /<article[\s\S]*<\/article>/.exec(html);
  return match === null ? html : `${match[0]}\n`;
}

/** The same paper in another style or mode: only the article's open tag changes (the stylesheet is shared). */
export function restyle(article: string, settings: StyleSettings, styleId: string, mode: PaperMode): string {
  const doc = /<article[^>]*\sdata-doc="([^"]*)"/.exec(article)?.[1] ?? null;
  return article.replace(/^\s*<article[^>]*>\n?/, articleOpen(settings, styleId, mode, doc === null ? null : unescapeAttr(doc)));
}

function unescapeAttr(value: string): string {
  return value.replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');
}

/** "18:42", "1:02:05": the chip text for a time in seconds. */
export function chipTime(seconds: number): string {
  const whole = Math.max(0, Math.floor(seconds));
  const h = Math.floor(whole / 3600);
  const m = Math.floor((whole % 3600) / 60);
  const s = whole % 60;
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}` : `${m}:${String(s).padStart(2, '0')}`;
}

/** A timestamp chip as the viewer markup writes it. */
export function chipHtml(seconds: number): string {
  const t = cssNumber(seconds);
  return `<a class="ts" href="#t=${t}" data-t="${t}" contenteditable="false">${chipTime(seconds)}</a>`;
}
