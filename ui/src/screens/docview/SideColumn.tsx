// The viewer's side column (DESIGN.md §12): "How this was made" from the generation record, the
// versions with Restore while version history is on, and the footnote.
import type { JSX } from 'preact';
import type { DocumentSummary, DocumentVersion, GenerationRecord, HistorySettings } from '../../bridge/types';
import { INPUT_PILLS, inputsUsed, PROVIDER_FULL, shortDuration, VERSION_REASONS, whenWords } from '../../format/documents';

interface HowMadeProps {
  summary: DocumentSummary;
  record: GenerationRecord | null;
  styleName: string;
  now: Date;
  onRegenerate: (() => void) | null;
}

export function HowMade({ summary, record, styleName, now, onRegenerate }: HowMadeProps): JSX.Element {
  if (record === null) {
    return (
      <div class="side-card how-made">
        <span class="lbl">How this was made</span>
        <dl class="how-facts">
          <dt>Written</dt>
          <dd>By you, in Memento</dd>
          <dt>Style</dt>
          <dd>{styleName}</dd>
          <dt>Changed</dt>
          <dd>{whenWords(summary.modifiedAt, now)}</dd>
        </dl>
        <span class="how-note">No AI was involved. Nothing was sent anywhere.</span>
      </div>
    );
  }
  const local = record.providerId === 'local';
  const used = inputsUsed(record.inputs);
  return (
    <div class="side-card how-made">
      <span class="lbl">How this was made</span>
      <dl class="how-facts">
        <dt>Template</dt>
        <dd>{record.templateName}</dd>
        <dt>Style</dt>
        <dd>{styleName}</dd>
        <dt>Provider</dt>
        <dd>{local ? `${record.modelLabel} (this PC)` : PROVIDER_FULL[record.providerId]}</dd>
        <dt>Generated</dt>
        <dd>
          {whenWords(record.startedAt, now)} · {shortDuration(record.durationMs)}
        </dd>
      </dl>
      <div class="how-sent">
        <span class="how-note">{local ? 'Read by the local model' : 'Sent to the provider'}</span>
        <div class="pill-row how-pills">
          {used.length === 0 ? (
            <span class="how-note">Only the instructions</span>
          ) : (
            used.map((key) => (
              <span key={key} class="pill done">
                {INPUT_PILLS[key]}
              </span>
            ))
          )}
        </div>
        <span class="how-note">{local ? 'Everything stayed on this PC. Audio and video were not used.' : 'Audio and video were not sent.'}</span>
      </div>
      {onRegenerate === null ? null : (
        <button class="btn ghost side-btn" type="button" onClick={onRegenerate}>
          Change structure and regenerate
        </button>
      )}
    </div>
  );
}

interface VersionsProps {
  history: HistorySettings | null;
  versions: DocumentVersion[] | null;
  now: Date;
  onRestore: (version: DocumentVersion, number: number) => void;
  /** After 1.2.0: a version's title opens it read-only (the current one goes back to it). */
  onOpen?: (version: DocumentVersion, number: number) => void;
  /** The version open read-only, or null. */
  openId?: string | null;
}

/** Newest first, numbered from the oldest (version 1). */
export function numberedVersions(versions: readonly DocumentVersion[]): { version: DocumentVersion; number: number }[] {
  const oldestFirst = [...versions].sort((a, b) => Date.parse(a.at) - Date.parse(b.at));
  return oldestFirst.map((version, i) => ({ version, number: version.version ?? i + 1 })).reverse();
}

export function versionMeta(version: DocumentVersion, now: Date): string {
  const parts = [VERSION_REASONS[version.reason], whenWords(version.at, now).replace(/^Today, /, 'today ').replace(/^Yesterday, /, 'yesterday ')];
  if (version.reason === 'edited' && version.changes > 0) {
    parts.push(`${version.changes} ${version.changes === 1 ? 'change' : 'changes'}`);
  }
  return parts.join(' · ');
}

/** A version's title: a button that opens it when the viewer offers that, else plain text. */
function VersionTitle({ text, version, number, open, onOpen }: { text: string; version: DocumentVersion; number: number; open: boolean; onOpen: VersionsProps['onOpen'] }): JSX.Element {
  if (onOpen === undefined) {
    return <span class="doc-ver-title">{text}</span>;
  }
  return (
    <button
      class="doc-ver-title doc-ver-open"
      type="button"
      aria-pressed={open}
      title={version.id === 'current' ? 'Back to the version you have now' : 'Open this version to read it or restore it'}
      onClick={() => {
        onOpen(version, number);
      }}
    >
      {text}
    </button>
  );
}

export function Versions({ history, versions, now, onRestore, onOpen, openId = null }: VersionsProps): JSX.Element {
  if (history?.keepVersions !== true) {
    return (
      <div class="versions-block">
        <span class="lbl">Versions</span>
        <p class="how-note versions-off">Version history is off, so earlier versions are not kept. Turn it on in Settings › Documents.</p>
      </div>
    );
  }
  const list = versions === null ? null : numberedVersions(versions);
  return (
    <div class="versions-block">
      <div class="versions-head">
        <span class="lbl">Versions</span>
        <span class="how-note">History on · {history.keepDays} days</span>
      </div>
      {list === null ? (
        <span class="how-note">Reading versions…</span>
      ) : (
        list.map(({ version, number }, i) =>
          i === 0 ? (
            <div key={version.id} class="ver cur doc-ver">
              <VersionTitle text={`Version ${number} · current`} version={version} number={number} open={openId === null} onOpen={onOpen} />
              <span class="doc-ver-meta">{versionMeta(version, now)}</span>
            </div>
          ) : (
            <div key={version.id} class={openId === version.id ? 'ver doc-ver doc-ver--open' : 'ver doc-ver'}>
              <div class="doc-ver-row">
                <VersionTitle text={`Version ${number}`} version={version} number={number} open={openId === version.id} onOpen={onOpen} />
                <button
                  class="btn ghost doc-ver-restore"
                  type="button"
                  aria-label={`Restore version ${number}`}
                  onClick={() => {
                    onRestore(version, number);
                  }}
                >
                  Restore
                </button>
              </div>
              <span class="doc-ver-meta">{versionMeta(version, now)}</span>
            </div>
          ),
        )
      )}
    </div>
  );
}
