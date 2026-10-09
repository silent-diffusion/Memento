// A pressed-in well of name pills with an inline input (DESIGN.md §14): the Details sheet's
// participants and Who spoke's names.
import type { JSX } from 'preact';
import { useState } from 'preact/hooks';
import { CloseIcon } from './icons';

interface NameWellProps {
  id: string;
  label: string;
  values: readonly string[];
  placeholder: string;
  onChange: (values: string[]) => void;
  /** Each name at most this long (the field stops there). */
  maxLength?: number;
  /** At most this many names; more typed ones are not added. */
  max?: number;
}

/** Enter or comma adds, Backspace on empty removes the last; a name already there (ignoring case) is not added twice. */
export function NameWell({ id, label, values, placeholder, onChange, maxLength, max = Number.POSITIVE_INFINITY }: NameWellProps): JSX.Element {
  const [draft, setDraft] = useState('');
  const addNames = (names: string[]): void => {
    const next = [...values];
    for (const raw of names) {
      const name = raw.trim();
      if (name !== '' && next.length < max && !next.some((v) => v.toLocaleLowerCase() === name.toLocaleLowerCase())) {
        next.push(name);
      }
    }
    if (next.length !== values.length) {
      onChange(next);
    }
  };
  const add = (): void => {
    addNames([draft]);
    setDraft('');
  };
  return (
    <div class="sheet-well">
      {values.map((value) => (
        <span key={value} class="pill done sheet-pill">
          {value}
          <button
            class="sheet-pill-remove"
            type="button"
            aria-label={`Remove ${value}`}
            onClick={() => {
              onChange(values.filter((v) => v !== value));
            }}
          >
            <CloseIcon size={10} />
          </button>
        </span>
      ))}
      <label class="sr" for={id}>
        {label}
      </label>
      <input
        id={id}
        class="sheet-well-input"
        type="text"
        placeholder={placeholder}
        value={draft}
        autocomplete="off"
        maxLength={maxLength}
        onInput={(event) => {
          // A comma ends a name, also when several arrive at once (pasted "Sam, Priya, ").
          const parts = event.currentTarget.value.split(',');
          const rest = parts.pop() ?? '';
          if (parts.length > 0) {
            addNames(parts);
            event.currentTarget.value = rest;
          }
          setDraft(rest);
        }}
        onKeyDown={(event) => {
          if (event.key === 'Enter') {
            event.preventDefault();
            add();
          } else if (event.key === 'Backspace' && draft === '' && values.length > 0) {
            onChange(values.slice(0, -1));
          }
        }}
        onBlur={add}
      />
    </div>
  );
}
