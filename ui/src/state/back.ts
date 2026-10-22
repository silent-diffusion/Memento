// The spoke header's back control and the ways to press it without the mouse (DESIGN.md §3, USER-GUIDE › Keyboard):
//   - Esc goes back once nothing else wants it. Menus, popovers, inline fields, dialogs and sheets take Esc first
//     (they stop it or mark it handled), and so do Review's filter, line selection and fullscreen video; the key only
//     reaches this handler when none of them is open. Never on the Recording session (Esc does nothing there, §8),
//     never while typing in a field, and not in the Builder while the template has unsaved changes.
//   - Alt+← and the keyboard's Browser Back key go back from any spoke, as in other Windows apps.
//   - The mouse's back button does the same, instead of stepping through WebView2's page history.
import { useServices } from './context';
import { backTargetOf, navigationOf, type BackTarget } from './navigation';
import type { AppStore } from './store';

export interface BackControl extends BackTarget {
  /** Moves there (a back move: the trail steps back). */
  go: () => void;
}

/** Where the open spoke's Back goes: the screen it was opened from when that is accepted, else `fallback`. */
export function useBack(fallback: BackTarget): BackControl {
  const { store, router } = useServices();
  const target = backTargetOf(navigationOf(store), store.route.value, fallback);
  return {
    ...target,
    go: () => {
      router.navigate(target.route, { back: true });
    },
  };
}

/** True for a field the key belongs to: inputs, text areas, selects and editable text. */
export function isTypingTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }
  if (target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement || target.isContentEditable) {
    return true;
  }
  if (target instanceof HTMLInputElement) {
    return !['checkbox', 'radio', 'button', 'submit', 'reset', 'range', 'color', 'file'].includes(target.type);
  }
  return false;
}

/** A menu, popover or listbox the key may belong to, even when it did not stop the key itself. */
function inPopover(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest('[role="menu"],[role="dialog"],[role="listbox"],.popover,.speaker-menu') !== null;
}

/** What the global key and mouse handlers decide for one event: go back, or leave it alone. */
export function wantsBack(event: KeyboardEvent, store: AppStore): boolean {
  const nav = navigationOf(store);
  if (nav.back === null || event.defaultPrevented || store.overlays.value > 0 || store.dialog.value !== null) {
    return false;
  }
  if ((event.altKey && !event.ctrlKey && !event.shiftKey && !event.metaKey && event.key === 'ArrowLeft') || event.key === 'BrowserBack') {
    return true;
  }
  if (event.key !== 'Escape' || event.altKey || event.ctrlKey || event.shiftKey || event.metaKey) {
    return false;
  }
  return nav.escapeBack && !isTypingTarget(event.target) && !inPopover(event.target);
}

/**
 * Esc, Alt+←, Browser Back and the mouse's back button press the open spoke's back control. Listens on the window
 * (bubble phase), so every handler on the page has had the key first. Returns the disconnect function.
 */
export function connectBackKeys(store: AppStore, win: Window = window): () => void {
  const onKey = (event: KeyboardEvent): void => {
    if (wantsBack(event, store)) {
      event.preventDefault();
      navigationOf(store).back?.();
    }
  };
  // The mouse's back (3) and forward (4) buttons: WebView2 would step through the page's history. Back presses the back
  // control instead; forward does nothing (there is no forward in hub and spoke).
  const onMouse = (event: MouseEvent): void => {
    if (event.button !== 3 && event.button !== 4) {
      return;
    }
    event.preventDefault();
    if (event.type === 'mouseup' && event.button === 3 && store.overlays.value === 0 && store.dialog.value === null) {
      navigationOf(store).back?.();
    }
  };
  win.addEventListener('keydown', onKey);
  win.addEventListener('mousedown', onMouse);
  win.addEventListener('mouseup', onMouse);
  return () => {
    win.removeEventListener('keydown', onKey);
    win.removeEventListener('mousedown', onMouse);
    win.removeEventListener('mouseup', onMouse);
  };
}
