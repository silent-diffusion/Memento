// Review › People › Who spoke (DESIGN.md §9): the recording's own speaker count and names, and
// what to do when the speakers found do not match: Reduce to {n} speakers (merges the speakers
// whose voices sound most alike, one Undo step) or Identify speakers again (listens again with the
// new count and names; names given in the transcript are kept where the same voice is found).
// 2.6.0: when the recording has no count of its own, the count check asks whether the number of
// speakers found is right; Yes remembers it, another number regroups the voices heard at once.
import type { JSX } from 'preact';
import { useState } from 'preact/hooks';
import type { Speaker, WhoSpoke } from '../../bridge/types';
import { SelectMenu } from '../../components/Menus';
import { countOptions, WhoSpokeEditor } from '../../components/WhoSpoke';
import { effectiveCount } from '../../format/speakerOrder';

/** "Auto", "3 speakers", "3 speakers · Ana, Ben, Chris". */
export function whoSpokeSummary(whoSpoke: WhoSpoke): string {
  const count = effectiveCount(whoSpoke);
  const people = count === null ? 'Auto' : `${count} ${count === 1 ? 'speaker' : 'speakers'}`;
  return whoSpoke.names.length === 0 ? people : `${people} · ${whoSpoke.names.join(', ')}`;
}

/** 2.0: why Identify speakers again is not offered once only the mix is kept. */
export const MIX_ONLY_SPEAKERS =
  'Only the mix was kept for this recording, so its speakers cannot be identified again by listening to each track. Reduce and renaming still work.';

/** The §17 sentence for Identify speakers again: what runs, how long, what is kept. */
export function identifyAgainText(whoSpoke: WhoSpoke, keepsVersions: boolean): string {
  const count = effectiveCount(whoSpoke);
  const as = count === null ? 'as Settings › Speakers says' : `as ${count} ${count === 1 ? 'speaker' : 'speakers'}`;
  return `Listens to the recording again and identifies the speakers ${as}, which takes a few minutes. Every other line gets a speaker again; lines you corrected keep their speaker and the voices under them guide the rest, and names you gave are kept${whoSpoke.names.length > 0 ? ', with the names above going to the others in the order they first speak' : ''}. ${keepsVersions ? 'The transcript as it is now is kept as a version you can restore.' : 'Undo cannot take this back.'}`;
}

/** 2.6.0: the count check's question, with how the speakers were grouped and what the details list. */
export function countCheckText(found: number, settingsDefault: number | null, participants: number): string {
  const speakers = `${found} ${found === 1 ? 'speaker' : 'speakers'}`;
  const how = settingsDefault === null ? `Grouped into ${speakers} by sound.` : `Grouped into ${speakers}, as Settings › Speakers says.`;
  const listed = participants === 0 ? '' : ` ${participants} ${participants === 1 ? 'participant is' : 'participants are'} listed.`;
  return `${how} Is that right?${listed}`;
}

/** 2.6.0: what Yes and another number do, in one line under the count check. */
export const COUNT_CHECK_HINT =
  'Yes keeps the speakers as they are and remembers the count for this recording. Another number regroups the voices heard into that many speakers at once; lines you corrected keep their speaker.';

interface PeopleWhoSpokeProps {
  whoSpoke: WhoSpoke;
  participants: readonly string[];
  /** The transcript's speakers, or null before speakers are identified. */
  speakers: readonly Speaker[] | null;
  settingsDefault: number | null;
  /** The speakers stage is identifying speakers now. */
  identifying: boolean;
  /** Version history is on: corrected lines are kept as a version before speakers are identified again. */
  keepsVersions: boolean;
  onChange: (next: WhoSpoke) => void;
  onReduce: (count: number) => void;
  onIdentifyAgain: () => void;
  /**
   * 2.6.0, the count check: the recording's count becomes `count`; with `regroup` the speakers are then
   * identified again at once (from the voices heard), else they stay as they are.
   */
  onConfirmCount: (count: number, regroup: boolean) => void;
  /** 2.0: "Keep only the mix" removed the separate tracks, so there is nothing to listen to again per track. */
  mixOnly?: boolean;
}

export function PeopleWhoSpoke({
  whoSpoke,
  participants,
  speakers,
  settingsDefault,
  identifying,
  keepsVersions,
  onChange,
  onReduce,
  onIdentifyAgain,
  onConfirmCount,
  mixOnly = false,
}: PeopleWhoSpokeProps): JSX.Element {
  const set = effectiveCount(whoSpoke) !== null || whoSpoke.names.length > 0;
  const [open, setOpen] = useState(false);
  // Changed here: offer to identify the speakers again with the new count and names.
  const [changed, setChanged] = useState(false);
  // Not now: the count check stays away for this visit to Review.
  const [checkDismissed, setCheckDismissed] = useState(false);
  const count = effectiveCount(whoSpoke);
  const found = speakers?.length ?? 0;
  const reduceTo = speakers !== null && count !== null && found > count ? count : null;
  const named = (speakers ?? []).filter((s) => s.renamed).length;
  const offerAgain = speakers !== null && !identifying && (reduceTo !== null || changed || (count !== null && found < count));
  // The speakers were grouped without a count of this recording's own: ask whether the number found is right.
  const check = speakers !== null && found > 0 && !identifying && count === null && whoSpoke.names.length === 0 && !changed && !checkDismissed;
  return (
    <div class="people-who">
      <button
        class="people-who-toggle"
        type="button"
        aria-expanded={open}
        aria-controls="people-who-editor"
        onClick={() => {
          setOpen(!open);
        }}
      >
        <span class="people-who-label">Who spoke</span>
        <span class={set ? 'people-who-value' : 'people-who-value people-who-value--auto'}>{whoSpokeSummary(whoSpoke)}</span>
      </button>
      {open ? (
        <div id="people-who-editor" class="people-who-editor">
          <WhoSpokeEditor
            idPrefix="review-who-spoke"
            value={whoSpoke}
            participants={participants}
            settingsDefault={settingsDefault}
            onChange={(next) => {
              setChanged(true);
              onChange(next);
            }}
          />
        </div>
      ) : null}
      {identifying ? <p class="people-who-note">Identifying speakers. Names update when it finishes.</p> : null}
      {check ? (
        <div class="people-who-check" role="group" aria-label="Speaker count check">
          <p class="people-who-note">{countCheckText(found, settingsDefault, participants.length)}</p>
          <div class="people-who-check-actions">
            <button
              class="btn ghost people-check-yes"
              type="button"
              onClick={() => {
                onConfirmCount(found, false);
              }}
            >
              Yes, {found}
            </button>
            <SelectMenu
              label="It was another number"
              size="main"
              value="0"
              buttonText="It was…"
              options={countOptions(null).filter((o) => o.value !== 'auto')}
              onChange={(next) => {
                onConfirmCount(Number(next), true);
              }}
            />
            <button
              class="btn link-btn people-check-later"
              type="button"
              onClick={() => {
                setCheckDismissed(true);
              }}
            >
              Not now
            </button>
          </div>
          <p class="people-who-hint">{COUNT_CHECK_HINT}</p>
        </div>
      ) : null}
      {offerAgain ? (
        <div class="people-who-offer" role="group" aria-label="Speakers found and expected">
          {count !== null ? (
            <p class="people-who-note">
              {found} {found === 1 ? 'speaker' : 'speakers'} found, {count} expected.
            </p>
          ) : null}
          {reduceTo !== null ? (
            <>
              <button
                class="btn ghost people-reduce"
                type="button"
                onClick={() => {
                  onReduce(reduceTo);
                }}
              >
                Reduce to {reduceTo} {reduceTo === 1 ? 'speaker' : 'speakers'}
              </button>
              <p class="people-who-hint">
                Merges the speakers whose voices sound most alike{named > 1 ? '; speakers you named stay apart' : ''}. Undo puts them back.
              </p>
            </>
          ) : null}
          {mixOnly ? (
            <p class="people-who-hint">{MIX_ONLY_SPEAKERS}</p>
          ) : (
            <>
              <button class="btn link-btn people-identify" type="button" title={identifyAgainText(whoSpoke, keepsVersions)} onClick={onIdentifyAgain}>
                Identify speakers again
              </button>
              <p class="people-who-hint">{identifyAgainText(whoSpoke, keepsVersions)}</p>
            </>
          )}
        </div>
      ) : null}
    </div>
  );
}
