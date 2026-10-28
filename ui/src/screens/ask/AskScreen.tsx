import type { JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import { BridgeCallError } from '../../bridge/client';
import type { AskContextInfo, AskSendSummary, AskTurn, ProviderInfo } from '../../bridge/types';
import { SpokeHeader } from '../../components/SpokeHeader';
import { formatSize } from '../../format/storage';
import { openSettings } from '../../state/actions';
import { useBack } from '../../state/back';
import { useServices } from '../../state/context';
import { answerParts, citationLabel, contextExplanation, contextSummary, destinationNote, initialProvider, sectionLabel } from './askText';
import './ask.css';

interface Running {
  /** null until ask.send answers (events for this recording are taken meanwhile). */
  jobId: string | null;
  text: string;
  context: AskContextInfo | null;
  /** "Ask before every send": waiting for the person. */
  confirm: AskSendSummary | null;
}

function messageOf(error: unknown): string {
  return error instanceof BridgeCallError || error instanceof Error ? error.message : 'Memento could not reach its host. Nothing was sent. Try again.';
}

/**
 * Ask (2.9.0): a conversation with an AI about one recording. Questions go to the chosen provider with the transcript
 * (whole, or the sections a search on this PC picks); answers stream in, cite lines that open Review, and say which
 * sections they were given. Without a ready provider the page explains how to set one up instead.
 */
export function AskScreen({ recordingId }: { recordingId: string }): JSX.Element {
  const services = useServices();
  const { bridge, store, router } = services;
  const back = useBack({ route: { name: 'review', recordingId }, label: 'Recording' });
  const title = store.library.value?.recordings.find((r) => r.id === recordingId)?.title ?? 'Recording';
  const [turns, setTurns] = useState<AskTurn[] | null>(null);
  const [providers, setProviders] = useState<ProviderInfo[] | null>(null);
  const [providerId, setProviderId] = useState<ProviderInfo['id'] | null>(null);
  const [draft, setDraft] = useState('');
  const [running, setRunning] = useState<Running | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [confirmClear, setConfirmClear] = useState(false);
  const runningRef = useRef<Running | null>(null);
  // Tokens can arrive faster than the page renders: the ref holds the latest state between renders.
  const setRun = (next: Running | null): void => {
    runningRef.current = next;
    setRunning(next);
  };
  const endRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const mounted = { live: true };
    void (async () => {
      try {
        const [conversation, list] = await Promise.all([bridge.call('ask.get', { recordingId }), bridge.call('providers.list')]);
        if (!mounted.live) {
          return;
        }
        setTurns(conversation.turns);
        setProviders(list.providers);
        setProviderId(initialProvider(list.providers, list.defaultProviderId ?? null)?.id ?? null);
        if (conversation.jobId !== null) {
          setRun({ jobId: conversation.jobId, text: '', context: null, confirm: null });
        }
      } catch (e) {
        if (mounted.live) {
          setError(messageOf(e));
          setTurns([]);
        }
      }
    })();
    return () => {
      mounted.live = false;
    };
  }, [bridge, recordingId]);

  useEffect(
    () =>
      bridge.on('ask.progress', (event) => {
        const current = runningRef.current;
        if (event.recordingId !== recordingId || current === null || (current.jobId !== null && current.jobId !== event.jobId)) {
          return;
        }
        switch (event.kind) {
          case 'context':
            setRun({ ...current, jobId: event.jobId, context: event.context ?? null });
            return;
          case 'token':
            setRun({ ...current, jobId: event.jobId, text: current.text + event.text });
            return;
          case 'done':
          case 'cancelled':
          case 'failed': {
            const turn = event.turn;
            if (turn != null) {
              setTurns((list) => [...(list ?? []), turn]);
            }
            setRun(null);
            if (event.kind === 'failed') {
              setError(event.text);
            } else if (event.kind === 'cancelled') {
              setNotice(event.text);
            }
            return;
          }
        }
      }),
    [bridge, recordingId],
  );

  useEffect(() => {
    // jsdom has no scrollIntoView.
    const end = endRef.current;
    if (end !== null && typeof Element.prototype.scrollIntoView === 'function') {
      end.scrollIntoView({ block: 'end' });
    }
  }, [turns?.length, running?.text]);

  const provider = providers?.find((p) => p.id === providerId) ?? null;
  const anyReady = providers?.some((p) => p.ready) ?? false;

  const send = async (): Promise<void> => {
    const question = draft.trim();
    if (question === '' || running !== null || provider === null) {
      return;
    }
    setError(null);
    setNotice(null);
    const pending: Running = { jobId: null, text: '', context: null, confirm: null };
    setRun(pending);
    try {
      const result = await bridge.call('ask.send', { recordingId, question, providerId: provider.id });
      setDraft('');
      if (result.confirmationRequired === true && result.summary != null) {
        const summary = result.summary;
        const held = { ...(runningRef.current ?? pending), jobId: result.jobId, confirm: summary };
        setRun(held);
        setTurns((list) => [...(list ?? []), { ...result.question, id: `pending-${result.question.id}` }]);
        return;
      }
      setTurns((list) => [...(list ?? []), result.question]);
      if (runningRef.current !== null) {
        const started = { ...runningRef.current, jobId: result.jobId };
        setRun(started);
      }
    } catch (e) {
      setRun(null);
      setError(messageOf(e));
    }
  };

  const confirm = async (approved: boolean): Promise<void> => {
    const current = running;
    if (current?.jobId == null) {
      return;
    }
    // The question shows as asked once it is sent; a decline takes it back off the page (nothing was kept).
    setTurns((list) =>
      approved ? (list ?? []).map((t) => (t.id.startsWith('pending-') ? { ...t, id: t.id.slice('pending-'.length) } : t)) : (list ?? []).filter((t) => !t.id.startsWith('pending-')),
    );
    // A decline frees the page at once; its cancelled event then finds nothing running.
    setRun(approved ? { ...current, confirm: null } : null);
    if (!approved) {
      setNotice('Nothing was sent.');
    }
    try {
      await bridge.call('ask.confirm', { jobId: current.jobId, approved });
    } catch (e) {
      setRun(null);
      setError(messageOf(e));
    }
  };

  const stop = async (): Promise<void> => {
    if (running?.jobId == null) {
      return;
    }
    try {
      await bridge.call('ask.cancel', { jobId: running.jobId });
    } catch (e) {
      setRun(null);
      setError(messageOf(e));
    }
  };

  const clear = async (): Promise<void> => {
    setConfirmClear(false);
    try {
      const result = await bridge.call('ask.clear', { recordingId });
      setTurns(result.turns);
      setNotice('The conversation was cleared.');
      setError(null);
    } catch (e) {
      setError(messageOf(e));
    }
  };

  const openLine = (start: number): void => {
    router.navigate({ name: 'review', recordingId, atMs: Math.round(start * 1000) });
  };

  const onKeyDown = (event: KeyboardEvent): void => {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      void send();
    }
  };

  const loading = turns === null || providers === null;
  return (
    <>
      <SpokeHeader
        backLabel={back.label}
        onBack={back.go}
        title="Ask"
        meta={title}
        selfLabel="Ask"
        actions={
          turns !== null && turns.length > 0 ? (
            <button class="btn ghost" type="button" disabled={running !== null} onClick={() => {
              setConfirmClear(true);
            }}>
              Clear conversation
            </button>
          ) : undefined
        }
      />
      <main class="spoke-main ask-main">
        <div class="ask-column">
          {loading ? (
            <p class="ask-quiet">Loading the conversation…</p>
          ) : !anyReady ? (
            <section class="ask-setup" aria-labelledby="ask-setup-title">
              <h2 id="ask-setup-title" class="ask-setup-title">
                Ask needs an AI provider
              </h2>
              <p>
                Ask answers questions about this recording's transcript with an AI model. None is set up yet. Download the local model (nothing leaves this PC), connect your own server, or add a
                key for Claude or ChatGPT in Settings › AI and privacy.
              </p>
              <div>
                <button class="btn primary" type="button" onClick={() => {
                    openSettings(services, 'ai-privacy');
                  }}>
                  Open AI and privacy settings
                </button>
              </div>
            </section>
          ) : (
            <>
              {confirmClear ? (
                <div class="banner ask-banner" role="alertdialog" aria-label="Clear the conversation">
                  <span class="banner-text">Delete every question and answer about this recording? This can't be undone. The recording and its transcript are not touched.</span>
                  <button class="btn ghost" type="button" onClick={() => {
                      setConfirmClear(false);
                    }}>
                    Keep
                  </button>
                  <button class="btn primary" type="button" onClick={() => void clear()}>
                    Clear
                  </button>
                </div>
              ) : null}
              <ol class="ask-turns" aria-label="Conversation" aria-live="polite">
                {turns.length === 0 && running === null ? (
                  <li class="ask-quiet ask-empty">Ask anything about this recording: what was decided, who said what, what is still open. Answers cite the lines they come from.</li>
                ) : null}
                {turns.map((turn) => (turn.role === 'user' ? <Question key={turn.id} turn={turn} /> : <Answer key={turn.id} turn={turn} onOpen={openLine} />))}
                {running !== null && running.confirm === null ? (
                  <li class="ask-turn ask-answer ask-answer--live">
                    <p class="ask-text">{running.text === '' ? <span class="ask-quiet">Thinking…</span> : running.text}</p>
                    <div class="ask-answer-foot">
                      {running.context === null ? null : <span class="ask-quiet">{contextSummary(running.context)}</span>}
                      <button class="btn ghost" type="button" disabled={running.jobId === null} onClick={() => void stop()}>
                        Stop
                      </button>
                    </div>
                  </li>
                ) : null}
              </ol>
              {running?.confirm == null ? null : <SendConfirm summary={running.confirm} onAnswer={(approved) => void confirm(approved)} />}
              {error === null ? null : (
                <p class="ask-error" role="alert">
                  {error}
                </p>
              )}
              {notice === null ? null : (
                <p class="ask-quiet" role="status">
                  {notice}
                </p>
              )}
              <div ref={endRef} />
              <form
                class="ask-composer"
                onSubmit={(event) => {
                  event.preventDefault();
                  void send();
                }}
              >
                <label class="field-label" for="ask-question">
                  Your question
                </label>
                <textarea
                  id="ask-question"
                  class="ask-input"
                  rows={3}
                  maxLength={4000}
                  value={draft}
                  placeholder="What did we decide about the launch date?"
                  onInput={(event) => {
                    setDraft(event.currentTarget.value);
                  }}
                  onKeyDown={onKeyDown}
                />
                <div class="ask-composer-row">
                  <label class="ask-provider">
                    <span class="field-label">Answer with</span>
                    <select class="field" value={providerId ?? ''} onChange={(event) => {
                        setProviderId(event.currentTarget.value as ProviderInfo['id']);
                      }}>
                      {providers.map((p) => (
                        <option key={p.id} value={p.id} disabled={!p.ready}>
                          {p.ready ? `${p.name}${p.modelLabel === null ? '' : ` · ${p.modelLabel}`}` : `${p.name} (${p.reason ?? 'not ready'})`}
                        </option>
                      ))}
                    </select>
                  </label>
                  <button class="btn primary" type="submit" disabled={draft.trim() === '' || running !== null || provider === null}>
                    Ask
                  </button>
                </div>
                {provider === null ? null : <p class="field-note">{destinationNote(provider)} Enter sends; Shift+Enter starts a new line.</p>}
              </form>
            </>
          )}
        </div>
      </main>
    </>
  );
}

function Question({ turn }: { turn: AskTurn }): JSX.Element {
  return (
    <li class="ask-turn ask-question">
      <p class="ask-text">{turn.text}</p>
    </li>
  );
}

function Answer({ turn, onOpen }: { turn: AskTurn; onOpen: (start: number) => void }): JSX.Element {
  const parts = answerParts(turn.text, turn.citations);
  return (
    <li class="ask-turn ask-answer">
      <p class="ask-text">
        {parts.map((part, index) =>
          part.kind === 'text' ? (
            part.text
          ) : part.citation === null ? (
            <span key={index} class="ask-cite ask-cite--none" title={`Line ${part.line} was not in what the model was given, so it could not be checked`}>
              line {part.line}?
            </span>
          ) : (
            <button
              key={index}
              type="button"
              class="ask-cite"
              title={`Line ${part.line}: open in Review`}
              onClick={() => {
                if (part.citation !== null) {
                  onOpen(part.citation.start);
                }
              }}
            >
              {citationLabel(part.citation)}
            </button>
          ),
        )}
      </p>
      <div class="ask-answer-foot">
        <span class="ask-quiet">
          {turn.providerName ?? 'AI'}
          {turn.stopped === true ? ' · stopped before it finished' : ''}
          {turn.stayedOnPc === true ? ' · stayed on this PC' : ''}
        </span>
      </div>
      {turn.context == null ? null : (
        <details class="ask-sections">
          <summary>{contextSummary(turn.context)}</summary>
          <p class="ask-quiet">{contextExplanation(turn.context)}</p>
          {turn.context.sections.length === 0 ? null : (
            <ul class="ask-section-list">
              {turn.context.sections.map((section) => (
                <li key={section.number}>
                  <button type="button" class="ask-section-open" onClick={() => {
                      onOpen(section.start);
                    }}>
                    {sectionLabel(section)}
                  </button>
                  {section.title === null ? null : <span class="ask-quiet"> · {section.title}</span>}
                  {section.matched ? <span class="pill ask-matched">matched</span> : null}
                </li>
              ))}
            </ul>
          )}
        </details>
      )}
    </li>
  );
}

function SendConfirm({ summary, onAnswer }: { summary: AskSendSummary; onAnswer: (approved: boolean) => void }): JSX.Element {
  return (
    <section class="ask-confirm" role="alertdialog" aria-labelledby="ask-confirm-title">
      <h2 id="ask-confirm-title" class="ask-confirm-title">
        Send this question to {summary.providerName}?
      </h2>
      <p>
        {contextSummary(summary.context).replace('Answered from', 'It goes with')}, {formatSize(summary.bytes)} in all
        {summary.modelLabel === null ? '' : `, to ${summary.modelLabel}`}. Audio and video are never sent. You asked to confirm every send in Settings › AI and privacy.
      </p>
      <div class="ask-confirm-actions">
        <button class="btn ghost" type="button" onClick={() => {
            onAnswer(false);
          }}>
          Don't send
        </button>
        <button class="btn primary" type="button" onClick={() => {
            onAnswer(true);
          }}>
          Send
        </button>
      </div>
    </section>
  );
}
