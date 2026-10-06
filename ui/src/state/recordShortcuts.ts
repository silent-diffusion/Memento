// Recording session keys (DESIGN.md §8): Space pauses or resumes, Ctrl+M marks a highlight, Esc does
// nothing so a stray press can never stop a recording.

export type RecordShortcut = 'togglePause' | 'mark' | 'swallow';

export interface ShortcutEvent {
  key: string;
  ctrlKey: boolean;
  metaKey: boolean;
  altKey: boolean;
  shiftKey: boolean;
  repeat: boolean;
  target: EventTarget | null;
}

const TEXT_INPUT_TYPES = new Set(['text', 'search', 'email', 'url', 'tel', 'password', 'number', '']);

/** True for a field where Space types a character. */
export function isTextEntry(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }
  if (target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement || target.isContentEditable) {
    return true;
  }
  return target instanceof HTMLInputElement && TEXT_INPUT_TYPES.has(target.type);
}

/** True for a control that Space already activates (a button, switch, slider, tab, link, checkbox). */
function spaceActivates(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }
  if (target instanceof HTMLButtonElement || target instanceof HTMLAnchorElement || target instanceof HTMLInputElement) {
    return true;
  }
  const role = target.getAttribute('role');
  return role !== null && ['button', 'switch', 'slider', 'tab', 'option', 'menuitem', 'checkbox', 'radio', 'link'].includes(role);
}

/**
 * What a keydown means on the Recording session, or null to leave it alone. Space never fires from a
 * text field (it types) or from a focused control (it presses that control instead); Ctrl+M works
 * everywhere, including while writing a highlight note.
 */
export function recordShortcut(event: ShortcutEvent): RecordShortcut | null {
  const plain = !event.ctrlKey && !event.metaKey && !event.altKey;
  if (event.key === 'Escape' && plain) {
    return isTextEntry(event.target) ? null : 'swallow';
  }
  if ((event.key === 'm' || event.key === 'M') && event.ctrlKey && !event.altKey && !event.metaKey && !event.shiftKey) {
    return event.repeat ? 'swallow' : 'mark';
  }
  if ((event.key === ' ' || event.key === 'Spacebar') && plain && !event.shiftKey) {
    if (isTextEntry(event.target) || spaceActivates(event.target)) {
      return null;
    }
    return event.repeat ? 'swallow' : 'togglePause';
  }
  return null;
}
