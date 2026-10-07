// Review's transcript dialogs (DESIGN.md §5.19, §18 "Reprocess … simple dialogs"): Transcribe again
// (model and language for a new pass) and the confirmation before restoring a transcript version.
import type { JSX } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import type { ModelInfo } from '../bridge/types';
import { TRANSCRIPTION_LANGUAGES } from '../format/languages';
import { formatSize } from '../format/storage';
import { versionReasonText } from '../format/transcript';
import { useServices } from '../state/context';
import type { DialogRequest } from '../state/dialogs';
import { SelectMenu } from './Menus';
import { Dialog } from './Overlay';

function messageOf(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

function DialogError({ message }: { message: string | null }): JSX.Element | null {
  return message === null ? null : (
    <p class="dialog-error" role="alert">
      {message}
    </p>
  );
}

export function RetranscribeDialog({ request, close }: { request: Extract<DialogRequest, { kind: 'retranscribe' }>; close: () => void }): JSX.Element {
  const { bridge, store } = useServices();
  const settings = store.settings.value;
  const [models, setModels] = useState<ModelInfo[] | null>(null);
  const [modelId, setModelId] = useState(settings?.transcription.modelId ?? '');
  const [language, setLanguage] = useState(settings?.transcription.language ?? 'auto');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let live = true;
    bridge
      .call('models.list')
      .then(({ models: list }) => {
        if (!live) {
          return;
        }
        const installed = list.filter((m) => m.engine === 'transcription' && m.installed);
        setModels(installed);
        setModelId((current) => (installed.some((m) => m.id === current) ? current : (installed[0]?.id ?? '')));
      })
      .catch((e: unknown) => {
        if (live) {
          setError(messageOf(e, 'The installed models could not be listed.'));
          setModels([]);
        }
      });
    return () => {
      live = false;
    };
  }, [bridge]);

  const keepsVersion = settings?.history.keepVersions ?? true;
  const note = !request.hasTranscript
    ? 'The new transcript appears in Review when it is ready.'
    : keepsVersion
      ? 'The current transcript stays until the new one is ready, and is then kept as a version you can restore from Details.'
      : 'Version history is off, so the current transcript is replaced when the new one is ready.';

  const submit = (): void => {
    if (modelId === '') {
      return;
    }
    setBusy(true);
    bridge
      .call('transcript.retranscribe', { recordingId: request.recordingId, modelId, language })
      .then(() => {
        close();
      })
      .catch((e: unknown) => {
        setBusy(false);
        setError(messageOf(e, 'Transcription was not queued.'));
      });
  };

  return (
    <Dialog
      titleId="retranscribe-title"
      title="Transcribe again"
      onEscape={close}
      actions={
        <>
          <button class="btn g" type="button" onClick={close}>
            Cancel
          </button>
          <button class="btn p" type="button" data-autofocus disabled={busy || modelId === ''} onClick={submit}>
            Transcribe again
          </button>
        </>
      }
    >
      <p class="dialog-body">
        <span class="dialog-strong">{request.title}</span> is transcribed again on this PC with the model and language you choose. Nothing is
        uploaded.
      </p>
      <div class="dialog-form dialog-grid">
        <span class="field-label" id="retranscribe-model">
          Model
        </span>
        {models === null ? (
          <span class="field-note">Looking for installed models…</span>
        ) : models.length === 0 ? (
          <span class="field-note">No transcription model is installed. Install one in Settings › Transcription.</span>
        ) : (
          <SelectMenu<string>
            label="Model"
            value={modelId}
            options={models.map((m) => ({ value: m.id, label: `${m.name} · ${formatSize(m.sizeBytes)} · ${m.accuracyNote}` }))}
            onChange={setModelId}
          />
        )}
        <span class="field-label" id="retranscribe-language">
          Language
        </span>
        <SelectMenu<string> label="Language" value={language} options={TRANSCRIPTION_LANGUAGES} onChange={setLanguage} />
      </div>
      <p class="field-note">{note}</p>
      <DialogError message={error} />
    </Dialog>
  );
}

export function RestoreVersionDialog({ request, close }: { request: Extract<DialogRequest, { kind: 'restoreVersion' }>; close: () => void }): JSX.Element {
  const { bridge, store } = useServices();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const keepsVersion = store.settings.value?.history.keepVersions ?? true;
  const { version } = request;
  return (
    <Dialog
      titleId="restore-title"
      title="Restore this transcript version?"
      onEscape={close}
      actions={
        <>
          <button class="btn g" type="button" data-autofocus onClick={close}>
            Cancel
          </button>
          <button
            class="btn p"
            type="button"
            disabled={busy}
            onClick={() => {
              setBusy(true);
              bridge
                .call('transcript.restoreVersion', { recordingId: request.recordingId, versionId: version.id })
                .then(() => {
                  close();
                })
                .catch((e: unknown) => {
                  setBusy(false);
                  setError(messageOf(e, 'The version was not restored.'));
                });
            }}
          >
            Restore
          </button>
        </>
      }
    >
      <p class="dialog-body">
        The transcript from <span class="dialog-strong">{request.when}</span> ({versionReasonText(version.reason).toLocaleLowerCase()},{' '}
        {version.segments.toLocaleString('en-US')} lines{version.engine === null ? '' : `, ${version.engine}`}) replaces the current one.{' '}
        {keepsVersion ? 'The current transcript is kept as a version, so nothing is lost.' : 'Version history is off, so the current transcript is not kept.'}
      </p>
      <DialogError message={error} />
    </Dialog>
  );
}
