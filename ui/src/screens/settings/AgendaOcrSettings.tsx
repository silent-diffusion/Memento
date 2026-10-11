// Settings › Documents › Agenda photos and scans (2.0, DESIGN.md §11): which text recognition reads
// an agenda photo or a scanned PDF. Windows OCR is built in, the default and the only choice until
// Tesseract English is installed from its model card here (download, SHA-256 check, Remove); then a
// segmented control chooses between the two. Nothing outside Settings changes: the Details sheet
// already says which engine read an agenda.
import type { JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import type { OcrEngineId } from '../../bridge/types';
import { Segmented } from '../../components/Controls';
import { updateSettings } from '../../state/actions';
import { useServices } from '../../state/context';
import { ModelCards, useModels } from './ModelCards';
import { SettingsGroup, SettingsRow } from './SettingsParts';

/** The catalog id of Tesseract's English language data. */
export const TESSERACT_MODEL_ID = 'tesseract-eng';

export const OCR_ENGINES: readonly { value: OcrEngineId; label: string }[] = [
  { value: 'windows', label: 'Windows OCR' },
  { value: 'tesseract', label: 'Tesseract' },
];

/** Why Remove is off while Tesseract reads the agendas. */
export const TESSERACT_KEEP = 'Agenda photos and scans are read with it. Choose Windows OCR first.';

export function AgendaOcrSettings(): JSX.Element {
  const services = useServices();
  const { bridge, store } = services;
  const settings = store.settings.value;
  const models = useModels(bridge);
  const [error, setError] = useState<string | null>(null);
  const installed = models.state.models.some((m) => m.id === TESSERACT_MODEL_ID && m.installed);

  // The engine in effect follows the language data (the host answers windows while it is missing), so the snapshot is
  // read again when Tesseract finishes installing or is removed.
  const lastInstalled = useRef<boolean | null>(null);
  useEffect(() => {
    if (!models.state.loaded) {
      return undefined;
    }
    if (lastInstalled.current === null || lastInstalled.current === installed) {
      lastInstalled.current = installed;
      return undefined;
    }
    lastInstalled.current = installed;
    let live = true;
    bridge
      .call('settings.get')
      .then((fresh) => {
        if (live) {
          store.settings.value = fresh;
        }
      })
      .catch(() => undefined);
    return () => {
      live = false;
    };
  }, [bridge, store, models.state.loaded, installed]);

  if (settings === null) {
    return <></>;
  }
  const engine = settings.documents.ocrEngine;
  const choose = (ocrEngine: OcrEngineId): void => {
    void updateSettings(services, { documents: { ocrEngine } }).then(setError);
  };
  return (
    <SettingsGroup label="Agenda photos and scans">
      <SettingsRow
        label="Text recognition"
        description={
          installed
            ? 'Reads agendas from photos and scanned PDFs on this PC. Tesseract marks the words it is unsure of; Windows OCR does not say how sure it is.'
            : 'Reads agendas from photos and scanned PDFs on this PC with Windows OCR. Install Tesseract below to choose it instead.'
        }
        below={
          <>
            {error === null ? null : (
              <p class="settings-inline" role="alert">
                {error}
              </p>
            )}
            <ModelCards
              api={models}
              engine="ocr"
              selectable={false}
              defaultId=""
              label="Tesseract language data"
              installedStatus={engine === 'tesseract' ? 'Installed · in use' : 'Installed'}
              keep={engine === 'tesseract' ? { [TESSERACT_MODEL_ID]: TESSERACT_KEEP } : {}}
              onDefault={() => undefined}
            />
          </>
        }
      >
        {installed ? (
          <Segmented<OcrEngineId> label="Text recognition" value={engine} options={OCR_ENGINES} onChange={choose} />
        ) : (
          <span class="settings-value">Windows OCR</span>
        )}
      </SettingsRow>
    </SettingsGroup>
  );
}
