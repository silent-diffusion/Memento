// Where a click landed in a piece of text, as a character offset into the words the reader sees, so
// an editor that replaces the text can put its caret there (Review: click a line to correct it).
// Screen-reader-only additions (`.sr`, "(low confidence)") are not part of the text and are skipped.

const HIDDEN = '.sr';

/** The offset into the visible text of `root` of the boundary point (`node`, `offset`). */
export function visibleTextOffset(root: Node, node: Node, offset: number): number {
  const doc = root.ownerDocument ?? document;
  const boundary = doc.createRange();
  boundary.setStart(node, offset);
  boundary.collapse(true);
  const walker = doc.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  let total = 0;
  for (let text = walker.nextNode() as Text | null; text !== null; text = walker.nextNode() as Text | null) {
    const hidden = (text.parentElement?.closest(HIDDEN) ?? null) !== null;
    if (text === node) {
      return total + (hidden ? 0 : Math.min(offset, text.length));
    }
    // A text node that starts before the boundary lies wholly before it (the boundary is not inside it).
    if (boundary.comparePoint(text, 0) < 0) {
      total += hidden ? 0 : text.length;
    } else {
      break;
    }
  }
  return total;
}

/** WebView2 (Chromium) has caretPositionFromPoint; without it (jsdom) the editor puts the caret at the end. */
interface CaretDocument {
  caretPositionFromPoint?: (x: number, y: number) => { offsetNode: Node; offset: number } | null;
}

/** The visible-text offset under the point (x, y) inside `root`, or null when the browser cannot tell. */
export function caretOffsetAt(root: HTMLElement, x: number, y: number): number | null {
  const doc = root.ownerDocument as Document & CaretDocument;
  let node: Node | null = null;
  let offset = 0;
  if (typeof doc.caretPositionFromPoint === 'function') {
    const position = doc.caretPositionFromPoint(x, y);
    node = position?.offsetNode ?? null;
    offset = position?.offset ?? 0;
  }
  if (node === null || !root.contains(node)) {
    return null;
  }
  return visibleTextOffset(root, node, offset);
}

/** Text is being selected (dragged over) rather than clicked: leave it to the selection. */
export function isSelectingIn(root: HTMLElement): boolean {
  const selection = root.ownerDocument.getSelection();
  if (selection === null || selection.isCollapsed || selection.rangeCount === 0) {
    return false;
  }
  const range = selection.getRangeAt(0);
  return root.contains(range.commonAncestorContainer);
}
