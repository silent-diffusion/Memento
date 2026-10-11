// "Preview exactly what will be sent" as a full-height read-only sheet (2.0, DESIGN.md §10.6), built
// like the Live output sheet (§10.5): on the left an overview and every request in pipeline order (Map,
// then Verify), with a search field that narrows them to the ones holding the words; on the right the
// overview (provider and model, "Ask before every send", each input ticked with its size, what is held
// back) or the selected request's exact system prompt and user turn. Copies go through the host's
// clipboard (generation.copyPreview). Nothing is sent from here.
import type { JSX } from 'preact';
import { createPortal } from 'preact/compat';
import { useState } from 'preact/hooks';
import type { GenerationPreviewResult, PreviewInput, PreviewPass, ProviderInfo } from '../../bridge/types';
import { CloseIcon, LockIcon, SearchIcon } from '../../components/icons';
import { moveFocus } from '../../components/keyboard';
import { useModal } from '../../components/Overlay';
import { payloadSize, PROVIDER_FULL } from '../../format/documents';
import { formatSize } from '../../format/storage';
import './liveoutput.css';
import './previewsheet.css';

/** The overview's id in the list (passes are p1, p2…). */
export const OVERVIEW = 'overview';

const count = (n: number): string => n.toLocaleString('en-US');

/** "4,210 characters · 4.2 KB · about 1,100 tokens". */
export function passSize(pass: PreviewPass): string {
  return `${count(pass.characters)} characters · ${formatSize(pass.bytes)} · about ${count(pass.tokens)} tokens`;
}

/** The input's state in words: "Sent", "Not ticked", "Held back", "Nothing to send". */
export function inputState(input: PreviewInput): string {
  if (input.included) {
    return 'Sent';
  }
  if (!input.ticked) {
    return 'Not ticked';
  }
  return input.note?.startsWith('Held back') === true ? 'Held back' : 'Nothing to send';
}

/** The passes holding every word of the query (case-insensitive), in their order. */
export function matchingPasses(passes: readonly PreviewPass[], query: string): PreviewPass[] {
  const words = query.toLocaleLowerCase().split(/\s+/).filter((w) => w !== '');
  if (words.length === 0) {
    return [...passes];
  }
  return passes.filter((p) => {
    const text = `${p.title}\n${p.system}\n${p.user}`.toLocaleLowerCase();
    return words.every((w) => text.includes(w));
  });
}

/** How many times the query's words occur in a text. */
export function hits(text: string, query: string): number {
  const words = query.toLocaleLowerCase().split(/\s+/).filter((w) => w !== '');
  const lower = text.toLocaleLowerCase();
  let total = 0;
  for (const word of words) {
    for (let at = lower.indexOf(word); at >= 0; at = lower.indexOf(word, at + word.length)) {
      total += 1;
    }
  }
  return total;
}

/** The text with the query's words marked. */
function Highlighted({ text, query }: { text: string; query: string }): JSX.Element {
  const words = query.toLocaleLowerCase().split(/\s+/).filter((w) => w !== '');
  if (words.length === 0) {
    return <>{text}</>;
  }
  const escaped = words.map((w) => w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
  const parts = text.split(new RegExp(`(${escaped.join('|')})`, 'gi'));
  return <>{parts.map((part, i) => (i % 2 === 1 ? <mark key={i} class="preview-mark">{part}</mark> : part))}</>;
}

interface PreviewSheetProps {
  provider: ProviderInfo;
  /** Null while generation.preview answers. */
  preview: GenerationPreviewResult | null;
  error: string | null;
  /** Copies the whole preview (null) or one pass through the host; resolves with the words for the status line. */
  onCopy: (passId: string | null) => Promise<string>;
  onClose: () => void;
}

/** The provider the preview was composed for (the host's answer), else the Builder's choice while it loads. */
export function previewProvider(chosen: ProviderInfo, preview: GenerationPreviewResult | null): ProviderInfo {
  const host = preview?.provider;
  return host == null ? chosen : { ...chosen, id: host.id, name: host.name, kind: host.kind, modelLabel: host.modelLabel ?? chosen.modelLabel };
}

export function PreviewSheet({ provider: chosen, preview, error, onCopy, onClose }: PreviewSheetProps): JSX.Element {
  const provider = previewProvider(chosen, preview);
  const { ref, onKeyDown } = useModal(onClose);
  const [selectedId, setSelectedId] = useState<string>(OVERVIEW);
  const [query, setQuery] = useState('');
  const [status, setStatus] = useState('');
  const [copying, setCopying] = useState(false);
  const local = provider.kind === 'local';
  const passes = preview?.passes ?? [];
  const shown = matchingPasses(passes, query);
  const selected = selectedId === OVERVIEW ? null : (passes.find((p) => p.id === selectedId) ?? null);
  const searching = query.trim() !== '';
  const groups = [
    { step: 'map' as const, label: 'Map', passes: shown.filter((p) => p.step === 'map') },
    { step: 'verify' as const, label: 'Verify', passes: shown.filter((p) => p.step === 'verify') },
  ].filter((g) => g.passes.length > 0);

  const copy = (passId: string | null): void => {
    setCopying(true);
    onCopy(passId)
      .then((words) => {
        setStatus(words);
      })
      .catch(() => {
        setStatus('');
      })
      .finally(() => {
        setCopying(false);
      });
  };

  const sub = local
    ? `${provider.modelLabel ?? 'The local model'} on this PC · nothing leaves it`
    : `${provider.name}${provider.modelLabel === null ? '' : ` · ${provider.modelLabel}`} · Ask before every send ${preview?.askBeforeSend === false ? 'off' : 'on'}`;

  return createPortal(
    <div class="scrim scrim--sheet">
      <div
        ref={ref}
        class="sheet live-sheet preview-sheet"
        role="dialog"
        aria-modal="true"
        aria-labelledby="preview-title"
        aria-describedby="preview-sub"
        tabIndex={-1}
        onKeyDown={onKeyDown}
      >
        <div class="sheet-header live-header">
          <div class="live-heading">
            <h2 id="preview-title" class="sheet-title">
              {local ? 'What the local model reads' : 'What will be sent'}
            </h2>
            <span id="preview-sub" class="live-sub">
              {sub}
            </span>
          </div>
          <button class="icon-btn sheet-close" type="button" aria-label="Close" onClick={onClose}>
            <CloseIcon size={18} />
          </button>
        </div>
        {error !== null ? (
          <div class="preview-state">
            <p class="dialog-error" role="alert">
              {error}
            </p>
          </div>
        ) : preview === null ? (
          <div class="preview-state">
            <p class="payload-loading" role="status">
              Putting the requests together on this PC…
            </p>
          </div>
        ) : (
          <div class="live-body">
            <div class="live-passes preview-list">
              <div class="preview-search">
                <label class="sr" for="preview-search">
                  Search the requests
                </label>
                <SearchIcon size={16} class="preview-search-icon" />
                <input
                  id="preview-search"
                  class="field preview-field"
                  type="search"
                  placeholder="Search the requests"
                  autocomplete="off"
                  value={query}
                  onInput={(event) => {
                    setQuery(event.currentTarget.value);
                  }}
                  onKeyDown={(event) => {
                    if (event.key === 'ArrowDown') {
                      event.preventDefault();
                      event.currentTarget.closest('.preview-list')?.querySelector<HTMLElement>('.live-pass')?.focus();
                    }
                  }}
                />
              </div>
              <p class="preview-count" role="status">
                {searching ? `${count(shown.length)} of ${count(passes.length)} requests hold “${query.trim()}”` : `${count(passes.length)} ${passes.length === 1 ? 'request' : 'requests'}`}
              </p>
              <nav
                class="preview-nav"
                aria-label="Overview and requests"
                onKeyDown={(event) => {
                  const moved = moveFocus(event, event.currentTarget, '.live-pass', 'vertical');
                  const id = moved?.dataset.pass;
                  if (id !== undefined) {
                    setSelectedId(id);
                  }
                }}
              >
                <section class="live-group" aria-label="Overview">
                  <ol class="live-list">
                    <li>
                      <button
                        class={selected === null ? 'live-pass on' : 'live-pass'}
                        type="button"
                        data-pass={OVERVIEW}
                        aria-current={selected === null ? 'true' : undefined}
                        tabIndex={selected === null ? 0 : -1}
                        onClick={() => {
                          setSelectedId(OVERVIEW);
                        }}
                      >
                        <span class="live-pass-text">
                          <span class="live-pass-title">Inputs and what is held back</span>
                          <span class="live-pass-meta">{payloadSize(preview.bytes, preview.chunks)}</span>
                        </span>
                      </button>
                    </li>
                  </ol>
                </section>
                {groups.map((group) => (
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
                              tabIndex={current ? 0 : -1}
                              onClick={() => {
                                setSelectedId(pass.id);
                              }}
                            >
                              <span class="live-pass-text">
                                <span class="live-pass-title">{pass.title}</span>
                                <span class="live-pass-meta">
                                  {pass.exact ? `${count(pass.characters)} characters` : 'An illustration'}
                                  {searching ? ` · ${count(hits(`${pass.system}\n${pass.user}`, query))} matches` : ''}
                                </span>
                              </span>
                            </button>
                          </li>
                        );
                      })}
                    </ol>
                  </section>
                ))}
                {searching && shown.length === 0 ? <p class="live-empty">No request holds these words.</p> : null}
              </nav>
            </div>
            {selected === null ? <Overview preview={preview} provider={provider} /> : <PassPane pass={selected} provider={provider} query={query} />}
          </div>
        )}
        <div class="sheet-footer preview-footer">
          <span class="sheet-footer-note">
            {local ? 'Read on this PC; nothing leaves it. Read only: change the inputs in the builder.' : `Nothing is sent until you press Generate. Audio and video are never sent.`}
          </span>
          <span class="sr" role="status" aria-live="polite">
            {status}
          </span>
          {status === '' ? null : <span class="preview-copied">{status}</span>}
          {preview === null || error !== null ? null : (
            <>
              {selected === null ? null : (
                <button
                  class="btn g"
                  type="button"
                  disabled={copying}
                  onClick={() => {
                    copy(selected.id);
                  }}
                >
                  Copy this request
                </button>
              )}
              <button
                class="btn g"
                type="button"
                disabled={copying}
                onClick={() => {
                  copy(null);
                }}
              >
                Copy everything
              </button>
            </>
          )}
          <button class="btn p" type="button" onClick={onClose}>
            Done
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}

function Overview({ preview, provider }: { preview: GenerationPreviewResult; provider: ProviderInfo }): JSX.Element {
  const local = provider.kind === 'local';
  const inputs = preview.inputs ?? [];
  const redactions = preview.redactions ?? [];
  const map = (preview.passes ?? []).filter((p) => p.step === 'map').length;
  return (
    <section class="live-pane preview-overview" aria-label="Inputs and what is held back">
      <div class="preview-scroll" tabIndex={0} aria-label="Overview">
        <dl class="payload-facts preview-facts">
          <dt>{local ? 'Read by' : 'Sent to'}</dt>
          <dd>
            {local ? `${provider.modelLabel ?? 'The local model'} on this PC` : PROVIDER_FULL[provider.id]}
            {!local && preview.provider?.modelLabel != null ? ` · ${preview.provider.modelLabel}` : ''}
          </dd>
          <dt>Ask before every send</dt>
          <dd>{local ? 'Not needed: nothing leaves this PC' : preview.askBeforeSend === false ? 'Off: Generate sends at once' : 'On: you confirm before anything is sent'}</dd>
          <dt>Requests</dt>
          <dd>
            {map === 0 ? 'None: nothing in this template is written by AI' : `${count(map)} to read the transcript, then the checks built from their answers`}
          </dd>
          <dt>Size</dt>
          <dd class="mono">{payloadSize(preview.bytes, preview.chunks)}</dd>
        </dl>
        <h3 class="lbl preview-heading">Inputs</h3>
        <table class="preview-inputs">
          <thead>
            <tr>
              <th scope="col">Input</th>
              <th scope="col">State</th>
              <th scope="col">Size</th>
            </tr>
          </thead>
          <tbody>
            {inputs.map((input) => (
              <tr key={input.key} class={input.included ? 'preview-input preview-input--sent' : 'preview-input'} data-input={input.key}>
                <th scope="row">
                  {input.label}
                  {input.summary === null ? null : <span class="preview-input-note">{input.summary}</span>}
                  {input.note === null ? null : <span class="preview-input-note">{input.note}</span>}
                </th>
                <td>{inputState(input)}</td>
                <td class="mono">{input.included ? `${count(input.characters)} characters · ${formatSize(input.bytes)}` : '—'}</td>
              </tr>
            ))}
            {['Audio', 'Video'].map((name) => (
              <tr key={name} class="preview-input preview-input--never" aria-disabled="true">
                <th scope="row">
                  <LockIcon size={12} /> {name}
                </th>
                <td>Never sent</td>
                <td class="mono">—</td>
              </tr>
            ))}
          </tbody>
        </table>
        <h3 class="lbl preview-heading">Held back or changed</h3>
        {redactions.length === 0 ? (
          <p class="preview-none">Nothing beyond the inputs that are not ticked.</p>
        ) : (
          <ul class="preview-redactions">
            {redactions.map((r) => (
              <li key={r}>{r}</li>
            ))}
          </ul>
        )}
        {preview.warnings.length === 0 ? null : (
          <ul class="payload-warnings">
            {preview.warnings.map((w) => (
              <li key={w}>{w}</li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}

function PassPane({ pass, provider, query }: { pass: PreviewPass; provider: ProviderInfo; query: string }): JSX.Element {
  const local = provider.kind === 'local';
  return (
    <section class="live-pane preview-pass" aria-label={pass.title}>
      <div class="live-pane-head">
        <div class="live-pane-title">
          <h3 class="live-title">{pass.title}</h3>
          <span class="live-stats mono">{passSize(pass)}</span>
        </div>
      </div>
      {pass.exact ? null : (
        <p class="preview-illustration">An illustration: each claim and the lines it cites come from the answers, so this request is put together while generating.</p>
      )}
      <div class="live-label-row">
        <span class="lbl" id="preview-system-label">
          System prompt
        </span>
      </div>
      <pre class="live-well live-request mono" tabIndex={0} aria-labelledby="preview-system-label">
        <Highlighted text={pass.system} query={query} />
      </pre>
      <div class="live-label-row">
        <span class="lbl" id="preview-user-label">
          {local ? 'Request · read by the local model' : `Request · sent to ${provider.name}`}
        </span>
      </div>
      <pre class="live-well mono" tabIndex={0} aria-labelledby="preview-user-label">
        <Highlighted text={pass.user} query={query} />
      </pre>
    </section>
  );
}
