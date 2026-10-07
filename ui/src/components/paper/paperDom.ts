// The viewer markup contract (src/Memento.Documents/Render: DocumentHtmlRenderer, HtmlToBlocks):
// `article.paper` → `header.paper-titleblock` → `div.paper-row[data-cols]` →
// `section.paper-module[data-id][data-module][data-size][data-link]` with `h2.paper-h`; timestamp
// chips `<a class="ts" href="#t=S" data-t="S" contenteditable="false">m:ss</a>`. The UI shows the
// host's HTML as it is: it parses it, keeps only the elements and attributes of the contract, and
// applies inline styles through the CSSOM (the page's Content-Security-Policy forbids inline style
// attributes and <style> elements).

/** Elements a paper may hold (what the renderer writes and what light edits add). */
export const PAPER_TAGS: ReadonlySet<string> = new Set([
  'article', 'header', 'section', 'div', 'p', 'span', 'a', 'strong', 'em', 'b', 'i', 'u', 'br', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
  'ul', 'ol', 'li', 'table', 'colgroup', 'col', 'thead', 'tbody', 'tr', 'th', 'td', 'dl', 'dt', 'dd', 'blockquote', 'footer', 'cite', 'sup', 'hr',
]);

/** Dropped with everything inside them. */
const DROPPED: ReadonlySet<string> = new Set(['script', 'style', 'template', 'iframe', 'object', 'embed', 'link', 'meta', 'title', 'noscript', 'svg', 'math', 'img', 'video', 'audio', 'form', 'input', 'button', 'textarea', 'select']);

const ATTRIBUTES: ReadonlySet<string> = new Set(['class', 'href', 'colspan', 'rowspan', 'contenteditable', 'aria-hidden', 'aria-label']);

function allowedAttribute(name: string, value: string): boolean {
  if (name.startsWith('data-')) {
    return true;
  }
  if (name === 'href') {
    // Only in-page timestamp links; nothing navigates away.
    return value.startsWith('#');
  }
  return ATTRIBUTES.has(name);
}

function copy(source: Node, into: Node, doc: Document): void {
  for (const child of Array.from(source.childNodes)) {
    if (child.nodeType === 3) {
      into.appendChild(doc.createTextNode(child.textContent ?? ''));
      continue;
    }
    if (!(child instanceof Element)) {
      continue;
    }
    const tag = child.tagName.toLowerCase();
    if (DROPPED.has(tag)) {
      continue;
    }
    if (!PAPER_TAGS.has(tag)) {
      // An unknown wrapper: keep what it says, not the element.
      copy(child, into, doc);
      continue;
    }
    const element = doc.createElement(tag);
    for (const attribute of Array.from(child.attributes)) {
      const name = attribute.name.toLowerCase();
      if (name === 'style') {
        element.style.cssText = attribute.value;
      } else if (allowedAttribute(name, attribute.value)) {
        element.setAttribute(name, attribute.value);
      }
    }
    copy(child, element, doc);
    into.appendChild(element);
  }
}

/** The host's paper HTML (a page or a fragment) as a live `article.paper`, or null when there is none. */
export function importPaper(html: string, doc: Document = document): HTMLElement | null {
  const parsed = new DOMParser().parseFromString(html, 'text/html');
  const article = parsed.querySelector('article.paper');
  if (article === null) {
    return null;
  }
  const holder = doc.createElement('div');
  const wrapper = parsed.createElement('div');
  wrapper.appendChild(article);
  copy(wrapper, holder, doc);
  const result = holder.firstElementChild;
  return result instanceof HTMLElement ? result : null;
}

/** What documents.saveEdit receives: the article without what the viewer added for editing. */
export function serializePaper(article: HTMLElement): string {
  const clone = article.cloneNode(true) as HTMLElement;
  clone.removeAttribute('contenteditable');
  clone.removeAttribute('role');
  clone.removeAttribute('aria-multiline');
  clone.removeAttribute('aria-label');
  clone.removeAttribute('tabindex');
  clone.removeAttribute('spellcheck');
  return `${clone.outerHTML}\n`;
}

/** Contract breaches in a paper: elements outside the contract and timestamp chips without their time. */
export function markupProblems(article: Element): string[] {
  const problems: string[] = [];
  if (!article.matches('article.paper')) {
    problems.push('the root is not article.paper');
  }
  for (const el of Array.from(article.querySelectorAll('*'))) {
    const tag = el.tagName.toLowerCase();
    if (!PAPER_TAGS.has(tag)) {
      problems.push(`<${tag}>`);
    }
  }
  for (const chip of Array.from(article.querySelectorAll('a.ts'))) {
    const t = chip.getAttribute('data-t');
    if (t === null || chip.getAttribute('href') !== `#t=${t}` || chip.getAttribute('contenteditable') !== 'false') {
      problems.push(`timestamp chip ${chip.textContent}`);
    }
  }
  for (const module of Array.from(article.querySelectorAll('section.paper-module'))) {
    if (!(module.parentElement?.classList.contains('paper-row') ?? false)) {
      problems.push(`module ${module.getAttribute('data-id') ?? ''} outside a row`);
    }
    if (module.querySelector(':scope > h2.paper-h') === null) {
      problems.push(`module ${module.getAttribute('data-id') ?? ''} without its heading`);
    }
  }
  return problems;
}

/** The transcript time (seconds) a timestamp chip, timeline time or quote time points at. */
export function timestampOf(target: EventTarget | null): number | null {
  if (!(target instanceof Element)) {
    return null;
  }
  const link = target.closest('a.ts, a.tl-t, a.q-t');
  const t = link?.getAttribute('data-t');
  if (t === null || t === undefined) {
    return null;
  }
  const seconds = Number(t);
  return Number.isFinite(seconds) && seconds >= 0 ? seconds : null;
}

/** "18:42", "1:02:05". */
export function chipText(seconds: number): string {
  const whole = Math.max(0, Math.floor(seconds));
  const h = Math.floor(whole / 3600);
  const m = Math.floor((whole % 3600) / 60);
  const s = whole % 60;
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}` : `${m}:${String(s).padStart(2, '0')}`;
}

/** "18:42" or "1:02:05" (or plain seconds) → seconds; null when it is not a time. */
export function parseChipText(text: string): number | null {
  const trimmed = text.trim();
  if (!/^\d+(:\d{1,2}){0,2}$/.test(trimmed)) {
    return null;
  }
  const parts = trimmed.split(':').map(Number);
  if (parts.slice(1).some((p) => p >= 60)) {
    return null;
  }
  return parts.reduce((total, part) => total * 60 + part, 0);
}

/** A timestamp chip exactly as the renderer writes it. */
export function createChip(seconds: number, doc: Document = document): HTMLAnchorElement {
  const t = String(Math.round(seconds * 1000) / 1000);
  const chip = doc.createElement('a');
  chip.className = 'ts';
  chip.setAttribute('href', `#t=${t}`);
  chip.setAttribute('data-t', t);
  chip.setAttribute('contenteditable', 'false');
  chip.textContent = chipText(seconds);
  return chip;
}
