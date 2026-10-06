// Keyboard helpers (DESIGN.md §7): focus trapping for dialogs and sheets, and arrow-key movement
// inside chip groups, segmented controls and menus.

const FOCUSABLE = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(',');

/** Tabbable elements inside `container`, in DOM order. */
export function focusableWithin(container: HTMLElement): HTMLElement[] {
  return [...container.querySelectorAll<HTMLElement>(FOCUSABLE)].filter(
    (el) => el.tabIndex >= 0 && !el.hasAttribute('hidden') && el.closest('[hidden],[inert]') === null,
  );
}

/**
 * Keeps Tab and Shift+Tab inside `container`. Returns true when it moved focus (the caller should
 * then not let the event continue).
 */
export function trapTab(event: KeyboardEvent, container: HTMLElement): boolean {
  if (event.key !== 'Tab') {
    return false;
  }
  const items = focusableWithin(container);
  const first = items[0];
  const last = items.at(-1);
  if (first === undefined || last === undefined) {
    event.preventDefault();
    return true;
  }
  const active = document.activeElement;
  const inside = active instanceof HTMLElement && container.contains(active);
  if (event.shiftKey && (active === first || !inside)) {
    event.preventDefault();
    last.focus();
    return true;
  }
  if (!event.shiftKey && (active === last || !inside)) {
    event.preventDefault();
    first.focus();
    return true;
  }
  return false;
}

export type RovingAxis = 'horizontal' | 'vertical' | 'both';

/**
 * Arrow keys (and Home/End) move focus among `selector` items inside `container`, wrapping at the
 * ends. Returns the newly focused item, or null when the key was not a movement key.
 */
export function moveFocus(
  event: KeyboardEvent,
  container: HTMLElement,
  selector: string,
  axis: RovingAxis = 'horizontal',
): HTMLElement | null {
  const back = axis === 'vertical' ? ['ArrowUp'] : axis === 'horizontal' ? ['ArrowLeft'] : ['ArrowLeft', 'ArrowUp'];
  const forward = axis === 'vertical' ? ['ArrowDown'] : axis === 'horizontal' ? ['ArrowRight'] : ['ArrowRight', 'ArrowDown'];
  const isMove = back.includes(event.key) || forward.includes(event.key) || event.key === 'Home' || event.key === 'End';
  if (!isMove) {
    return null;
  }
  const items = [...container.querySelectorAll<HTMLElement>(selector)].filter(
    (el) => !(el instanceof HTMLButtonElement && el.disabled),
  );
  if (items.length === 0) {
    return null;
  }
  const current = items.findIndex((el) => el === document.activeElement);
  let next: number;
  if (event.key === 'Home') {
    next = 0;
  } else if (event.key === 'End') {
    next = items.length - 1;
  } else if (back.includes(event.key)) {
    next = current <= 0 ? items.length - 1 : current - 1;
  } else {
    next = current < 0 || current === items.length - 1 ? 0 : current + 1;
  }
  event.preventDefault();
  const target = items[next] ?? null;
  target?.focus();
  return target;
}
