// Popover menus: a select-style ghost button with a listbox (sort, settings values) and an actions
// menu (the row ⋯). Keyboard: Enter, Space or ArrowDown opens; arrows move; Enter picks; Esc closes
// and returns focus to the button; Tab closes. Inside a pane that scrolls on its own the popover
// floats on <body> (Floating.tsx) so the pane cannot clip it.
import type { ComponentChildren, JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import { PopoverLayer, useFloatingPopovers } from './Floating';
import { CheckIcon, ChevronDownIcon } from './icons';
import { moveFocus } from './keyboard';

function usePopover(): {
  open: boolean;
  floating: boolean;
  show: (focus: 'selected' | 'first') => void;
  close: (returnFocus: boolean) => void;
  rootRef: { current: HTMLDivElement | null };
  buttonRef: { current: HTMLButtonElement | null };
  popRef: { current: HTMLElement | null };
} {
  const [open, setOpen] = useState(false);
  const [focusOn, setFocusOn] = useState<'selected' | 'first'>('selected');
  const rootRef = useRef<HTMLDivElement | null>(null);
  const buttonRef = useRef<HTMLButtonElement | null>(null);
  const popRef = useRef<HTMLElement | null>(null);
  const floating = useFloatingPopovers();

  useEffect(() => {
    if (!open) {
      return undefined;
    }
    const pop = popRef.current;
    const target =
      (focusOn === 'selected' ? pop?.querySelector<HTMLElement>('[aria-selected="true"]') : null) ??
      pop?.querySelector<HTMLElement>('[role="option"],[role="menuitem"]');
    target?.focus();
    const onPointerDown = (event: PointerEvent): void => {
      const inside = event.target instanceof Node && (rootRef.current?.contains(event.target) === true || popRef.current?.contains(event.target) === true);
      if (event.target instanceof Node && !inside) {
        setOpen(false);
      }
    };
    document.addEventListener('pointerdown', onPointerDown);
    return () => {
      document.removeEventListener('pointerdown', onPointerDown);
    };
  }, [open, focusOn]);

  return {
    open,
    floating,
    show: (focus) => {
      setFocusOn(focus);
      setOpen(true);
    },
    close: (returnFocus) => {
      setOpen(false);
      if (returnFocus) {
        buttonRef.current?.focus();
      }
    },
    rootRef,
    buttonRef,
    popRef,
  };
}

function popKeyDown(
  event: KeyboardEvent,
  pop: HTMLElement | null,
  selector: string,
  close: (returnFocus: boolean) => void,
  floating = false,
): void {
  if (event.key === 'Escape') {
    event.preventDefault();
    event.stopPropagation();
    close(true);
    return;
  }
  if (event.key === 'Tab') {
    // A floating popover sits at the end of <body>: hand focus back to the button first, so Tab
    // moves on from the button as it would from a popover in place.
    close(floating);
    return;
  }
  if (pop !== null) {
    moveFocus(event, pop, selector, 'vertical');
  }
}

export interface SelectOption<T extends string> {
  value: T;
  label: string;
}

interface SelectMenuProps<T extends string> {
  /** Accessible name of the choice ("Sort", "Recording type"). */
  label: string;
  value: T;
  options: readonly SelectOption<T>[];
  onChange: (value: T) => void;
  /** Visible button text; defaults to the chosen option's label. */
  buttonText?: string;
  /** `sort` (36 px, text-2) or `field` (34 px settings value, text). */
  variant?: 'sort' | 'field' | 'chip';
  disabled?: boolean;
}

export function SelectMenu<T extends string>({
  label,
  value,
  options,
  onChange,
  buttonText,
  variant = 'field',
  disabled = false,
}: SelectMenuProps<T>): JSX.Element {
  const pop = usePopover();
  const text = buttonText ?? options.find((o) => o.value === value)?.label ?? value;
  const listId = useRef(`listbox-${Math.random().toString(36).slice(2, 9)}`).current;

  const pick = (option: T): void => {
    pop.close(true);
    if (option !== value) {
      onChange(option);
    }
  };

  return (
    <div class="menu-root" ref={pop.rootRef}>
      <button
        ref={pop.buttonRef}
        class={variant === 'sort' ? 'btn ghost sort-btn' : variant === 'chip' ? 'btn ghost chip-select' : 'btn ghost select-btn'}
        type="button"
        aria-haspopup="listbox"
        aria-expanded={pop.open}
        aria-controls={pop.open ? listId : undefined}
        aria-label={`${label}: ${text}`}
        disabled={disabled}
        onClick={() => {
          if (pop.open) {
            pop.close(false);
          } else {
            pop.show('selected');
          }
        }}
        onKeyDown={(event) => {
          if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            pop.show('selected');
          }
        }}
      >
        <span class="select-text">{text}</span>
        <ChevronDownIcon size={14} class="select-chevron" />
      </button>
      {pop.open ? (
        <PopoverLayer anchorRef={pop.rootRef} popRef={pop.popRef}>
          <ul
            id={listId}
            ref={(el) => {
              pop.popRef.current = el;
            }}
            class="popover"
            role="listbox"
            aria-label={label}
            onKeyDown={(event) => {
              popKeyDown(event, pop.popRef.current, '[role="option"]', pop.close, pop.floating);
            }}
          >
            {options.map((option) => {
              const selected = option.value === value;
              return (
                <li
                  key={option.value}
                  class="item menu-item"
                  role="option"
                  aria-selected={selected}
                  tabIndex={-1}
                  onClick={() => {
                    pick(option.value);
                  }}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter' || event.key === ' ') {
                      event.preventDefault();
                      pick(option.value);
                    }
                  }}
                >
                  <span class="menu-check" aria-hidden="true">
                    {selected ? <CheckIcon size={12} /> : null}
                  </span>
                  {option.label}
                </li>
              );
            })}
          </ul>
        </PopoverLayer>
      ) : null}
    </div>
  );
}

interface MultiSelectMenuProps<T extends string> {
  /** Accessible name of the choice ("Format for Transcript"). */
  label: string;
  values: readonly T[];
  options: readonly SelectOption<T>[];
  /** The new choice, in option order; never empty (the last ticked option cannot be unticked). */
  onChange: (values: T[]) => void;
  disabled?: boolean;
}

/**
 * A select-style button whose listbox takes several values (the transcript's formats in the
 * Export dialog: "JSON + SRT"). Enter or Space ticks or unticks an option and keeps the list open;
 * Esc closes it. At least one option stays ticked.
 */
export function MultiSelectMenu<T extends string>({ label, values, options, onChange, disabled = false }: MultiSelectMenuProps<T>): JSX.Element {
  const pop = usePopover();
  const chosen = options.filter((o) => values.includes(o.value));
  const text = chosen.length === 0 ? 'None' : chosen.map((o) => o.label).join(' + ');
  const listId = useRef(`listbox-${Math.random().toString(36).slice(2, 9)}`).current;

  const toggle = (option: T): void => {
    const on = values.includes(option);
    if (on && values.length === 1) {
      return;
    }
    const next = options.map((o) => o.value).filter((v) => (v === option ? !on : values.includes(v)));
    onChange(next);
  };

  return (
    <div class="menu-root" ref={pop.rootRef}>
      <button
        ref={pop.buttonRef}
        class="btn ghost select-btn"
        type="button"
        aria-haspopup="listbox"
        aria-expanded={pop.open}
        aria-controls={pop.open ? listId : undefined}
        aria-label={`${label}: ${text}`}
        disabled={disabled}
        onClick={() => {
          if (pop.open) {
            pop.close(false);
          } else {
            pop.show('selected');
          }
        }}
        onKeyDown={(event) => {
          if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            pop.show('selected');
          }
        }}
      >
        <span class="select-text">{text}</span>
        <ChevronDownIcon size={14} class="select-chevron" />
      </button>
      {pop.open ? (
        <PopoverLayer anchorRef={pop.rootRef} popRef={pop.popRef}>
          <ul
            id={listId}
            ref={(el) => {
              pop.popRef.current = el;
            }}
            class="popover"
            role="listbox"
            aria-label={label}
            aria-multiselectable="true"
            onKeyDown={(event) => {
              popKeyDown(event, pop.popRef.current, '[role="option"]', pop.close, pop.floating);
            }}
          >
            {options.map((option) => {
              const selected = values.includes(option.value);
              return (
                <li
                  key={option.value}
                  class="item menu-item"
                  role="option"
                  aria-selected={selected}
                  tabIndex={-1}
                  onClick={() => {
                    toggle(option.value);
                  }}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter' || event.key === ' ') {
                      event.preventDefault();
                      toggle(option.value);
                    }
                  }}
                >
                  <span class="menu-check" aria-hidden="true">
                    {selected ? <CheckIcon size={12} /> : null}
                  </span>
                  {option.label}
                </li>
              );
            })}
          </ul>
        </PopoverLayer>
      ) : null}
    </div>
  );
}

export interface MenuAction {
  label: string;
  run: () => void;
  /** Listed but not offered yet; `note` says why ("Available in a later version"). */
  disabled?: boolean;
  note?: string;
  /**
   * A small submenu: the label becomes a group heading and these items follow it, indented, in
   * the same menu (so arrow keys reach them directly). `run` of the parent is not used.
   */
  children?: readonly MenuAction[];
}

function MenuItemButton({ action, sub, close }: { action: MenuAction; sub: boolean; close: () => void }): JSX.Element {
  const classes = ['item', 'menu-item', action.note === undefined ? '' : 'menu-item--noted', sub ? 'menu-item--sub' : ''].filter((c) => c !== '').join(' ');
  return (
    <button
      class={classes}
      type="button"
      role="menuitem"
      tabIndex={-1}
      aria-disabled={action.disabled === true ? true : undefined}
      onClick={(event) => {
        event.stopPropagation();
        if (action.disabled === true) {
          return;
        }
        close();
        action.run();
      }}
    >
      <span class="menu-item-label">{action.label}</span>
      {action.note === undefined ? null : <span class="menu-item-note">{action.note}</span>}
    </button>
  );
}

interface ActionMenuProps {
  /** Accessible name of the trigger ("More actions for Q3 planning sync"). */
  label: string;
  triggerClass: string;
  children: ComponentChildren;
  actions: readonly MenuAction[];
}

export function ActionMenu({ label, triggerClass, children, actions }: ActionMenuProps): JSX.Element {
  const pop = usePopover();
  return (
    <div class={pop.open ? 'menu-root menu-root--open' : 'menu-root'} ref={pop.rootRef}>
      <button
        ref={pop.buttonRef}
        class={triggerClass}
        type="button"
        aria-label={label}
        aria-haspopup="menu"
        aria-expanded={pop.open}
        onClick={(event) => {
          event.stopPropagation();
          if (pop.open) {
            pop.close(false);
          } else {
            pop.show('first');
          }
        }}
        onKeyDown={(event) => {
          if (event.key === 'ArrowDown') {
            event.preventDefault();
            pop.show('first');
          }
        }}
      >
        {children}
      </button>
      {pop.open ? (
        <PopoverLayer anchorRef={pop.rootRef} popRef={pop.popRef}>
          <div
            ref={(el) => {
              pop.popRef.current = el;
            }}
            class="popover popover--menu"
            role="menu"
            aria-label={label}
            onKeyDown={(event) => {
              popKeyDown(event, pop.popRef.current, '[role="menuitem"]', pop.close, pop.floating);
            }}
          >
            {actions.map((action) =>
              action.children === undefined ? (
                <MenuItemButton
                  key={action.label}
                  action={action}
                  sub={false}
                  close={() => {
                    pop.close(true);
                  }}
                />
              ) : (
                <div key={action.label} class="menu-group" role="group" aria-label={action.label}>
                  <span class="menu-group-label" aria-hidden="true">
                    {action.label}
                  </span>
                  {action.children.map((child) => (
                    <MenuItemButton
                      key={child.label}
                      action={child}
                      sub
                      close={() => {
                        pop.close(true);
                      }}
                    />
                  ))}
                </div>
              ),
            )}
          </div>
        </PopoverLayer>
      ) : null}
    </div>
  );
}
