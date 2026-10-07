// Sample transcripts for the browser preview. Every line is invented for Memento's mock library; the
// people are the sample library's fictional participants. A line is [speaker, text] or
// [speaker, text, atSeconds]: `{word}` marks a word the engine was unsure of, and a speaker written
// as `?S` marks an uncertain speaker assignment.
import { seedFromId } from '../format/waveform';
import type { Speaker, SpeakerColour, Transcript, TranscriptSegment, TranscriptWord } from './types';

export type ScriptLine = readonly [speaker: string, text: string, at?: number];

/** A block of lines spread over [from, to) seconds of the recording. */
export interface ScriptBlock {
  from: number;
  to: number;
  lines: readonly ScriptLine[];
}

export interface Script {
  /** Speaker key -> [name, renamed by the user]. */
  speakers: Record<string, readonly [string, boolean]>;
  blocks: readonly ScriptBlock[];
  /** Lines the user corrected (found by how they start now), with the engine's original wording. */
  edits?: readonly { startsWith: string; original: string }[];
  /** Whether the speakers stage ran (false: every segment has speaker null). */
  identified: boolean;
}

const m = (min: number, sec = 0): number => min * 60 + sec;

// "Design review: library screen" (1:10:02). The six chapters match the recording's own.
const DESIGN_REVIEW: Script = {
  speakers: { S: ['Sam Okafor', true], A: ['Aiko Tanaka', true], L: ['Lena Fischer', true], X: ['Speaker 4', false] },
  identified: true,
  edits: [{ startsWith: 'The ground in dark', original: 'The ground in dark is warmer than pure black. One F, one E, one B.' }],
  blocks: [
    {
      from: 2,
      to: m(6, 30),
      lines: [
        ['S', 'Okay, I think everyone is here. Thanks for making time. The goal today is to agree the library layout before build starts on Monday.'],
        ['A', 'I shared the mockups in the channel this morning. There are three states: populated, first run and the grid view.'],
        ['S', 'Great. Lena, you had a list of questions from the research sessions?'],
        ['L', 'A few. Mostly about how much status people want to see at a glance, and whether the processing card is noisy.'],
        ['S', 'Let us keep those for the density part. First, does anyone object to hub and spoke as the overall model?'],
        ['X', 'No objection. It matches how people described their week: find the recording, open it, do one thing with it.'],
        ['A', 'And it keeps the sidebar out, which was the thing everyone complained about in the {prototype}.'],
        ['S', 'Right. So the library is home and everything else opens on top of it.'],
        ['L', 'One small thing: the back control has to return you to exactly where you were, scroll position and filters included.'],
        ['A', 'That is in the spec already. The row you opened stays selected too.'],
        ['?X', 'Mm-hm.'],
        ['S', 'Good. Timebox: forty minutes for the walkthrough and density, then dark theme, then owners.'],
        ['X', 'Can we also leave five minutes for the empty state? It is the first thing anyone sees.'],
        ['S', 'Yes, it is on the agenda after dark theme.'],
        ['A', 'Okay, I will share my screen.'],
      ],
    },
    {
      from: m(6, 30),
      to: m(18, 10),
      lines: [
        ['A', 'This is the populated library. Heading, summary line, then sort and the two view toggles on the right.'],
        ['A', 'The summary line reflects the current filter, so if you pick interviews it says three recordings, not fourteen.'],
        ['L', 'People liked that in testing. They used it to check that a search had actually done something.'],
        ['S', 'What does the summary say when the library is huge? Hours and minutes?'],
        ['A', 'Hours and minutes, yes. Eight hours thirty-eight minutes, all on this PC.'],
        ['X', 'I like that "all on this PC" is right there. It answers the privacy question before anyone asks it.'],
        ['?L', 'Agreed.'],
        ['A', 'Below that sits the processing card. It only appears while something is running.'],
        ['S', 'And it shows one recording, with "plus two more" if several are processing?'],
        ['A', 'Exactly. We tried stacking cards and it pushed the whole list below the fold.'],
        ['L', 'Can you show the stages on the card? I want to see how the progress reads.'],
        ['A', 'Stored, transcribing, speakers, minutes. Done is green at full width, active is the accent at its percentage.'],
        ['X', 'And queued has no fill at all?'],
        ['A', 'No fill, just the track. It reads as waiting without being alarming.'],
        ['S', 'Then the type chips. All, meetings, interviews, lectures and so on.'],
        ['L', 'Custom types go after research, right? Someone in the study had a type called book notes.'],
        ['A', 'After research, in the order they were created.'],
        ['?S', 'Okay.'],
        ['S', 'And the groups by date. Today, yesterday, earlier this week, then months.'],
        ['X', 'What happens at the start of a month? Does September become "last month"?'],
        ['A', 'No, it stays September. Relative names stop at earlier this week.'],
        ['S', 'Fine. Let us look at a single row.'],
        ['A', 'Type icon, title, meta line, pills, duration and a chevron. The whole row is one button.'],
        ['L', 'The meta line is type, people and when. Meeting, five people, ten in the morning.'],
        ['X', 'Where does the {camera} icon go for recordings with video?'],
        ['A', 'Before the meta text, fourteen pixels, with an accessible name: includes video.'],
        ['S', 'Okay. That is the walkthrough. Let us get into density.'],
      ],
    },
    {
      from: m(18, 10),
      to: m(34, 0),
      lines: [
        ['S', 'Okay, next item. The rows feel a little tight to me on the {fourteen-forty} mockup. Can we talk about density?', m(18, 10)],
        ['A', 'I had the same note. If every row carries pills, the list gets busy fast.', m(18, 24)],
        [
          'X',
          'Proposal: rows stay at 68 pixels, pills only for stages that have actually run, and nothing at all for audio-only recordings except a small caption.',
          m(18, 42),
        ],
        ['L', 'That works. The caption is quieter than an empty pill would be.', m(19, 5)],
        ['S', 'Agreed then. Aiko, can you update the {Figma} file and I will take the dark variant?', m(19, 20)],
        ['A', 'Yes. One more: the processing card should disappear when a filter is active, otherwise it looks like a search result.', m(19, 31)],
        ['L', 'Agreed. A filter means "show me these", and the card is not one of them.'],
        ['S', 'What about compact density? Settings has comfortable and compact.'],
        ['A', 'Compact drops the row to fifty-six pixels and hides nothing. Same columns, less air.'],
        ['X', 'Does compact also apply to the transcript? The settings description says library and transcript.'],
        ['A', 'It should. Segment padding goes from twelve to eight.'],
        ['L', 'Let us make sure the hit targets stay above thirty-six pixels in compact.'],
        ['?A', 'Noted.'],
        ['S', 'Now the pills themselves. Done, active, queued.'],
        ['A', 'Done has the check icon. Active is accent soft with the percentage. Queued is a dashed outline.'],
        ['X', 'And failed? We do not have a design for failed yet.'],
        ['L', 'In the error sheet I proposed an outline in the danger colour and the label "Transcript failed, retry".'],
        ['S', 'Retry inside the pill?'],
        ['L', 'Clicking the pill retries. Keyboard users get the same action in the row menu.'],
        ['X', 'I would want the stored pill to show next to a failed one, so it is clear the audio itself is safe.'],
        ['A', 'That is the rule now: a finished stored stage is hidden unless something failed.'],
        ['S', 'Good, I like that. It turns the scary state into "your recording is fine, one step failed".'],
        ['L', 'Also no red-only status. The failed pill carries the word failed, not just the colour.'],
        ['?X', 'Yes.'],
        ['S', 'Right. What about the speaker count in the meta line? "One speaker" sounds odd for a solo recording.'],
        ['A', 'Solo recordings say "Just me". One speaker is when exactly one other person is listed.'],
        ['X', '{Huh}, I had not thought of that distinction. That is nice.'],
        ['S', 'Next, the chevron. Does it stay when the row menu appears?'],
        ['A', 'On hover the chevron fades and the three-dot menu takes its place.'],
        ['L', 'That needs to work on keyboard focus too, not just on hover.'],
        ['A', 'It does. Focus anywhere in the row shows the menu button.'],
        ['S', 'Okay. I think density is settled. Rows at sixty-eight, compact at fifty-six, pills only for real stages.'],
        ['X', 'I will write that up in the decisions doc.'],
      ],
    },
    {
      from: m(34, 0),
      to: m(52, 40),
      lines: [
        ['S', 'Dark theme. The question is whether it ships with version one or comes later.'],
        ['A', 'Every screen on the canvas exists in both themes already. The cost is mostly testing.'],
        ['L', 'In the study, four of six people used dark mode in Windows. It is not a niche.'],
        ['X', 'Then it has to follow the system setting and switch live, without a restart.'],
        ['S', 'That is the decision in the design doc too. Follow Windows by default, override in settings.'],
        ['A', 'The ground in dark is warmer than pure black: hex 1F1E1B.'],
        ['L', 'And the shadows are mostly the dark half. The light half is very faint.'],
        ['?S', 'Mm.'],
        ['S', 'Does the accent change in dark?'],
        ['A', 'It goes lighter, a soft orange, so it keeps its contrast on the dark ground.'],
        ['X', 'We should check every text pair. The design says four point five to one at minimum.'],
        ['A', 'I checked them all in the tokens. Tertiary text was the closest call.'],
        ['S', 'What about the speaker colours in the transcript?'],
        ['A', 'They have dark variants too. Blue, teal, violet and amber, all lighter in dark.'],
        ['L', 'And they are never used for text. The names stay in the normal text colour.'],
        ['S', 'Good. So the colour is a hint, not the only cue.'],
        ['X', 'One risk is screenshots in the documentation. If we only shoot light, people on dark will be confused.'],
        ['S', 'We will shoot both. It is cheap once the pages exist.'],
        ['A', 'The document paper stays white in both themes, by the way. It is a page, not a surface.'],
        ['L', 'That makes sense. People expect a printed page to look like paper.'],
        ['?L', 'Right.'],
        ['S', 'Any objection to shipping dark with version one?'],
        ['X', 'None.'],
        ['L', 'None from me.'],
        ['S', 'Decision: dark theme ships with version one, following Windows.'],
        ['A', 'I will make sure every new screen is checked in both themes before we call it done.'],
        ['S', '{Perfect}. Moving on.'],
      ],
    },
    {
      from: m(52, 40),
      to: m(61, 20),
      lines: [
        ['S', 'The empty state. Aiko, can you show the first-run screen?'],
        ['A', 'A ring with the accent dot, "Your library is empty", and one line about everything staying on this PC.'],
        ['L', 'Then two buttons: start your first recording, and import audio or video.'],
        ['X', 'Import is not built yet, is it?'],
        ['A', 'Not yet. The button stays, and it says plainly that import arrives in a later version.'],
        ['S', 'And the three feature cards underneath.'],
        ['A', 'Record what you choose, transcribe on this PC, turn it into documents.'],
        ['L', 'The third card has to say that AI is optional and off by default. That came up in every interview.'],
        ['X', 'Do we show the empty state again if someone deletes everything?'],
        ['A', 'No. Once the first recording exists we never show it again. A filtered empty list has its own message.'],
        ['S', 'What is the search field doing on the empty screen?'],
        ['A', 'It is disabled, with the placeholder "Search will work once you have a recording".'],
        ['L', 'That is honest. Better than a search that silently finds nothing.'],
        ['X', 'Who owns the empty state build?'],
        ['S', 'I will take it. It is small and I know the copy.'],
      ],
    },
    {
      from: m(61, 20),
      to: m(70, 0),
      lines: [
        ['S', 'Okay, owners. Aiko, the library list and the grid.'],
        ['A', 'Yes, list first, grid after. I will have the list ready for review by Thursday.'],
        ['S', 'Lena, you will write up the research notes behind each decision?'],
        ['L', 'I will attach them to the decisions doc so we can point to them later.'],
        ['S', 'And I have the empty state and the dark variant of the rows.'],
        ['X', 'I will take the processing card and the failed pill, since I raised it.'],
        ['S', 'Great. Anything we are deliberately not deciding today?'],
        ['L', 'Multi-select and bulk delete. They are not designed yet.'],
        ['A', 'Context menus on rows too, beyond the three-dot menu.'],
        ['S', 'Let us park those for the next review. Same time next week?'],
        ['X', 'Works for me.'],
        ['A', 'Same.'],
        ['L', 'I will send the invite.'],
        ['S', 'Thanks, everyone. Good session.'],
      ],
    },
  ],
};

// "Interview — Priya Natarajan, product research" (48:30).
const RESEARCH_INTERVIEW: Script = {
  speakers: { T: ['Tomás Rivera', true], P: ['Priya Natarajan', true] },
  identified: true,
  blocks: [
    {
      from: 3,
      to: m(48, 0),
      lines: [
        ['T', 'Thanks for doing this, Priya. To start, could you describe how your team shares research findings today?'],
        ['P', 'Mostly in a shared folder of slide decks, plus a weekly readout. The decks are where findings go to be forgotten, honestly.'],
        ['T', 'Why forgotten?'],
        ['P', 'Because nobody can search inside them. You remember that someone said something about onboarding, but not which deck or which week.'],
        ['T', 'How do you find it when you need it?'],
        ['P', 'I message the person who ran the study and hope they remember. It works until they leave the company.'],
        ['T', 'Do you record the sessions themselves?'],
        ['P', 'Always, with consent. But the recordings sit on a drive and nobody watches a ninety-minute video to find one quote.'],
        ['T', 'What would make the recordings useful?'],
        ['P', 'Being able to jump to the moment. If I search for "pricing", I want the three places someone talked about it, with the words.'],
        ['?T', 'Right.'],
        ['P', 'And I want to trust that the transcript is right, or at least know where it is not.'],
        ['T', 'Say more about trust.'],
        ['P', 'Automatic transcripts get names and numbers wrong. If the tool shows me which words it was unsure of, I can check only those.'],
        ['T', 'Where do the recordings live today? On your laptop, or a cloud service?'],
        ['P', 'Our legal team does not allow cloud transcription for customer sessions, so mostly laptops and one {encrypted} share.'],
        ['T', 'So local processing is a requirement, not a preference.'],
        ['P', 'A hard requirement. If anything leaves the machine, I need to be asked first and see exactly what goes.'],
        ['T', 'How do you turn sessions into something the team reads?'],
        ['P', 'I write a summary with five or six themes and a few quotes for each. It takes me a full day per study.'],
        ['T', 'Which part takes longest?'],
        ['P', 'Finding the quotes again. I know they exist, I just cannot find them fast.'],
        ['T', 'If speaker names were detected automatically, would you rename them by hand?'],
        ['P', 'Yes, and I would want it to remember the interviewer, since that is me in every session.'],
        ['T', 'That is helpful. Last question: what would make you stop using a tool like this?'],
        ['P', 'If it ever lost a recording. Everything else I can work around.'],
        ['T', 'Thank you, that is a great place to stop.'],
      ],
    },
  ],
};

// "Notes for the README" (6:41), a solo dictation: speakers are not identified.
const README_NOTES: Script = {
  speakers: {},
  identified: false,
  blocks: [
    {
      from: 1,
      to: m(6, 35),
      lines: [
        ['', 'Notes for the readme. First section should say what Memento is in one sentence: it records, transcribes and organises meetings on your own PC.'],
        ['', 'Second, the promise. Nothing leaves the computer unless you choose to export it or send it to an AI service yourself.'],
        ['', 'Then a short list of what works today. Recording separate tracks, the library, review with playback, and local transcription.'],
        ['', 'Mention that each source is saved as its own track, so a microphone and the meeting app can be transcribed separately.'],
        ['', 'A section on {requirements}. Windows eleven, and a graphics card helps but is not required.'],
        ['', 'Say plainly that the CPU fallback is slower. Maybe give an example: an hour of audio in about fifteen minutes on a recent laptop.'],
        ['', 'Installation is one installer. Models download from inside Settings, and each one shows its size before it starts.'],
        ['', 'Add a privacy paragraph. Audio and video never leave the PC, not even when AI features are switched on.'],
        ['', 'Finally, how to report a problem, and where the logs live. No personal data in the logs.'],
        ['', 'That is enough for a first draft. Keep it short.'],
      ],
    },
  ],
};

// "Customer call: onboarding feedback" (37:50): the transcript failed at 64%; this is the partial pass.
const ONBOARDING_CALL: Script = {
  speakers: {},
  identified: false,
  blocks: [
    {
      from: 4,
      to: m(24, 10),
      lines: [
        ['', 'Thanks for joining, Mira. We wanted to hear how the first two weeks with the product have gone.'],
        ['', 'Overall well. The setup was quick, but the first day was confusing because the dashboard was empty.'],
        ['', 'Confusing in what way?'],
        ['', 'There was nothing to tell me what to do next. I clicked around for a while before I found the import button.'],
        ['', 'That is useful. Did the welcome email help at all?'],
        ['', 'I did not see it. It went to a shared inbox that nobody reads.'],
        ['', 'Once you imported your data, what happened?'],
        ['', 'It took about twenty minutes to process, and there was no sign that anything was happening.'],
        ['', 'So a progress indicator would have helped.'],
        ['', 'Definitely. Even a rough estimate. I almost cancelled it because I thought it had frozen.'],
        ['', 'How did the rest of your team get started?'],
        ['', 'Omar set up the integrations. He found the settings page easier than I did, but he reads documentation.'],
        ['', 'What would you change first if you could change one thing?'],
        ['', 'A checklist on the first screen. Three or four steps, and it goes away once you have done them.'],
        ['', 'That matches what we have heard from others. Can I ask about the {invoicing} module next?'],
      ],
    },
  ],
};

// "CS 301, lecture 12: consensus protocols" (1:28:45): one lecturer; speakers were not identified.
const LECTURE: Script = {
  speakers: {},
  identified: false,
  blocks: [
    {
      from: 5,
      to: m(88, 0),
      lines: [
        ['', 'Good morning. Last week we looked at why replication is hard. Today we look at how a group of machines agrees on a single value.'],
        ['', 'The setting is simple. Several servers, messages can be delayed or lost, and any server might crash at any time.'],
        ['', 'We want two properties. Safety: no two servers ever decide different values. And liveness: eventually, someone decides.'],
        ['', 'There is a famous result that says you cannot guarantee both in a fully asynchronous system with even one faulty process.'],
        ['', 'So practical protocols guarantee safety always, and liveness only when the network behaves for long enough.'],
        ['', 'The core idea is a majority. If any two majorities overlap, a decision made by one majority cannot be contradicted by another.'],
        ['', 'With five servers, a majority is three. You can lose two servers and still make progress.'],
        ['', 'Most protocols elect a leader. The leader proposes, the followers accept, and once a majority accepts, the value is committed.'],
        ['', 'Leaders are elected for a numbered term. A server that sees a higher term steps down immediately.'],
        ['', 'Why numbered terms? Because an old leader that was cut off might come back and keep sending proposals.'],
        ['', 'The term number lets everyone recognise those stale messages and ignore them.'],
        ['', 'Now, what does the leader replicate? Not single values, but a log of commands, in order.'],
        ['', 'Each server applies the committed log to its own state machine, so they all end up in the same state.'],
        ['', 'For next week, read the chapter on log compaction and {snapshotting}, and try the exercise on election timeouts.'],
      ],
    },
  ],
};

const GENERIC: Record<string, Script> = {
  '20261001-174000-field': {
    speakers: {},
    identified: false,
    blocks: [
      {
        from: 2,
        to: m(14, 10),
        lines: [
          ['', 'Field notes from the site visit. The workshop is smaller than the photos suggested, about forty square metres.'],
          ['', 'They record every client call on a phone propped against a mug. Audio quality is poor but usable.'],
          ['', 'The owner keeps notes in a paper diary and types them up on Fridays. That is where things get lost.'],
          ['', 'Two people share one laptop. Whoever is on a call takes it into the back room for quiet.'],
          ['', 'Internet is unreliable here, which is a strong argument for anything that works offline.'],
          ['', 'Follow up: send them the {beta} build and check whether their laptop has enough memory for the small model.'],
        ],
      },
    ],
  },
  '20260827-200000-books': {
    speakers: {},
    identified: false,
    blocks: [
      {
        from: 2,
        to: m(22, 30),
        lines: [
          ['', 'Reading notes, chapter five, on replication. The main trade-off is between consistency and availability during a network partition.'],
          ['', 'Single-leader replication is the easiest to reason about. All writes go through one node.'],
          ['', 'Replication lag means a follower can show stale data. Reading your own writes needs special care.'],
          ['', 'Multi-leader setups help across data centres but conflicts become your problem.'],
          ['', 'Leaderless systems use quorums for reads and writes. The arithmetic is simple; the edge cases are not.'],
          ['', 'Note to self: compare this with the consensus lecture from CS 301, the overlap argument is the same.'],
        ],
      },
    ],
  },
  '20260929-090000-board': {
    speakers: { F: ['Felix Andersen', true], D: ['Dana Whitfield', true], M: ['Marcus Lee', true] },
    identified: true,
    blocks: [
      {
        from: 4,
        to: m(52, 0),
        lines: [
          ['F', 'Let us walk through the board deck once, start to finish, and time it.'],
          ['D', 'Slide two is the quarter in numbers. Revenue up eleven percent, churn flat.'],
          ['M', 'I would lead with churn being flat. The board asked about it twice last time.'],
          ['F', 'Good point. Swap the order and put the churn chart first.'],
          ['D', 'Slide five is hiring. We are three roles behind plan, all in engineering.'],
          ['M', 'We should say why. Two offers were declined over {relocation}.'],
          ['F', 'And what we are doing about it: remote roles from next quarter.'],
          ['D', 'Last slide is the ask. Approval for the second data centre.'],
          ['F', 'That took thirty-eight minutes. We need to be under thirty.'],
        ],
      },
    ],
  },
  '20260924-101500-mlcan': {
    speakers: { J: ['Jonah Berg', true], M: ['Marcus Lee', true] },
    identified: true,
    blocks: [
      {
        from: 3,
        to: m(40, 30),
        lines: [
          ['J', 'Thanks for coming in, Marcus. Could you start with a project you are proud of?'],
          ['M', 'Last year I rebuilt our search indexing so it runs incrementally. Reindexing went from six hours to a few minutes.'],
          ['J', 'What was the hardest part?'],
          ['M', 'Deletes. Knowing when a document had gone without scanning everything again.'],
          ['J', 'How did you test it?'],
          ['M', 'We replayed a week of production changes against both versions and compared the results.'],
          ['J', 'Tell me about a disagreement with a colleague and how it ended.'],
          ['M', 'We argued about caching. I lost the argument, and in hindsight they were right.'],
          ['J', 'Do you have questions for us?'],
          ['M', 'How do you decide what not to build? That tells me a lot about a team.'],
        ],
      },
    ],
  },
  '20260918-160000-thall': {
    speakers: { F: ['Felix Andersen', true], P: ['Priya Natarajan', true], S: ['Sam Okafor', true], X: ['Speaker 4', false] },
    identified: true,
    blocks: [
      {
        from: 5,
        to: m(105, 0),
        lines: [
          ['F', 'Welcome to the town hall. We will do updates for twenty minutes and leave the rest for questions.'],
          ['F', 'The headline: we are on plan for the year, and the new office opens in January.'],
          ['P', 'From research, the biggest theme this quarter is trust. Customers want to know where their data lives.'],
          ['S', 'On design, the library work starts next month. You will see mockups in the channel soon.'],
          ['X', 'Question from the floor: will the new office have quiet rooms for calls?'],
          ['F', 'Yes, six of them, bookable from the calendar.'],
          ['X', 'Is the four-day week pilot continuing?'],
          ['F', 'It is continuing until March, then we decide with the data from the {survey}.'],
          ['P', 'One more from the chat: will there be a recording of this session? Yes, it is being recorded now.'],
          ['F', 'Thanks, everyone. See you next quarter.'],
        ],
      },
    ],
  },
  '20261006-100000-q3plan': {
    speakers: {
      P: ['Priya Natarajan', true],
      S: ['Sam Okafor', true],
      L: ['Lena Fischer', true],
      M: ['Marcus Lee', true],
    },
    identified: true,
    blocks: [
      {
        from: 3,
        to: m(62, 0),
        lines: [
          ['P', 'Let us start with a quick Q2 recap, then the hiring plan, then the launch date.'],
          ['S', 'Q2 closed slightly ahead. The beta reached two hundred teams, which was the target.'],
          ['L', 'Hiring: two designers start next month, and we are still looking for a data engineer.'],
          ['P', 'We should lock the launch date before the offsite, otherwise marketing cannot plan the announcement.'],
          ['M', 'Agreed. Can we say the second week of November and revisit if the beta slips?'],
          ['P', 'Fine by me. Then the open question is who owns the launch checklist.'],
          ['S', 'I can own it, with Lena on the design side.'],
          ['L', 'Budget asks next. We need one more contractor for the documentation work.'],
          ['M', 'That fits within the Q3 envelope if we delay the second {conference}.'],
          ['P', 'Good. Let us write that down and move to open questions.'],
        ],
      },
    ],
  },
};

const SCRIPTS: Record<string, Script> = {
  '20261005-160000-dsrev': DESIGN_REVIEW,
  '20261006-141500-pnint': RESEARCH_INTERVIEW,
  '20261005-091200-readme': README_NOTES,
  '20260930-130500-onbrd': ONBOARDING_CALL,
  '20261003-110000-cs301': LECTURE,
  ...GENERIC,
};

/** The long sample recording that the `?stage=` and `?segments=` flags act on. */
export const LONG_SAMPLE_ID = '20261005-160000-dsrev';

/** Lines the Recording session's live draft cycles through (`?live=1`). */
export const LIVE_DRAFT_LINES: readonly string[] = (GENERIC['20261006-100000-q3plan']?.blocks[0]?.lines ?? []).map(([, text]) => text.replace(/[{}]/g, ''));

export function hasScript(recordingId: string): boolean {
  return Object.hasOwn(SCRIPTS, recordingId);
}

/** Deterministic pseudo-random numbers in 0..1 for one recording. */
function randomFor(seed: number): () => number {
  let value = seed % 233_280;
  return () => {
    value = (value * 9301 + 49_297) % 233_280;
    return value / 233_280;
  };
}

const round3 = (n: number): number => Math.round(n * 1000) / 1000;

interface PlacedLine {
  key: string;
  uncertain: boolean;
  text: string;
  lowWords: Set<number>;
  start: number;
  end: number;
}

/** Splits `{word}` markers out of a line: the plain text and the indexes of the marked words. */
function parseLine(raw: string): { text: string; lowWords: Set<number> } {
  const lowWords = new Set<number>();
  const words = raw.split(/\s+/).filter((w) => w !== '');
  const plain = words.map((word, index) => {
    if (word.includes('{')) {
      lowWords.add(index);
    }
    return word.replace(/[{}]/g, '');
  });
  return { text: plain.join(' '), lowWords };
}

/** Lines of one block get the block's time in proportion to their length; `at` pins a line. */
function placeBlock(block: ScriptBlock): PlacedLine[] {
  const placed: PlacedLine[] = [];
  const lines = block.lines.map(([speaker, raw, at]) => ({ speaker, raw, at, ...parseLine(raw) }));
  let index = 0;
  let cursor = block.from;
  while (index < lines.length) {
    // The run up to the next pinned line (or the block's end).
    let next = index + 1;
    while (next < lines.length && lines[next]?.at === undefined) {
      next += 1;
    }
    const runStart = lines[index]?.at ?? cursor;
    const runEnd = lines[next]?.at ?? block.to;
    const run = lines.slice(index, next);
    const weight = run.reduce((sum, l) => sum + l.text.length + 40, 0);
    let at = runStart;
    for (const line of run) {
      const slot = ((runEnd - runStart) * (line.text.length + 40)) / weight;
      const words = line.text.split(' ').length;
      const speech = Math.min(Math.max(0.8, slot - 0.6), words / 2.5 + 0.6);
      placed.push({
        key: line.speaker.replace('?', ''),
        uncertain: line.speaker.startsWith('?'),
        text: line.text,
        lowWords: line.lowWords,
        start: round3(at),
        end: round3(at + speech),
      });
      at += slot;
    }
    cursor = runEnd;
    index = next;
  }
  return placed;
}

/** Word timings shared out over the segment in proportion to length; confidences seeded. */
function wordsFor(text: string, start: number, end: number, lowWords: ReadonlySet<number>, random: () => number): TranscriptWord[] {
  const tokens = text.split(' ').filter((t) => t !== '');
  const total = tokens.reduce((sum, t) => sum + t.length + 1, 0);
  let at = start;
  return tokens.map((token, index) => {
    const length = ((end - start) * (token.length + 1)) / total;
    const r = random();
    // A few words the engine was unsure of besides the marked ones.
    const low = lowWords.has(index) || r < 0.012;
    const c = low ? 0.28 + random() * 0.18 : 0.84 + random() * 0.15;
    const word = { w: token, s: round3(at), e: round3(at + length * 0.92), c: round3(c) };
    at += length;
    return word;
  });
}

function talkTimes(segments: readonly TranscriptSegment[], speakers: Speaker[]): Speaker[] {
  const totals = new Map<string, number>();
  for (const segment of segments) {
    if (segment.speaker !== null) {
      totals.set(segment.speaker, (totals.get(segment.speaker) ?? 0) + Math.round((segment.end - segment.start) * 1000));
    }
  }
  return speakers.map((s) => ({ ...s, talkTimeMs: totals.get(s.id) ?? 0 }));
}

export { talkTimes as withTalkTimes };

export interface BuildOptions {
  durationMs: number;
  trackId: string | null;
  threshold: number;
  keepWords: boolean;
  engine: Transcript['engine'];
  /** Only the segments that end before this fraction of the recording (a partial, failed pass). */
  upTo?: number;
  /** Repeat the script's lines to this many segments over the recording (`?segments=10000`). */
  segmentCount?: number;
  /** ISO time of the sample edit. */
  editedAt: string;
  /** Assign speakers (default: whether the script's speakers stage ran). */
  identify?: boolean;
}

/** A recording made in the preview: the planning lines, two unnamed voices, over its whole length. */
function fallbackScript(durationMs: number): Script {
  const lines = LIVE_DRAFT_LINES.map((text, i) => [i % 2 === 0 ? 'A' : 'B', text] as const);
  const to = Math.max(4, durationMs / 1000 - 1);
  return {
    speakers: { A: ['Speaker 1', false], B: ['Speaker 2', false] },
    identified: true,
    blocks: [{ from: Math.min(1, to / 4), to, lines: lines.slice(0, Math.max(1, Math.min(lines.length, Math.ceil(to / 6)))) }],
  };
}

/** The sample transcript of a recording; recordings without a script get the planning lines. */
export function buildTranscript(recordingId: string, options: BuildOptions): Transcript {
  const script = SCRIPTS[recordingId] ?? fallbackScript(options.durationMs);
  const identified = options.identify ?? script.identified;
  const random = randomFor(seedFromId(recordingId));
  let placed = script.blocks.flatMap(placeBlock);
  if (options.segmentCount !== undefined && options.segmentCount > 0) {
    placed = stretch(placed, options.segmentCount, options.durationMs / 1000);
  }
  const keys = Object.keys(script.speakers);
  const speakerIds = new Map(keys.map((key, i) => [key, `sp${i + 1}`]));
  const limit = options.upTo === undefined ? Number.POSITIVE_INFINITY : (options.durationMs / 1000) * options.upTo;
  const segments: TranscriptSegment[] = [];
  placed.forEach((line, index) => {
    if (line.end > limit) {
      return;
    }
    const words = wordsFor(line.text, line.start, line.end, line.lowWords, random);
    const edit = script.edits?.find((e) => line.text.startsWith(e.startsWith));
    const speaker = identified ? (speakerIds.get(line.key) ?? null) : null;
    segments.push({
      id: `g${String(index + 1).padStart(5, '0')}`,
      start: line.start,
      end: line.end,
      track: options.trackId,
      speaker,
      speakerConfidence: speaker === null ? null : round3(line.uncertain ? 0.42 + random() * 0.22 : 0.8 + random() * 0.19),
      text: line.text,
      confidence: edit === undefined ? Math.min(1, ...words.map((w) => w.c)) : 1,
      words: options.keepWords ? (edit === undefined ? words : words.map((w) => ({ ...w, c: 1 }))) : [],
      edited: edit === undefined ? null : { at: options.editedAt, original: edit.original },
    });
  });
  const speakers: Speaker[] = identified
    ? keys.map((key, i) => {
        const [name, renamed] = script.speakers[key] ?? [`Speaker ${i + 1}`, false];
        return { id: `sp${i + 1}`, name, renamed, color: ((i % 4) + 1) as SpeakerColour, talkTimeMs: 0 };
      })
    : [];
  return {
    schemaVersion: 1,
    language: 'en',
    languageDetected: true,
    engine: options.engine,
    speakers: talkTimes(segments, speakers),
    segments,
    reviewed: false,
    version: 1,
    lowConfidenceThreshold: options.threshold,
  };
}

/** The script's lines repeated to `count` segments spread evenly over the recording. */
function stretch(lines: readonly PlacedLine[], count: number, durationSeconds: number): PlacedLine[] {
  if (lines.length === 0) {
    return [];
  }
  const slot = durationSeconds / count;
  const out: PlacedLine[] = [];
  for (let i = 0; i < count; i++) {
    const line = lines[i % lines.length];
    if (line === undefined) {
      continue;
    }
    const start = round3(i * slot);
    out.push({ ...line, start, end: round3(start + Math.max(0.2, slot * 0.9)) });
  }
  return out;
}

/** Speakers for a transcript whose speakers stage just ran: "Speaker 1…n" for a script without names. */
export function scriptSpeakers(recordingId: string): readonly (readonly [string, boolean])[] {
  return Object.values(SCRIPTS[recordingId]?.speakers ?? {});
}
