// Modal surfaces (DESIGN.md §5.19): the dialog and the side sheet. Both render into document.body
// above a scrim that covers everything below the header, trap focus, restore it on close, and make
// the shell inert while open. Esc runs `onEscape`, which must always be the non-destructive outcome.
import type { ComponentChildren, JSX } from 'preact';
import { createPortal } from 'preact/compat';
import { useEffect, useRef } from 'preact/hooks';
import { useServices } from '../state/context';
import { CloseIcon } from './icons';
import { focusableWithin, trapTab } from './keyboard';

/**
 * Focus goes to the element marked `data-autofocus`, else the first focusable element that is not a
 * destructive button (`.d`), so Enter never deletes by default.
 */
export function initialFocusTarget(container: HTMLElement): HTMLElement | null {
  const marked = container.querySelector<HTMLElement>('[data-autofocus]');
  if (marked !== null) {
    return marked;
  }
  return focusableWithin(container).find((el) => !el.classList.contains('d')) ?? null;
}

/** Focus, Esc, Tab trapping and the inert shell for a modal surface (M3: the export dialog uses it directly). */
export function useModal(onEscape: () => void): {
  ref: { current: HTMLDivElement | null };
  onKeyDown: (event: KeyboardEvent) => void;
} {
  const { store } = useServices();
  const ref = useRef<HTMLDivElement | null>(null);
  const escape = useRef(onEscape);
  escape.current = onEscape;

  useEffect(() => {
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    store.overlays.value += 1;
    const container = ref.current;
    if (container !== null) {
      (initialFocusTarget(container) ?? container).focus();
    }
    return () => {
      store.overlays.value = Math.max(0, store.overlays.value - 1);
      // Wait for the shell to lose `inert`, then put focus back where it was.
      queueMicrotask(() => {
        if (previous?.isConnected === true) {
          previous.focus();
        }
      });
    };
  }, [store]);

  const onKeyDown = (event: KeyboardEvent): void => {
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      escape.current();
      return;
    }
    if (ref.current !== null) {
      trapTab(event, ref.current);
    }
  };
  return { ref, onKeyDown };
}

interface DialogProps {
  titleId: string;
  title: string;
  /** What Esc does: Cancel, Later. Never the destructive action. */
  onEscape: () => void;
  children: ComponentChildren;
  /** Right-aligned action buttons: ghost first, then the primary or destructive one. */
  actions: ComponentChildren;
  width?: number;
}

export function Dialog({ titleId, title, onEscape, children, actions, width = 560 }: DialogProps): JSX.Element {
  const { ref, onKeyDown } = useModal(onEscape);
  return createPortal(
    <div class="scrim">
      <div
        ref={ref}
        class="dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        style={{ width: `min(${width}px, calc(100vw - 48px))` }}
        onKeyDown={onKeyDown}
      >
        <div class="dialog-text">
          <h2 id={titleId} class="dialog-title">
            {title}
          </h2>
          {children}
        </div>
        <div class="dialog-actions">{actions}</div>
      </div>
    </div>,
    document.body,
  );
}

interface SideSheetProps {
  titleId: string;
  title: string;
  onClose: () => void;
  children: ComponentChildren;
  /** Footer: a note at the left and the primary action at the right. */
  footer?: ComponentChildren;
}

/** The 480 px side sheet anchored right below the header (DESIGN.md §5.19, §14). */
export function SideSheet({ titleId, title, onClose, children, footer }: SideSheetProps): JSX.Element {
  const { ref, onKeyDown } = useModal(onClose);
  return createPortal(
    <div class="scrim scrim--sheet">
      <div ref={ref} class="sheet" role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1} onKeyDown={onKeyDown}>
        <div class="sheet-header">
          <h2 id={titleId} class="sheet-title">
            {title}
          </h2>
          <button class="icon-btn sheet-close" type="button" aria-label="Close" onClick={onClose}>
            <CloseIcon size={18} />
          </button>
        </div>
        <div class="sheet-body">{children}</div>
        {footer === undefined ? null : <div class="sheet-footer">{footer}</div>}
      </div>
    </div>,
    document.body,
  );
}
