// The Recording session (DESIGN.md §8), transcribed from renders/Record.dc.html. One job: make
// recording easy and obviously safe. The host owns the session; this screen starts it, mirrors its
// recording.state / levels events, and routes to Review once finalize reports `ready`.
// 2.0 video (§18.1, §18.2, §18.6; renders/RecordVideo.dc.html): the VIDEO section, the picker, the
// live preview, the video lane, the Encoder card and the video variants of the footer and banners.
import { effect } from '@preact/signals';
import type { JSX } from 'preact';
import { useCallback, useEffect, useMemo, useRef, useState } from 'preact/hooks';
import type { AgendaItem, AudioSource, Highlight, PipState, RecordingStatePayload, RecordingType, VideoListSourcesResult, VideoRole, VideoSource } from '../../bridge/types';
import { BannerSlot } from '../../components/Banners';
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
import { goToLibrary } from '../../state/actions';
import { useServices } from '../../state/context';
import { createDetailsSaver, emptyDetails } from '../../state/detailsSaver';
import { recordShortcut } from '../../state/recordShortcuts';
import { endVideoTracks } from '../../state/videoEvents';
import { recordingBytesPerSecond } from '../../format/estimate';
import { videoSettingsOf } from '../../format/video';
import { previewNoticeText, rowNoteText, videoFooterLine, videoProblemCard, type VideoProblemCard } from '../../format/videoProblems';
import { AgendaCard, HighlightsCard, LiveTranscriptCard, RecordFooter, SourcesCard, StageCard, type SourceRow } from './RecordParts';
import { adoptCurrentSession } from '../../state/currentSession';
import { TracksCard } from './TracksCard';
import { CapturePicker, type PickerMode } from './CapturePicker';
import { EncoderCard, LivePreview } from './VideoCards';
import { VideoSources, type RowNote, type VideoCardAction, type VideoRow } from './VideoSources';
import { PREVIEW_EVERY_MS, ROW_EVERY_MS, useThumbnail } from './useThumbnail';
import { useQuietSources } from './useQuietSources';
import {
  findSource,
  initialSelection,
  isLive,
  NO_SOURCES,
  previewCaption,
  rateNote,
  selectedVideoIds,
  selectedRoles,
  selectionFromStatus,
  sublineSources,
  displayLine,
  videoBytesPerSecond,
  type VideoSelection,
} from './videoSelection';
import './video.css';

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
  // 2.0 video
  const [videoAvailable, setVideoAvailable] = useState(true);
  const [videoSources, setVideoSources] = useState<VideoListSourcesResult | null>(null);
  const [videoError, setVideoError] = useState<string | null>(null);
  const [selection, setSelection] = useState<VideoSelection | null>(null);
  const [picker, setPicker] = useState<PickerMode | null>(null);
  const [videoBusy, setVideoBusy] = useState<ReadonlySet<VideoRow | 'pip'>>(new Set());
  const [cameraRetry, setCameraRetry] = useState(0);
  const videoSettings = videoSettingsOf(settings?.recording);

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
    const before = store.recording.value;
    bridge
      .call('recording.current')
      .then(({ session: current }) => {
        if (live && current !== null) {
          adoptCurrentSession(store.recording, before, current);
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

  // Sources: enumerate on open; Add enumerates again (2.0: displays, windows and cameras too).
  const loadVideoSources = useCallback(() => {
    bridge
      .call('video.listSources')
      .then((result) => {
        setVideoError(null);
        setVideoSources(result);
      })
      .catch((error: unknown) => {
        setVideoError(`${messageOf(error, 'Memento did not answer.')} Press Add to look again.`);
      });
  }, [bridge]);
  const loadSources = useCallback(() => {
    setSourcesError(null);
    bridge
      .call('sources.list')
      .then((result) => {
        store.sources.value = result.audio;
        setVideoAvailable(result.videoAvailable);
        if (result.videoAvailable) {
          loadVideoSources();
        }
      })
      .catch((error: unknown) => {
        setSourcesError(`${messageOf(error, 'Memento did not answer.')} Press Add to look again.`);
      });
  }, [bridge, store, loadVideoSources]);
  useEffect(loadSources, [loadSources]);
  // A display, window or camera came or went.
  useEffect(() => bridge.on('video.sourcesChanged', loadVideoSources), [bridge, loadVideoSources]);

  // The video choice starts from Settings › Recording › Video defaults.
  useEffect(() => {
    if (selection === null && videoSources !== null && settings !== null) {
      setSelection(initialSelection(videoSettingsOf(settings.recording), videoSources));
    }
  }, [selection, videoSources, settings]);

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
  const quiet = useQuietSources(store.levels, live ? (own?.sessionId ?? null) : null);
  const lost = store.lostSource.value;

  // 2.0 video: while recording the rows follow the session's tracks; before, the page's own choice.
  const videoStatus = own !== null && store.videoStatus.value?.sessionId === own.sessionId ? store.videoStatus.value : null;
  const videoTracks = videoStatus?.videoTracks ?? [];
  const videoProblems = own === null ? [] : store.videoProblems.value.filter((p) => p.sessionId === own.sessionId);
  const effective: VideoSelection | null =
    !live || selection === null
      ? selection
      : videoStatus === null
        ? { ...selection, screen: false, window: false, camera: false }
        : selectionFromStatus(videoTracks, videoStatus.pip, selection);
  const list = videoSources ?? NO_SOURCES;
  const screenTrack = videoTracks.find((t) => t.trackId === 'screen');
  const cameraTrack = videoTracks.find((t) => t.trackId === 'camera');
  const screenSourceId = effective === null ? null : effective.window ? effective.windowId : effective.screen ? effective.displayId : null;
  const cameraSourceId = effective?.camera === true ? effective.cameraId : null;
  const capturing = (track: typeof screenTrack): boolean => !live || track === undefined || track.state === 'recording' || track.state === 'paused';
  const showVideo = videoAvailable && (screenSourceId !== null || cameraSourceId !== null);
  const screenThumb = useThumbnail(bridge, screenSourceId !== null && capturing(screenTrack) ? screenSourceId : null, 560, PREVIEW_EVERY_MS);
  const cameraIsMain = screenSourceId === null;
  const cameraThumb = useThumbnail(
    bridge,
    cameraSourceId !== null && capturing(cameraTrack) ? cameraSourceId : null,
    cameraIsMain ? 560 : 144,
    cameraIsMain ? PREVIEW_EVERY_MS : ROW_EVERY_MS,
    cameraRetry,
  );

  const videoCards: VideoProblemCard[] = [];
  if (!live) {
    const camera = findSource(list, cameraSourceId);
    if (screenSourceId !== null && screenThumb.errorCode === 'video.captureBlocked') {
      const card = videoProblemCard(screenThumb.errorCode, 'screen', '', null, false);
      if (card !== null) {
        videoCards.push(card);
      }
    }
    if (camera !== null && (cameraThumb.errorCode === 'video.cameraInUse' || cameraThumb.errorCode === 'video.cameraPrivacy')) {
      const card = videoProblemCard(cameraThumb.errorCode, 'camera', camera.name, cameraThumb.errorDetail, false);
      if (card !== null) {
        videoCards.push(card);
      }
    }
  } else {
    for (const problem of videoProblems) {
      const card = videoProblemCard(problem.code, problem.trackId, problem.sourceName, problem.detail, true);
      if (card !== null && !videoCards.some((c) => c.kind === card.kind)) {
        videoCards.push(card);
      }
    }
  }
  const rowNotes: Partial<Record<VideoRow, RowNote>> = {};
  for (const track of live ? videoTracks : []) {
    if (track.state === 'recording' || track.state === 'paused') {
      continue;
    }
    const problem = [...videoProblems].reverse().find((p) => p.trackId === track.trackId);
    const row: VideoRow = track.trackId === 'camera' ? 'camera' : track.sourceId.startsWith('window:') ? 'window' : 'screen';
    rowNotes[row] = problem === undefined ? { text: track.reason ?? 'Stopped', tone: 'danger' } : rowNoteText(problem);
  }
  const screenDisplay = findSource(list, effective?.displayId ?? null);
  const screenLine = screenDisplay === null ? null : displayLine(screenDisplay, videoSettings, effective?.screen === true && isLive(screenTrack) ? screenTrack : undefined);

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
            quietDb: live && on ? (quiet.get(source.id) ?? null) : null,
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

  // --- video actions (§18.1, §18.2, §18.6) -----------------------------------------------------

  const markBusy = (key: VideoRow | 'pip', on: boolean): void => {
    setVideoBusy((before) => {
      const next = new Set(before);
      if (on) {
        next.add(key);
      } else {
        next.delete(key);
      }
      return next;
    });
  };

  const videoFailed = (title: string) => (error: unknown) => {
    store.toasts.show({ tone: 'warning', title, body: `${messageOf(error, 'Memento did not answer.')} Audio keeps recording.` });
  };

  /** Starts, switches or ends a role's track while recording; the tracks come back in the answer. */
  const setVideoSource = (role: VideoRole, sourceId: string | null, key: VideoRow, failTitle: string): void => {
    if (own === null) {
      return;
    }
    markBusy(key, true);
    bridge
      .call('recording.setVideoSource', { sessionId: own.sessionId, role, sourceId })
      .then(({ videoTracks: next }) => {
        const current = store.videoStatus.value;
        store.videoStatus.value = current?.sessionId === own.sessionId ? { ...current, videoTracks: next } : { sessionId: own.sessionId, videoTracks: next, pip: selection?.pip ?? videoStatusPip() };
      })
      .catch(videoFailed(failTitle))
      .finally(() => {
        markBusy(key, false);
      });
  };
  const videoStatusPip = (): PipState => ({ enabled: false, corner: videoSettings.video.pipCorner });

  const sourceName = (id: string | null): string => findSource(list, id)?.name ?? 'That source';

  const toggleVideo = (row: VideoRow, on: boolean): void => {
    if (!live || own === null) {
      setSelection((s) => {
        if (s === null) {
          return s;
        }
        if (row === 'screen') {
          return { ...s, screen: on, window: on ? false : s.window };
        }
        if (row === 'window') {
          if (on && s.windowId === null) {
            setPicker('window');
            return s;
          }
          return { ...s, window: on, screen: on ? false : s.screen };
        }
        return { ...s, camera: on };
      });
      return;
    }
    if (row === 'camera') {
      setVideoSource('camera', on ? (effective?.cameraId ?? null) : null, 'camera', on ? `${sourceName(effective?.cameraId ?? null)} could not be turned on` : 'The camera could not be turned off');
      return;
    }
    const id = row === 'window' ? (effective?.windowId ?? null) : (effective?.displayId ?? null);
    if (on) {
      if (id === null) {
        setPicker(row === 'window' ? 'window' : 'screen');
        return;
      }
      setVideoSource('screen', id, row, `The screen track could not switch to ${sourceName(id)}`);
    } else if ((row === 'window' && effective?.window === true) || (row === 'screen' && effective?.screen === true)) {
      setVideoSource('screen', null, row, 'The screen track could not be turned off');
    }
  };

  const useCaptureSource = (source: VideoSource): void => {
    setPicker(null);
    const isWindow = source.kind === 'window';
    setSelection((s) => (s === null ? s : isWindow ? { ...s, windowId: source.id, window: true, screen: false } : { ...s, displayId: source.id, screen: true, window: false }));
    if (live && own !== null) {
      setVideoSource('screen', source.id, isWindow ? 'window' : 'screen', `The screen track could not switch to ${source.name}`);
    }
  };

  const chooseCamera = (cameraId: string): void => {
    setSelection((s) => (s === null ? s : { ...s, cameraId, camera: true }));
    if (live && own !== null) {
      setVideoSource('camera', cameraId, 'camera', `${sourceName(cameraId)} could not be turned on`);
    }
  };

  const changePip = (pip: PipState): void => {
    setSelection((s) => (s === null ? s : { ...s, pip }));
    if (!live || own === null) {
      return;
    }
    markBusy('pip', true);
    const before = store.videoStatus.value;
    if (before?.sessionId === own.sessionId) {
      store.videoStatus.value = { ...before, pip };
    }
    bridge
      .call('recording.setPip', { sessionId: own.sessionId, enabled: pip.enabled, corner: pip.corner })
      .catch((error: unknown) => {
        if (before?.sessionId === own.sessionId) {
          store.videoStatus.value = before;
        }
        videoFailed('The camera inset could not be changed')(error);
      })
      .finally(() => {
        markBusy('pip', false);
      });
  };

  const videoCardAction = (card: VideoProblemCard, action: VideoCardAction): void => {
    switch (action) {
      case 'retry':
        if (live) {
          setVideoSource('camera', effective?.cameraId ?? null, 'camera', `${sourceName(effective?.cameraId ?? null)} could not be turned on`);
        } else {
          setCameraRetry((n) => n + 1);
        }
        return;
      case 'withoutCamera':
        toggleVideo('camera', false);
        return;
      case 'pickCamera':
        document.querySelector<HTMLButtonElement>('.rec-camera-select button')?.click();
        return;
      case 'openSettings':
        bridge.call('video.openPrivacySettings', { which: card.kind === 'captureBlocked' ? 'screenCapture' : 'camera' }).catch(videoFailed('Windows settings did not open'));
        return;
      case 'audioOnly':
        if (live && own !== null) {
          endVideoTracks(bridge, store, own.sessionId).catch(videoFailed('Video could not be turned off'));
        } else {
          setSelection((s) => (s === null ? s : { ...s, screen: false, window: false, camera: false }));
        }
        return;
    }
  };

  const start = async (): Promise<void> => {
    const audioIds = selected ?? [];
    if (audioIds.length === 0 || starting) {
      return;
    }
    // A video source that cannot start never keeps the audio from starting (BRIDGE.md video section).
    const videoIds = videoAvailable ? selectedVideoIds(selection) : [];
    const sourceIds = [...audioIds, ...videoIds];
    setStageError(null);
    setStarting(true);
    startingRef.current = true;
    const title = details.title.trim() === '' ? defaultTitle(details.type) : details.title.trim();
    try {
      const result = await bridge.call('recording.start', { title, type: details.type, sourceIds });
      setOwnSessionId(result.sessionId);
      // Both tracks are always recorded; the inset is only where the preview and Review draw the camera.
      const roles = selectedRoles(videoIds.length > 0 ? selection : null);
      if (selection !== null && roles.includes('screen') && roles.includes('camera')) {
        void bridge.call('recording.setPip', { sessionId: result.sessionId, enabled: selection.pip.enabled, corner: selection.pip.corner }).catch(videoFailed('The camera inset could not be set'));
      }
      saver.update({ title });
      router.navigate({ name: 'record', sessionId: result.sessionId });
      void saver.attach(result.recordingId);
      setHighlights([]);
      // The host remembers the sources and type for next time; read them back so Settings › Recording shows them.
      // (Writing this screen's copy of the recording settings back would undo the remembered type and any change
      // the host made since the copy was read.)
      void bridge
        .call('settings.get')
        .then((current) => {
          store.settings.value = current;
        })
        .catch((error: unknown) => {
          console.warn('[record] settings.get failed', error);
        });
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
    const toggled = { ...agenda, items: agenda.items.map((i) => (i.id === item.id ? { ...i, covered: !i.covered } : i)) };
    const recordingId = saver.recordingId.value;
    if (recordingId === null) {
      saver.update({ agenda: toggled });
      return;
    }
    // M3: agenda.setCovered marks one item; the mark shows at once and the host's answer confirms it.
    saver.receive({ agenda: toggled });
    bridge
      .call('agenda.setCovered', { recordingId, itemId: item.id, covered: !item.covered })
      .then((result) => {
        saver.receive({ agenda: result.agenda });
      })
      .catch((error: unknown) => {
        saver.receive({ agenda });
        store.toasts.show({ tone: 'warning', title: `"${item.text}" was not marked`, body: messageOf(error, 'Memento did not answer.') });
      });
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
  const videoParts = showVideo;
  const subline =
    phase === 'ready'
      ? videoParts && selectedCount > 0
        ? sublineSources(effective, selectedCount)
        : `${selectedCount === 0 ? 'No' : selectedCount} audio ${selectedCount === 1 ? 'source' : 'sources'} selected · audio only`
      : own === null
        ? 'Writing the tracks to this PC'
        : phase === 'finalizing'
          ? `Writing ${own.tracks.length} ${own.tracks.length === 1 ? 'track' : 'tracks'} to this PC. Review opens when they are ready.`
          : phase === 'stopped'
            ? `Everything up to ${formatTimecode(own.elapsedMs)} is saved on this PC.`
            : videoParts
              ? `Started ${formatClock(parseIso(own.startedAt))} · ${sublineSources(effective, liveTracks.length)}`
              : `Started ${formatClock(parseIso(own.startedAt))} · ${liveTracks.length} audio ${liveTracks.length === 1 ? 'track' : 'tracks'} · audio only`;

  const formats: TrackFormat[] = live
    ? liveTracks.map((t) => ({ sampleRate: t.sampleRate, channels: t.channels }))
    : (sources ?? []).filter((s) => (selected ?? []).includes(s.id)).map(sourceFormat);

  const videoRate = showVideo ? videoBytesPerSecond(effective, list, videoSettings, live && videoStatus !== null ? videoTracks : null) : 0;
  const videoFooter = showVideo
    ? { line: live ? videoFooterLine(videoTracks, videoProblems) : null, bytesPerSecond: videoRate, note: rateNote(effective, videoSettings, live && videoStatus !== null ? videoTracks : null) }
    : null;
  const pip = effective?.pip ?? { enabled: false, corner: videoSettings.video.pipCorner };
  // Over the preview: why the main picture is missing (a track that waits or ended, a source Windows blocks).
  const mainThumb = screenSourceId !== null ? screenThumb : cameraThumb;
  const mainTrack = screenSourceId !== null ? screenTrack : cameraTrack;
  const mainNotice: string | null = live
    ? mainTrack !== undefined && mainTrack.state !== 'recording' && mainTrack.state !== 'paused'
      ? previewNoticeText(mainTrack, [...videoProblems].reverse().find((p) => p.trackId === mainTrack.trackId))
      : null
    : mainThumb.errorCode === 'video.captureBlocked' || mainThumb.errorCode === 'video.cameraPrivacy' || mainThumb.errorCode === 'video.cameraInUse'
      ? rowNoteText({ code: mainThumb.errorCode, atMs: 0, detail: mainThumb.errorDetail }).text
      : null;
  const preview =
    showVideo && phase !== 'finalizing' && phase !== 'stopped' ? (
      <LivePreview
        phase={phase}
        main={screenSourceId !== null ? screenThumb.dataUrl : cameraThumb.dataUrl}
        insetOn={screenSourceId !== null && cameraSourceId !== null && pip.enabled}
        inset={cameraThumb.dataUrl}
        pip={pip}
        caption={previewCaption(effective, list, videoSettings, videoTracks)}
        notice={mainNotice}
      />
    ) : null;

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
        <BannerSlot
          video={
            own !== null && live && videoTracks.length > 0
              ? {
                  sessionId: own.sessionId,
                  bytesPerSecond: recordingBytesPerSecond(formats) + videoRate,
                  videoRecording: videoTracks.some((t) => t.state === 'recording' || t.state === 'waiting'),
                  quality: videoSettings.video.quality,
                }
              : null
          }
        />
        <div class={videoAvailable ? 'rec-columns rec-columns--video' : 'rec-columns'}>
          <SourcesCard
            rows={rows}
            error={sourcesError}
            onToggle={toggleSource}
            onRescan={loadSources}
            video={
              <VideoSources
                available={videoAvailable}
                sources={videoSources}
                loadError={videoError}
                selection={effective}
                screenLine={screenLine}
                recordingVideo={live && videoTracks.some((t) => t.state === 'recording')}
                notes={rowNotes}
                busy={videoBusy}
                thumbs={{ screen: screenThumb, camera: cameraThumb }}
                cards={videoCards}
                onToggle={toggleVideo}
                onOpenPicker={setPicker}
                onCamera={chooseCamera}
                onPip={changePip}
                onCardAction={videoCardAction}
              />
            }
          />

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
              preview={preview}
            />
            {own !== null && phase !== 'ready' ? (
              <TracksCard sessionId={own.sessionId} tracks={own.tracks} levels={store.levels} clock={clock} running={phase === 'recording'} video={store.videoStatus} />
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
            <LiveTranscriptCard
              live={own !== null && store.liveTranscript.value?.sessionId === own.sessionId ? store.liveTranscript.value : null}
              timing={store.settings.value?.transcription.timing ?? null}
              phase={phase}
              elapsedMs={own?.elapsedMs ?? 0}
              video={showVideo}
            />
            {live ? <EncoderCard tracks={videoTracks} /> : null}
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
        video={videoFooter}
      />
      {picker !== null && videoSources !== null ? (
        <CapturePicker
          bridge={bridge}
          sources={videoSources}
          initialMode={picker}
          displayId={effective?.displayId ?? null}
          windowId={effective?.windowId ?? null}
          includePointer={videoSettings.video.includePointer}
          onUse={useCaptureSource}
          onClose={() => {
            setPicker(null);
          }}
        />
      ) : null}
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
