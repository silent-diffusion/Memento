// DESIGN.md §2.4: keyboard activation (Enter, Space) plays the same press animation as a click.
// Chromium only applies :active for Space on buttons, so the focused control gets `is-pressed`
// while Enter or Space is held. The styles live next to the :active rules in shell.css.

const PRESSABLE = 'button.btn, button.icon-btn, button.chip, button.seg, button.nav, button.row, button.card';
const PRESSED_CLASS = 'is-pressed';

function pressableTarget(event: Event): HTMLElement | null {
  const target = event.target;
  if (!(target instanceof HTMLElement) || !target.matches(PRESSABLE)) {
    return null;
  }
  return target instanceof HTMLButtonElement && target.disabled ? null : target;
}

function isActivationKey(event: KeyboardEvent): boolean {
  return event.key === 'Enter' || event.key === ' ';
}

/** Installs the keyboard press feedback on `root`. Returns the uninstall function. */
export function installPressFeedback(root: Document | HTMLElement): () => void {
  const onKeyDown = (event: Event): void => {
    if (event instanceof KeyboardEvent && isActivationKey(event)) {
      pressableTarget(event)?.classList.add(PRESSED_CLASS);
    }
  };
  const release = (event: Event): void => {
    if (event instanceof KeyboardEvent && !isActivationKey(event)) {
      return;
    }
    pressableTarget(event)?.classList.remove(PRESSED_CLASS);
  };
  root.addEventListener('keydown', onKeyDown);
  root.addEventListener('keyup', release);
  root.addEventListener('focusout', release);
  return () => {
    root.removeEventListener('keydown', onKeyDown);
    root.removeEventListener('keyup', release);
    root.removeEventListener('focusout', release);
  };
}
