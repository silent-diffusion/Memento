// Interim Record spoke: enough to start, pause and stop a session with the default sources so the
// session events (state, levels, source loss, footer) can be exercised. The designed Recording
// session screen (DESIGN.md §8) replaces it.
import type { JSX } from 'preact';
import { useState } from 'preact/hooks';
import { SpokeHeader } from '../components/SpokeHeader';
import { StatusFooter } from '../components/StatusFooter';
import { formatTimecode } from '../format/duration';
import { typeName } from '../format/recording';
import { formatClock, parseIso } from '../format/when';
import { goToLibrary } from '../state/actions';
import { useServices } from '../state/context';

export function RecordPlaceholder(): JSX.Element {
  const services = useServices();
  const { bridge, store, router } = services;
  const session = store.recording.value;
  const settings = store.settings.value;
  const [error, setError] = useState<string | null>(null);
  const active = session !== null && (session.state === 'recording' || session.state === 'paused');

  const start = async (): Promise<void> => {
    setError(null);
    try {
      const type = settings?.recording.defaultType ?? 'meeting';
      const sources = (await bridge.call('sources.list')).audio;
      const remembered = settings?.recording.defaultSourceIds ?? [];
      const sourceIds = remembered.filter((id) => sources.some((s) => s.id === id));
      const result = await bridge.call('recording.start', {
        title: `Untitled ${typeName(type).toLocaleLowerCase()}`,
        type,
        sourceIds: sourceIds.length > 0 ? sourceIds : sources.filter((s) => s.isDefault).map((s) => s.id),
      });
      router.navigate({ name: 'record', sessionId: result.sessionId });
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The recording could not start.');
    }
  };

  const stop = async (): Promise<void> => {
    if (session === null) {
      return;
    }
    try {
      const result = await bridge.call('recording.stop', { sessionId: session.sessionId });
      router.navigate({ name: 'review', recordingId: result.recordingId });
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The recording could not be stopped.');
    }
  };

  const togglePause = (): void => {
    if (session === null) {
      return;
    }
    const method = session.state === 'paused' ? 'recording.resume' : 'recording.pause';
    bridge.call(method, { sessionId: session.sessionId }).catch((e: unknown) => {
      setError(e instanceof Error ? e.message : 'The recording did not answer.');
    });
  };

  const liveTracks = session?.tracks.filter((t) => t.endedEarlyAtMs === null).length ?? 0;

  return (
    <>
      <SpokeHeader
        backLabel="Library"
        onBack={() => {
          goToLibrary(services);
        }}
        title={active ? 'Recording' : 'New recording'}
      />
      <main class="spoke-main">
        <div class="placeholder-card placeholder-card--centred">
          <span class="lbl placeholder-state">
            {active ? (
              <>
                <span class={session.state === 'recording' ? 'pulse proc-dot' : 'proc-dot'} aria-hidden="true" />
                {session.state === 'recording' ? 'Recording' : 'Paused'}
              </>
            ) : (
              'Ready to record'
            )}
          </span>
          <span class={active ? 'mono placeholder-timer' : 'mono placeholder-timer placeholder-timer--ready'} role="timer">
            {formatTimecode(active ? session.elapsedMs : 0)}
          </span>
          {active ? (
            <span class="placeholder-text">
              Started {formatClock(parseIso(session.startedAt))} · {liveTracks} audio {liveTracks === 1 ? 'track' : 'tracks'} · audio only
            </span>
          ) : null}
          <div class="placeholder-actions">
            {active ? (
              <>
                <button class="btn ghost small-btn" type="button" onClick={togglePause}>
                  {session.state === 'paused' ? 'Resume' : 'Pause'}
                </button>
                <button
                  class="btn primary placeholder-start"
                  type="button"
                  onClick={() => {
                    void stop();
                  }}
                >
                  Stop
                </button>
              </>
            ) : (
              <button
                class="btn primary placeholder-start"
                type="button"
                onClick={() => {
                  void start();
                }}
              >
                <span class="rec-dot" aria-hidden="true" />
                Start recording
              </button>
            )}
          </div>
          <p class="placeholder-note">
            {active
              ? 'Everything is saved to this PC as it records. Stop opens the recording.'
              : 'Starts the moment you press, with your default sources. The full recording screen arrives in the next update.'}
          </p>
          {error === null ? null : (
            <p class="dialog-error" role="alert">
              {error}
            </p>
          )}
        </div>
      </main>
      <StatusFooter status={store.footer.value} lostAtMs={store.lostSource.value?.atMs ?? null} />
    </>
  );
}
