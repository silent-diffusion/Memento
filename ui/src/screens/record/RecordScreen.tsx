// The Recording session (DESIGN.md §8), transcribed from renders/Record.dc.html. One job: make
// recording easy and obviously safe. The host owns the session; this screen starts it, mirrors its
// recording.state / levels events, and routes to Review once finalize reports `ready`.
import { effect } from '@preact/signals';
import type { JSX } from 'preact';
import { useCallback, useEffect, useMemo, useRef, useState } from 'preact/hooks';
import type { AgendaItem, AudioSource, Highlight, RecordingStatePayload, RecordingType } from '../../bridge/types';
import { DetailsSheet, typeOptions } from '../../components/DetailsSheet';
import { NotesIcon } from '../../components/icons';
import { SelectMenu } from '../../components/Menus';
import { SpokeHeader } from '../../components/SpokeHeader';
import { formatTimecode } from '../../format/duration';
import { createElapsedClock } from '../../format/elapsed';
import { sourceFormat, type TrackFormat } from '../../format/estimate';
import type { RecordPhase } from '../../format/recordFooter';
import { isBuiltInType, typeName } from '../../format/recording';
import { formatClock, parseIso } from '../../format/when';
import { goToLibrary, updateSettings } from '../../state/actions';
import { useServices } from '../../state/context';
import { createDetailsSaver, emptyDetails } from '../../state/detailsSaver';
import { recordShortcut } from '../../state/recordShortcuts';
import { AgendaCard, HighlightsCard, LiveTranscriptCard, RecordFooter, SourcesCard, StageCard, type SourceRow } from './RecordParts';
import { TracksCard } from './TracksCard';

/** "Untitled meeting"; custom types keep their own capitalisation. */
export function defaultTitle(type: RecordingType): string {
  return `Untitled ${isBuiltInType(type) ? typeName(type).toLocaleLowerCase() : type}`;
}

function messageOf(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

const ACTIVE_STATES = new Set<RecordingStatePayload['state']>(['recording', 'paused', 'finalizing']);

export function RecordScreen(): JSX.Element {
  const services = useServices();
  const { bridge, store, router } = services;
  const settings = store.settings.value;
  const initialType = settings?.recording.defaultType ?? 'meeting';

  const saver = useMemo(() => createDetailsSaver(bridge, emptyDetails(defaultTitle(initialType), initialType)), [bridge]);
  const clock = useMemo(() => createElapsedClock(), []);
  const details = saver.details.value;
  const titleTouched = useRef(false);

  // The session this screen shows: one it started, or an active one it rejoined.
  const [ownSessionId, setOwnSessionId] = useState<string | null>(null);
  const session = store.recording.value;
  const own = session !== null && session.sessionId === ownSessionId ? session : null;
  const [stopping, setStopping] = useState(false);
  const [starting, setStarting] = useState(false);
  const startingRef = useRef(false);
  const [stageError, setStageError] = useState<string | null>(null);
  const [sheetOpen, setSheetOpen] = useState(false);
  const [highlights, setHighlights] = useState<Highlight[]>([]);
  const [focusHighlight, setFocusHighlight] = useState<string | null>(null);
  const [selected, setSelected] = useState<string[] | null>(null);
  const [pendingSources, setPendingSources] = useState<ReadonlySet<string>>(new Set());
  const [sourcesError, setSourcesError] = useState<string | null>(null);
  const routed = useRef<string | null>(null);

  const phase: RecordPhase =
    own === null
      ? stopping
        ? 'finalizing'
        : 'ready'
      : own.state === 'ready'
        ? 'finalizing'
        : own.state;

  useEffect(() => () => {
    saver.dispose();
  }, [saver]);

  // Follow the host clock: every recording.state feeds the interpolating timer.
  useEffect(
    () =>
      effect(() => {
        const state = store.recording.value;
        if (state !== null) {
          clock.observe(state.sessionId, state.elapsedMs, state.state === 'recording', performance.now());
        }
      }),
    [store, clock],
  );

  // Rejoin: after a reload (or coming back from the Library) the host still has the session.
  useEffect(() => {
    let live = true;
    bridge
      .call('recording.current')
      .then(({ session: current }) => {
        if (live && current !== null) {
          store.recording.value = current;
        }
      })
      .catch((error: unknown) => {
        console.warn('[record] recording.current failed', error);
      });
    return () => {
      live = false;
    };
  }, [bridge, store]);

  useEffect(() => {
    if (session !== null && ACTIVE_STATES.has(session.state) && session.sessionId !== ownSessionId) {
      setOwnSessionId(session.sessionId);
      if (startingRef.current) {
        return;
      }
      // An existing session: take its details and highlights from the project.
      const recordingId = session.recordingId;
      bridge
        .call('project.get', { recordingId })
        .then((project) => {
          if (saver.recordingId.value === null) {
            saver.adopt(recordingId, project.details);
          }
          setHighlights(project.highlights);
        })
        .catch(() => {
          if (saver.recordingId.value === null) {
            saver.adopt(recordingId, { ...saver.details.value, title: saver.details.value.title });
          }
        });
    }
  }, [session, ownSessionId, bridge, saver]);

  // Start replaces its own button: keep keyboard users in place on Pause rather than at the top.
  const live = phase === 'recording' || phase === 'paused';
  useEffect(() => {
    if (live && (document.activeElement === document.body || document.activeElement === null)) {
      document.querySelector<HTMLElement>('.rec-round')?.focus({ preventScroll: true });
    }
  }, [live]);

  // Finalize finished: open Review for this recording.
  useEffect(() => {
    if (own?.state === 'ready' && routed.current !== own.recordingId) {
      routed.current = own.recordingId;
      const recordingId = own.recordingId;
      void saver.flush().finally(() => {
        router.navigate({ name: 'review', recordingId });
      });
    }
  }, [own, router, saver]);

  // Sources: enumerate on open; Add enumerates again.
  const loadSources = useCallback(() => {
    setSourcesError(null);
    bridge
      .call('sources.list')
      .then((result) => {
        store.sources.value = result.audio;
      })
      .catch((error: unknown) => {
        setSourcesError(`${messageOf(error, 'Memento did not answer.')} Press Add to look again.`);
      });
  }, [bridge, store]);
  useEffect(loadSources, [loadSources]);

  const sources = store.sources.value;
  // The remembered selection, else the defaults the host marks.
  useEffect(() => {
    if (selected !== null || sources === null || settings === null) {
      return;
    }
    const remembered = settings.recording.defaultSourceIds.filter((id) => sources.some((s) => s.id === id));
    setSelected(remembered.length > 0 ? remembered : sources.filter((s) => s.isDefault).map((s) => s.id));
  }, [sources, settings, selected]);

  // A changed default type renames a title nobody has touched.
  useEffect(() => {
    if (!titleTouched.current && own === null && settings !== null && details.title === defaultTitle(details.type) && details.type !== settings.recording.defaultType) {
      const type = settings.recording.defaultType;
      saver.update({ type, title: defaultTitle(type) });
    }
  }, [settings, own, details, saver]);

  const liveTracks = own?.tracks.filter((t) => t.endedEarlyAtMs === null) ?? [];
  const liveSourceIds = new Set(liveTracks.map((t) => t.sourceId));
  const levelBySource = new Map((store.levels.value?.sessionId === own?.sessionId ? store.levels.value?.levels : undefined)?.map((l) => [l.sourceId, l.rms]) ?? []);
  const lost = store.lostSource.value;

  const rows: SourceRow[] | null =
    sources === null
      ? null
      : sources.map((source) => {
          const on = live ? liveSourceIds.has(source.id) : (selected ?? []).includes(source.id);
          return {
            source,
            on,
            busy: pendingSources.has(source.id),
            rms: live ? (levelBySource.get(source.id) ?? 0) : null,
            lostAt: live && lost?.sourceId === source.id && lost.sessionId === own?.sessionId ? lost.atMs : null,
          };
        });

  const toggleSource = (source: AudioSource, on: boolean): void => {
    if (!live || own === null) {
      setSelected((current) => {
        const set = new Set(current ?? []);
        if (on) {
          set.add(source.id);
        } else {
          set.delete(source.id);
        }
        return (sources ?? []).filter((s) => set.has(s.id)).map((s) => s.id);
      });
      return;
    }
    setPendingSources((p) => new Set([...p, source.id]));
    bridge
      .call('recording.setSource', { sessionId: own.sessionId, sourceId: source.id, enabled: on })
      .then((result) => {
        const current = store.recording.value;
        if (current?.sessionId === own.sessionId) {
          store.recording.value = { ...current, tracks: result.tracks };
        }
        if (on && store.lostSource.value?.sourceId === source.id) {
          store.lostSource.value = null;
        }
      })
      .catch((error: unknown) => {
        store.toasts.show({
          tone: 'warning',
          title: on ? `${source.name} could not be turned on` : `${source.name} could not be turned off`,
          body: `${messageOf(error, 'Memento did not answer.')} The other tracks keep recording.`,
        });
      })
      .finally(() => {
        setPendingSources((p) => {
          const next = new Set(p);
          next.delete(source.id);
          return next;
        });
      });
  };

  const start = async (): Promise<void> => {
    const sourceIds = selected ?? [];
    if (sourceIds.length === 0 || starting) {
      return;
    }
    setStageError(null);
    setStarting(true);
    startingRef.current = true;
    const title = details.title.trim() === '' ? defaultTitle(details.type) : details.title.trim();
    try {
      const result = await bridge.call('recording.start', { title, type: details.type, sourceIds });
      setOwnSessionId(result.sessionId);
      saver.update({ title });
      router.navigate({ name: 'record', sessionId: result.sessionId });
      void saver.attach(result.recordingId);
      setHighlights([]);
      // Remember the selection for next time (Settings › Recording shows it too).
      if (settings !== null && settings.recording.defaultSourceIds.join('|') !== sourceIds.join('|')) {
        void updateSettings(services, { recording: { ...settings.recording, defaultSourceIds: sourceIds } });
      }
    } catch (error) {
      setStageError(messageOf(error, 'The recording could not start. Nothing was recorded.'));
    } finally {
      setStarting(false);
      startingRef.current = false;
    }
  };

  const togglePause = (): void => {
    if (own === null || !live) {
      return;
    }
    const method = own.state === 'paused' ? 'recording.resume' : 'recording.pause';
    bridge.call(method, { sessionId: own.sessionId }).catch((error: unknown) => {
      setStageError(messageOf(error, 'The recording did not answer. It is still running.'));
    });
  };

  const stop = (): void => {
    if (own === null || stopping) {
      return;
    }
    setStopping(true);
    setStageError(null);
    bridge.call('recording.stop', { sessionId: own.sessionId }).catch((error: unknown) => {
      setStopping(false);
      setStageError(messageOf(error, 'The recording could not be stopped. It is still recording.'));
    });
  };

  const mark = (): void => {
    if (own === null || !live) {
      return;
    }
    bridge
      .call('recording.markHighlight', { sessionId: own.sessionId })
      .then(({ highlight }) => {
        setHighlights((list) => [...list.filter((h) => h.id !== highlight.id), highlight].sort((a, b) => a.atMs - b.atMs));
        setFocusHighlight(highlight.id);
      })
      .catch((error: unknown) => {
        store.toasts.show({ tone: 'warning', title: 'The highlight was not marked', body: messageOf(error, 'Memento did not answer.') });
      });
  };

  const saveNote = (highlight: Highlight, note: string): void => {
    const recordingId = own?.recordingId ?? saver.recordingId.value;
    if (recordingId === null) {
      return;
    }
    setHighlights((list) => list.map((h) => (h.id === highlight.id ? { ...h, note } : h)));
    bridge.call('annotations.updateHighlight', { recordingId, highlight: { id: highlight.id, note } }).catch((error: unknown) => {
      store.toasts.show({
        tone: 'warning',
        title: `The note at ${formatTimecode(highlight.atMs)} was not saved`,
        body: `${messageOf(error, 'Memento did not answer.')} The highlight itself is kept.`,
      });
    });
  };

  const toggleAgenda = (item: AgendaItem): void => {
    const agenda = saver.details.value.agenda;
    saver.update({ agenda: { ...agenda, items: agenda.items.map((i) => (i.id === item.id ? { ...i, covered: !i.covered } : i)) } });
  };

  // Space pauses or resumes, Ctrl+M marks a highlight, Esc does nothing (DESIGN.md §8).
  const keys = useRef({ togglePause, mark, live });
  keys.current = { togglePause, mark, live };
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      if (store.overlays.value > 0 || event.defaultPrevented) {
        return;
      }
      const action = recordShortcut(event);
      if (action === null) {
        return;
      }
      event.preventDefault();
      if (!keys.current.live) {
        return;
      }
      if (action === 'togglePause') {
        keys.current.togglePause();
      } else if (action === 'mark') {
        keys.current.mark();
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [store]);

  const selectedCount = (selected ?? []).length;
  const subline =
    phase === 'ready'
      ? `${selectedCount === 0 ? 'No' : selectedCount} audio ${selectedCount === 1 ? 'source' : 'sources'} selected · audio only`
      : own === null
        ? 'Writing the tracks to this PC'
        : phase === 'finalizing'
          ? `Writing ${own.tracks.length} ${own.tracks.length === 1 ? 'track' : 'tracks'} to this PC. Review opens when they are ready.`
          : phase === 'stopped'
            ? `Everything up to ${formatTimecode(own.elapsedMs)} is saved on this PC.`
            : `Started ${formatClock(parseIso(own.startedAt))} · ${liveTracks.length} audio ${liveTracks.length === 1 ? 'track' : 'tracks'} · audio only`;

  const formats: TrackFormat[] = live
    ? liveTracks.map((t) => ({ sampleRate: t.sampleRate, channels: t.channels }))
    : (sources ?? []).filter((s) => (selected ?? []).includes(s.id)).map(sourceFormat);

  const titleEditor = (
    <>
      <label class="sr" for="rec-title">
        Recording title
      </label>
      <input
        id="rec-title"
        class="field rec-title-input"
        type="text"
        value={details.title}
        autocomplete="off"
        onInput={(event) => {
          titleTouched.current = true;
          saver.update({ title: event.currentTarget.value });
        }}
        onBlur={(event) => {
          if (event.currentTarget.value.trim() === '') {
            saver.update({ title: defaultTitle(saver.details.value.type) });
          }
        }}
        onKeyDown={(event) => {
          if (event.key === 'Enter') {
            event.currentTarget.blur();
          }
        }}
      />
      <SelectMenu
        label="Recording type"
        variant="chip"
        value={details.type}
        options={typeOptions(details.type)}
        onChange={(type) => {
          const patch: { type: RecordingType; title?: string } = { type };
          if (details.title === defaultTitle(details.type)) {
            patch.title = defaultTitle(type);
          }
          saver.update(patch);
        }}
      />
    </>
  );

  return (
    <>
      <SpokeHeader
        backLabel="Library"
        onBack={() => {
          goToLibrary(services);
        }}
        titleEditor={titleEditor}
        focusBack={false}
        actions={
          <button
            class="btn ghost spoke-ghost"
            type="button"
            aria-haspopup="dialog"
            aria-pressed={sheetOpen}
            onClick={() => {
              setSheetOpen(true);
            }}
          >
            <NotesIcon size={16} />
            Details and agenda
          </button>
        }
      />
      <main class="rec-main">
        <div class="rec-columns">
          <SourcesCard rows={rows} error={sourcesError} onToggle={toggleSource} onRescan={loadSources} />

          <section class="rec-centre" aria-label="Recording">
            <StageCard
              phase={phase}
              clock={clock}
              subline={subline}
              canStart={selectedCount > 0}
              busy={starting || stopping}
              error={stageError}
              onStart={() => {
                void start();
              }}
              onTogglePause={togglePause}
              onStop={stop}
              onMark={mark}
              onOpenRecording={() => {
                if (own !== null) {
                  router.navigate({ name: 'review', recordingId: own.recordingId });
                }
              }}
            />
            {own !== null && phase !== 'ready' ? (
              <TracksCard sessionId={own.sessionId} tracks={own.tracks} levels={store.levels} clock={clock} running={phase === 'recording'} />
            ) : null}
            {own !== null && highlights.length > 0 ? (
              <HighlightsCard
                highlights={highlights}
                focusId={focusHighlight}
                onFocused={() => {
                  setFocusHighlight(null);
                }}
                onNote={saveNote}
              />
            ) : null}
          </section>

          <section class="rec-side" aria-label="Live transcript and agenda">
            <LiveTranscriptCard engine={store.footer.value?.engine ?? null} />
            <AgendaCard
              details={details}
              onToggle={toggleAgenda}
              onOpenDetails={() => {
                setSheetOpen(true);
              }}
            />
          </section>
        </div>
      </main>
      <RecordFooter
        phase={phase}
        status={store.footer.value}
        lastCheckpointAt={own?.lastCheckpointAt ?? store.footer.value?.recording.lastCheckpointAt ?? null}
        lostAtMs={lost?.sessionId === own?.sessionId ? (lost?.atMs ?? null) : null}
        formats={formats}
        now={services.now}
      />
      {sheetOpen ? (
        <DetailsSheet
          saver={saver}
          onClose={() => {
            setSheetOpen(false);
          }}
        />
      ) : null}
    </>
  );
}
