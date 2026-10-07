import type { JSX } from 'preact';
import { MoreIcon, SearchIcon, SettingsIcon } from './icons';
import { ActionMenu } from './Menus';

export const SEARCH_PLACEHOLDER = 'Search recordings, transcripts, people';
export const SEARCH_PLACEHOLDER_EMPTY = 'Search will work once you have a recording';

interface LibraryHeaderProps {
  /** True while the library is empty (or not yet loaded): search has nothing to search. */
  searchDisabled: boolean;
  searchPlaceholder: string;
  query: string;
  onSearch: (query: string) => void;
  onNewRecording: () => void;
  onOpenSettings: () => void;
  /** While a recording is running, the primary button returns to it. */
  recordingActive?: boolean;
  /** M3: More › Import audio or video. */
  onImport?: () => void;
}

/** The 60 px Library header (DESIGN.md §3): wordmark, search, New recording, Settings. */
export function LibraryHeader(props: LibraryHeaderProps): JSX.Element {
  return (
    <header class="app-header">
      <div class="wordmark">
        <span class="wordmark-ring" aria-hidden="true">
          <span class="wordmark-dot" />
        </span>
        <span class="wordmark-name">Memento</span>
      </div>
      <div class="header-search">
        <div class="search-wrap">
          <label class="sr" for="lib-search">
            Search recordings
          </label>
          <SearchIcon size={18} class="search-icon" />
          <input
            id="lib-search"
            class="search"
            type="search"
            autocomplete="off"
            spellcheck={false}
            disabled={props.searchDisabled}
            placeholder={props.searchPlaceholder}
            value={props.query}
            onInput={(event) => {
              props.onSearch(event.currentTarget.value);
            }}
          />
        </div>
      </div>
      <div class="header-actions">
        <button class="btn primary new-recording" type="button" onClick={props.onNewRecording}>
          <span class="rec-dot" aria-hidden="true" />
          {props.recordingActive === true ? 'Back to recording' : 'New recording'}
        </button>
        {props.onImport === undefined ? null : (
          <ActionMenu
            label="More library actions"
            triggerClass="icon-btn header-icon-btn"
            actions={[{ label: 'Import audio or video…', run: props.onImport }]}
          >
            <MoreIcon size={20} />
          </ActionMenu>
        )}
        <button class="icon-btn header-icon-btn" type="button" aria-label="Settings" onClick={props.onOpenSettings}>
          <SettingsIcon size={20} />
        </button>
      </div>
    </header>
  );
}
