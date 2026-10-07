// The formatting toolbar (DESIGN.md §12, renders/DocView.dc.html): Bold, Italic | H2, ¶ |
// bulleted, numbered, table | Insert timestamp. The current block type is pressed in. It is for
// light edits, not a word processor. Arrow keys move along it (one Tab stop).
import type { JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import { moveFocus } from '../../components/keyboard';
import { BulletedListIcon, ClockIcon, NumberedListIcon, TableIcon } from '../../components/paper/icons';
import { chipText, parseChipText } from '../../components/paper/paperDom';
import type { BlockKind } from './editing';

export type ToolbarCommand = 'bold' | 'italic' | 'heading' | 'paragraph' | 'bulleted' | 'numbered' | 'table';

interface ToolbarProps {
  /** The block the caret is in; null when it is not in the paper's editable text. */
  kind: BlockKind | null;
  bold: boolean;
  italic: boolean;
  onCommand: (command: ToolbarCommand) => void;
  onTimestamp: (seconds: number) => void;
  /** Where the timestamp field starts: Review's playhead for this recording, in seconds. */
  playheadSeconds: number | null;
  durationSeconds: number;
}

export function Toolbar({ kind, bold, italic, onCommand, onTimestamp, playheadSeconds, durationSeconds }: ToolbarProps): JSX.Element {
  const [asking, setAsking] = useState(false);
  const [time, setTime] = useState('0:00');
  const [error, setError] = useState<string | null>(null);
  const input = useRef<HTMLInputElement | null>(null);
  const opener = useRef<HTMLButtonElement | null>(null);

  useEffect(() => {
    if (asking) {
      input.current?.focus();
      input.current?.select();
    }
  }, [asking]);

  const disabled = kind === null;
  const button = (command: ToolbarCommand, label: string, content: JSX.Element | string, pressed: boolean, extra = ''): JSX.Element => (
    <button
      class={['icon-btn', 'tb-btn', pressed ? 'on' : '', extra].filter((c) => c !== '').join(' ')}
      type="button"
      aria-label={label}
      aria-pressed={pressed}
      disabled={disabled}
      tabIndex={command === 'bold' ? 0 : -1}
      // The caret stays in the paper.
      onMouseDown={(event) => {
        event.preventDefault();
      }}
      onClick={() => {
        onCommand(command);
      }}
    >
      {content}
    </button>
  );

  const insert = (): void => {
    const seconds = parseChipText(time);
    if (seconds === null) {
      setError('Type a time like 18:42 or 1:02:05.');
      return;
    }
    if (durationSeconds > 0 && seconds > durationSeconds) {
      setError(`The recording ends at ${chipText(durationSeconds)}.`);
      return;
    }
    setAsking(false);
    setError(null);
    onTimestamp(seconds);
  };

  return (
    <div class="doc-toolbar-wrap">
      <div
        class="doc-toolbar"
        role="toolbar"
        aria-label="Formatting"
        onKeyDown={(event) => {
          moveFocus(event, event.currentTarget, 'button', 'horizontal');
        }}
      >
        {button('bold', 'Bold', 'B', bold, 'tb-bold')}
        {button('italic', 'Italic', 'I', italic, 'tb-italic')}
        <span class="tb-divider" aria-hidden="true" />
        {button('heading', 'Heading', 'H2', kind === 'heading', 'tb-small')}
        {button('paragraph', 'Paragraph', '¶', kind === 'paragraph', 'tb-small')}
        <span class="tb-divider" aria-hidden="true" />
        {button('bulleted', 'Bulleted list', <BulletedListIcon size={16} />, kind === 'bulleted')}
        {button('numbered', 'Numbered list', <NumberedListIcon size={16} />, kind === 'numbered')}
        {button('table', 'Table', <TableIcon size={16} />, false)}
        <span class="tb-divider" aria-hidden="true" />
        <button
          ref={opener}
          class="btn ghost tb-stamp"
          type="button"
          tabIndex={-1}
          disabled={disabled}
          aria-haspopup="dialog"
          aria-expanded={asking}
          onMouseDown={(event) => {
            event.preventDefault();
          }}
          onClick={() => {
            setTime(chipText(playheadSeconds ?? 0));
            setError(null);
            setAsking((a) => !a);
          }}
        >
          <ClockIcon size={14} />
          Insert timestamp
        </button>
      </div>
      {asking ? (
        <div
          class="stamp-pop"
          role="dialog"
          aria-label="Insert a timestamp"
          onKeyDown={(event) => {
            if (event.key === 'Escape') {
              event.preventDefault();
              setAsking(false);
              opener.current?.focus();
            }
          }}
        >
          <label class="stamp-label" for="stamp-time">
            Time in the recording
          </label>
          <div class="stamp-row">
            <input
              ref={input}
              id="stamp-time"
              class="field mono stamp-field"
              type="text"
              inputMode="numeric"
              autocomplete="off"
              value={time}
              aria-describedby="stamp-hint"
              onInput={(event) => {
                setTime(event.currentTarget.value);
              }}
              onKeyDown={(event) => {
                if (event.key === 'Enter') {
                  event.preventDefault();
                  insert();
                }
              }}
            />
            <button class="btn p small-btn" type="button" onClick={insert}>
              Insert
            </button>
            <button
              class="btn g small-btn"
              type="button"
              onClick={() => {
                setAsking(false);
              }}
            >
              Cancel
            </button>
          </div>
          <span id="stamp-hint" class={error === null ? 'stamp-hint' : 'stamp-hint stamp-hint--error'} role={error === null ? undefined : 'alert'}>
            {error ?? (playheadSeconds === null ? 'Starts at 0:00; open Review to pick the moment.' : 'Where you were in Review.')}
          </span>
        </div>
      ) : null}
    </div>
  );
}
