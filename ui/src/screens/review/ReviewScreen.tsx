// Review and transcript (DESIGN.md §9), transcribed from renders/Review.dc.html: outline, player and
// transcript, details. Transcription arrives in M2, so the transcript area says so plainly and the
// audio, chapters, highlights, topics, people and details all work now.
import type { JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import type { Project, RecordingDetails } from '../../bridge/types';
import { DetailsSheet } from '../../components/DetailsSheet';
import { DocumentPlusIcon, MoreIcon, TranscriptLinesIcon } from '../../components/icons';
import { ActionMenu } from '../../components/Menus';
import { SpokeHeader } from '../../components/SpokeHeader';
import { formatDuration } from '../../format/duration';
import { activeChapterIndex } from '../../format/player';
import { peopleWording, typeName } from '../../format/recording';
import { calendarDaysBetween, formatClock, formatWhen, parseIso } from '../../format/when';
import { goToLibrary, openRecord, requestDelete } from '../../state/actions';
import { useServices } from '../../state/context';
import { createDetailsSaver, type DetailsSaver } from '../../state/detailsSaver';
import { PlayerStrip, usePeaks, usePlayer } from './Player';
import { DetailsPane, OutlinePane, type DetailsTab } from './ReviewPanes';

function messageOf(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

/** "Meeting · Yesterday, 4:00 PM · 1:10:02 · 4 people · audio + video". */
export function reviewMeta(project: Project, now: Date): string {
  const { summary } = project;
  const date = parseIso(summary.createdAt);
  const days = calendarDaysBetween(date, now);
  const when = days <= 0 ? `Today, ${formatClock(date)}` : days === 1 ? `Yesterday, ${formatClock(date)}` : formatWhen(date, now);
  return [
    typeName(project.details.type),
    when,
    formatDuration(summary.durationMs),
    peopleWording(project.details.participants.length),
    summary.hasVideo ? 'audio + video' : 'audio only',
  ].join(' · ');
}

const EXPORT_NOTICE = {
  kind: 'notice' as const,
  title: 'Export arrives in a later version',
  body: 'Copies of the audio, the separate tracks and the details will be written to a folder you choose. Until then the recording stays complete and safe inside Memento on this PC.',
};

const DOCUMENT_NOTICE = {
  kind: 'notice' as const,
  title: 'Documents arrive in a later version',
  body: 'Minutes, summaries and other documents are built from the transcript, and transcription arrives in a later version. The recording is complete and ready for it.',
};

export function ReviewScreen({ recordingId }: { recordingId: string }): JSX.Element {
  const services = useServices();
  const { bridge, store } = services;
  const known = store.library.value?.recordings.find((r) => r.id === recordingId) ?? null;
  const [project, setProject] = useState<Project | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState<DetailsTab>('details');
  const [sheetOpen, setSheetOpen] = useState(false);
  const [reload, setReload] = useState(0);

  // Read the project, and again whenever the host says it changed (finalize, rename, details).
  useEffect(() => {
    let live = true;
    bridge
      .call('project.get', { recordingId })
      .then((p) => {
        if (live) {
          setProject(p);
          setError(null);
        }
      })
      .catch((e: unknown) => {
        if (live) {
          setError(messageOf(e, 'The recording could not be opened.'));
        }
      });
    return () => {
      live = false;
    };
  }, [bridge, recordingId, reload]);

  useEffect(
    () =>
      bridge.on('library.changed', (payload) => {
        if (payload.recordingIds.includes(recordingId)) {
          setReload((n) => n + 1);
        }
      }),
    [bridge, recordingId],
  );

  const player = usePlayer(project?.mixUrl ?? null, project?.summary.durationMs ?? known?.durationMs ?? 0);
  const peaks = usePeaks(project?.peaksUrl ?? null);
  // One saver for the sheet, created with the first project read; Edit details re-adopts the latest.
  const saverRef = useRef<DetailsSaver | null>(null);
  if (saverRef.current === null && project !== null) {
    saverRef.current = createDetailsSaver(bridge, project.details);
  }
  const saver = saverRef.current;
  useEffect(
    () => () => {
      saverRef.current?.dispose();
    },
    [],
  );

  const fail = (title: string) => (e: unknown): void => {
    store.toasts.show({ tone: 'warning', title, body: `${messageOf(e, 'Memento did not answer.')} Nothing else changed.` });
  };

  const patch = (changes: Partial<Project>): void => {
    setProject((p) => (p === null ? p : { ...p, ...changes }));
  };

  const updateDetails = (details: Partial<RecordingDetails>, failure: string): void => {
    bridge
      .call('project.updateDetails', { recordingId, details })
      .then((p) => {
        setProject(p);
      })
      .catch(fail(failure));
  };

  const addHighlight = (): void => {
    bridge
      .call('annotations.addHighlight', { recordingId, highlight: { atMs: Math.round(player.positionMs), note: '' } })
      .then(({ highlights }) => {
        patch({ highlights });
      })
      .catch(fail('The highlight was not added'));
  };

  const title = project?.summary.title ?? known?.title ?? 'Recording';
  const now = services.now();
  const meta = project === null ? undefined : reviewMeta(project, now);
  const inProgress = project !== null && (project.summary.state === 'recording' || project.summary.state === 'finalizing');
  const chapterIndex = project === null ? -1 : activeChapterIndex(project.chapters, player.positionMs);
  const chapterName = project?.chapters[chapterIndex]?.title ?? 'Transcript';

  return (
    <>
      <SpokeHeader
        backLabel="Library"
        onBack={() => {
          goToLibrary(services);
        }}
        title={title}
        {...(meta === undefined ? {} : { meta })}
        actions={
          <>
            <button
              class="btn ghost spoke-ghost"
              type="button"
              onClick={() => {
                store.dialog.value = EXPORT_NOTICE;
              }}
            >
              Export
            </button>
            <button
              class="btn primary spoke-primary"
              type="button"
              onClick={() => {
                store.dialog.value = DOCUMENT_NOTICE;
              }}
            >
              <DocumentPlusIcon size={16} />
              Create document
            </button>
            <ActionMenu
              label="More actions"
              triggerClass="icon-btn spoke-more"
              actions={[
                {
                  label: 'Rename',
                  run: () => {
                    store.dialog.value = { kind: 'rename', recordingId, title };
                  },
                },
                {
                  label: 'Change type',
                  run: () => {
                    store.dialog.value = { kind: 'changeType', recordingId, title, type: project?.details.type ?? known?.type ?? 'meeting' };
                  },
                },
                { label: 'Reprocess', run: () => undefined, disabled: true, note: 'Available in a later version' },
                {
                  label: 'Delete',
                  run: () => {
                    void requestDelete(services, recordingId);
                  },
                },
              ]}
            >
              <MoreIcon size={18} />
            </ActionMenu>
          </>
        }
      />
      {/* The real player: the host's mix, streamed from https://library.memento/. */}
      <audio ref={player.audioRef} src={project?.mixUrl ?? undefined} preload="metadata" />
      {error !== null ? (
        <main class="spoke-main">
          <div class="placeholder-card">
            <p class="placeholder-text" role="alert">
              {error}
            </p>
          </div>
        </main>
      ) : project === null ? (
        <main class="spoke-main" aria-busy="true">
          <div class="placeholder-card">
            <p class="placeholder-text">Opening the recording…</p>
          </div>
        </main>
      ) : (
        <div class="review-panes">
          <OutlinePane
            project={project}
            positionMs={player.positionMs}
            onSeek={player.seek}
            onAddChapter={(atMs, chapterTitle) => {
              bridge
                .call('annotations.addChapter', { recordingId, chapter: { atMs, title: chapterTitle } })
                .then(({ chapters }) => {
                  patch({ chapters });
                })
                .catch(fail('The chapter was not added'));
            }}
            onAddTopic={(label) => {
              bridge
                .call('annotations.addTopic', { recordingId, topic: { label } })
                .then(({ topics }) => {
                  patch({ topics });
                })
                .catch(fail('The topic was not added'));
            }}
            onRemoveTopic={(topic) => {
              bridge
                .call('annotations.removeTopic', { recordingId, topicId: topic.id })
                .then(({ topics }) => {
                  patch({ topics });
                })
                .catch(fail('The topic was not removed'));
            }}
            onRenamePerson={(index, name) => {
              const participants = project.details.participants.map((p, i) => (i === index ? name : p));
              updateDetails({ participants }, 'The name was not changed');
            }}
          />

          <section class="review-centre" aria-label="Player and transcript">
            <PlayerStrip player={player} peaks={peaks} hasMedia={project.mixUrl !== null} onHighlight={addHighlight} />
            <div class="transcript">
              <div class="transcript-head">
                <span class="lbl">{chapterName}</span>
              </div>
              {inProgress ? (
                <div class="tx-empty">
                  <span class="tx-empty-tile" aria-hidden="true">
                    <TranscriptLinesIcon size={20} />
                  </span>
                  <h2 class="tx-empty-title">This recording is still in progress</h2>
                  <p class="tx-empty-text">It opens here once it has stopped and its tracks are stored on this PC.</p>
                  <button
                    class="btn ghost spoke-ghost"
                    type="button"
                    onClick={() => {
                      openRecord(services);
                    }}
                  >
                    Back to the recording
                  </button>
                </div>
              ) : (
                <div class="tx-empty">
                  <span class="tx-empty-tile" aria-hidden="true">
                    <TranscriptLinesIcon size={20} />
                  </span>
                  <h2 class="tx-empty-title">Not transcribed yet</h2>
                  <p class="tx-empty-text">
                    Transcription arrives in a later version of Memento. The audio is complete:{' '}
                    {project.tracks.length} {project.tracks.length === 1 ? 'track' : 'tracks'}, {formatDuration(project.summary.durationMs)}, stored on
                    this PC. You can listen, add chapters and highlights now; the transcript will line up with them later.
                  </p>
                </div>
              )}
            </div>
          </section>

          <DetailsPane
            project={project}
            tab={tab}
            onTab={setTab}
            now={now}
            onEditDetails={() => {
              saver?.adopt(recordingId, project.details);
              setSheetOpen(true);
            }}
            onTags={(tags) => {
              updateDetails({ tags }, 'The tags were not saved');
            }}
            onCreateDocument={() => {
              store.dialog.value = DOCUMENT_NOTICE;
            }}
          />
        </div>
      )}
      {sheetOpen && saver !== null ? (
        <DetailsSheet
          saver={saver}
          onClose={() => {
            setSheetOpen(false);
            setReload((n) => n + 1);
          }}
        />
      ) : null}
    </>
  );
}
