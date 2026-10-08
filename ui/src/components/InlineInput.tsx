// A one-line field that edits a name in place (DESIGN.md §9): Review's chapters, highlights, people
// and highlight notes under a transcript line. Enter or leaving the field saves, Esc cancels, and
// an empty field keeps the old name.
import type { JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';

export interface InlineInputProps {
  label: string;
  initial?: string;
  placeholder: string;
  onCommit: (value: string) => void;
  onCancel: () => void;
  class?: string;
  maxLength?: number;
}

export function InlineInput({ label, initial = '', placeholder, onCommit, onCancel, class: className, maxLength = 200 }: InlineInputProps): JSX.Element {
  const ref = useRef<HTMLInputElement | null>(null);
  const [value, setValue] = useState(initial);
  const done = useRef(false);
  useEffect(() => {
    ref.current?.focus();
    ref.current?.select();
  }, []);
  const finish = (commit: boolean): void => {
    if (done.current) {
      return;
    }
    done.current = true;
    const text = value.trim();
    if (commit && text !== '') {
      onCommit(text);
    } else {
      onCancel();
    }
  };
  return (
    <input
      ref={ref}
      class={`field inline-input ${className ?? ''}`}
      type="text"
      aria-label={label}
      placeholder={placeholder}
      value={value}
      maxLength={maxLength}
      onClick={(event) => {
        event.stopPropagation();
      }}
      onDblClick={(event) => {
        event.stopPropagation();
      }}
      onInput={(event) => {
        setValue(event.currentTarget.value);
      }}
      onKeyDown={(event) => {
        // Ctrl+Z and the like still reach the app's undo (state/undo.ts), which leaves them to the field while it has changes.
        if (event.key === 'Enter') {
          event.preventDefault();
          event.stopPropagation();
          finish(true);
        } else if (event.key === 'Escape') {
          event.preventDefault();
          event.stopPropagation();
          finish(false);
        }
      }}
      onBlur={() => {
        finish(true);
      }}
    />
  );
}
