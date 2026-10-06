import type { ComponentChildren, JSX } from 'preact';
import { useEffect, useRef } from 'preact/hooks';
import { ChevronLeftIcon } from './icons';

interface SpokeHeaderProps {
  /** The screen the back control returns to ("Library"). */
  backLabel: string;
  onBack: () => void;
  /** Static title text (Review, Settings). */
  title?: ComponentChildren;
  /** In place of the title: a title input and its controls (Record, Builder). */
  titleEditor?: ComponentChildren;
  meta?: string;
  /** Right-aligned actions: ghost buttons first, the one primary action last. */
  actions?: ComponentChildren;
  /**
   * Focus the back control when the spoke opens (default). The Recording session opts out so Space
   * pauses rather than pressing Back; the back control is still the first Tab stop.
   */
  focusBack?: boolean;
}

/**
 * The spoke header (DESIGN.md §3): back control first in the tab order, a 24 px groove, the title,
 * then actions. The back control is focused when the spoke opens.
 */
export function SpokeHeader({ backLabel, onBack, title, titleEditor, meta, actions, focusBack = true }: SpokeHeaderProps): JSX.Element {
  const back = useRef<HTMLButtonElement | null>(null);
  useEffect(() => {
    // A dialog that opened with the screen (recovery at launch) keeps its focus.
    if (focusBack && (document.activeElement === document.body || document.activeElement === null)) {
      back.current?.focus({ preventScroll: true });
    }
  }, [focusBack]);
  return (
    <header class="app-header spoke-header">
      <button ref={back} class="btn ghost back-btn" type="button" onClick={onBack} data-spoke-back>
        <ChevronLeftIcon size={16} />
        {backLabel}
      </button>
      <span class="spoke-divider" aria-hidden="true" />
      {titleEditor === undefined ? (
        <div class="spoke-title-block">
          <span class="spoke-title">{title}</span>
          {meta === undefined ? null : <span class="spoke-meta">{meta}</span>}
        </div>
      ) : (
        <div class="spoke-title-edit">{titleEditor}</div>
      )}
      {actions === undefined ? null : <div class="spoke-actions">{actions}</div>}
    </header>
  );
}
