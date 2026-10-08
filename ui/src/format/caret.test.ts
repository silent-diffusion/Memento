import { afterEach, describe, expect, it } from 'vitest';
import { caretOffsetAt, isSelectingIn, visibleTextOffset } from './caret';

describe('caret offsets for click-to-edit', () => {
  afterEach(() => {
    document.body.innerHTML = '';
    document.getSelection()?.removeAllRanges();
  });

  const line = (): HTMLElement => {
    const root = document.createElement('span');
    // What SegmentText draws: plain runs, a low-confidence word with its screen-reader note, a search match.
    root.innerHTML = 'And it keeps the <span class="lowc">prototype<span class="sr"> (low confidence)</span></span> honest, <span class="hl">dark theme</span> too.';
    document.body.append(root);
    return root;
  };

  it('counts only the words the reader sees', () => {
    const root = line();
    const [first, low, , match, last] = [...root.childNodes];
    if (first === undefined || low === undefined || match === undefined || last === undefined) {
      throw new Error('unexpected markup');
    }
    expect(visibleTextOffset(root, first, 4)).toBe(4);
    const lowWord = low.firstChild;
    const srText = low.lastChild?.firstChild;
    if (lowWord === null || srText === null || srText === undefined) {
      throw new Error('unexpected markup');
    }
    expect(visibleTextOffset(root, lowWord, 5)).toBe('And it keeps the proto'.length);
    // A click on the hidden note lands at the end of the word before it.
    expect(visibleTextOffset(root, srText, 6)).toBe('And it keeps the prototype'.length);
    const after = match.firstChild;
    if (after === null) {
      throw new Error('unexpected markup');
    }
    expect(visibleTextOffset(root, after, 4)).toBe('And it keeps the prototype honest, dark'.length);
    expect(visibleTextOffset(root, last, last.textContent?.length ?? 0)).toBe('And it keeps the prototype honest, dark theme too.'.length);
    // An element boundary: before the second child.
    expect(visibleTextOffset(root, root, 1)).toBe('And it keeps the '.length);
  });

  it('uses the browser’s caret position under the pointer when it has one', () => {
    const root = line();
    const target = root.childNodes[2];
    if (target === undefined) {
      throw new Error('unexpected markup');
    }
    let answer: { offsetNode: Node; offset: number } = { offsetNode: target, offset: 3 };
    Object.defineProperty(document, 'caretPositionFromPoint', { value: () => answer, configurable: true });
    try {
      expect(caretOffsetAt(root, 10, 10)).toBe('And it keeps the prototype ho'.length);
      // A point outside the line gives nothing.
      answer = { offsetNode: document.body, offset: 0 };
      expect(caretOffsetAt(root, 10, 10)).toBeNull();
    } finally {
      Reflect.deleteProperty(document, 'caretPositionFromPoint');
    }
    expect(caretOffsetAt(root, 10, 10)).toBeNull();
  });

  it('tells a drag selection from a click', () => {
    const root = line();
    const selection = document.getSelection();
    const text = root.firstChild;
    if (selection === null || text === null) {
      throw new Error('no selection');
    }
    expect(isSelectingIn(root)).toBe(false);
    const range = document.createRange();
    range.setStart(text, 0);
    range.setEnd(text, 3);
    selection.addRange(range);
    expect(isSelectingIn(root)).toBe(true);
  });
});
