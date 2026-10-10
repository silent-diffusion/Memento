// Review › People › Who spoke (DESIGN.md §9): the recording's own speaker count and names, and
// what to do when the speakers found do not match: Reduce to {n} speakers (merges the speakers
// whose voices sound most alike, one Undo step) or Identify speakers again (listens again with the
// new count and names; names given in the transcript are kept where the same voice is found).
import type { JSX } from 'preact';
import { useState } from 'preact/hooks';
import type { Speaker, WhoSpoke } from '../../bridge/types';
import { WhoSpokeEditor } from '../../components/WhoSpoke';
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
  return `Listens to the recording again and identifies the speakers ${as}, which takes a few minutes. Every line gets a speaker again; names you gave are kept where the same voice is found${whoSpoke.names.length > 0 ? ', and the names above go to the others in the order they first speak' : ''}. ${keepsVersions ? 'Lines you corrected are kept as a transcript version you can restore.' : 'Undo cannot take this back.'}`;
}

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
  /** 2.0: "Keep only the mix" removed the separate tracks, so there is nothing to listen to again per track. */
  mixOnly?: boolean;
}

export function PeopleWhoSpoke({ whoSpoke, participants, speakers, settingsDefault, identifying, keepsVersions, onChange, onReduce, onIdentifyAgain, mixOnly = false }: PeopleWhoSpokeProps): JSX.Element {
  const set = effectiveCount(whoSpoke) !== null || whoSpoke.names.length > 0;
  const [open, setOpen] = useState(false);
  // Changed here: offer to identify the speakers again with the new count and names.
  const [changed, setChanged] = useState(false);
  const count = effectiveCount(whoSpoke);
  const found = speakers?.length ?? 0;
  const reduceTo = speakers !== null && count !== null && found > count ? count : null;
  const named = (speakers ?? []).filter((s) => s.renamed).length;
  const offerAgain = speakers !== null && !identifying && (reduceTo !== null || changed || (count !== null && found < count));
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
                class="btn ghost small-btn people-reduce"
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
