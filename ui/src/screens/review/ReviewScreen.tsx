// Review and transcript (DESIGN.md §9), transcribed from renders/Review.dc.html: outline, player and
// transcript, details. The transcript comes from transcript.get and follows the playhead; speakers,
// topics, history and transcript versions come with it (M2).
import type { JSX } from 'preact';
import { useEffect, useMemo, useRef, useState } from 'preact/hooks';
import type { Project, RecordingDetails, TranscriptSearchMatch, TranscriptVersion } from '../../bridge/types';
import { DetailsSheet } from '../../components/DetailsSheet';
import type { AgendaMode } from '../../components/agenda/useAgendaImport';
import { StatusFooter } from '../../components/StatusFooter';
import { CheckIcon, DocumentPlusIcon, MoreIcon } from '../../components/icons';
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
import { TranscriptPane } from './TranscriptPane';
import { useTranscript, useTranscriptSearch } from './useTranscript';
import { rememberPlayhead } from '../docview/playhead';

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

/** `startAtMs` (M4): a document's timestamp chip opens Review at that moment. */
export function ReviewScreen({ recordingId, startAtMs }: { recordingId: string; startAtMs?: number }): JSX.Element {
  const services = useServices();
  const { bridge, store } = services;
  // M4: Create document opens the Document builder.
  const createDocument = (): void => {
    services.router.navigate({ name: 'builder', recordingId, templateId: null, documentId: null });
  };
  const known = store.library.value?.recordings.find((r) => r.id === recordingId) ?? null;
  const [project, setProject] = useState<Project | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState<DetailsTab>('details');
  const [sheetOpen, setSheetOpen] = useState(false);
  // M3: Replace in Review › Details opens the sheet at the agenda drop zone.
  const [sheetAgendaMode, setSheetAgendaMode] = useState<AgendaMode | null>(null);
  const [reload, setReload] = useState(0);
  const [versions, setVersions] = useState<TranscriptVersion[] | null>(null);
  const revealRef = useRef<((segmentId: string) => void) | null>(null);

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

  const transcriptApi = useTranscript(bridge, recordingId, project?.summary.stages ?? null);
  const transcript = transcriptApi.result?.transcript ?? null;
  const search = useTranscriptSearch(bridge, recordingId, transcript?.version ?? null);
  const historySettings = store.settings.value?.history ?? null;

  // Transcript versions for the Details tab, while version history is on.
  useEffect(() => {
    if (historySettings?.keepVersions !== true || transcript === null) {
      setVersions(null);
      return undefined;
    }
    let live = true;
    bridge
      .call('transcript.versions', { recordingId })
      .then(({ versions: list }) => {
        if (live) {
          setVersions(list);
        }
      })
      .catch((e: unknown) => {
        console.warn('[review] transcript.versions failed', e);
        if (live) {
          setVersions([]);
        }
      });
    return () => {
      live = false;
    };
  }, [bridge, recordingId, transcript?.version, historySettings?.keepVersions, transcript === null]);

  // Speaker renames and new passes change the history; the project read brings it.
  useEffect(
    () =>
      bridge.on('transcript.changed', (payload) => {
        if (payload.recordingId === recordingId) {
          setReload((n) => n + 1);
        }
      }),
    [bridge, recordingId],
  );

  const player = usePlayer(project?.mixUrl ?? null, project?.summary.durationMs ?? known?.durationMs ?? 0);
  const peaks = usePeaks(project?.peaksUrl ?? null);
  // M4: the viewer's Insert timestamp starts where the player is.
  useEffect(() => {
    rememberPlayhead(store, recordingId, player.positionMs);
  }, [store, recordingId, player.positionMs]);
  // M4: opened from a document's timestamp chip: the player goes there, then the transcript line shows.
  const startDone = useRef({ seek: false, reveal: false });
  useEffect(() => {
    if (startAtMs === undefined || project === null) {
      return;
    }
    if (!startDone.current.seek) {
      startDone.current.seek = true;
      player.seek(startAtMs);
    }
    const segment = transcript?.segments.find((seg) => seg.start * 1000 <= startAtMs && startAtMs < seg.end * 1000) ?? transcript?.segments.find((seg) => seg.start * 1000 >= startAtMs);
    if (!startDone.current.reveal && segment !== undefined) {
      startDone.current.reveal = true;
      revealRef.current?.(segment.id);
    }
  }, [startAtMs, project, transcript]);
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

  const warn = (title: string, message: string): void => {
    store.toasts.show({ tone: 'warning', title, body: `${message} Nothing else changed.` });
  };
  const fail = (title: string) => (e: unknown): void => {
    warn(title, messageOf(e, 'Memento did not answer.'));
  };
  const report = (title: string) => (message: string | null): void => {
    if (message !== null) {
      warn(title, message);
    }
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

  const jump = (match: TranscriptSearchMatch): void => {
    player.seek(match.start * 1000);
    revealRef.current?.(match.segmentId);
  };

  const title = project?.summary.title ?? known?.title ?? 'Recording';
  const now = services.now();
  const meta = project === null ? undefined : reviewMeta(project, now);
  const inProgress = project !== null && (project.summary.state === 'recording' || project.summary.state === 'finalizing');
  const chapterIndex = project === null ? -1 : activeChapterIndex(project.chapters, player.positionMs);
  const chapterName = project?.chapters[chapterIndex]?.title ?? 'Transcript';
  const speakersIdentified = transcript !== null && transcript.speakers.length > 0;
  const currentVersion = useMemo(
    () =>
      transcript === null
        ? null
        : { version: transcript.version, engine: `${transcript.engine.model} · ${transcript.engine.device}`, segments: transcript.segments.length },
    [transcript],
  );

  return (
    <>
      <SpokeHeader
        backLabel="Library"
        onBack={() => {
          goToLibrary(services);
        }}
        title={title}
        {...(meta === undefined ? {} : { meta })}
        metaExtra={
          transcript?.reviewed === true ? (
            <span class="pill done spoke-meta-pill">
              <CheckIcon size={11} />
              Reviewed
            </span>
          ) : null
        }
        actions={
          <>
            <button
              class="btn ghost spoke-ghost"
              type="button"
              onClick={() => {
                store.dialog.value = { kind: 'export', recordingId };
              }}
            >
              Export
            </button>
            <button
              class="btn primary spoke-primary"
              type="button"
              onClick={createDocument}
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
                {
                  label: 'Reprocess',
                  run: () => undefined,
                  children: [
                    {
                      label: 'Transcribe again…',
                      disabled: inProgress || project === null,
                      ...(inProgress ? { note: 'Once the recording is stored' } : {}),
                      run: () => {
                        store.dialog.value = { kind: 'retranscribe', recordingId, title, hasTranscript: transcript !== null };
                      },
                    },
                    {
                      label: 'Identify speakers again',
                      disabled: transcript === null,
                      ...(transcript === null ? { note: 'Needs a transcript first' } : {}),
                      run: () => {
                        void transcriptApi.retry('speakers').then(report('Speakers were not identified again'));
                      },
                    },
                  ],
                },
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
            speakers={speakersIdentified ? transcript.speakers : null}
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
            onRenameSpeaker={(speaker, name) => {
              void transcriptApi.renameSpeaker(speaker.id, name).then(report(`${speaker.name} was not renamed`));
            }}
            onMergeSpeakers={(from, into) => {
              void transcriptApi.mergeSpeakers(from.id, into.id).then(report(`${from.name} was not merged into ${into.name}`));
            }}
          />

          <section class="review-centre" aria-label="Player and transcript">
            <PlayerStrip
              player={player}
              peaks={peaks}
              hasMedia={project.mixUrl !== null}
              onHighlight={addHighlight}
              search={transcript !== null && transcript.segments.length > 0 ? search : null}
              onJump={jump}
            />
            <TranscriptPane
              project={project}
              api={transcriptApi}
              search={search}
              player={player}
              chapterName={chapterName}
              inProgress={inProgress}
              revealRef={revealRef}
              onBackToRecording={() => {
                openRecord(services);
              }}
              onShowHistory={() => {
                setTab('history');
                document.getElementById('review-tab-history')?.focus();
              }}
              onError={warn}
            />
          </section>

          <DetailsPane
            project={project}
            tab={tab}
            onTab={setTab}
            now={now}
            history={historySettings}
            versions={versions}
            currentVersion={currentVersion}
            onRestore={(version, when) => {
              store.dialog.value = { kind: 'restoreVersion', recordingId, version, when };
            }}
            onRetry={(stage) => {
              void transcriptApi.retry(stage).then(report('The stage was not retried'));
            }}
            onEditDetails={() => {
              saver?.adopt(recordingId, project.details);
              setSheetAgendaMode(null);
              setSheetOpen(true);
            }}
            onReplaceAgenda={() => {
              saver?.adopt(recordingId, project.details);
              setSheetAgendaMode('drop');
              setSheetOpen(true);
            }}
            onTags={(tags) => {
              updateDetails({ tags }, 'The tags were not saved');
            }}
            onCreateDocument={createDocument}
          />
        </div>
      )}
      {/* M3: Review has no footer (DESIGN.md §3) except while an export it started is running. */}
      {store.footer.value?.export?.active === true ? <StatusFooter status={store.footer.value} /> : null}
      {sheetOpen && saver !== null ? (
        <DetailsSheet
          saver={saver}
          agendaMode={sheetAgendaMode}
          onClose={() => {
            setSheetOpen(false);
            setReload((n) => n + 1);
          }}
        />
      ) : null}
    </>
  );
}
