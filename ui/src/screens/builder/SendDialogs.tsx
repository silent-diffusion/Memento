// What will be sent (DESIGN.md §18: a read-only sheet listing each input with its size) and the
// "ask before every send" confirmation (§5.19: provider, inputs, size and chunk count).
import type { JSX } from 'preact';
import type { GenerationPreviewResult, GenerationSendSummary, InputSelection, ProviderInfo } from '../../bridge/types';
import { Dialog, SideSheet } from '../../components/Overlay';
import { INPUT_PILLS, inputsUsed, payloadSize, PROVIDER_FULL } from '../../format/documents';

function InputPills({ inputs }: { inputs: InputSelection }): JSX.Element {
  const used = inputsUsed(inputs);
  return (
    <div class="pill-row send-pills">
      {used.length === 0 ? <span class="send-none">Only the instructions</span> : used.map((key) => <span key={key} class="pill done">{INPUT_PILLS[key]}</span>)}
    </div>
  );
}

interface PayloadSheetProps {
  provider: ProviderInfo;
  /** Null while generation.preview answers. */
  preview: GenerationPreviewResult | null;
  error: string | null;
  onClose: () => void;
}

export function PayloadSheet({ provider, preview, error, onClose }: PayloadSheetProps): JSX.Element {
  const local = provider.kind === 'local';
  return (
    <SideSheet
      titleId="payload-title"
      title={local ? 'What the local model reads' : 'What will be sent'}
      onClose={onClose}
      footer={
        <>
          <span class="sheet-footer-note">{local ? 'Stays on this PC. Nothing is sent anywhere.' : 'Nothing is sent until you press Generate.'}</span>
          <button class="btn p" type="button" onClick={onClose}>
            Done
          </button>
        </>
      }
    >
      <div class="payload">
        {error !== null ? (
          <p class="dialog-error" role="alert">
            {error}
          </p>
        ) : preview === null ? (
          <p class="payload-loading" role="status">
            Putting the payload together on this PC…
          </p>
        ) : (
          <>
            <dl class="payload-facts">
              <dt>{local ? 'Read by' : 'Sent to'}</dt>
              <dd>{local ? `${provider.modelLabel ?? 'The local model'} on this PC` : PROVIDER_FULL[provider.id]}</dd>
              <dt>Size</dt>
              <dd class="mono">{payloadSize(preview.bytes, preview.chunks)}</dd>
              <dt>Inputs</dt>
              <dd>
                <InputPills inputs={preview.inputsUsed} />
              </dd>
            </dl>
            <p class="payload-never">Audio and video are never sent.</p>
            {preview.warnings.length === 0 ? null : (
              <ul class="payload-warnings">
                {preview.warnings.map((w) => (
                  <li key={w}>{w}</li>
                ))}
              </ul>
            )}
            <label class="lbl payload-label" for="payload-text">
              Exactly this text
            </label>
            <textarea id="payload-text" class="field payload-text mono" readOnly rows={18} value={preview.payloadText} />
          </>
        )}
      </div>
    </SideSheet>
  );
}

interface ConfirmSendProps {
  summary: GenerationSendSummary;
  documentWord: string;
  onAnswer: (approved: boolean) => void;
}

export function ConfirmSendDialog({ summary, documentWord, onAnswer }: ConfirmSendProps): JSX.Element {
  return (
    <Dialog
      titleId="confirm-send-title"
      title={`Send to ${summary.providerName}?`}
      onEscape={() => {
        onAnswer(false);
      }}
      width={600}
      actions={
        <>
          <button
            class="btn g"
            type="button"
            onClick={() => {
              onAnswer(false);
            }}
          >
            Cancel
          </button>
          <button
            class="btn p"
            type="button"
            data-autofocus
            onClick={() => {
              onAnswer(true);
            }}
          >
            Send
          </button>
        </>
      }
    >
      <p class="dialog-body">
        To write the {documentWord}, Memento sends these parts of the recording to {PROVIDER_FULL[summary.providerId]}. You asked to confirm every send in Settings › AI and
        privacy.
      </p>
      <dl class="payload-facts confirm-facts">
        <dt>Provider</dt>
        <dd>
          {PROVIDER_FULL[summary.providerId]}
          {summary.modelLabel === null ? '' : ` · ${summary.modelLabel}`}
        </dd>
        <dt>Inputs</dt>
        <dd>
          <InputPills inputs={summary.inputsUsed} />
        </dd>
        <dt>Size</dt>
        <dd class="mono">{payloadSize(summary.bytes, summary.chunks)}</dd>
      </dl>
      <p class="dialog-body confirm-never">Audio and video are never sent.</p>
    </Dialog>
  );
}
