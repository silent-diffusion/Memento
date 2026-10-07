// Import audio or video (DESIGN.md §6, BRIDGE.md M3 library.importMedia): the host's picker, then
// the new project appears in the processing card as its stages run. A file Windows cannot decode
// is reported inline with what was (not) changed and a way on (DESIGN.md §17).
import type { JSX } from 'preact';
import { reloadLibrary } from '../../state/actions';
import { useServices, type AppServices } from '../../state/context';
import { jobsOf } from '../../state/jobs';

export async function importMedia(services: AppServices): Promise<void> {
  const { bridge, store } = services;
  const jobs = jobsOf(store);
  jobs.importError.value = null;
  try {
    const result = await bridge.call('library.importMedia', {});
    if (result.cancelled || result.recordingId === null) {
      return;
    }
    store.libraryView.value = { ...store.libraryView.value, selectedId: result.recordingId };
    reloadLibrary(services);
  } catch (error) {
    jobs.importError.value = error instanceof Error ? error.message : 'Memento did not answer. Nothing was added to the library.';
  }
}

export function ImportErrorCard(): JSX.Element | null {
  const services = useServices();
  const jobs = jobsOf(services.store);
  const message = jobs.importError.value;
  if (message === null) {
    return null;
  }
  return (
    <div class="import-error" role="alert">
      <span class="import-error-dot" aria-hidden="true" />
      <div class="import-error-text">
        <span class="import-error-lead">The file was not imported</span>
        <span>{message}</span>
      </div>
      <div class="import-error-actions">
        <button
          class="btn g small-btn"
          type="button"
          onClick={() => {
            void importMedia(services);
          }}
        >
          Choose another file
        </button>
        <button
          class="btn toast-quiet"
          type="button"
          onClick={() => {
            jobs.importError.value = null;
          }}
        >
          Dismiss
        </button>
      </div>
    </div>
  );
}
