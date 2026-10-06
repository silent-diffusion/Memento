// Stand-in for the Review spoke until it is built: the recording's title and meta with a way back.
import type { JSX } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import type { Project } from '../bridge/types';
import { SpokeHeader } from '../components/SpokeHeader';
import { formatDuration } from '../format/duration';
import { peopleWording, typeName } from '../format/recording';
import { formatWhen, parseIso } from '../format/when';
import { goToLibrary } from '../state/actions';
import { useServices } from '../state/context';

export function ReviewPlaceholder({ recordingId }: { recordingId: string }): JSX.Element {
  const services = useServices();
  const { bridge, store } = services;
  const known = store.library.value?.recordings.find((r) => r.id === recordingId) ?? null;
  const [project, setProject] = useState<Project | null>(null);
  const [error, setError] = useState<string | null>(null);

  // A just-stopped recording joins the library after finalizing; read it again once it is listed.
  const listed = known !== null;
  useEffect(() => {
    let live = true;
    setError(null);
    bridge
      .call('project.get', { recordingId })
      .then((p) => {
        if (live) {
          setProject(p);
        }
      })
      .catch((e: unknown) => {
        if (live) {
          setError(e instanceof Error ? e.message : 'The recording could not be opened.');
        }
      });
    return () => {
      live = false;
    };
  }, [bridge, recordingId, listed]);

  const summary = project?.summary ?? known;
  const meta =
    summary === null
      ? undefined
      : `${typeName(summary.type)} · ${formatWhen(parseIso(summary.createdAt), services.now())} · ${formatDuration(summary.durationMs)} · ${peopleWording(summary.participantCount)} · ${summary.hasVideo ? 'audio and video' : 'audio only'}`;

  return (
    <>
      <SpokeHeader
        backLabel="Library"
        onBack={() => {
          goToLibrary(services);
        }}
        title={summary?.title ?? 'Recording'}
        {...(meta === undefined ? {} : { meta })}
      />
      <main class="spoke-main">
        <div class="placeholder-card">
          {error !== null ? (
            <p class="placeholder-text" role="alert">
              {error}
            </p>
          ) : (
            <>
              <h1 class="placeholder-title">{summary?.title ?? 'Opening the recording'}</h1>
              <p class="placeholder-text">
                The player, chapters, highlights and history for this recording open here in the next update.
                {project === null ? '' : ` ${project.tracks.length} ${project.tracks.length === 1 ? 'track is' : 'tracks are'} stored on this PC.`}
              </p>
            </>
          )}
        </div>
      </main>
    </>
  );
}
