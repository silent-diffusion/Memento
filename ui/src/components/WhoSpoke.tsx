// Who spoke (DESIGN.md §8, §9, §14): a recording's own speaker expectations, set on the Record
// screen's details or in Review's People pane. How many (Auto or 1–20, a number that sticks) and/or
// the names of the known speakers, with the participants offered as the source in one click ("Use
// participants"). Speakers are then identified with that many and named in order of first
// appearance; Settings › Speakers › Expected speakers stays the default for recordings without one.
import type { JSX } from 'preact';
import type { WhoSpoke } from '../bridge/types';
import { effectiveCount, WHO_SPOKE_MAX, whoSpokeFromParticipants } from '../format/speakerOrder';
import { NameWell } from './NameWell';
import { SelectMenu, type SelectOption } from './Menus';

const AUTO = 'auto';

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

interface WhoSpokeEditorProps {
  value: WhoSpoke;
  onChange: (next: WhoSpoke) => void;
  /** The recording's participants, offered as the names. */
  participants: readonly string[];
  /** Settings › Speakers › Expected speakers, or null for Auto. */
  settingsDefault: number | null;
  /** Ids stay unique when the editor appears twice on a screen. */
  idPrefix: string;
}

export function WhoSpokeEditor({ value, onChange, participants, settingsDefault, idPrefix }: WhoSpokeEditorProps): JSX.Element {
  const fromParticipants = whoSpokeFromParticipants(value, participants);
  const canUseParticipants = fromParticipants.names.length > 0 && fromParticipants.names.join('\n') !== value.names.join('\n');
  return (
    <div class="who-spoke" data-who-spoke={idPrefix}>
      <div class="who-spoke-row">
        <span class="who-spoke-label" id={`${idPrefix}-count-label`}>
          How many
        </span>
        <SelectMenu
          label="How many people spoke"
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
            onChange({ ...value, names });
          }}
        />
      </div>
      <p class="who-spoke-hint">{whoSpokeHint(value, settingsDefault)}</p>
    </div>
  );
}
