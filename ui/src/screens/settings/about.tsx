// Settings › General, H1: Updates (current version, last check, Check now, "Install updates automatically",
// Restart to update) and About (version, library path, and the bundled licenses from docs/THIRD-PARTY.md, read
// into the page when the UI is built). Nothing here contacts anything except "Check now", which asks the host.
import type { JSX } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import { THIRD_PARTY } from 'virtual:third-party-licenses';
import type { UpdateStatus } from '../../bridge/types';
import { Toggle } from '../../components/Controls';
import { updateSettings } from '../../state/actions';
import { useServices } from '../../state/context';
import { parseReleaseNotes, type NotesBlock } from './releaseNotes';
import { OnOff, SettingsGroup, SettingsRow } from './SettingsParts';

/** One block of THIRD-PARTY.md: a paragraph, or a table's rows (without the header and separator). */
export type LicenseBlock = { kind: 'text'; text: string } | { kind: 'table'; header: string[]; rows: string[][] };

/** Inline Markdown to plain words: `code` → code, [text](url) → text (url), **bold** → bold. */
function plain(text: string): string {
  return text
    .replace(/\[([^\]]+)\]\(([^)]+)\)/g, '$1 ($2)')
    .replace(/`([^`]*)`/g, '$1')
    .replace(/\*\*([^*]+)\*\*/g, '$1')
    .trim();
}

function cells(line: string): string[] {
  return line
    .trim()
    .replace(/^\|/, '')
    .replace(/\|$/, '')
    .split('|')
    .map((cell) => plain(cell));
}

/** Reads THIRD-PARTY.md into paragraphs and tables; the title line is left out. */
export function parseLicenses(markdown: string): LicenseBlock[] {
  const blocks: LicenseBlock[] = [];
  const lines = markdown.split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i] ?? '';
    if (line.trim() === '' || line.startsWith('# ')) {
      continue;
    }
    if (line.trim().startsWith('|')) {
      const header = cells(line);
      const rows: string[][] = [];
      i++;
      while (i < lines.length && (lines[i] ?? '').trim().startsWith('|')) {
        const row = lines[i] ?? '';
        if (!/^\s*\|[\s:|-]+\|\s*$/.test(row)) {
          rows.push(cells(row));
        }
        i++;
      }
      i--;
      blocks.push({ kind: 'table', header, rows });
      continue;
    }
    blocks.push({ kind: 'text', text: plain(line.replace(/^#+\s*/, '')) });
  }
  return blocks;
}

const LICENSES = parseLicenses(THIRD_PARTY);

function Licenses(): JSX.Element {
  return (
    <div class="about-licenses" id="about-licenses">
      {LICENSES.map((block, i) =>
        block.kind === 'text' ? (
          <p key={i} class="about-licenses-text">
            {block.text}
          </p>
        ) : (
          <ul key={i} class="about-licenses-list">
            {block.rows.map((row, j) => (
              <li key={j}>
                <span class="about-licenses-name">{row[0]}</span>
                {row[1] === undefined ? null : <span class="about-licenses-license"> · {row[1]}</span>}
                {row[2] === undefined ? null : <span class="about-licenses-use"> · {row[2]}</span>}
              </li>
            ))}
          </ul>
        ),
      )}
    </div>
  );
}

function checkedLine(status: UpdateStatus): string {
  if (status.lastCheckedAt === null) {
    return 'Not checked yet in this session.';
  }
  const at = new Date(status.lastCheckedAt);
  return `Last checked ${at.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })}.`;
}

/** The release notes of an available update: read-only plain text, scrollable (2.1.0). */
function ReleaseNotes({ blocks, version }: { blocks: NotesBlock[]; version: string }): JSX.Element {
  return (
    <div class="update-notes" role="region" aria-label={`Release notes for Memento ${version}`} tabIndex={0}>
      {blocks.map((block, i) =>
        block.kind === 'heading' ? (
          <p key={i} class="update-notes-heading">
            {block.text}
          </p>
        ) : block.kind === 'paragraph' ? (
          <p key={i} class="update-notes-text">
            {block.text}
          </p>
        ) : (
          <ul key={i} class="update-notes-list">
            {block.items.map((item, j) => (
              <li key={j}>{item}</li>
            ))}
          </ul>
        ),
      )}
    </div>
  );
}

export function UpdatesGroup(): JSX.Element | null {
  const services = useServices();
  const { bridge, store } = services;
  const settings = store.settings.value;
  const status = store.updates.value;
  const [checking, setChecking] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let live = true;
    bridge
      .call('updates.status')
      .then((result) => {
        if (live) {
          store.updates.value = result;
        }
      })
      .catch(() => undefined);
    return () => {
      live = false;
    };
  }, [bridge, store]);

  if (settings === null) {
    return null;
  }
  const general = settings.general;
  const unavailable = status?.state === 'unavailable';
  const checkNow = (): void => {
    setChecking(true);
    setError(null);
    bridge
      .call('updates.check')
      .then((result) => {
        store.updates.value = result;
      })
      .catch((e: unknown) => {
        setError(e instanceof Error ? e.message : 'Memento did not answer.');
      })
      .finally(() => {
        setChecking(false);
      });
  };
  const restart = (): void => {
    setError(null);
    bridge.call('updates.apply').catch((e: unknown) => {
      setError(e instanceof Error ? e.message : 'Memento did not answer.');
    });
  };
  const updateNow = (): void => {
    setError(null);
    bridge
      .call('updates.download')
      .then((result) => {
        store.updates.value = result;
      })
      .catch((e: unknown) => {
        setError(e instanceof Error ? e.message : 'Memento did not answer.');
      });
  };
  const ready = status?.state === 'ready' && status.availableVersion !== null;
  const available = status?.state === 'available' && status.availableVersion !== null && !status.deferred;
  const downloading = status?.state === 'downloading';
  const message = error ?? status?.message ?? null;
  const failed = error !== null || status?.state === 'failed';
  const notes = available ? parseReleaseNotes(status.releaseNotes) : [];
  return (
    <SettingsGroup label="Updates">
      <SettingsRow
        label="Updates"
        description={
          unavailable
            ? 'This copy of Memento was not installed with Setup, so it cannot update itself.'
            : 'Check now looks for a newer version on GitHub; Update now downloads it, and it installs when you restart.'
        }
        below={
          <>
            {available ? (
              <div class="update-available">
                <p class="settings-inline settings-inline--ok" role="status">
                  Memento {status.availableVersion} is available. Nothing is downloaded until you choose Update now.
                </p>
                {notes.length === 0 ? (
                  <p class="update-notes-empty">This release has no release notes.</p>
                ) : (
                  <ReleaseNotes blocks={notes} version={status.availableVersion ?? ''} />
                )}
              </div>
            ) : null}
            {downloading ? (
              <div class="settings-progress" role="status">
                <span class="settings-progress-label">
                  Downloading Memento {status.availableVersion ?? ''} · {Math.round(status.percent ?? 0)}%
                </span>
                <span class="settings-progress-track" aria-hidden="true">
                  <span class="settings-progress-fill" style={{ width: `${Math.max(0, Math.min(100, status.percent ?? 0))}%` }} />
                </span>
              </div>
            ) : null}
            {status === null || unavailable ? null : (
              <p class="settings-inline settings-inline--quiet" role="status">
                {ready ? `Memento ${status.availableVersion ?? ''} is downloaded and installs when you restart. ` : ''}
                {checkedLine(status)}
              </p>
            )}
            {message === null ? null : (
              <p class={failed ? 'settings-inline' : 'settings-inline settings-inline--ok'} role={failed ? 'alert' : 'status'}>
                {message}
              </p>
            )}
          </>
        }
      >
        <span class="settings-note">Version {status?.currentVersion ?? store.version.value?.version ?? ''}</span>
        {ready ? (
          <button class="btn p" type="button" onClick={restart}>
            Restart to update to {status.availableVersion}
          </button>
        ) : available ? (
          <button class="btn p" type="button" onClick={updateNow}>
            Update now
          </button>
        ) : (
          <button class="btn ghost" type="button" disabled={unavailable || checking || downloading || status?.state === 'checking'} onClick={checkNow}>
            {checking || status?.state === 'checking' ? 'Checking…' : 'Check now'}
          </button>
        )}
      </SettingsRow>
      <SettingsRow
        label="Install updates automatically"
        description="Checks when Memento starts and once a day, never while recording or processing. Off: Memento only checks when you choose Check now."
      >
        <OnOff on={general.autoUpdate} />
        <Toggle
          label="Install updates automatically"
          checked={general.autoUpdate}
          onChange={(autoUpdate) => {
            void updateSettings(services, { general: { ...general, autoUpdate } }).then(setError);
          }}
        />
      </SettingsRow>
    </SettingsGroup>
  );
}

export function AboutGroup(): JSX.Element | null {
  const { store } = useServices();
  const settings = store.settings.value;
  const version = store.version.value;
  const [open, setOpen] = useState(false);
  if (settings === null) {
    return null;
  }
  return (
    <SettingsGroup label="About">
      <SettingsRow label="Version" description={version === null ? 'Memento for Windows.' : `Memento for Windows, running on ${version.osVersion}.`}>
        <span class="settings-note">Memento {version?.version ?? ''}</span>
      </SettingsRow>
      <SettingsRow label="Library" description="Where your recordings, transcripts and settings for each recording are kept.">
        <span class="mono settings-path" title={settings.libraryPath}>
          {settings.libraryPath}
        </span>
      </SettingsRow>
      <SettingsRow
        label="Licenses"
        description="The open-source components inside Memento and the licenses they come with."
        below={open ? <Licenses /> : null}
      >
        <button
          class="btn ghost"
          type="button"
          aria-expanded={open}
          aria-controls="about-licenses"
          onClick={() => {
            setOpen(!open);
          }}
        >
          {open ? 'Hide licenses' : 'Show licenses'}
        </button>
      </SettingsRow>
    </SettingsGroup>
  );
}
