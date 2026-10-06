import type { ComponentChildren, JSX } from 'preact';
import { useEffect, useRef } from 'preact/hooks';
import { ChevronLeftIcon } from './icons';

interface SpokeHeaderProps {
  /** The screen the back control returns to ("Library"). */
  backLabel: string;
  onBack: () => void;
  /** Static title text, or a title input for Record and Builder. */
  title: ComponentChildren;
  meta?: string;
  /** Right-aligned actions: ghost buttons first, the one primary action last. */
  actions?: ComponentChildren;
}

/**
 * The spoke header (DESIGN.md §3): back control first in the tab order, a 24 px groove, the title,
 * then actions. The back control is focused when the spoke opens.
 */
export function SpokeHeader({ backLabel, onBack, title, meta, actions }: SpokeHeaderProps): JSX.Element {
  const back = useRef<HTMLButtonElement | null>(null);
  useEffect(() => {
    // A dialog that opened with the screen (recovery at launch) keeps its focus.
    if (document.activeElement === document.body || document.activeElement === null) {
      back.current?.focus({ preventScroll: true });
    }
  }, []);
  return (
    <header class="app-header spoke-header">
      <button ref={back} class="btn ghost back-btn" type="button" onClick={onBack} data-spoke-back>
        <ChevronLeftIcon size={16} />
        {backLabel}
      </button>
      <span class="spoke-divider" aria-hidden="true" />
      <div class="spoke-title-block">
        <span class="spoke-title">{title}</span>
        {meta === undefined ? null : <span class="spoke-meta">{meta}</span>}
      </div>
      {actions === undefined ? null : <div class="spoke-actions">{actions}</div>}
    </header>
  );
}
