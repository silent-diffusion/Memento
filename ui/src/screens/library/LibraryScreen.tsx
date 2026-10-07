import type { JSX } from 'preact';
import { useLayoutEffect } from 'preact/hooks';
import type { LibrarySort, RecordingSummary, RecordingType } from '../../bridge/types';
import { BannerSlot } from '../../components/Banners';
import { ChipGroup } from '../../components/Controls';
import { EmptyLibrary } from '../../components/EmptyLibrary';
import { GridIcon, ListIcon } from '../../components/icons';
import { LibraryHeader, SEARCH_PLACEHOLDER, SEARCH_PLACEHOLDER_EMPTY } from '../../components/LibraryHeader';
import { SelectMenu } from '../../components/Menus';
import { StatusFooter } from '../../components/StatusFooter';
import { retryRemedy, stageName, summaryLine } from '../../format/recording';
import { openRecord, openRecording, openSettings, reloadLibrary, requestDelete } from '../../state/actions';
import { useServices, type AppServices } from '../../state/context';
import { updateLibraryView } from '../../state/data';
import {
  filterChips,
  groupRecordings,
  isNarrowed,
  SORT_LABELS,
  SORT_OPTIONS,
  type LibraryViewAction,
} from '../../state/libraryView';
import { ImportErrorCard, importMedia } from './ImportMedia';
import { ProcessingCard } from './ProcessingCard';
import { RecordingCard, RecordingRow, type ItemHandlers } from './RecordingItems';

function itemHandlers(services: AppServices): ItemHandlers {
  const { store } = services;
  const select = (recording: RecordingSummary): void => {
    store.libraryView.value = { ...store.libraryView.value, selectedId: recording.id };
  };
  return {
    open: (recording) => {
      openRecording(services, recording.id);
    },
    rename: (recording) => {
      select(recording);
      store.dialog.value = { kind: 'rename', recordingId: recording.id, title: recording.title };
    },
    changeType: (recording) => {
      select(recording);
      store.dialog.value = { kind: 'changeType', recordingId: recording.id, title: recording.title, type: recording.type };
    },
    remove: (recording) => {
      void requestDelete(services, recording.id);
    },
    retry: (recording, stage) => {
      select(recording);
      const remedyId = retryRemedy(recording.stages.find((s) => s.stage === stage));
      services.bridge.call('processing.retry', { recordingId: recording.id, stage, ...(remedyId === undefined ? {} : { remedyId }) }).catch((error: unknown) => {
        store.toasts.show({
          tone: 'warning',
          title: remedyId === 'importAgain' ? 'The file was not imported again' : `${stageName(stage)} was not retried`,
          body: `${error instanceof Error ? error.message : 'Memento did not answer.'} The recording itself is safe.`,
        });
      });
    },
  };
}

/** Returning from a spoke: put the scroll offset back and focus the row that was opened. */
function useRestoreLibraryPosition(services: AppServices): void {
  useLayoutEffect(() => {
    const view = services.store.libraryView.value;
    if (view.selectedId !== null) {
      const row = document.querySelector<HTMLElement>(`[data-recording-id="${CSS.escape(view.selectedId)}"]`);
      row?.focus({ preventScroll: true });
    }
    if (view.scrollY > 0) {
      window.scrollTo(0, view.scrollY);
    }
  }, [services]);
}

/** The Library hub (DESIGN.md §3, §4, §6, §16). */
export function LibraryScreen(): JSX.Element {
  const services = useServices();
  const { bridge, store } = services;
  const library = store.library.value;
  const result = store.libraryResult.value ?? library;
  const view = store.libraryView.value;
  const loadError = store.loadError.value;
  const settings = store.settings.value;
  const isEmpty = library !== null && library.totalCount === 0 && library.recordings.length === 0;
  const now = services.now();

  useRestoreLibraryPosition(services);

  const dispatch = (action: LibraryViewAction): void => {
    updateLibraryView(bridge, store, action);
  };

  const header = (
    <LibraryHeader
      searchDisabled={library === null || isEmpty}
      searchPlaceholder={isEmpty ? SEARCH_PLACEHOLDER_EMPTY : SEARCH_PLACEHOLDER}
      query={view.query}
      onSearch={(query) => {
        dispatch({ type: 'search', query });
      }}
      onNewRecording={() => {
        openRecord(services);
      }}
      onOpenSettings={() => {
        openSettings(services);
      }}
      recordingActive={store.recording.value?.state === 'recording' || store.recording.value?.state === 'paused'}
      onImport={() => {
        void importMedia(services);
      }}
    />
  );
  const footer = <StatusFooter status={store.footer.value} lostAtMs={store.lostSource.value?.atMs ?? null} />;

  if (loadError !== null || library === null || isEmpty) {
    return (
      <>
        {header}
        <main class="library-main library-main--centred">
          <BannerSlot />
          {loadError !== null ? (
            <div class="load-error" role="alert">
              <p class="load-error-text">The library could not be shown. {loadError}</p>
              <button
                class="btn ghost load-error-retry"
                type="button"
                onClick={() => {
                  store.loadError.value = null;
                  reloadLibrary(services);
                }}
              >
                Try again
              </button>
            </div>
          ) : isEmpty ? (
            <EmptyLibrary
              onStartRecording={() => {
                openRecord(services);
              }}
              onImport={() => {
                void importMedia(services);
              }}
              notice={<ImportErrorCard />}
            />
          ) : null}
        </main>
        {footer}
      </>
    );
  }

  const recordings = result?.recordings ?? [];
  const narrowed = isNarrowed(view);
  const groups = groupRecordings(recordings, view.sort, now);
  const handlers = itemHandlers(services);
  const processing = store.processing.value;
  const showProcessing = view.layout === 'list' && !narrowed && processing?.current != null;
  const compact = settings?.listDensity === 'compact';

  return (
    <>
      {header}
      <main class="library-main">
        <div class="library-column">
          <BannerSlot />
          <ImportErrorCard />

          <div class="lib-heading">
            <div class="lib-heading-text">
              <h1 class="lib-title">Library</h1>
              <p class="lib-summary" aria-live="polite">
                {summaryLine(result?.totalCount ?? recordings.length, result?.totalDurationMs ?? 0)}
              </p>
            </div>
            <div class="lib-heading-actions">
              <SelectMenu<LibrarySort>
                label="Sort"
                variant="sort"
                value={view.sort}
                options={SORT_OPTIONS}
                buttonText={SORT_LABELS[view.sort]}
                onChange={(sort) => {
                  dispatch({ type: 'sort', sort });
                }}
              />
              <button
                class={view.layout === 'list' ? 'icon-btn view-toggle on' : 'icon-btn view-toggle'}
                type="button"
                aria-label="List view"
                aria-pressed={view.layout === 'list'}
                onClick={() => {
                  dispatch({ type: 'layout', layout: 'list' });
                }}
              >
                <ListIcon size={16} />
              </button>
              <button
                class={view.layout === 'grid' ? 'icon-btn view-toggle on' : 'icon-btn view-toggle'}
                type="button"
                aria-label="Grid view"
                aria-pressed={view.layout === 'grid'}
                onClick={() => {
                  dispatch({ type: 'layout', layout: 'grid' });
                }}
              >
                <GridIcon size={16} />
              </button>
            </div>
          </div>

          {showProcessing ? (
            <ProcessingCard
              processing={processing}
              now={now}
              onOpen={(recordingId) => {
                openRecording(services, recordingId);
              }}
            />
          ) : null}

          <ChipGroup<RecordingType | 'all'>
            label="Filter by type"
            value={view.type}
            chips={filterChips(library.recordings)}
            onChange={(recordingType) => {
              dispatch({ type: 'filter', recordingType });
            }}
          />

          {groups.length === 0 ? (
            <div class="no-match">No recordings match. Try another type or clear the search.</div>
          ) : (
            groups.map((group) => (
              <section key={group.key} class={view.layout === 'grid' ? 'lib-group lib-group--grid' : 'lib-group'} aria-labelledby={`group-${group.key}`}>
                <h2 id={`group-${group.key}`} class="lib-group-label">
                  {group.label}
                </h2>
                {view.layout === 'grid' ? (
                  <ul class="lib-grid">
                    {group.items.map((recording) => (
                      <RecordingCard
                        key={recording.id}
                        recording={recording}
                        selected={view.selectedId === recording.id}
                        now={now}
                        handlers={handlers}
                        query={view.query}
                      />
                    ))}
                  </ul>
                ) : (
                  <ul class={compact ? 'lib-list lib-list--compact' : 'lib-list'}>
                    {group.items.map((recording) => (
                      <RecordingRow
                        key={recording.id}
                        recording={recording}
                        selected={view.selectedId === recording.id}
                        now={now}
                        handlers={handlers}
                        query={view.query}
                      />
                    ))}
                  </ul>
                )}
              </section>
            ))
          )}
        </div>
      </main>
      {footer}
    </>
  );
}
