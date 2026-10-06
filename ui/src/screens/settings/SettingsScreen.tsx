import type { JSX } from 'preact';
import { BannerSlot } from '../../components/Banners';
import { PathIcon, SETTINGS_ICON_PATHS } from '../../components/icons';
import { moveFocus } from '../../components/keyboard';
import { SpokeHeader } from '../../components/SpokeHeader';
import { goToLibrary } from '../../state/actions';
import { useServices } from '../../state/context';
import { SETTINGS_SECTIONS, type SettingsSection } from '../../state/router';
import { GeneralSection, RecordingSection, StorageSection } from './sections';
import { LaterCard } from './SettingsParts';

interface SectionInfo {
  label: string;
  blurb: string;
  Body: () => JSX.Element;
}

/** DESIGN.md §11 sections in order. Those not built yet say so with one card. */
export const SECTIONS: Record<SettingsSection, SectionInfo> = {
  general: { label: 'General', blurb: 'Appearance, startup and where your library lives.', Body: GeneralSection },
  recording: { label: 'Recording', blurb: 'What a new recording captures and how it is kept safe.', Body: RecordingSection },
  transcription: { label: 'Transcription', blurb: 'Runs on this PC. Nothing is uploaded.', Body: LaterCard },
  speakers: { label: 'Speakers', blurb: 'Who said what, worked out locally.', Body: LaterCard },
  'ai-privacy': {
    label: 'AI and privacy',
    blurb: 'Optional. Memento records, transcribes and exports without any of this.',
    Body: LaterCard,
  },
  documents: { label: 'Documents', blurb: 'Templates, styles and version history for generated documents.', Body: LaterCard },
  export: { label: 'Export', blurb: 'Copies saved outside Memento. The project inside Memento stays the original.', Body: LaterCard },
  storage: { label: 'Storage and history', blurb: 'How much space the library uses and how to get some back.', Body: StorageSection },
};

/** Settings spoke (DESIGN.md §11, renders/Settings.dc.html). */
export function SettingsScreen({ section }: { section: SettingsSection }): JSX.Element {
  const services = useServices();
  const { store, router } = services;
  const info = SECTIONS[section];
  const version = store.version.value?.version;
  const path = store.settings.value?.libraryPath;
  return (
    <>
      <SpokeHeader
        backLabel="Library"
        onBack={() => {
          goToLibrary(services);
        }}
        title="Settings"
      />
      <main class="settings-main">
        <div class="settings-layout">
          <nav
            class="settings-nav"
            aria-label="Settings sections"
            onKeyDown={(event) => {
              moveFocus(event, event.currentTarget, '.nav', 'vertical');
            }}
          >
            {SETTINGS_SECTIONS.map((key) => (
              <button
                key={key}
                class={key === section ? 'nav on' : 'nav'}
                type="button"
                aria-current={key === section ? 'page' : undefined}
                onClick={() => {
                  router.navigate({ name: 'settings', section: key });
                }}
              >
                <PathIcon d={SETTINGS_ICON_PATHS[key]} size={18} />
                {SECTIONS[key].label}
              </button>
            ))}
          </nav>
          <section class="settings-content" aria-labelledby="settings-section-title">
            <BannerSlot />
            <div class="settings-heading">
              <h1 id="settings-section-title" class="settings-title">
                {info.label}
              </h1>
              <p class="settings-blurb">{info.blurb}</p>
            </div>
            <info.Body key={section} />
          </section>
        </div>
      </main>
      <footer class="app-footer">
        <span>Changes save as you make them.</span>
        <span>
          {version === undefined ? 'Memento' : `Memento ${version}`}
          {path === undefined ? '' : ` · library at ${path}`}
        </span>
      </footer>
    </>
  );
}
