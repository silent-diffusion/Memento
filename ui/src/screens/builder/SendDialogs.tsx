// The "ask before every send" confirmation (§5.19: provider, inputs, size and chunk count). What will
// be sent is the full sheet in PreviewSheet.tsx (2.0, DESIGN.md §10.6).
import type { JSX } from 'preact';
import type { GenerationSendSummary, InputSelection } from '../../bridge/types';
import { Dialog } from '../../components/Overlay';
import { INPUT_PILLS, inputsUsed, payloadSize, PROVIDER_FULL } from '../../format/documents';

function InputPills({ inputs }: { inputs: InputSelection }): JSX.Element {
  const used = inputsUsed(inputs);
  return (
    <div class="pill-row send-pills">
      {used.length === 0 ? <span class="send-none">Only the instructions</span> : used.map((key) => <span key={key} class="pill done">{INPUT_PILLS[key]}</span>)}
    </div>
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
