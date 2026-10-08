// The Review player strip (DESIGN.md §9, §5.11), transcribed from renders/Review.dc.html: waveform
// from peaks.json, the scrubber under it, transport, the mono position, speed, Skip silences,
// transcript search and Highlight. The real <audio> element plays the host's mixUrl.
import type { JSX } from 'preact';
import { useEffect, useMemo, useRef, useState } from 'preact/hooks';
import type { TranscriptSearchMatch } from '../../bridge/types';
import { BackTenIcon, ChevronDownIcon, ChevronUpIcon, FlagIcon, ForwardTenIcon, PauseIcon, PlayIcon, SearchIcon } from '../../components/icons';
import { SelectMenu } from '../../components/Menus';
import { formatDuration } from '../../format/duration';
import { parsePeaks, PLAYBACK_RATES, rateLabel, resamplePeaks, scrubKeyTarget, waveBarHeight } from '../../format/player';
import { createSilenceSkipper, skippedBars, skippedCaption, SKIPPED_CAPTION_MS, type Silence, type SilenceSkipper } from '../../format/silences';
import { matchCountText } from '../../format/transcript';
import type { SearchApi } from './useTranscript';

export const WAVE_BARS = 160;

export type PeaksState = { kind: 'none' } | { kind: 'loading' } | { kind: 'ready'; bars: number[] } | { kind: 'failed' };

/** Reads peaks.json from the host's URL. Null URL: the recording is not finalized yet. */
export function usePeaks(peaksUrl: string | null): PeaksState {
  const [state, setState] = useState<PeaksState>({ kind: peaksUrl === null ? 'none' : 'loading' });
  useEffect(() => {
    if (peaksUrl === null) {
      setState({ kind: 'none' });
      return undefined;
    }
    let live = true;
    setState({ kind: 'loading' });
    fetch(peaksUrl)
      .then((response) => {
        if (!response.ok) {
          throw new Error(`peaks.json answered ${response.status}`);
        }
        return response.json() as Promise<unknown>;
      })
      .then((json) => {
        const peaks = parsePeaks(json);
        if (live) {
          setState(peaks === null ? { kind: 'failed' } : { kind: 'ready', bars: resamplePeaks(peaks, WAVE_BARS) });
        }
      })
      .catch((error: unknown) => {
        console.warn('[review] peaks.json could not be read', error);
        if (live) {
          setState({ kind: 'failed' });
        }
      });
    return () => {
      live = false;
    };
  }, [peaksUrl]);
  return state;
}

export interface PlayerApi {
  positionMs: number;
  durationMs: number;
  playing: boolean;
  rate: number;
  /** A manual seek (buttons, scrubber, chapters, lines, search): Skip silences waits 1 s after it. */
  seek: (ms: number) => void;
  toggle: () => void;
  setRate: (rate: number) => void;
  /** The scrubber is held: Skip silences never jumps meanwhile. */
  setScrubbing: (scrubbing: boolean) => void;
  /** The last jump over a silence (how long, where it landed) while its caption shows; `id` restarts the fade. */
  skipped: { seconds: number; atMs: number; id: number } | null;
}

/**
 * The <audio> element's state. Without media the position still moves, so chapters can be placed.
 * `silences` (Skip silences on) are jumped over while playing, from the time-update loop.
 */
export function usePlayer(
  mixUrl: string | null,
  fallbackDurationMs: number,
  silences: readonly Silence[] | null = null,
): PlayerApi & { audioRef: { current: HTMLAudioElement | null } } {
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const [positionMs, setPositionMs] = useState(0);
  const [mediaDurationMs, setMediaDurationMs] = useState<number | null>(null);
  const [playing, setPlaying] = useState(false);
  const [rate, setRateState] = useState(1);
  const [skipped, setSkipped] = useState<{ seconds: number; atMs: number; id: number } | null>(null);
  const pendingSeek = useRef<number | null>(null);
  const rateRef = useRef(1);
  const silencesRef = useRef(silences);
  silencesRef.current = silences;
  const skipperRef = useRef<SilenceSkipper | null>(null);
  skipperRef.current ??= createSilenceSkipper();
  const skipper = skipperRef.current;
  const captionTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  useEffect(
    () => () => {
      clearTimeout(captionTimer.current);
    },
    [],
  );

  useEffect(() => {
    const audio = audioRef.current;
    if (audio === null) {
      return undefined;
    }
    let frame = 0;
    /** While playing with Skip silences on: jumps over the gap the playhead is in. True if it jumped. */
    const skipSilence = (): boolean => {
      const gaps = silencesRef.current;
      if (gaps === null || audio.paused) {
        return false;
      }
      const from = audio.currentTime;
      const target = skipper.check(from, gaps);
      if (target === null) {
        return false;
      }
      audio.currentTime = target;
      setPositionMs(target * 1000);
      setSkipped((last) => ({ seconds: target - from, atMs: target * 1000, id: (last?.id ?? 0) + 1 }));
      clearTimeout(captionTimer.current);
      captionTimer.current = setTimeout(() => {
        setSkipped(null);
      }, SKIPPED_CAPTION_MS);
      return true;
    };
    const tick = (): void => {
      if (!skipSilence()) {
        setPositionMs(audio.currentTime * 1000);
      }
      if (!audio.paused) {
        frame = requestAnimationFrame(tick);
      }
    };
    const onMeta = (): void => {
      setMediaDurationMs(Number.isFinite(audio.duration) ? audio.duration * 1000 : null);
      if (pendingSeek.current !== null) {
        audio.currentTime = pendingSeek.current / 1000;
        pendingSeek.current = null;
      }
      audio.playbackRate = rateRef.current;
    };
    const onPlay = (): void => {
      setPlaying(true);
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(tick);
    };
    const onPause = (): void => {
      setPlaying(false);
      setPositionMs(audio.currentTime * 1000);
    };
    const onTime = (): void => {
      if (audio.paused) {
        setPositionMs(audio.currentTime * 1000);
      } else {
        // Animation frames stop while the window is hidden; time updates keep the skipping going.
        skipSilence();
      }
    };
    audio.addEventListener('loadedmetadata', onMeta);
    audio.addEventListener('play', onPlay);
    audio.addEventListener('pause', onPause);
    audio.addEventListener('ended', onPause);
    audio.addEventListener('timeupdate', onTime);
    return () => {
      cancelAnimationFrame(frame);
      audio.removeEventListener('loadedmetadata', onMeta);
      audio.removeEventListener('play', onPlay);
      audio.removeEventListener('pause', onPause);
      audio.removeEventListener('ended', onPause);
      audio.removeEventListener('timeupdate', onTime);
    };
  }, [mixUrl]);

  const durationMs = mediaDurationMs ?? fallbackDurationMs;

  const seek = (ms: number): void => {
    const target = Math.max(0, Math.min(durationMs, ms));
    skipper.noteManualSeek();
    setPositionMs(target);
    const audio = audioRef.current;
    if (audio !== null && mixUrl !== null && audio.readyState >= 1) {
      audio.currentTime = target / 1000;
    } else {
      pendingSeek.current = target;
    }
  };

  return {
    audioRef,
    positionMs,
    durationMs,
    playing,
    rate,
    seek,
    toggle: () => {
      const audio = audioRef.current;
      if (audio === null || mixUrl === null) {
        return;
      }
      if (audio.paused) {
        audio.play().catch((error: unknown) => {
          console.warn('[review] playback could not start', error);
        });
      } else {
        audio.pause();
      }
    },
    setRate: (next) => {
      setRateState(next);
      rateRef.current = next;
      if (audioRef.current !== null) {
        audioRef.current.playbackRate = next;
      }
    },
    setScrubbing: skipper.setScrubbing,
    skipped,
  };
}

interface PlayerStripProps {
  player: PlayerApi;
  peaks: PeaksState;
  hasMedia: boolean;
  onHighlight: () => void;
  /** Null until there is a transcript to search. */
  search: SearchApi | null;
  onJump: (match: TranscriptSearchMatch) => void;
  skip: SkipSilencesState;
}

/** The Skip silences chip: on or off, and whether there is a transcript to find the silences in. */
export interface SkipSilencesState {
  available: boolean;
  on: boolean;
  onToggle: (on: boolean) => void;
  /** The silences that are skipped while it is on. */
  gaps: readonly Silence[];
}

function Waveform({ player, peaks, skipped }: { player: PlayerApi; peaks: PeaksState; skipped: readonly Silence[] | null }): JSX.Element {
  const played = player.durationMs > 0 ? player.positionMs / player.durationMs : 0;
  const barCount = peaks.kind === 'ready' ? peaks.bars.length : 0;
  // Skip silences on: the bars standing for skipped time are dimmed.
  const dimmed = useMemo(() => (skipped === null ? null : skippedBars(barCount, player.durationMs, skipped)), [skipped, barCount, player.durationMs]);
  if (peaks.kind !== 'ready') {
    const text =
      peaks.kind === 'failed'
        ? 'The waveform could not be read. Playback still works.'
        : peaks.kind === 'loading'
          ? 'Reading the waveform…'
          : 'Waveform appears once the recording is finalized';
    return (
      <div class="wave wave--empty">
        <span class="wave-empty-text">{text}</span>
      </div>
    );
  }
  return (
    <div
      class="wave"
      aria-hidden="true"
      onPointerDown={(event) => {
        const box = event.currentTarget.getBoundingClientRect();
        player.seek(((event.clientX - box.left) / Math.max(1, box.width)) * player.durationMs);
      }}
    >
      {peaks.bars.map((value, i) => (
        <div
          key={i}
          class={`wave-bar${i / peaks.bars.length < played ? ' played' : ''}${dimmed?.[i] === true ? ' skipped' : ''}`}
          style={{ height: `${waveBarHeight(value)}px` }}
        />
      ))}
    </div>
  );
}

/** The real control under the bars (DESIGN.md §5.11): Left/Right 5 s, Shift 30 s, Home/End. */
function Scrubber({ player }: { player: PlayerApi }): JSX.Element {
  const dragging = useRef(false);
  const fraction = player.durationMs > 0 ? Math.min(1, player.positionMs / player.durationMs) : 0;
  const seekAt = (event: PointerEvent, el: HTMLElement): void => {
    const box = el.getBoundingClientRect();
    player.seek(((event.clientX - box.left) / Math.max(1, box.width)) * player.durationMs);
  };
  return (
    <div
      class="scrubber"
      role="slider"
      tabIndex={0}
      aria-label="Playback position"
      aria-valuemin={0}
      aria-valuemax={Math.round(player.durationMs / 1000)}
      aria-valuenow={Math.round(player.positionMs / 1000)}
      aria-valuetext={`${formatDuration(player.positionMs)} of ${formatDuration(player.durationMs)}`}
      onKeyDown={(event) => {
        const target = scrubKeyTarget(event.key, event.shiftKey, player.positionMs, player.durationMs);
        if (target !== null) {
          event.preventDefault();
          player.seek(target);
        }
      }}
      onPointerDown={(event) => {
        dragging.current = true;
        player.setScrubbing(true);
        event.currentTarget.setPointerCapture(event.pointerId);
        seekAt(event, event.currentTarget);
      }}
      onPointerMove={(event) => {
        if (dragging.current) {
          seekAt(event, event.currentTarget);
        }
      }}
      onPointerUp={() => {
        dragging.current = false;
        player.setScrubbing(false);
      }}
      onPointerCancel={() => {
        dragging.current = false;
        player.setScrubbing(false);
      }}
    >
      <div class="scrubber-track">
        <div class="scrubber-fill" style={{ width: `${fraction * 100}%` }} />
      </div>
      <div class="scrubber-knob" style={{ left: `${fraction * 100}%` }} />
    </div>
  );
}

/**
 * The transcript search in the player strip: live results with a count, Enter for the next match and
 * Shift+Enter for the previous one, Esc clears. Without a transcript it says why it is off.
 */
function TranscriptSearchField({ search, onJump }: { search: SearchApi | null; onJump: (match: TranscriptSearchMatch) => void }): JSX.Element {
  const step = (direction: 1 | -1): void => {
    const match = search?.step(direction) ?? null;
    if (match !== null) {
      onJump(match);
    }
  };
  const active = search !== null && search.query.trim() !== '';
  return (
    <div class="player-search" role="search">
      <label class="sr" for="tx-search">
        Search transcript
      </label>
      <SearchIcon size={16} class="player-search-icon" />
      <input
        id="tx-search"
        class={active ? 'field player-search-field player-search-field--counted' : 'field player-search-field'}
        type="search"
        placeholder={search === null ? 'No transcript to search yet' : 'Search transcript'}
        disabled={search === null}
        value={search?.query ?? ''}
        autocomplete="off"
        aria-describedby={active ? 'tx-search-count' : undefined}
        onInput={(event) => {
          search?.setQuery(event.currentTarget.value);
        }}
        onKeyDown={(event) => {
          if (event.key === 'Enter') {
            event.preventDefault();
            step(event.shiftKey ? -1 : 1);
          } else if (event.key === 'Escape') {
            event.preventDefault();
            event.stopPropagation();
            search?.clear();
          }
        }}
      />
      {active ? (
        <span class="player-search-tools">
          <span id="tx-search-count" class="player-search-count" aria-live="polite">
            {search.answered ? matchCountText(search.current, search.matches.length) : 'Searching…'}
          </span>
          <button
            class="icon-btn player-search-step"
            type="button"
            aria-label="Previous match (Shift+Enter)"
            disabled={search.matches.length === 0}
            onClick={() => {
              step(-1);
            }}
          >
            <ChevronUpIcon size={14} />
          </button>
          <button
            class="icon-btn player-search-step"
            type="button"
            aria-label="Next match (Enter)"
            disabled={search.matches.length === 0}
            onClick={() => {
              step(1);
            }}
          >
            <ChevronDownIcon size={14} />
          </button>
        </span>
      ) : null}
    </div>
  );
}

export function PlayerStrip({ player, peaks, hasMedia, onHighlight, search, onJump, skip }: PlayerStripProps): JSX.Element {
  const skipping = skip.available && skip.on;
  const landedAt = player.durationMs > 0 && player.skipped !== null ? Math.min(1, player.skipped.atMs / player.durationMs) : 0;
  return (
    <div class="player">
      {/* Where playback landed after a jump; visual only (the chip's pressed state says it is on). */}
      {skipping && player.skipped !== null ? (
        <span key={player.skipped.id} class="mono player-skipped" aria-hidden="true" style={{ left: `clamp(72px, calc(32px + ${landedAt} * (100% - 64px)), calc(100% - 72px))` }}>
          {skippedCaption(player.skipped.seconds)}
        </span>
      ) : null}
      <Waveform player={player} peaks={peaks} skipped={skipping ? skip.gaps : null} />
      <Scrubber player={player} />
      <div class="player-controls">
        <button
          class="icon-btn player-skip"
          type="button"
          aria-label="Back 10 seconds"
          onClick={() => {
            player.seek(player.positionMs - 10_000);
          }}
        >
          <BackTenIcon size={18} />
        </button>
        <button
          class="btn player-play"
          type="button"
          aria-label={player.playing ? 'Pause' : 'Play'}
          disabled={!hasMedia}
          title={hasMedia ? undefined : 'Playback is ready once the recording is stored'}
          onClick={player.toggle}
        >
          {player.playing ? <PauseIcon size={18} /> : <PlayIcon size={18} />}
        </button>
        <button
          class="icon-btn player-skip"
          type="button"
          aria-label="Forward 10 seconds"
          onClick={() => {
            player.seek(player.positionMs + 10_000);
          }}
        >
          <ForwardTenIcon size={18} />
        </button>
        <span class="mono player-time">
          {formatDuration(player.positionMs)} <span class="player-total">/ {formatDuration(player.durationMs)}</span>
        </span>
        <div class="speed-menu">
          <SelectMenu
            label="Playback speed"
            variant="sort"
            value={String(player.rate)}
            buttonText={rateLabel(player.rate)}
            options={PLAYBACK_RATES.map((r) => ({ value: String(r), label: rateLabel(r) }))}
            onChange={(value) => {
              player.setRate(Number(value));
            }}
          />
        </div>
        <button
          class={skipping ? 'chip on player-silences' : 'chip player-silences'}
          type="button"
          aria-pressed={skipping}
          disabled={!skip.available}
          title={skip.available ? 'Jump over pauses longer than 1.5 s between transcript lines' : 'Available once transcribed'}
          onClick={() => {
            skip.onToggle(!skip.on);
          }}
        >
          Skip silences
        </button>
        <span class="player-spacer" />
        <TranscriptSearchField search={search} onJump={onJump} />
        <button class="btn ghost spoke-ghost" type="button" onClick={onHighlight}>
          <FlagIcon size={16} />
          Highlight
        </button>
      </div>
    </div>
  );
}
