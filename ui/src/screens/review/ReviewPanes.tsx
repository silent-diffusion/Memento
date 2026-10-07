// Review's outline (left) and details (right) panes (DESIGN.md §9), transcribed from
// renders/Review.dc.html.
import { Fragment, type JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import type { Chapter, Highlight, HistoryEntry, HistorySettings, Project, Speaker, StageName, Topic, TranscriptVersion } from '../../bridge/types';
import { TagEditor } from '../../components/DetailsSheet';
import { DocumentIcon, MergeIcon, PencilIcon, PlusIcon } from '../../components/icons';
import { moveFocus } from '../../components/keyboard';
import { ActionMenu } from '../../components/Menus';
import { formatDuration } from '../../format/duration';
import { activeChapterIndex, chapterInsertIndex, historyTone } from '../../format/player';
import { typeName } from '../../format/recording';
import { speakerColourVar, talkShare, versionReasonText } from '../../format/transcript';
import { calendarDaysBetween, formatClock, formatShortDate, parseIso } from '../../format/when';

// ---------------------------------------------------------------------------------------------
// Outline
// ---------------------------------------------------------------------------------------------

interface InlineInputProps {
  label: string;
  initial?: string;
  placeholder: string;
  onCommit: (value: string) => void;
  onCancel: () => void;
  class?: string;
}

/** A one-line input that commits on Enter or blur and cancels on Esc or when left empty. */
function InlineInput({ label, initial = '', placeholder, onCommit, onCancel, class: className }: InlineInputProps): JSX.Element {
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
      onInput={(event) => {
        setValue(event.currentTarget.value);
      }}
      onKeyDown={(event) => {
        if (event.key === 'Enter') {
          event.preventDefault();
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

interface OutlineProps {
  project: Project;
  positionMs: number;
  onSeek: (ms: number) => void;
  onAddChapter: (atMs: number, title: string) => void;
  onAddTopic: (label: string) => void;
  onRemoveTopic: (topic: Topic) => void;
  onRenamePerson: (index: number, name: string) => void;
  /** The transcript's speakers once speakers are identified; null shows the participants instead. */
  speakers: Speaker[] | null;
  onRenameSpeaker: (speaker: Speaker, name: string) => void;
  onMergeSpeakers: (from: Speaker, into: Speaker) => void;
}

const SPEAKER_COLOURS = ['var(--sp1)', 'var(--sp2)', 'var(--sp3)', 'var(--sp4)'] as const;

export function speakerColour(index: number): string {
  return SPEAKER_COLOURS[index % SPEAKER_COLOURS.length] ?? 'var(--sp1)';
}

function ChapterRow({ chapter, active, onSeek }: { chapter: Chapter; active: boolean; onSeek: (ms: number) => void }): JSX.Element {
  return (
    <button
      class={active ? 'chap on' : 'chap'}
      type="button"
      aria-current={active ? 'true' : undefined}
      onClick={() => {
        onSeek(chapter.atMs);
      }}
    >
      <span class="mono chap-at">{formatDuration(chapter.atMs)}</span>
      <span class="chap-title">{chapter.title}</span>
    </button>
  );
}

function HighlightRow({ highlight, onSeek }: { highlight: Highlight; onSeek: (ms: number) => void }): JSX.Element {
  return (
    <button
      class="chap"
      type="button"
      onClick={() => {
        onSeek(highlight.atMs);
      }}
    >
      <span class="mono chap-at">{formatDuration(highlight.atMs)}</span>
      <span class={highlight.note === '' ? 'chap-note chap-note--empty' : 'chap-note'}>{highlight.note === '' ? 'Highlight' : highlight.note}</span>
    </button>
  );
}

/** People from the transcript (DESIGN.md §9): dot, name, talk-time share, rename, merge. */
function SpeakerList({ speakers, onRename, onMerge }: { speakers: Speaker[]; onRename: OutlineProps['onRenameSpeaker']; onMerge: OutlineProps['onMergeSpeakers'] }): JSX.Element {
  const [renaming, setRenaming] = useState<string | null>(null);
  return (
    <>
      {speakers.map((speaker) => (
        <div key={speaker.id} class="person" data-speaker-id={speaker.id}>
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
              <span class="person-name">{speaker.name}</span>
              <span class="person-share" title="Share of talk time">
                {talkShare(speaker, speakers)}
              </span>
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
              {speakers.length > 1 ? (
                <ActionMenu
                  label={`Merge ${speaker.name} into someone else`}
                  triggerClass="icon-btn person-rename person-merge"
                  actions={[
                    {
                      label: `Merge ${speaker.name} into`,
                      run: () => undefined,
                      children: speakers
                        .filter((s) => s.id !== speaker.id)
                        .map((into) => ({
                          label: into.name,
                          run: () => {
                            onMerge(speaker, into);
                          },
                        })),
                    },
                  ]}
                >
                  <MergeIcon size={13} />
                </ActionMenu>
              ) : null}
            </>
          )}
        </div>
      ))}
    </>
  );
}

export function OutlinePane({
  project,
  positionMs,
  onSeek,
  onAddChapter,
  onAddTopic,
  onRemoveTopic,
  onRenamePerson,
  speakers,
  onRenameSpeaker,
  onMergeSpeakers,
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
              <ChapterRow chapter={chapter} active={i === active} onSeek={onSeek} />
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
            project.highlights.map((h) => <HighlightRow key={h.id} highlight={h} onSeek={onSeek} />)
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
            <SpeakerList speakers={speakers} onRename={onRenameSpeaker} onMerge={onMergeSpeakers} />
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

function HistoryList({ history, now, onRetry }: { history: HistoryEntry[]; now: Date; onRetry: (stage: StageName) => void }): JSX.Element {
  if (history.length === 0) {
    return <p class="outline-empty">Nothing has happened to this recording yet.</p>;
  }
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
        return (
          <li key={`${entry.at}-${i}`} class="history-item">
            <span class={`history-dot history-dot--${tone}`} aria-hidden="true" />
            <span class="history-text">
              <span class="history-title">
                {entry.summary}
                {tone === 'failed' ? <span class="sr"> (failed)</span> : null}
              </span>
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
            {history?.keepVersions === true && currentVersion !== null ? (
              <VersionsCard versions={versions} current={currentVersion} keepDays={history.keepDays} now={now} onRestore={onRestore} />
            ) : null}
            <button class="btn ghost edit-details" type="button" aria-haspopup="dialog" onClick={onEditDetails}>
              Edit details
            </button>
          </>
        ) : tab === 'documents' ? (
          <div class="docs">
            <div class="docs-empty">
              <span class="docs-empty-tile" aria-hidden="true">
                <DocumentIcon size={18} />
              </span>
              <span class="docs-empty-title">No documents yet</span>
              <span class="docs-empty-text">
                Minutes, summaries and notes are built from the transcript. Creating documents arrives in a later version.
              </span>
            </div>
            <button class="btn primary docs-create" type="button" onClick={onCreateDocument}>
              Create document
            </button>
            <p class="docs-note">Documents are saved inside this recording and can be exported on their own.</p>
          </div>
        ) : (
          <HistoryList history={project.history} now={now} onRetry={onRetry} />
        )}
      </div>
    </aside>
  );
}
