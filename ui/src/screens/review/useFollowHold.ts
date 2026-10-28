// 2.7.0: any interaction with the transcript or People holds the transcript's follow-the-playhead
// (state/followPlayhead.ts): pointer presses, keys, typing, focus in a field, text selection, and the
// menus those areas open (they float on <body>, components/Floating.tsx). Areas opt in with
// `data-follow-hold`; the page's own focus moves and scrolls do not count.
import { useEffect } from 'preact/hooks';
import type { FollowPlayhead } from '../../state/followPlayhead';

const AREA = '[data-follow-hold]';
const FLOATING = '[role="menu"],[role="listbox"],.popover,.speaker-menu';

function isField(target: EventTarget | null): boolean {
  return (
    target instanceof HTMLInputElement ||
    target instanceof HTMLTextAreaElement ||
    target instanceof HTMLSelectElement ||
    (target instanceof HTMLElement && target.isContentEditable)
  );
}

function inArea(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest(AREA) !== null;
}

/** Wires the document's interaction events to `follow` (read through the ref, so the pane can remount). */
export function useFollowHold(followRef: { current: FollowPlayhead | null }): void {
  useEffect(() => {
    const doc = document;
    const onAction = (event: Event): void => {
      const follow = followRef.current;
      if (follow === null) {
        return;
      }
      const target = event.target;
      if (inArea(target)) {
        follow.noteInteraction();
        return;
      }
      // A menu opened from the transcript or People keeps the hold while it is used.
      if (target instanceof Element && target.closest(FLOATING) !== null && !follow.shouldFollow()) {
        follow.noteInteraction();
      }
    };
    const onFocusIn = (event: FocusEvent): void => {
      if (isField(event.target) && inArea(event.target)) {
        followRef.current?.setFieldFocused(true);
      }
    };
    const onFocusOut = (event: FocusEvent): void => {
      if (isField(event.target) && inArea(event.target) && !(isField(event.relatedTarget) && inArea(event.relatedTarget))) {
        followRef.current?.setFieldFocused(false);
      }
    };
    const onSelection = (): void => {
      const selection = doc.getSelection();
      if (selection === null || selection.isCollapsed) {
        return;
      }
      const node = selection.anchorNode;
      const element = node instanceof Element ? node : (node?.parentElement ?? null);
      if (inArea(element)) {
        followRef.current?.noteInteraction();
      }
    };
    const events = ['pointerdown', 'keydown', 'input', 'contextmenu'] as const;
    for (const name of events) {
      doc.addEventListener(name, onAction, true);
    }
    doc.addEventListener('focusin', onFocusIn, true);
    doc.addEventListener('focusout', onFocusOut, true);
    doc.addEventListener('selectionchange', onSelection);
    return () => {
      for (const name of events) {
        doc.removeEventListener(name, onAction, true);
      }
      doc.removeEventListener('focusin', onFocusIn, true);
      doc.removeEventListener('focusout', onFocusOut, true);
      doc.removeEventListener('selectionchange', onSelection);
      followRef.current?.setFieldFocused(false);
    };
  }, [followRef]);
}
