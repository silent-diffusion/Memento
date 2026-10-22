import type { ComponentChildren, JSX } from 'preact';
import { useEffect, useRef } from 'preact/hooks';
import { useServices } from '../state/context';
import { navigationOf } from '../state/navigation';
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
  /** After the meta text, e.g. Review's "Reviewed" pill. */
  metaExtra?: ComponentChildren;
  /** Right-aligned actions: ghost buttons first, the one primary action last. */
  actions?: ComponentChildren;
  /**
   * Focus the back control when the spoke opens (default). The Recording session opts out so Space
   * pauses rather than pressing Back; the back control is still the first Tab stop.
   */
  focusBack?: boolean;
  /**
   * What a back control that returns to this screen reads ("Design review", "Meeting minutes"); defaults to the
   * title when it is plain text. Pushed onto the navigation trail when the person moves on (state/navigation.ts).
   */
  selfLabel?: string;
  /**
   * Esc presses Back once nothing else on the screen wants it (default). The Recording session turns it off (§8: Esc
   * does nothing there), and the Builder while the template has unsaved changes. Alt+← always works.
   */
  escapeBack?: boolean;
}

/**
 * The spoke header (DESIGN.md §3): back control first in the tab order, a 24 px groove, the title,
 * then actions. The back control is focused when the spoke opens, and Esc, Alt+← and the mouse's back
 * button press it (state/back.ts).
 */
export function SpokeHeader({ backLabel, onBack, title, titleEditor, meta, metaExtra, actions, focusBack = true, selfLabel, escapeBack = true }: SpokeHeaderProps): JSX.Element {
  const { store } = useServices();
  const back = useRef<HTMLButtonElement | null>(null);
  const pressBack = useRef(onBack);
  pressBack.current = onBack;
  useEffect(() => {
    // A dialog that opened with the screen (recovery at launch) keeps its focus.
    if (focusBack && (document.activeElement === document.body || document.activeElement === null)) {
      back.current?.focus({ preventScroll: true });
    }
  }, [focusBack]);
  // The keyboard and the mouse's back button reach the back control through the navigation state.
  useEffect(() => {
    const nav = navigationOf(store);
    const press = (): void => {
      pressBack.current();
    };
    nav.back = press;
    return () => {
      if (nav.back === press) {
        nav.back = null;
      }
    };
  }, [store]);
  const ownLabel = selfLabel ?? (typeof title === 'string' ? title : null);
  // After every render, so the leaving screen's cleanup (which runs before the new screen's effects) never wins.
  useEffect(() => {
    const nav = navigationOf(store);
    nav.escapeBack = escapeBack;
    nav.label = ownLabel;
  });
  return (
    <header class="app-header spoke-header">
      <button
        ref={back}
        class="btn ghost back-btn"
        type="button"
        onClick={onBack}
        data-spoke-back
        title={`Back to ${backLabel} (${escapeBack ? 'Esc or ' : ''}Alt+←)`}
        aria-keyshortcuts={escapeBack ? 'Escape Alt+ArrowLeft' : 'Alt+ArrowLeft'}
      >
        <ChevronLeftIcon size={16} />
        <span class="back-label">{backLabel}</span>
      </button>
      <span class="spoke-divider" aria-hidden="true" />
      {titleEditor === undefined ? (
        <div class="spoke-title-block">
          <span class="spoke-title">{title}</span>
          {meta === undefined ? null : (
            <span class="spoke-meta-row">
              <span class="spoke-meta">{meta}</span>
              {metaExtra ?? null}
            </span>
          )}
        </div>
      ) : (
        <div class="spoke-title-edit">{titleEditor}</div>
      )}
      {actions === undefined ? null : <div class="spoke-actions">{actions}</div>}
    </header>
  );
}
