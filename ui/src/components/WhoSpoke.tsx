// Who spoke (DESIGN.md §8, §9, §14): a recording's own speaker expectations, set on the Record
// screen's details or in Review's People pane. How many (Auto or 1–20, a number that sticks) and/or
// the names of the known speakers, with the participants offered as the source in one click ("Use
// participants"). Speakers are then identified with that many and named in order of first
// appearance; Settings › Speakers › Expected speakers stays the default for recordings without one.
// While recording (2.6.0), each name has a Speaking now button: pressed while that person talks, it
// stamps the recording timer's time, and the speakers stage names the voice heard then after them.
import type { JSX } from 'preact';
import type { SpokeMark, WhoSpoke } from '../bridge/types';
import { effectiveCount, marksFor, WHO_SPOKE_MAX, whoSpokeFromParticipants } from '../format/speakerOrder';
import { CloseIcon } from './icons';
import { NameWell } from './NameWell';
import { SelectMenu, type SelectOption } from './Menus';

const AUTO = 'auto';

/** At most this many Speaking now marks, as the host allows. */
export const SPOKE_MARKS_MAX = 200;

/** "Auto", "1 speaker", "2 speakers" … "20 speakers". */
export function countOptions(settingsDefault: number | null): SelectOption<string>[] {
  const auto = settingsDefault === null ? 'Auto' : `Auto (Settings: ${settingsDefault})`;
  return [
    { value: AUTO, label: auto },
    ...Array.from({ length: WHO_SPOKE_MAX }, (_, i) => ({ value: String(i + 1), label: `${i + 1} ${i === 0 ? 'speaker' : 'speakers'}` })),
  ];
}

/** What the count and names will do, in one sentence under the controls. */
export function whoSpokeHint(whoSpoke: WhoSpoke, settingsDefault: number | null): string {
  const count = effectiveCount(whoSpoke);
  const names = whoSpoke.names.length;
  if (count === null) {
    return settingsDefault === null
      ? 'Memento finds how many people spoke. Set a number if it finds too many.'
      : `Speakers are identified as ${settingsDefault}, as Settings › Speakers says.`;
  }
  const people = `${count} ${count === 1 ? 'speaker' : 'speakers'}`;
  if (names === 0) {
    return `Speakers are identified as ${people}.`;
  }
  return `Speakers are identified as ${people} and named in the order they first speak. Rename any that are off in Review.`;
}

/** The Speaking now hint while recording: with names to press beside, or before any name is listed. */
export function speakingNowHint(names: number): string {
  return names === 0
    ? 'Add the names, then press Speaking now beside a name while that person talks: their speaker is named after them when the recording is processed.'
    : 'Press Speaking now while a person talks, and their speaker is named after them when the recording is processed.';
}

/** "1 mark", "2 marks". */
export function markCount(n: number): string {
  return `${n} ${n === 1 ? 'mark' : 'marks'}`;
}

function sameName(a: string, b: string): boolean {
  return a.trim().toLocaleLowerCase() === b.trim().toLocaleLowerCase();
}

interface WhoSpokeEditorProps {
  value: WhoSpoke;
  onChange: (next: WhoSpoke) => void;
  /** The recording's participants, offered as the names. */
  participants: readonly string[];
  /** Settings › Speakers › Expected speakers, or null for Auto. */
  settingsDefault: number | null;
  /** Ids stay unique when the editor appears twice on a screen. */
  idPrefix: string;
  /**
   * 2.6.0, while recording: the recording timer's time now (recorded ms), so Speaking now can stamp a
   * mark. Absent outside a recording, where the marks are only counted.
   */
  speakingNow?: (() => number) | undefined;
}

export function WhoSpokeEditor({ value, onChange, participants, settingsDefault, idPrefix, speakingNow }: WhoSpokeEditorProps): JSX.Element {
  const fromParticipants = whoSpokeFromParticipants(value, participants);
  const canUseParticipants = fromParticipants.names.length > 0 && fromParticipants.names.join('\n') !== value.names.join('\n');
  const marksOf = (name: string): number => value.marks.filter((m) => sameName(m.name, name)).length;
  // While recording every name has a row (the button); otherwise only the names with marks, read-only.
  const rows = speakingNow === undefined ? value.names.filter((name) => marksOf(name) > 0) : value.names;
  const full = value.marks.length >= SPOKE_MARKS_MAX;
  const mark = (name: string): void => {
    if (speakingNow === undefined || full) {
      return;
    }
    const next: SpokeMark = { name, atMs: Math.max(0, Math.round(speakingNow())) };
    onChange({ ...value, marks: [...value.marks, next] });
  };
  const clear = (name: string): void => {
    onChange({ ...value, marks: value.marks.filter((m) => !sameName(m.name, name)) });
  };
  return (
    <div class="who-spoke" data-who-spoke={idPrefix}>
      <div class="who-spoke-row">
        <span class="who-spoke-label" id={`${idPrefix}-count-label`}>
          How many
        </span>
        <SelectMenu
          label="How many people spoke"
          size="main"
          value={value.count === null ? AUTO : String(value.count)}
          options={countOptions(settingsDefault)}
          onChange={(next) => {
            onChange({ ...value, count: next === AUTO ? null : Number(next) });
          }}
        />
      </div>
      <div class="who-spoke-names">
        <div class="who-spoke-row who-spoke-row--names">
          <span class="who-spoke-label">Names</span>
          {canUseParticipants ? (
            <button
              class="link-btn who-spoke-use"
              type="button"
              onClick={() => {
                onChange(fromParticipants);
              }}
            >
              Use participants
            </button>
          ) : null}
        </div>
        <NameWell
          id={`${idPrefix}-name`}
          label="Add a speaker's name"
          values={value.names}
          max={WHO_SPOKE_MAX}
          maxLength={100}
          placeholder={value.names.length === 0 ? 'In the order they first speak' : 'Add a name'}
          onChange={(names) => {
            // A name taken off the list takes its Speaking now marks with it.
            onChange({ ...value, names, marks: marksFor(value.marks, names) });
          }}
        />
        {rows.length > 0 ? (
          <ul class="who-spoke-marks" aria-label="Speaking now marks">
            {rows.map((name) => {
              const n = marksOf(name);
              return (
                <li key={name} class="who-spoke-mark">
                  <span class="who-spoke-mark-name">{name}</span>
                  <span class="who-spoke-mark-count mono">{n === 0 ? '' : markCount(n)}</span>
                  {speakingNow === undefined ? null : (
                    <button
                      class="btn ghost ghost-row who-spoke-mark-btn"
                      type="button"
                      aria-label={`Mark ${name} speaking now`}
                      disabled={full}
                      title={full ? `At most ${SPOKE_MARKS_MAX} marks can be kept.` : undefined}
                      onClick={() => {
                        mark(name);
                      }}
                    >
                      Speaking now
                    </button>
                  )}
                  {speakingNow !== undefined && n > 0 ? (
                    <button
                      class="icon-btn who-spoke-mark-clear"
                      type="button"
                      aria-label={`Clear ${name}'s marks`}
                      onClick={() => {
                        clear(name);
                      }}
                    >
                      <CloseIcon size={10} />
                    </button>
                  ) : null}
                </li>
              );
            })}
          </ul>
        ) : null}
        {speakingNow === undefined ? null : <p class="who-spoke-hint">{speakingNowHint(value.names.length)}</p>}
      </div>
      <p class="who-spoke-hint">{whoSpokeHint(value, settingsDefault)}</p>
    </div>
  );
}
