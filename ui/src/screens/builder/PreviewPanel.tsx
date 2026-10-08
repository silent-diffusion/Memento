// The Builder's right column (DESIGN.md §10.3): Preview | Inputs and output, the generation progress
// card and the §17 "AI provider failed" card in the preview's place, and the notice card.
import type { JSX } from 'preact';
import type { InputSelection, ProviderId, ProviderInfo, Style, TemplateOutput } from '../../bridge/types';
import { Segmented, Toggle } from '../../components/Controls';
import { CheckIcon } from '../../components/icons';
import { moveFocus } from '../../components/keyboard';
import { NEVER_SENT, NeverSentRow } from '../../components/NeverSentRow';
import { AlertIcon } from '../../components/paper/icons';
import { Paper } from '../../components/paper/Paper';
import { INPUT_NAMES } from '../../format/documents';
import type { BuilderTab } from './draft';
import { failureLead, failureWords, progressWords, type ActiveGeneration } from './generation';

const TABS: readonly { id: BuilderTab; label: string }[] = [
  { id: 'preview', label: 'Preview' },
  { id: 'inputs', label: 'Inputs and output' },
];

export interface InputRow {
  key: keyof InputSelection;
  /** The right-hand note: "with speakers", "agenda.docx", "3 marked", "off in Settings". */
  note: string;
  /** Settings › AI and privacy › What may be shared does not allow it. */
  locked: boolean;
}

interface PreviewPanelProps {
  tab: BuilderTab;
  onTab: (tab: BuilderTab) => void;
  documentWord: string;
  previewHtml: string | null;
  previewError: string | null;
  styleName: string;
  paper: string;
  onEditStyle: () => void;
  job: ActiveGeneration | null;
  moduleName: (moduleId: string) => string | null;
  onCancel: () => void;
  /** Opens the Live output sheet (generating and failed states); left out when there is nothing to show. */
  onShowLiveOutput?: (() => void) | undefined;
  onRetry: () => void;
  /** A ready provider other than the one that failed, for "Switch to …". */
  alternative: ProviderInfo | null;
  onSwitch: (provider: ProviderInfo) => void;
  onDismissFailure: () => void;
  inputs: InputSelection;
  inputRows: readonly InputRow[];
  onInputs: (inputs: InputSelection) => void;
  providers: readonly ProviderInfo[] | null;
  externalAiEnabled: boolean;
  providerId: ProviderId | null;
  onProvider: (id: ProviderId) => void;
  onOpenAiSettings: () => void;
  styles: readonly Style[];
  styleId: string;
  onStyle: (styleId: string) => void;
  onEditStyles: () => void;
  output: TemplateOutput;
  onOutput: (output: TemplateOutput) => void;
  /** Without a recording there is nothing to send (a template opened from Settings). */
  canPreviewPayload: boolean;
  onPreviewPayload: () => void;
}

export function ProgressCard({
  job,
  moduleName,
  onCancel,
  onShowLiveOutput,
}: {
  job: ActiveGeneration;
  moduleName: (id: string) => string | null;
  onCancel: () => void;
  onShowLiveOutput?: (() => void) | undefined;
}): JSX.Element {
  const words = progressWords(job, moduleName);
  const percent = Math.max(0, Math.min(100, words.percent));
  return (
    <div class="gen-card" role="status" aria-live="polite">
      <div class="gen-card-head">
        <span class="gen-label">
          <span class="gen-dot pulse" aria-hidden="true" />
          {job.phase === 'starting' ? 'Starting' : job.phase === 'confirm' ? 'Waiting for your answer' : 'Generating'}
        </span>
        <span class="mono gen-percent">{Math.round(percent)}%</span>
      </div>
      <span class="gen-title">{words.title}</span>
      <span class="gen-detail">{words.detail}</span>
      <span class="gen-track" aria-hidden="true">
        <span class="gen-fill" style={{ width: `${percent}%` }} />
      </span>
      <div class={onShowLiveOutput === undefined ? 'gen-actions' : 'gen-actions gen-actions--live'}>
        <button class="btn g small-btn" type="button" onClick={onCancel} disabled={job.jobId === null || job.phase === 'confirm'}>
          Cancel
        </button>
        {onShowLiveOutput === undefined ? null : (
          <button class="btn g small-btn" type="button" aria-haspopup="dialog" onClick={onShowLiveOutput} disabled={job.phase === 'confirm'}>
            Show live output
          </button>
        )}
        <span class="gen-note">{job.provider.kind === 'local' ? 'Running on this PC. Nothing leaves it.' : 'Audio and video are not sent.'}</span>
      </div>
    </div>
  );
}

function FailureCard({
  job,
  alternative,
  onRetry,
  onSwitch,
  onDismiss,
  onShowLiveOutput,
}: {
  job: ActiveGeneration;
  alternative: ProviderInfo | null;
  onRetry: () => void;
  onSwitch: (provider: ProviderInfo) => void;
  onDismiss: () => void;
  onShowLiveOutput?: (() => void) | undefined;
}): JSX.Element {
  const lead = failureLead(job);
  return (
    <div class="ai-failure" role="alert">
      <div class="ai-failure-text">
        <AlertIcon size={18} class="ai-failure-icon" />
        <span>
          <span class="ai-failure-lead">{lead}</span> <span class="ai-failure-body">{failureWords(job.failure ?? '')}</span>
        </span>
      </div>
      <div class="ai-failure-actions">
        <button class="btn g" type="button" onClick={onRetry}>
          Try again
        </button>
        {alternative === null ? null : (
          <button
            class="btn g"
            type="button"
            onClick={() => {
              onSwitch(alternative);
            }}
          >
            Switch to {alternative.kind === 'local' ? 'the local model' : alternative.name}
          </button>
        )}
        {onShowLiveOutput === undefined ? null : (
          <button class="btn g" type="button" aria-haspopup="dialog" onClick={onShowLiveOutput}>
            Show live output
          </button>
        )}
        <button class="btn link-btn ai-failure-dismiss" type="button" onClick={onDismiss}>
          Back to the preview
        </button>
      </div>
    </div>
  );
}

function providerNote(p: ProviderInfo): string {
  if (!p.ready) {
    return `${p.vendor} · ${(p.reason ?? 'not ready').toLocaleLowerCase()}`;
  }
  return p.kind === 'local' ? `${p.modelLabel ?? 'Installed'} · on this PC` : `${p.vendor} · key saved`;
}

export function PreviewPanel(props: PreviewPanelProps): JSX.Element {
  const { tab, job } = props;
  const running = job !== null && (job.phase === 'starting' || job.phase === 'running' || job.phase === 'confirm');
  const failed = job?.phase === 'failed';
  const cloud = (props.providers ?? []).filter((p) => p.kind === 'cloud');
  const local = (props.providers ?? []).filter((p) => p.kind === 'local');
  const shownProviders = props.externalAiEnabled ? [...cloud, ...local] : local;
  return (
    <section class="builder-side" aria-label="Preview and inputs">
      <div
        class="seg-well builder-tabs"
        role="tablist"
        aria-label="Preview or inputs"
        onKeyDown={(event) => {
          const moved = moveFocus(event, event.currentTarget, '[role="tab"]', 'horizontal');
          const id = moved?.dataset.tab as BuilderTab | undefined;
          if (id !== undefined) {
            props.onTab(id);
          }
        }}
      >
        {TABS.map((t) => (
          <button
            key={t.id}
            id={`builder-tab-${t.id}`}
            class={t.id === tab ? 'seg on' : 'seg'}
            type="button"
            role="tab"
            data-tab={t.id}
            aria-selected={t.id === tab}
            aria-controls={`builder-panel-${t.id}`}
            tabIndex={t.id === tab ? 0 : -1}
            onClick={() => {
              props.onTab(t.id);
            }}
          >
            {t.label}
          </button>
        ))}
      </div>

      {running ? <ProgressCard job={job} moduleName={props.moduleName} onCancel={props.onCancel} onShowLiveOutput={props.onShowLiveOutput} /> : null}
      {failed ? (
        <FailureCard job={job} alternative={props.alternative} onRetry={props.onRetry} onSwitch={props.onSwitch} onDismiss={props.onDismissFailure} onShowLiveOutput={props.onShowLiveOutput} />
      ) : null}

      {tab === 'preview' ? (
        <div id="builder-panel-preview" role="tabpanel" aria-labelledby="builder-tab-preview" class="builder-preview">
          <div class="preview-caption">
            <span>
              How the {props.documentWord} will be laid out · <span class="preview-caption-strong">{props.styleName}</span> style · {props.paper}
            </span>
            <button class="btn link-btn preview-edit-style" type="button" onClick={props.onEditStyle}>
              Edit style
            </button>
          </div>
          {props.previewError === null ? (
            <Paper html={props.previewHtml} label={`Preview of the ${props.documentWord} layout`} class="builder-paper" loadingText="Drawing the layout…" />
          ) : (
            <p class="builder-inline-error" role="alert">
              {props.previewError}
            </p>
          )}
          <p class="preview-foot">Grey bars stand for text the AI will write. Headings, order and columns are exactly what you will get.</p>
        </div>
      ) : (
        <div id="builder-panel-inputs" role="tabpanel" aria-labelledby="builder-tab-inputs" class="inputs-card">
          <div class="inputs-block">
            <span class="lbl" id="inputs-receives">
              What the AI receives
            </span>
            <div class="inputs-checks" role="group" aria-labelledby="inputs-receives">
              {props.inputRows.map((row) => (
                <label key={row.key} class={row.locked ? 'input-check input-check--locked' : 'input-check'}>
                  <input
                    class="chk"
                    type="checkbox"
                    checked={!row.locked && props.inputs[row.key]}
                    disabled={row.locked}
                    onChange={() => {
                      props.onInputs({ ...props.inputs, [row.key]: !props.inputs[row.key] });
                    }}
                  />
                  <span class="input-name">{INPUT_NAMES[row.key]}</span>
                  <span class="input-note">{row.note}</span>
                </label>
              ))}
              {NEVER_SENT.map((name) => (
                <NeverSentRow key={name} name={name} rowClass="input-check input-check--locked" nameClass="input-name" noteClass="input-note" />
              ))}
            </div>
          </div>
          <div class="inputs-groove" aria-hidden="true" />
          <div class="inputs-block">
            <span class="lbl" id="inputs-provider">
              Provider
            </span>
            {!props.externalAiEnabled ? (
              <div class="provider-off">
                <span>External AI is off in Settings › AI and privacy, so nothing can be sent to Claude or ChatGPT.</span>
                {local.some((p) => p.ready) ? <span>The local model on this PC can still write the {props.documentWord}.</span> : null}
                <button class="btn link-btn" type="button" onClick={props.onOpenAiSettings}>
                  Open AI and privacy settings
                </button>
              </div>
            ) : null}
            {props.providers === null ? (
              <span class="inputs-note">Checking the providers…</span>
            ) : (
              <div class="providers" role="radiogroup" aria-labelledby="inputs-provider">
                {shownProviders.map((p) => (
                  <label
                    key={p.id}
                    class={['provider', props.providerId === p.id ? 'on' : '', p.ready ? '' : 'provider--off', p.gpuNote === null ? '' : 'provider--detail'].filter((c) => c !== '').join(' ')}
                  >
                    <input
                      class="chk"
                      type="radio"
                      name="provider"
                      checked={props.providerId === p.id}
                      disabled={!p.ready}
                      onChange={() => {
                        props.onProvider(p.id);
                      }}
                    />
                    <span class="provider-name">{p.name}</span>
                    <span class="provider-note">{providerNote(p)}</span>
                    {/* The local model on the processor because another program holds the card: who, how much, the fix. */}
                    {p.gpuNote === null ? null : <span class="provider-detail">{p.gpuNote}</span>}
                  </label>
                ))}
              </div>
            )}
            <span class="inputs-note">
              Keys live in{' '}
              <button class="btn link-btn inline-link" type="button" onClick={props.onOpenAiSettings}>
                Settings › AI and privacy
              </button>
              .
            </span>
          </div>
          <div class="inputs-groove" aria-hidden="true" />
          <div class="inputs-block">
            <div class="inputs-block-head">
              <span class="lbl" id="inputs-style">
                Style
              </span>
              <button class="btn link-btn" type="button" onClick={props.onEditStyles}>
                Edit styles
              </button>
            </div>
            <div class="style-choice">
              <Segmented<string>
                label="Style"
                value={props.styleId}
                options={props.styles.map((s) => ({ value: s.id, label: s.name }))}
                onChange={props.onStyle}
              />
            </div>
            <span class="inputs-note">Same structure, different look. Styles never change what is written.</span>
          </div>
          <div class="inputs-groove" aria-hidden="true" />
          <div class="inputs-block">
            <span class="lbl">Output</span>
            <div class="outputs">
              <div class="output-row">
                <CheckIcon size={16} class="output-ok" />
                <span class="output-name">Saved inside this recording</span>
                <span class="input-note">always</span>
              </div>
              <div class="output-row">
                <Toggle label="Also export as Word document" checked={props.output.alsoExportDocx} onChange={(alsoExportDocx) => { props.onOutput({ ...props.output, alsoExportDocx }); }} />
                <span class="output-name">Also export as Word (.docx)</span>
              </div>
              <div class="output-row">
                <Toggle label="Also export as PDF" checked={props.output.alsoExportPdf === true} onChange={(alsoExportPdf) => { props.onOutput({ ...props.output, alsoExportPdf }); }} />
                <span class="output-name">Also export as PDF</span>
              </div>
              <div class="output-row">
                <Toggle label="Also export as Markdown" checked={props.output.alsoExportMarkdown} onChange={(alsoExportMarkdown) => { props.onOutput({ ...props.output, alsoExportMarkdown }); }} />
                <span class="output-name">Also export as Markdown</span>
              </div>
            </div>
          </div>
        </div>
      )}

      <div class="notice-card">
        <span>Nothing leaves this PC until you press Generate.</span>
        <button class="btn notice-link" type="button" aria-haspopup="dialog" disabled={!props.canPreviewPayload} onClick={props.onPreviewPayload}>
          Preview exactly what will be sent
        </button>
      </div>
    </section>
  );
}

