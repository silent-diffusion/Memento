// Live output (DESIGN.md §10, §11): a full-height sheet beside the generating Builder or viewer that
// shows the exchange with the model as it happens: every pass in pipeline order on the left
// (segment → map → reduce → verify → grounding), and the selected pass's exact request and its reply
// on the right, the local model's reply streaming in as tokens arrive. It follows the newest output
// until the person scrolls up or picks a pass, and stays readable once the generation has finished.
import type { JSX } from 'preact';
import { createPortal } from 'preact/compat';
import { useEffect, useLayoutEffect, useRef, useState } from 'preact/hooks';
import type { GenerationOutputStep } from '../../bridge/types';
import { CloseIcon } from '../../components/icons';
import { moveFocus } from '../../components/keyboard';
import { useModal } from '../../components/Overlay';
import { useServices } from '../../state/context';
import { closeLiveOutput, liveOutputOf, liveOutputOpen, type LiveOutput, type LivePass } from './liveOutput';
import './liveoutput.css';

/** The pipeline's steps in order, with the words the list uses. */
export const STEP_LABELS: readonly { step: GenerationOutputStep; label: string }[] = [
  { step: 'segment', label: 'Segment' },
  { step: 'map', label: 'Map' },
  { step: 'reduce', label: 'Reduce' },
  { step: 'verify', label: 'Verify' },
  { step: 'grounding', label: 'Grounding' },
];

/** Within this many pixels of the end the reply counts as "at the end" (following resumes there). */
const AT_END_PX = 24;

const count = (n: number): string => n.toLocaleString('en-US');

function seconds(ms: number): string {
  return ms < 1000 ? `${Math.max(0, Math.round(ms))} ms` : `${(ms / 1000).toFixed(1)} s`;
}

/** "Claude" or "Qwen3.5 4B on this PC". */
export function providerWords(live: LiveOutput): string {
  return live.provider.kind === 'local' ? `${live.provider.modelLabel ?? 'The local model'} on this PC` : live.provider.name;
}

/** The list's second line for a pass. */
export function passMeta(pass: LivePass, live: LiveOutput): string {
  if (pass.inCode) {
    return pass.elapsedMs === null ? 'In code' : `In code · ${seconds(pass.elapsedMs)}`;
  }
  const tokens = pass.outputTokens === null ? null : `${count(pass.outputTokens)} ${pass.outputTokens === 1 ? 'token' : 'tokens'}`;
  switch (pass.status) {
    case 'running':
      if (!pass.streamed) {
        return `Waiting for ${live.provider.name}`;
      }
      return tokens === null ? 'Reading the request' : `Writing · ${tokens}`;
    case 'stopped':
      return tokens === null ? 'Stopped' : `Stopped · ${tokens}`;
    case 'done':
      return [tokens, pass.elapsedMs === null ? null : seconds(pass.elapsedMs)].filter((p) => p !== null).join(' · ') || 'Done';
  }
}

/** The counters over the reply: tokens, tokens per second (the local model) and time. */
export function passStats(pass: LivePass): string[] {
  const stats: string[] = [];
  if (pass.outputTokens !== null) {
    stats.push(`${count(pass.outputTokens)} ${pass.outputTokens === 1 ? 'token' : 'tokens'}`);
  }
  if (pass.streamed && pass.tokensPerSecond !== null) {
    stats.push(`${pass.tokensPerSecond.toFixed(1)} tokens/s`);
  }
  if (pass.elapsedMs !== null) {
    stats.push(seconds(pass.elapsedMs));
  }
  if (pass.promptTokens !== null) {
    stats.push(`request ${count(pass.promptTokens)} tokens`);
  }
  return stats;
}

function statusWords(live: LiveOutput): string {
  if (!live.ended && live.outcome === 'running') {
    return live.provider.kind === 'local' ? 'Writing' : 'Sending and receiving';
  }
  switch (live.outcome) {
    case 'done':
      return 'Finished · read only';
    case 'failed':
      return 'Stopped by a failure · read only';
    case 'cancelled':
      return 'Cancelled · read only';
    default:
      return 'Finished · read only';
  }
}

/** The newest pass: the one still running, else the last. */
function newest(passes: readonly LivePass[]): LivePass | null {
  for (let i = passes.length - 1; i >= 0; i--) {
    const pass = passes[i];
    if (pass?.status === 'running') {
      return pass;
    }
  }
  return passes[passes.length - 1] ?? null;
}

function StatusDot({ pass }: { pass: LivePass }): JSX.Element {
  const kind = pass.status === 'running' ? 'running' : pass.status === 'stopped' ? 'stopped' : 'done';
  return <span class={`live-dot live-dot--${kind}${kind === 'running' ? ' pulse' : ''}`} aria-hidden="true" />;
}

interface SheetProps {
  live: LiveOutput;
  onClose: () => void;
}

export function LiveOutputSheet({ live, onClose }: SheetProps): JSX.Element {
  const { ref, onKeyDown } = useModal(onClose);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [follow, setFollow] = useState(true);
  const [announcement, setAnnouncement] = useState('');
  const replyRef = useRef<HTMLPreElement | null>(null);
  /** Where following last put the reply's scroll position. */
  const followedTop = useRef(0);
  const running = !live.ended && live.outcome === 'running';
  const latest = newest(live.passes);
  const selected = (follow ? latest : (live.passes.find((p) => p.id === selectedId) ?? latest)) ?? null;
  const local = live.provider.kind === 'local';

  // Following keeps the reply at its end as text arrives; scrolling up (or choosing a pass) pauses it. The jump is
  // instant, never animated (and so the same with reduced motion).
  useLayoutEffect(() => {
    const well = replyRef.current;
    if (follow && well !== null) {
      well.scrollTop = well.scrollHeight;
      followedTop.current = well.scrollTop;
    }
  }, [follow, selected?.id, selected?.reply]);

  // A pass that finishes while followed is announced once, politely.
  const lastDone = useRef<string | null>(null);
  useEffect(() => {
    const finished = [...live.passes].reverse().find((p) => p.status === 'done' && !p.inCode);
    if (follow && finished !== undefined && finished.id !== lastDone.current) {
      lastDone.current = finished.id;
      setAnnouncement(`${finished.title}: ${passMeta(finished, live)}.`);
    }
  }, [live.passes, follow]);

  const choose = (pass: LivePass): void => {
    setSelectedId(pass.id);
    setFollow(false);
  };

  // Text arriving only ever moves the end down, so a scroll position above the one following set is the person's.
  const onReplyScroll = (): void => {
    const well = replyRef.current;
    if (well === null) {
      return;
    }
    if (follow && well.scrollTop < followedTop.current - 4) {
      setSelectedId(selected?.id ?? null);
      setFollow(false);
      return;
    }
    const atEnd = well.scrollHeight - well.scrollTop - well.clientHeight <= AT_END_PX;
    if (!follow && atEnd && running && selected !== null && selected.id === latest?.id) {
      setSelectedId(null);
      setFollow(true);
    }
  };

  const resume = (): void => {
    setSelectedId(null);
    setFollow(true);
  };

  const groups = STEP_LABELS.map((g) => ({ ...g, passes: live.passes.filter((p) => p.step === g.step) })).filter((g) => g.passes.length > 0);
  const stats = selected === null ? [] : passStats(selected);

  return createPortal(
    <div class="scrim scrim--sheet">
      <div
        ref={ref}
        class="sheet live-sheet"
        role="dialog"
        aria-modal="true"
        aria-labelledby="live-title"
        aria-describedby="live-sub"
        tabIndex={-1}
        onKeyDown={onKeyDown}
      >
        <div class="sheet-header live-header">
          <div class="live-heading">
            <h2 id="live-title" class="sheet-title">
              Live output
            </h2>
            <span id="live-sub" class="live-sub">
              {running ? <span class="gen-dot pulse live-head-dot" aria-hidden="true" /> : null}
              {providerWords(live)} · {statusWords(live)}
            </span>
          </div>
          <button class="icon-btn sheet-close" type="button" aria-label="Close" onClick={onClose}>
            <CloseIcon size={18} />
          </button>
        </div>
        <div class="live-body">
          <nav
            class="live-passes"
            aria-label="Passes"
            onKeyDown={(event) => {
              const moved = moveFocus(event, event.currentTarget, '.live-pass', 'vertical');
              const id = moved?.dataset.pass;
              const pass = id === undefined ? undefined : live.passes.find((p) => p.id === id);
              if (pass !== undefined) {
                choose(pass);
              }
            }}
          >
            {groups.length === 0 ? (
              <p class="live-empty" role="status">
                {running ? (local ? 'Loading the model and preparing the first request…' : 'Preparing the first request…') : 'Nothing was exchanged.'}
              </p>
            ) : (
              groups.map((group) => (
                <section key={group.step} class="live-group" aria-label={group.label}>
                  <h3 class="lbl live-group-label">
                    {group.label}
                    {group.passes.length > 1 ? <span class="live-group-count"> · {group.passes.length}</span> : null}
                  </h3>
                  <ol class="live-list">
                    {group.passes.map((pass) => {
                      const current = selected?.id === pass.id;
                      return (
                        <li key={pass.id}>
                          <button
                            class={current ? 'live-pass on' : 'live-pass'}
                            type="button"
                            data-pass={pass.id}
                            aria-current={current ? 'true' : undefined}
                            tabIndex={current || (selected === null && pass === live.passes[0]) ? 0 : -1}
                            onClick={() => {
                              choose(pass);
                            }}
                          >
                            <StatusDot pass={pass} />
                            <span class="live-pass-text">
                              <span class="live-pass-title">{pass.title}</span>
                              <span class="live-pass-meta">{passMeta(pass, live)}</span>
                            </span>
                          </button>
                        </li>
                      );
                    })}
                  </ol>
                </section>
              ))
            )}
          </nav>
          <section class={selected === null ? "live-pane live-pane--empty" : selected.inCode ? "live-pane live-pane--code" : "live-pane"} aria-label={selected === null ? "Selected pass" : selected.title}>
            {selected === null ? (
              <p class="live-empty">The requests and replies appear here as they are sent and received.</p>
            ) : (
              <>
                <div class="live-pane-head">
                  <div class="live-pane-title">
                    <h3 class="live-title">{selected.title}</h3>
                    <span class="live-stats mono">{stats.join(' · ')}</span>
                  </div>
                  {running ? (
                    follow ? (
                      <span class="live-follow-note" role="status">
                        Following the newest output
                      </span>
                    ) : (
                      <button class="btn g small-btn" type="button" onClick={resume}>
                        Follow live output
                      </button>
                    )
                  ) : null}
                </div>
                {selected.inCode ? null : (
                  <>
                    <div class="live-label-row">
                      <span class="lbl" id="live-request-label">
                        {local ? 'Request · read by the local model' : `Request · sent to ${live.provider.name}`}
                      </span>
                    </div>
                    <pre class="live-well live-request mono" tabIndex={0} aria-labelledby="live-request-label">
                      {selected.request}
                    </pre>
                  </>
                )}
                <div class="live-label-row">
                  <span class="lbl" id="live-reply-label">
                    {selected.inCode ? 'What was done in code' : 'Reply'}
                  </span>
                  {!selected.inCode && !selected.streamed ? <span class="live-whole">Replies from {live.provider.name} arrive whole</span> : null}
                  {selected.stopReason !== null && selected.stopReason !== 'eog' && selected.stopReason !== 'end_turn' && selected.stopReason !== 'Completed' ? (
                    <span class="live-whole">Stopped: {selected.stopReason}</span>
                  ) : null}
                </div>
                <pre
                  ref={replyRef}
                  class={`live-well live-reply ${selected.inCode ? '' : 'mono'}`}
                  tabIndex={0}
                  aria-labelledby="live-reply-label"
                  aria-busy={selected.status === 'running' ? 'true' : 'false'}
                  onScroll={onReplyScroll}
                >
                  {selected.reply === '' && selected.status === 'running' ? (
                    <span class="live-waiting">{selected.streamed ? 'Reading the request…' : `Waiting for ${live.provider.name}…`}</span>
                  ) : (
                    selected.reply
                  )}
                  {selected.status === 'running' && selected.streamed && selected.reply !== '' ? <span class="live-caret" aria-hidden="true" /> : null}
                </pre>
              </>
            )}
          </section>
        </div>
        <div class="sheet-footer">
          <span class="sheet-footer-note">
            {local
              ? 'Read by the local model on this PC; nothing leaves it. Shown only here and not saved.'
              : `Only what was sent to ${live.provider.name} and what it answered. Nothing more is sent; shown only here and not saved.`}
          </span>
          <button class="btn p" type="button" onClick={onClose}>
            Done
          </button>
        </div>
        <div class="sr" role="status" aria-live="polite">
          {announcement}
        </div>
      </div>
    </div>,
    document.body,
  );
}

/** Renders the sheet wherever the app is, while it is open (it survives the Builder opening the viewer). */
export function LiveOutputLayer(): JSX.Element | null {
  const { store } = useServices();
  const live = liveOutputOf(store).value;
  const open = liveOutputOpen(store).value;
  if (!open || live === null) {
    return null;
  }
  return (
    <LiveOutputSheet
      live={live}
      onClose={() => {
        closeLiveOutput(store);
      }}
    />
  );
}
