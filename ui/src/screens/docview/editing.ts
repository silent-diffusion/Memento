// The Document viewer's light edits (DESIGN.md §12 toolbar): bold, italic, sub-heading or
// paragraph, bulleted and numbered lists, a table, and a timestamp chip. Each works on the paper's
// DOM directly (no document.execCommand) and writes only markup the host reads back
// (HtmlToBlocks): <strong>, <em>, <h3 class="paper-sub">, <p>, <ul>/<ol>/<li>,
// <table class="paper-table"> with a header row, and <a class="ts" data-t contenteditable="false">.
// Module headings (h2.paper-h), the title block and locked data blocks are never changed here.
import { createChip } from '../../components/paper/paperDom';

export type BlockKind = 'heading' | 'paragraph' | 'bulleted' | 'numbered' | 'cell' | 'other';

const TEXT_BLOCKS = 'p, h3, h4, h5, h6, li, td, th, dd, dt, blockquote';

/** The range inside the paper where an edit may happen: in a module's content, not in a locked block. */
export function editableRange(article: HTMLElement, range: Range | null): Range | null {
  if (range === null) {
    return null;
  }
  const node = range.commonAncestorContainer;
  const element = node instanceof Element ? node : node.parentElement;
  if (element === null || !article.contains(element)) {
    return null;
  }
  if (element.closest('[contenteditable="false"]') !== null) {
    return null;
  }
  const module = element.closest('section.paper-module');
  if (module === null || !article.contains(module)) {
    return null;
  }
  if (element.closest('h2.paper-h') !== null) {
    return null;
  }
  return range;
}

/** The selection's range when it lies in the paper. */
export function selectionRange(article: HTMLElement): Range | null {
  const selection = article.ownerDocument.getSelection();
  if (selection === null || selection.rangeCount === 0) {
    return null;
  }
  const range = selection.getRangeAt(0);
  return article.contains(range.commonAncestorContainer) ? range : null;
}

function elementOf(node: Node): Element | null {
  return node instanceof Element ? node : node.parentElement;
}

/** The text block the caret is in (a paragraph, sub-heading, list item, cell). */
export function currentBlock(article: HTMLElement, range: Range | null): HTMLElement | null {
  const usable = editableRange(article, range);
  if (usable === null) {
    return null;
  }
  const block = elementOf(usable.startContainer)?.closest<HTMLElement>(TEXT_BLOCKS) ?? null;
  return block !== null && article.contains(block) ? block : null;
}

/** What the toolbar shows pressed in. */
export function blockKind(block: HTMLElement | null): BlockKind | null {
  if (block === null) {
    return null;
  }
  const tag = block.tagName.toLowerCase();
  if (tag === 'p') {
    return 'paragraph';
  }
  if (/^h[3-6]$/.test(tag)) {
    return 'heading';
  }
  if (tag === 'li') {
    return block.parentElement?.tagName.toLowerCase() === 'ol' ? 'numbered' : 'bulleted';
  }
  if (tag === 'td' || tag === 'th') {
    return 'cell';
  }
  return 'other';
}

/** The block that sits directly in the module section. */
function topBlock(block: Element): Element | null {
  let el: Element | null = block;
  while (el !== null && !(el.parentElement?.matches('section.paper-module') ?? false)) {
    el = el.parentElement;
  }
  return el;
}

function moveChildren(from: Element, to: Element): void {
  while (from.firstChild !== null) {
    to.appendChild(from.firstChild);
  }
}

function placeCaret(node: Node, atEnd = true): void {
  const doc = node.ownerDocument;
  const selection = doc?.getSelection();
  if (doc === null || selection === null || selection === undefined) {
    return;
  }
  const range = doc.createRange();
  range.selectNodeContents(node);
  range.collapse(!atEnd);
  selection.removeAllRanges();
  selection.addRange(range);
}

/** Bold (strong) or italic (em): wraps the selection in one block, or unwraps it when already set. */
export function toggleInline(article: HTMLElement, range: Range | null, tag: 'strong' | 'em'): boolean {
  const usable = editableRange(article, range);
  if (usable === null) {
    return false;
  }
  const same = tag === 'strong' ? 'strong, b' : 'em, i';
  const inside = elementOf(usable.commonAncestorContainer)?.closest(same) ?? null;
  if (inside !== null && article.contains(inside)) {
    const parent = inside.parentNode;
    if (parent === null) {
      return false;
    }
    const first = inside.firstChild;
    while (inside.firstChild !== null) {
      parent.insertBefore(inside.firstChild, inside);
    }
    inside.remove();
    if (first !== null) {
      placeCaret(first);
    }
    // Join the text the emphasis split, so the paragraph reads back as one run.
    parent.normalize();
    return true;
  }
  if (usable.collapsed) {
    return false;
  }
  const startBlock = elementOf(usable.startContainer)?.closest(TEXT_BLOCKS);
  const endBlock = elementOf(usable.endContainer)?.closest(TEXT_BLOCKS);
  if (startBlock === null || startBlock === undefined || startBlock !== endBlock) {
    // Emphasis stays inside one paragraph or item.
    return false;
  }
  const wrapper = article.ownerDocument.createElement(tag);
  wrapper.appendChild(usable.extractContents());
  usable.insertNode(wrapper);
  const selection = article.ownerDocument.getSelection();
  if (selection !== null) {
    const after = article.ownerDocument.createRange();
    after.selectNodeContents(wrapper);
    selection.removeAllRanges();
    selection.addRange(after);
  }
  return true;
}

function replaceTag(block: Element, tag: string, className: string | null): HTMLElement {
  const next = block.ownerDocument.createElement(tag);
  if (className !== null) {
    next.className = className;
  }
  moveChildren(block, next);
  if (next.childNodes.length === 0) {
    next.appendChild(block.ownerDocument.createElement('br'));
  }
  block.replaceWith(next);
  return next;
}

/** A list item out of its list as a paragraph; the items around it stay lists. */
function liftItem(item: HTMLElement): HTMLElement {
  const list = item.parentElement;
  const doc = item.ownerDocument;
  const paragraph = doc.createElement('p');
  moveChildren(item, paragraph);
  if (paragraph.childNodes.length === 0) {
    paragraph.appendChild(doc.createElement('br'));
  }
  if (list === null) {
    item.replaceWith(paragraph);
    return paragraph;
  }
  const after = doc.createElement(list.tagName.toLowerCase());
  let sibling = item.nextElementSibling;
  while (sibling !== null) {
    const next = sibling.nextElementSibling;
    after.appendChild(sibling);
    sibling = next;
  }
  item.remove();
  list.after(paragraph);
  if (after.children.length > 0) {
    paragraph.after(after);
  }
  if (list.children.length === 0) {
    list.remove();
  }
  return paragraph;
}

/** H2 (a sub-heading in the module) or ¶ (a paragraph) for the current block. */
export function setBlock(article: HTMLElement, range: Range | null, kind: 'heading' | 'paragraph'): boolean {
  const block = currentBlock(article, range);
  const current = blockKind(block);
  if (block === null || current === null || current === 'cell' || current === 'other') {
    return false;
  }
  if (current === kind) {
    return true;
  }
  let target: HTMLElement = block;
  if (current === 'bulleted' || current === 'numbered') {
    target = liftItem(block);
    if (kind === 'paragraph') {
      placeCaret(target);
      return true;
    }
  }
  const next = kind === 'heading' ? replaceTag(target, 'h3', 'paper-sub') : replaceTag(target, 'p', null);
  placeCaret(next);
  return true;
}

/** Bulleted or numbered: makes the paragraph a list, switches the list's kind, or lifts the item out. */
export function toggleList(article: HTMLElement, range: Range | null, ordered: boolean): boolean {
  const block = currentBlock(article, range);
  const current = blockKind(block);
  if (block === null || current === null || current === 'cell' || current === 'other') {
    return false;
  }
  const doc = article.ownerDocument;
  const tag = ordered ? 'ol' : 'ul';
  if (current === 'bulleted' || current === 'numbered') {
    if ((current === 'numbered') === ordered) {
      placeCaret(liftItem(block));
      return true;
    }
    const list = block.parentElement;
    if (list === null || list.classList.contains('timeline')) {
      return false;
    }
    replaceTag(list, tag, null);
    return true;
  }
  const list = doc.createElement(tag);
  const item = doc.createElement('li');
  moveChildren(block, item);
  if (item.childNodes.length === 0) {
    item.appendChild(doc.createElement('br'));
  }
  list.appendChild(item);
  block.replaceWith(list);
  placeCaret(item);
  return true;
}

/** A three-column table with a header row, after the current block. */
export function insertTable(article: HTMLElement, range: Range | null): boolean {
  const usable = editableRange(article, range);
  if (usable === null) {
    return false;
  }
  const start = elementOf(usable.startContainer);
  const anchor = start === null ? null : topBlock(start);
  if (anchor === null) {
    return false;
  }
  const doc = article.ownerDocument;
  const table = doc.createElement('table');
  table.className = 'paper-table';
  const head = doc.createElement('thead');
  const headRow = doc.createElement('tr');
  for (const name of ['Column 1', 'Column 2', 'Column 3']) {
    const th = doc.createElement('th');
    th.textContent = name;
    headRow.appendChild(th);
  }
  head.appendChild(headRow);
  const body = doc.createElement('tbody');
  const row = doc.createElement('tr');
  for (let i = 0; i < 3; i++) {
    const td = doc.createElement('td');
    td.appendChild(doc.createElement('br'));
    row.appendChild(td);
  }
  body.appendChild(row);
  table.append(head, body);
  anchor.after(table);
  const firstCell = row.firstElementChild;
  if (firstCell !== null) {
    placeCaret(firstCell, false);
  }
  return true;
}

/** A timestamp chip at the caret, followed by a space so typing carries on after it. */
export function insertTimestamp(article: HTMLElement, range: Range | null, seconds: number): boolean {
  const usable = editableRange(article, range);
  if (usable === null) {
    return false;
  }
  const start = elementOf(usable.startContainer);
  const locked = start?.closest('table.paper-transcript, ol.timeline') ?? null;
  if (locked !== null) {
    return false;
  }
  const doc = article.ownerDocument;
  const chip = createChip(seconds, doc);
  const space = doc.createTextNode(' ');
  const at = usable.cloneRange();
  at.collapse(false);
  at.insertNode(space);
  at.insertNode(chip);
  const selection = doc.getSelection();
  if (selection !== null) {
    const after = doc.createRange();
    after.setStartAfter(space);
    after.collapse(true);
    selection.removeAllRanges();
    selection.addRange(after);
  }
  return true;
}
