// Review's outline (left) and details (right) panes (DESIGN.md §9), transcribed from
// renders/Review.dc.html.
import { Fragment, type JSX } from 'preact';
import { useRef, useState } from 'preact/hooks';
import type { Chapter, Highlight, HistoryEntry, HistoryLink, HistorySettings, Project, Speaker, StageName, Topic, TranscriptVersion } from '../../bridge/types';
import { TagEditor } from '../../components/DetailsSheet';
import { AttachmentsSection } from '../../components/attachments/AttachmentsSection';
import { CloseIcon, MergeIcon, PencilIcon, PlusIcon } from '../../components/icons';
import { InlineInput } from '../../components/InlineInput';
import { SpeakerChooser } from '../../components/SpeakerChooser';
import { moveFocus } from '../../components/keyboard';
import { DocumentsTab } from '../docview/DocumentsTab';
import { formatDuration } from '../../format/duration';
import { asOfWording, linksByIndex, noVersionNote } from '../../format/history';
import { activeChapterIndex, chapterInsertIndex, historyTone } from '../../format/player';
import { typeName } from '../../format/recording';
import { speakerColourVar, talkShare, versionReasonText } from '../../format/transcript';
import { calendarDaysBetween, formatClock, formatShortDate, parseIso } from '../../format/when';

// ---------------------------------------------------------------------------------------------
// Outline
// ---------------------------------------------------------------------------------------------

interface OutlineProps {
  project: Project;
  positionMs: number;
  onSeek: (ms: number) => void;
  onAddChapter: (atMs: number, title: string) => void;
  /** Clicking a chapter's or highlight's name edits it in place; × removes it (Undo brings it back). */
  onRenameChapter: (chapter: Chapter, title: string) => void;
  onRemoveChapter: (chapter: Chapter) => void;
  onRenameHighlight: (highlight: Highlight, note: string) => void;
  onRemoveHighlight: (highlight: Highlight) => void;
  onAddTopic: (label: string) => void;
  onRemoveTopic: (topic: Topic) => void;
  onRenamePerson: (index: number, name: string) => void;
  /** The transcript's speakers once speakers are identified; null shows the participants instead. */
  speakers: Speaker[] | null;
  onRenameSpeaker: (speaker: Speaker, name: string) => void;
  onMergeSpeakers: (from: Speaker, into: Speaker) => void;
  /** After 1.2.0: a click on a speaker's name filters the transcript to their lines. */
  speakerFilter?: SpeakerFilterProps;
}

/** The People list's part in the transcript filter (DESIGN.md §9, after 1.2.0). */
export interface SpeakerFilterProps {
  /** The speaker ids the transcript is filtered to. */
  selected: readonly string[];
  /** Lines per speaker id. */
  counts: ReadonlyMap<string, number>;
  /** `add`: Ctrl or Shift was held, so the speaker joins or leaves the others instead of replacing them. */
  onToggle: (speaker: Speaker, add: boolean) => void;
}

const SPEAKER_COLOURS = ['var(--sp1)', 'var(--sp2)', 'var(--sp3)', 'var(--sp4)'] as const;

export function speakerColour(index: number): string {
  return SPEAKER_COLOURS[index % SPEAKER_COLOURS.length] ?? 'var(--sp1)';
}

/**
 * A chapter or highlight in the outline (DESIGN.md §5.13): the time plays from there, the name edits in
 * place (Enter or leaving saves, Esc cancels), and × removes it.
 */
function OutlineRow({
  atMs,
  name,
  placeholder,
  noun,
  active,
  emptyName,
  onSeek,
  onRename,
  onRemove,
}: {
  atMs: number;
  name: string;
  placeholder: string;
  /** "chapter", "highlight". */
  noun: string;
  active: boolean;
  /** Shown when the name is empty ("Highlight"). */
  emptyName?: string;
  onSeek: (ms: number) => void;
  onRename: (name: string) => void;
  onRemove: () => void;
}): JSX.Element {
  const [editing, setEditing] = useState(false);
  const nameButton = useRef<HTMLButtonElement | null>(null);
  const time = formatDuration(atMs);
  const shown = name === '' ? (emptyName ?? '') : name;
  const nameClass = noun === 'chapter' ? 'chap-title' : name === '' ? 'chap-note chap-note--empty' : 'chap-note';
  const refocus = (): void => {
    requestAnimationFrame(() => {
      nameButton.current?.focus();
    });
  };
  return (
    <div class={active ? 'chap chap-row on' : 'chap chap-row'} aria-current={active ? 'true' : undefined}>
      <button
        class="chap-time"
        type="button"
        aria-label={`Play from ${time}${shown === '' ? '' : `, ${shown}`}`}
        onClick={() => {
          onSeek(atMs);
        }}
      >
        <span class="mono chap-at">{time}</span>
      </button>
      {editing ? (
        <InlineInput
          label={`Name of the ${noun} at ${time}. Enter saves, Esc cancels.`}
          initial={name}
          placeholder={placeholder}
          onCommit={(next) => {
            setEditing(false);
            if (next !== name) {
              onRename(next);
            }
            refocus();
          }}
          onCancel={() => {
            setEditing(false);
            refocus();
          }}
        />
      ) : (
        <button
          ref={nameButton}
          class="chap-name"
          type="button"
          aria-label={shown === '' ? `Name the ${noun} at ${time}` : `Rename ${shown}`}
          title="Click to rename"
          onClick={() => {
            setEditing(true);
          }}
        >
          <span class={nameClass}>{shown}</span>
        </button>
      )}
      {editing ? null : (
        <button
          class="chap-remove"
          type="button"
          aria-label={`Remove the ${noun} at ${time}${shown === '' ? '' : `, ${shown}`}`}
          title="Remove (Ctrl+Z brings it back)"
          onClick={onRemove}
        >
          <CloseIcon size={12} />
        </button>
      )}
    </div>
  );
}

/** The People list's merge menu: the other speakers in a searchable, scrolling list (SpeakerChooser). */
function MergeMenu({ speaker, speakers, onMerge }: { speaker: Speaker; speakers: Speaker[]; onMerge: OutlineProps['onMergeSpeakers'] }): JSX.Element {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLSpanElement | null>(null);
  const button = useRef<HTMLButtonElement | null>(null);
  const others = speakers.filter((s) => s.id !== speaker.id);
  return (
    <span class={open ? 'menu-root menu-root--open' : 'menu-root'} ref={root}>
      <button
        ref={button}
        class="icon-btn person-rename person-merge"
        type="button"
        aria-label={`Merge ${speaker.name} into someone else`}
        aria-haspopup="listbox"
        aria-expanded={open}
        onClick={() => {
          setOpen(!open);
        }}
        onKeyDown={(event) => {
          if (event.key === 'ArrowDown') {
            event.preventDefault();
            setOpen(true);
          }
        }}
      >
        <MergeIcon size={13} />
      </button>
      {open ? (
        <SpeakerChooser
          anchorRef={button}
          rootRef={root}
          label={`Merge ${speaker.name} into`}
          heading={`Merge ${speaker.name} into`}
          speakers={others}
          currentId={null}
          align="end"
          gap={8}
          onPick={(into) => {
            onMerge(speaker, into);
          }}
          onClose={(focusTrigger) => {
            setOpen(false);
            if (focusTrigger) {
              button.current?.focus();
            }
          }}
        />
      ) : null}
    </span>
  );
}

/** People from the transcript (DESIGN.md §9): dot, name, talk-time share, rename, merge. */
function SpeakerList({
  speakers,
  onRename,
  onMerge,
  filter,
}: {
  speakers: Speaker[];
  onRename: OutlineProps['onRenameSpeaker'];
  onMerge: OutlineProps['onMergeSpeakers'];
  filter?: SpeakerFilterProps | undefined;
}): JSX.Element {
  const [renaming, setRenaming] = useState<string | null>(null);
  return (
    <>
      {speakers.map((speaker) => (
        <div key={speaker.id} class={filter?.selected.includes(speaker.id) === true ? 'person person--filtered' : 'person'} data-speaker-id={speaker.id}>
          <span class="person-dot" aria-hidden="true" style={{ background: speakerColourVar(speaker.color) }} />
          {renaming === speaker.id ? (
            <InlineInput
              label={`New name for ${speaker.name}`}
              initial={speaker.name}
              placeholder="Name"
              class="person-input"
              onCommit={(next) => {
                setRenaming(null);
                if (next !== speaker.name) {
                  onRename(speaker, next);
                }
              }}
              onCancel={() => {
                setRenaming(null);
              }}
            />
          ) : (
            <>
              {filter === undefined ? (
                <span class="person-name">{speaker.name}</span>
              ) : (
                <SpeakerFilterButton speaker={speaker} filter={filter} />
              )}
              {filter?.selected.includes(speaker.id) === true ? (
                <span class="person-count">{lineWords(filter.counts.get(speaker.id) ?? 0)}</span>
              ) : (
                <span class="person-share" title="Share of talk time">
                  {talkShare(speaker, speakers)}
                </span>
              )}
              <button
                class="icon-btn person-rename"
                type="button"
                aria-label={`Rename ${speaker.name}`}
                onClick={() => {
                  setRenaming(speaker.id);
                }}
              >
                <PencilIcon size={13} />
              </button>
              {speakers.length > 1 ? <MergeMenu speaker={speaker} speakers={speakers} onMerge={onMerge} /> : null}
            </>
          )}
        </div>
      ))}
    </>
  );
}

function lineWords(lines: number): string {
  return `${lines} ${lines === 1 ? 'line' : 'lines'}`;
}

/**
 * The speaker's name as a toggle (after 1.2.0): a click shows only their lines, a second click shows
 * everyone again; Ctrl or Shift adds them to the speakers shown. While on, the row is pressed in and
 * says how many lines are theirs in place of the talk-time share.
 */
function SpeakerFilterButton({ speaker, filter }: { speaker: Speaker; filter: SpeakerFilterProps }): JSX.Element {
  const on = filter.selected.includes(speaker.id);
  const lines = filter.counts.get(speaker.id) ?? 0;
  const words = lineWords(lines);
  return (
    <button
      class="person-name person-filter"
      type="button"
      aria-pressed={on}
      aria-label={on ? `Showing ${speaker.name}'s ${words}. Click to show every line.` : `Show only ${speaker.name}'s ${words}`}
      title={on ? 'Click to show every line (Esc)' : 'Show only their lines · Ctrl+click to add them to the speakers shown'}
      onClick={(event) => {
        filter.onToggle(speaker, event.ctrlKey || event.shiftKey || event.metaKey);
      }}
    >
      <span class="person-filter-name">{speaker.name}</span>
    </button>
  );
}

export function OutlinePane({
  project,
  positionMs,
  onSeek,
  onAddChapter,
  onRenameChapter,
  onRemoveChapter,
  onRenameHighlight,
  onRemoveHighlight,
  onAddTopic,
  onRemoveTopic,
  onRenamePerson,
  speakers,
  onRenameSpeaker,
  onMergeSpeakers,
  speakerFilter,
}: OutlineProps): JSX.Element {
  const [newChapterAt, setNewChapterAt] = useState<number | null>(null);
  const [renaming, setRenaming] = useState<number | null>(null);
  const chapters = project.chapters;
  const active = activeChapterIndex(chapters, positionMs);
  const insertAt = newChapterAt === null ? -1 : chapterInsertIndex(chapters, newChapterAt);
  const people = project.details.participants;
  const userTopics = project.topics.filter((t) => t.origin === 'user');
  // Participants the speakers list does not already name, with their index in details.participants.
  const hasSpeakers = speakers !== null && speakers.length > 0;
  const speakerNames = new Set((speakers ?? []).map((s) => s.name.toLocaleLowerCase()));
  const otherParticipants = people.map((name, i) => ({ name, i })).filter(({ name }) => !speakerNames.has(name.toLocaleLowerCase()));

  const newChapterRow =
    newChapterAt === null ? null : (
      <div class="chap chap--new" key="new-chapter">
        <span class="mono chap-at">{formatDuration(newChapterAt)}</span>
        <InlineInput
          label={`Title for the chapter at ${formatDuration(newChapterAt)}`}
          placeholder="Chapter title"
          onCommit={(title) => {
            onAddChapter(newChapterAt, title);
            setNewChapterAt(null);
          }}
          onCancel={() => {
            setNewChapterAt(null);
          }}
        />
      </div>
    );

  return (
    <aside class="review-outline" aria-label="Outline">
      <div class="outline-group">
        <div class="outline-head">
          <span class="lbl">Chapters</span>
          <button
            class="icon-btn outline-add"
            type="button"
            aria-label={`Add a chapter at ${formatDuration(positionMs)}`}
            onClick={() => {
              setNewChapterAt(Math.round(positionMs));
            }}
          >
            <PlusIcon size={14} />
          </button>
        </div>
        <div class="outline-list">
          {chapters.length === 0 && newChapterAt === null ? <p class="outline-empty">No chapters yet. Press + to add one at the playhead.</p> : null}
          {chapters.map((chapter, i) => (
            <Fragment key={chapter.id}>
              {i === insertAt ? newChapterRow : null}
              <OutlineRow
                atMs={chapter.atMs}
                name={chapter.title}
                placeholder="Chapter title"
                noun="chapter"
                active={i === active}
                onSeek={onSeek}
                onRename={(title) => {
                  onRenameChapter(chapter, title);
                }}
                onRemove={() => {
                  onRemoveChapter(chapter);
                }}
              />
            </Fragment>
          ))}
          {insertAt === chapters.length ? newChapterRow : null}
        </div>
      </div>

      <div class="outline-group">
        <div class="outline-head">
          <span class="lbl">Highlights · {project.highlights.length}</span>
        </div>
        <div class="outline-list">
          {project.highlights.length === 0 ? (
            <p class="outline-empty">None yet. Highlight marks the playhead; during a recording, Ctrl+M does.</p>
          ) : (
            project.highlights.map((h) => (
              <OutlineRow
                key={h.id}
                atMs={h.atMs}
                name={h.note}
                emptyName="Highlight"
                placeholder="Highlight name"
                noun="highlight"
                active={false}
                onSeek={onSeek}
                onRename={(note) => {
                  onRenameHighlight(h, note);
                }}
                onRemove={() => {
                  onRemoveHighlight(h);
                }}
              />
            ))
          )}
        </div>
      </div>

      <div class="outline-group outline-group--pad">
        <span class="lbl">Topics</span>
        <TagEditor
          noun="topic"
          locked={project.topics.filter((t) => t.origin !== 'user').map((t) => t.label)}
          tags={userTopics.map((t) => t.label)}
          onChange={(labels) => {
            const added = labels.find((l) => !userTopics.some((t) => t.label === l));
            const removed = userTopics.find((t) => !labels.includes(t.label));
            if (added !== undefined) {
              onAddTopic(added);
            }
            if (removed !== undefined) {
              onRemoveTopic(removed);
            }
          }}
        />
      </div>

      <div class="outline-group">
        <div class="outline-head">
          <span class="lbl">People</span>
        </div>
        <div class="outline-list">
          {hasSpeakers ? (
            <SpeakerList speakers={speakers} onRename={onRenameSpeaker} onMerge={onMergeSpeakers} filter={speakerFilter} />
          ) : people.length === 0 ? (
            <p class="outline-empty">No participants listed. Add them with Edit details.</p>
          ) : null}
          {otherParticipants.length > 0 && hasSpeakers ? (
            <span class="outline-sub">Also listed as participants</span>
          ) : null}
          {(hasSpeakers ? otherParticipants : people.map((name, i) => ({ name, i }))).map(({ name, i }) => (
            <div key={`${name}-${i}`} class="person">
              <span class="person-dot" aria-hidden="true" style={{ background: hasSpeakers ? 'var(--line-strong)' : speakerColour(i) }} />
              {renaming === i ? (
                <InlineInput
                  label={`New name for ${name}`}
                  initial={name}
                  placeholder="Name"
                  class="person-input"
                  onCommit={(next) => {
                    setRenaming(null);
                    if (next !== name) {
                      onRenamePerson(i, next);
                    }
                  }}
                  onCancel={() => {
                    setRenaming(null);
                  }}
                />
              ) : (
                <>
                  <span class="person-name">{name}</span>
                  <span class="person-share" title={hasSpeakers ? 'Not matched to a speaker in the transcript' : 'Talk time appears once speakers are identified'}>
                    —
                  </span>
                  <button
                    class="icon-btn person-rename"
                    type="button"
                    aria-label={`Rename ${name}`}
                    onClick={() => {
                      setRenaming(i);
                    }}
                  >
                    <PencilIcon size={13} />
                  </button>
                </>
              )}
            </div>
          ))}
        </div>
      </div>
    </aside>
  );
}

// ---------------------------------------------------------------------------------------------
// Details | Documents | History
// ---------------------------------------------------------------------------------------------

export type DetailsTab = 'details' | 'documents' | 'history';

const TABS: readonly { id: DetailsTab; label: string }[] = [
  { id: 'details', label: 'Details' },
  { id: 'documents', label: 'Documents' },
  { id: 'history', label: 'History' },
];

/** "Oct 5, 2026 · 4:00 PM" for the fact grid. */
export function recordedWording(iso: string): string {
  const date = parseIso(iso);
  return `${formatShortDate(date, date)}, ${date.getFullYear()} · ${formatClock(date)}`;
}

/** "Today 4:00 PM", "Yesterday 4:00 PM", "Oct 5, 4:00 PM" in the History timeline. */
export function historyWhen(iso: string, now: Date): string {
  const date = parseIso(iso);
  const days = calendarDaysBetween(date, now);
  const prefix = days <= 0 ? 'Today' : days === 1 ? 'Yesterday' : `${formatShortDate(date, now)},`;
  return `${prefix} ${formatClock(date)}`;
}

const RETRYABLE: ReadonlySet<string> = new Set<StageName>(['stored', 'transcript', 'speakers', 'minutes', 'optimize']);

/** After 1.2.0: a line that made a stored version opens it; the others say on hover why they open nothing. */
export interface HistoryVersions {
  /** history.links; null while it is read. */
  links: HistoryLink[] | null;
  /** The line whose version is open, or null. */
  openIndex: number | null;
  onOpen: (link: HistoryLink, entry: HistoryEntry) => void;
}

function HistoryList({
  history,
  now,
  onRetry,
  versions,
}: {
  history: HistoryEntry[];
  now: Date;
  onRetry: (stage: StageName) => void;
  versions?: HistoryVersions;
}): JSX.Element {
  if (history.length === 0) {
    return <p class="outline-empty">Nothing has happened to this recording yet.</p>;
  }
  const links = linksByIndex(versions?.links ?? null);
  // Retry belongs to a stage's latest entry only: a failure that a later pass fixed is history.
  const latest = new Map<string, number>();
  history.forEach((entry, i) => {
    latest.set(entry.stage, i);
  });
  return (
    <ol class="history">
      {history.map((entry, i) => {
        const tone = historyTone(entry);
        const retryable = tone === 'failed' && RETRYABLE.has(entry.stage) && latest.get(entry.stage) === i;
        const link = links.get(i);
        return (
          <li key={`${entry.at}-${i}`} class={versions?.openIndex === i ? 'history-item history-item--open' : 'history-item'}>
            <span class={`history-dot history-dot--${tone}`} aria-hidden="true" />
            <span class="history-text">
              {versions !== undefined && link?.versionId != null ? (
                <button
                  class="history-title history-open"
                  type="button"
                  aria-pressed={versions.openIndex === i}
                  aria-label={`Open ${link.kind === 'transcript' ? 'the transcript' : 'the document'} as of ${asOfWording(entry.at, now)}, ${link.after}`}
                  title={link.versionId === 'current' ? 'Open this version (it is the one you have now)' : 'Open this version to read it or restore it'}
                  onClick={() => {
                    versions.onOpen(link, entry);
                  }}
                >
                  {entry.summary}
                </button>
              ) : (
                <span class="history-title" title={versions?.links == null ? undefined : noVersionNote(link)}>
                  {entry.summary}
                  {tone === 'failed' ? <span class="sr"> (failed)</span> : null}
                </span>
              )}
              <span class="history-detail">
                {historyWhen(entry.at, now)}
                {entry.detail === null ? '' : ` · ${entry.detail}`}
              </span>
              {retryable ? (
                <button
                  class="btn link-btn history-retry"
                  type="button"
                  aria-label={`Retry: ${entry.summary}`}
                  onClick={() => {
                    onRetry(entry.stage as StageName);
                  }}
                >
                  Retry
                </button>
              ) : null}
            </span>
          </li>
        );
      })}
    </ol>
  );
}

/** "Transcript versions" (Details tab, shown while version history is on): earlier transcripts to restore. */
function VersionsCard({
  versions,
  current,
  keepDays,
  now,
  onRestore,
}: {
  versions: TranscriptVersion[] | null;
  current: { version: number; engine: string; segments: number } | null;
  keepDays: number;
  now: Date;
  onRestore: (version: TranscriptVersion, when: string) => void;
}): JSX.Element {
  return (
    <div class="detail-group">
      <div class="detail-head">
        <span class="lbl">Transcript versions</span>
        <span class="detail-caption">History on · {keepDays} days</span>
      </div>
      <div class="versions">
        {current === null ? null : (
          <div class="ver cur version-card">
            <span class="version-title">Current · version {current.version}</span>
            <span class="version-meta">
              {current.engine} · {current.segments.toLocaleString('en-US')} lines
            </span>
          </div>
        )}
        {versions === null ? (
          <span class="fact-empty">Reading versions…</span>
        ) : versions.length === 0 ? (
          <span class="fact-empty">No earlier versions yet. One is kept whenever the transcript is edited or replaced.</span>
        ) : (
          versions.map((version) => {
            const when = historyWhen(version.at, now);
            return (
              <div key={version.id} class="ver version-card">
                <span class="version-text">
                  <span class="version-title">{versionReasonText(version.reason)}</span>
                  <span class="version-meta">
                    {when}
                    {version.engine === null ? '' : ` · ${version.engine}`} · {version.segments.toLocaleString('en-US')} lines
                  </span>
                </span>
                <button
                  class="btn ghost small-btn version-restore"
                  type="button"
                  aria-label={`Restore the version from ${when}`}
                  onClick={() => {
                    onRestore(version, when);
                  }}
                >
                  Restore
                </button>
              </div>
            );
          })
        )}
      </div>
    </div>
  );
}

interface DetailsPaneProps {
  project: Project;
  tab: DetailsTab;
  onTab: (tab: DetailsTab) => void;
  onEditDetails: () => void;
  onTags: (tags: string[]) => void;
  onCreateDocument: () => void;
  now: Date;
  onRetry: (stage: StageName) => void;
  /** Settings › Documents › History; versions show only while it is on. */
  history: HistorySettings | null;
  versions: TranscriptVersion[] | null;
  currentVersion: { version: number; engine: string; segments: number } | null;
  onRestore: (version: TranscriptVersion, when: string) => void;
  /** After 1.2.0: History lines open the version they made. */
  historyVersions?: HistoryVersions;
  /** M3: the agenda caption's Replace link. */
  onReplaceAgenda?: () => void;
}

export function DetailsPane({
  project,
  tab,
  onTab,
  onEditDetails,
  onTags,
  onCreateDocument,
  now,
  onRetry,
  history,
  versions,
  currentVersion,
  onRestore,
  historyVersions,
  onReplaceAgenda,
}: DetailsPaneProps): JSX.Element {
  const { details, summary, tracks } = project;
  const agenda = details.agenda;
  return (
    <aside class="review-details" aria-label="Recording details">
      <div
        class="seg-well review-tabs"
        role="tablist"
        aria-label="Recording details"
        onKeyDown={(event) => {
          const moved = moveFocus(event, event.currentTarget, '[role="tab"]', 'horizontal');
          const id = moved?.dataset.tab as DetailsTab | undefined;
          if (id !== undefined) {
            onTab(id);
          }
        }}
      >
        {TABS.map((t) => (
          <button
            key={t.id}
            id={`review-tab-${t.id}`}
            class={t.id === tab ? 'seg on' : 'seg'}
            type="button"
            role="tab"
            data-tab={t.id}
            aria-selected={t.id === tab}
            aria-controls={`review-panel-${t.id}`}
            tabIndex={t.id === tab ? 0 : -1}
            onClick={() => {
              onTab(t.id);
            }}
          >
            {t.label}
          </button>
        ))}
      </div>

      <div id={`review-panel-${tab}`} role="tabpanel" aria-labelledby={`review-tab-${tab}`} class="review-panel">
        {tab === 'details' ? (
          <>
            <dl class="facts">
              <dt>Type</dt>
              <dd class="fact-strong">{typeName(details.type)}</dd>
              <dt>Recorded</dt>
              <dd class="fact-strong">{recordedWording(summary.createdAt)}</dd>
              <dt>Duration</dt>
              <dd class="mono fact-mono">{formatDuration(summary.durationMs)}</dd>
              <dt>Platform</dt>
              <dd class={details.platform === '' ? 'fact-empty' : 'fact-strong'}>{details.platform === '' ? 'Not set' : details.platform}</dd>
              <dt>Tracks</dt>
              <dd class="fact-strong">
                {tracks.length}
                {tracks.length === 0 ? '' : <span class="fact-sub"> · {tracks.map((t) => t.name).join(', ')}</span>}
              </dd>
              <dt>Purpose</dt>
              <dd class={details.purpose === '' ? 'fact-empty' : ''}>{details.purpose === '' ? 'Not set' : details.purpose}</dd>
            </dl>
            <div class="detail-group">
              <span class="lbl">Participants</span>
              {details.participants.length === 0 ? (
                <span class="fact-empty">Just you, or nobody listed yet</span>
              ) : (
                <div class="pill-row">
                  {details.participants.map((p) => (
                    <span key={p} class="pill done">
                      {p}
                    </span>
                  ))}
                </div>
              )}
            </div>
            <div class="detail-group">
              <div class="detail-head">
                <span class="lbl">Agenda</span>
                {agenda.items.length === 0 ? null : (
                  <span class="detail-caption">
                    {agenda.source ?? 'Pasted text'} · {agenda.parsedLocally ? 'parsed locally' : 'extracted with AI'}
                    {onReplaceAgenda === undefined ? null : (
                      <>
                        {' · '}
                        <button class="btn link-btn" type="button" aria-haspopup="dialog" onClick={onReplaceAgenda}>
                          Replace
                        </button>
                      </>
                    )}
                  </span>
                )}
              </div>
              {agenda.items.length === 0 ? (
                <span class="fact-empty">No agenda</span>
              ) : (
                <ol class="agenda-read">
                  {agenda.items.map((item, i) => (
                    <li key={item.id}>
                      <span class="mono agenda-read-n">{i + 1}</span>
                      <span>{item.text}</span>
                    </li>
                  ))}
                </ol>
              )}
            </div>
            <div class="detail-group">
              <span class="lbl">Tags</span>
              <TagEditor tags={details.tags} onChange={onTags} />
            </div>
            <AttachmentsSection recordingId={summary.id} variant="review" />
            {history?.keepVersions === true && currentVersion !== null ? (
              <VersionsCard versions={versions} current={currentVersion} keepDays={history.keepDays} now={now} onRestore={onRestore} />
            ) : null}
            <button class="btn ghost edit-details" type="button" aria-haspopup="dialog" onClick={onEditDetails}>
              Edit details
            </button>
          </>
        ) : tab === 'documents' ? (
          // M4: the recording's documents (screens/docview/DocumentsTab.tsx).
          <DocumentsTab recordingId={summary.id} onCreate={onCreateDocument} />
        ) : (
          <HistoryList history={project.history} now={now} onRetry={onRetry} {...(historyVersions === undefined ? {} : { versions: historyVersions })} />
        )}
      </div>
    </aside>
  );
}
