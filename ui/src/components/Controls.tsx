// Form controls from DESIGN.md §5.7 (toggle), §5.8 (segmented) and §5.2 (filter chips).
import type { JSX } from 'preact';
import { moveFocus } from './keyboard';

interface ToggleProps {
  label: string;
  checked: boolean;
  onChange?: (checked: boolean) => void;
  disabled?: boolean;
}

/** `<button role="switch">`: a sunk track with a convex knob that snaps across. */
export function Toggle({ label, checked, onChange, disabled = false }: ToggleProps): JSX.Element {
  return (
    <button
      class={checked ? 'tog on' : 'tog'}
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange?.(!checked)}
    >
      <span />
    </button>
  );
}

interface SegmentedProps<T extends string> {
  label: string;
  value: T;
  options: readonly { value: T; label: string }[];
  onChange: (value: T) => void;
}

/** A sunk pill group whose chosen option is raised. Real buttons with `aria-pressed`; arrows move. */
export function Segmented<T extends string>({ label, value, options, onChange }: SegmentedProps<T>): JSX.Element {
  return (
    <div
      class="seg-well"
      role="group"
      aria-label={label}
      onKeyDown={(event) => {
        moveFocus(event, event.currentTarget, '.seg');
      }}
    >
      {options.map((option) => (
        <button
          key={option.value}
          class={option.value === value ? 'seg on' : 'seg'}
          type="button"
          aria-pressed={option.value === value}
          onClick={() => {
            if (option.value !== value) {
              onChange(option.value);
            }
          }}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}

interface ChipGroupProps<T extends string> {
  label: string;
  value: T;
  chips: readonly { value: T; label: string }[];
  onChange: (value: T) => void;
}

/**
 * Single-select filter chips. One Tab stop (the chosen chip); arrow keys move between chips and
 * Enter or Space chooses, so moving through the group never refetches the list on its own.
 */
export function ChipGroup<T extends string>({ label, value, chips, onChange }: ChipGroupProps<T>): JSX.Element {
  const selectedIndex = Math.max(
    0,
    chips.findIndex((c) => c.value === value),
  );
  return (
    <div
      class="chip-row"
      role="group"
      aria-label={label}
      onKeyDown={(event) => {
        const moved = moveFocus(event, event.currentTarget, '.chip', 'horizontal');
        if (moved !== null) {
          for (const chip of event.currentTarget.querySelectorAll<HTMLElement>('.chip')) {
            chip.tabIndex = chip === moved ? 0 : -1;
          }
        }
      }}
      onFocusOut={(event) => {
        // Leaving the group: the Tab stop goes back to the chosen chip.
        const next = event.relatedTarget;
        if (!(next instanceof Node) || !event.currentTarget.contains(next)) {
          for (const chip of event.currentTarget.querySelectorAll<HTMLElement>('.chip')) {
            chip.tabIndex = chip.getAttribute('aria-pressed') === 'true' ? 0 : -1;
          }
        }
      }}
    >
      {chips.map((chip, index) => (
        <button
          key={chip.value}
          class={chip.value === value ? 'chip on' : 'chip'}
          type="button"
          aria-pressed={chip.value === value}
          tabIndex={index === selectedIndex ? 0 : -1}
          onClick={() => {
            onChange(chip.value);
          }}
        >
          {chip.label}
        </button>
      ))}
    </div>
  );
}
