import { afterEach, describe, expect, it } from 'vitest';
import viewerFixture from '../../bridge/fixtures/meeting-minutes.corporate.viewer.html?raw';
import { importPaper, markupProblems } from '../../components/paper/paperDom';
import { blockKind, currentBlock, insertTable, insertTimestamp, setBlock, toggleInline, toggleList } from './editing';

function mount(): HTMLElement {
  const article = importPaper(viewerFixture);
  if (article === null) {
    throw new Error('no article');
  }
  article.setAttribute('contenteditable', 'true');
  document.body.append(article);
  return article;
}

/** Selects `length` characters from `offset` in the first text node of `el` (or a caret when length is 0). */
function select(el: Element, offset: number, length = 0): Range {
  const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
  const text = walker.nextNode();
  if (text === null) {
    throw new Error('no text');
  }
  const range = document.createRange();
  range.setStart(text, offset);
  range.setEnd(text, offset + length);
  const selection = document.getSelection();
  selection?.removeAllRanges();
  selection?.addRange(range);
  return range;
}

const module = (article: HTMLElement, type: string): HTMLElement => {
  const el = article.querySelector<HTMLElement>(`section[data-module="${type}"]`);
  if (el === null) {
    throw new Error(`no ${type}`);
  }
  return el;
};

const chips = (article: HTMLElement): string[] => [...article.querySelectorAll('a.ts')].map((a) => a.outerHTML);

describe('the viewer’s light edits (DESIGN.md §12 toolbar) keep the markup contract', () => {
  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('bolds and italicises inside one paragraph, and takes it off again', () => {
    const article = mount();
    const before = chips(article);
    const p = module(article, 'meetingPurpose').querySelector('p');
    if (p === null) {
      throw new Error('no paragraph');
    }
    expect(toggleInline(article, select(p, 0, 5), 'strong')).toBe(true);
    expect(p.innerHTML).toBe('<strong>Agree</strong> the library layout before build starts.');
    const strong = p.querySelector('strong');
    expect(strong === null ? false : toggleInline(article, select(strong, 1), 'strong')).toBe(true);
    expect(p.innerHTML).toBe('Agree the library layout before build starts.');
    expect(toggleInline(article, select(p, 6, 3), 'em')).toBe(true);
    expect(p.innerHTML).toBe('Agree <em>the</em> library layout before build starts.');
    // A caret alone does nothing; the module heading and locked blocks are never touched.
    expect(toggleInline(article, select(p, 2), 'strong')).toBe(false);
    expect(toggleInline(article, select(module(article, 'agenda').querySelector('h2') ?? p, 0, 3), 'strong')).toBe(false);
    expect(toggleInline(article, select(article.querySelector('.paper-meta') ?? p, 0, 3), 'strong')).toBe(false);
    expect(markupProblems(article)).toEqual([]);
    expect(chips(article)).toEqual(before);
  });

  it('turns a paragraph into a sub-heading and back, with the block type reported for the toolbar', () => {
    const article = mount();
    const p = module(article, 'meetingPurpose').querySelector('p');
    if (p === null) {
      throw new Error('no paragraph');
    }
    const range = select(p, 3);
    expect(blockKind(currentBlock(article, range))).toBe('paragraph');
    expect(setBlock(article, range, 'heading')).toBe(true);
    const heading = module(article, 'meetingPurpose').querySelector('h3.paper-sub');
    expect(heading?.textContent).toBe('Agree the library layout before build starts.');
    expect(blockKind(currentBlock(article, select(heading ?? p, 2)))).toBe('heading');
    expect(setBlock(article, select(heading ?? p, 2), 'paragraph')).toBe(true);
    expect(module(article, 'meetingPurpose').querySelector('h3')).toBeNull();
    expect(module(article, 'meetingPurpose').querySelectorAll('p')).toHaveLength(1);
    expect(markupProblems(article)).toEqual([]);
  });

  it('makes lists, switches their kind and lifts an item out, keeping its timestamp chips', () => {
    const article = mount();
    const before = chips(article);
    const decisions = module(article, 'decisions');
    const second = decisions.querySelectorAll('li')[1];
    if (second === undefined) {
      throw new Error('no item');
    }
    expect(blockKind(currentBlock(article, select(second, 2)))).toBe('bulleted');
    expect(toggleList(article, select(second, 2), true)).toBe(true);
    expect(decisions.querySelector('ol')?.children).toHaveLength(4);
    expect(decisions.querySelector('ul')).toBeNull();
    // The same kind again lifts the item out into a paragraph between two lists.
    const item = decisions.querySelectorAll('li')[1];
    expect(item === undefined ? false : toggleList(article, select(item, 2), true)).toBe(true);
    expect([...decisions.children].map((c) => c.tagName.toLowerCase())).toEqual(['h2', 'ol', 'p', 'ol']);
    expect(decisions.querySelector('p')?.querySelector('a.ts')?.textContent).toBe('18:42');
    // A paragraph becomes a bulleted list.
    const purpose = module(article, 'meetingPurpose').querySelector('p');
    expect(purpose === null ? false : toggleList(article, select(purpose, 1), false)).toBe(true);
    expect(module(article, 'meetingPurpose').querySelector('ul > li')?.textContent).toBe('Agree the library layout before build starts.');
    expect(markupProblems(article)).toEqual([]);
    expect(chips(article)).toEqual(before);
  });

  it('inserts a table with a header row after the block, and a timestamp chip at the caret', () => {
    const article = mount();
    const p = module(article, 'meetingPurpose').querySelector('p');
    if (p === null) {
      throw new Error('no paragraph');
    }
    expect(insertTable(article, select(p, 4))).toBe(true);
    const table = module(article, 'meetingPurpose').querySelector('table.paper-table');
    expect(table?.previousElementSibling).toBe(p);
    expect([...(table?.querySelectorAll('thead th') ?? [])].map((th) => th.textContent)).toEqual(['Column 1', 'Column 2', 'Column 3']);
    expect(table?.querySelectorAll('tbody td')).toHaveLength(3);
    expect(insertTimestamp(article, select(p, 5), 1122)).toBe(true);
    expect(p.innerHTML).toBe('Agree<a class="ts" href="#t=1122" data-t="1122" contenteditable="false">18:42</a>  the library layout before build starts.');
    // Not inside the transcript table (data, locked) or the title block.
    expect(insertTimestamp(article, select(article.querySelector('.tr-x') ?? p, 2), 5)).toBe(false);
    expect(insertTimestamp(article, select(article.querySelector('.paper-title') ?? p, 2), 5)).toBe(false);
    expect(markupProblems(article)).toEqual([]);
  });
});
