// Audio and video are never sent to any AI service (DESIGN.md §5.9, §10, §11), so they are not a
// choice: in "What the AI receives" and "What may be shared" they are greyed rows with a lock and
// the fixed note "never sent", not checkboxes. Not focusable; assistive technology reads them as a
// disabled item with a description.
import type { JSX } from 'preact';
import { useId } from 'preact/hooks';
import { LockIcon } from './icons';

export const NEVER_SENT: readonly string[] = ['Audio', 'Video'];

interface NeverSentRowProps {
  name: string;
  /** The checklist's own row, name and note classes, so the row lines up with the checkboxes. */
  rowClass: string;
  nameClass?: string;
  noteClass: string;
  /** Wraps the name and note (Settings stacks the note under the name). */
  textClass?: string;
}

export function NeverSentRow({ name, rowClass, nameClass, noteClass, textClass }: NeverSentRowProps): JSX.Element {
  const id = useId();
  const nameId = `${id}-name`;
  const noteId = `${id}-note`;
  const descriptionId = `${id}-why`;
  const text = (
    <>
      <span id={nameId} class={nameClass}>
        {name}
      </span>
      <span id={noteId} class={noteClass}>
        never sent
      </span>
    </>
  );
  return (
    <div
      class={`${rowClass} never-sent`}
      role="group"
      aria-disabled="true"
      aria-labelledby={`${nameId} ${noteId}`}
      aria-describedby={descriptionId}
      data-never-sent={name.toLocaleLowerCase()}
    >
      <span class="never-sent-lock" aria-hidden="true">
        <LockIcon size={13} />
      </span>
      {textClass === undefined ? text : <span class={textClass}>{text}</span>}
      <span id={descriptionId} class="sr">
        {name} stays on this PC and is never sent to any AI service, so it cannot be ticked.
      </span>
    </div>
  );
}
