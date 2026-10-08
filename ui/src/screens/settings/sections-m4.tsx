// M4 rows of Settings (DESIGN.md §11): Documents › Defaults (default template and style, the
// templates and styles manager) and AI and privacy › the default provider and the local model,
// installed through the model manager like the transcription models.
import type { JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import type { ProviderId, ProviderInfo, Style, Template } from '../../bridge/types';
import { SelectMenu } from '../../components/Menus';
import { updateSettings } from '../../state/actions';
import { useServices } from '../../state/context';
import { TemplatesManager } from '../styleeditor/TemplatesManager';
import { ModelCards, useModels } from './ModelCards';
import { SettingsGroup, SettingsRow } from './SettingsParts';

function InlineMessage({ message }: { message: string | null }): JSX.Element | null {
  return message === null ? null : (
    <p class="settings-inline" role="alert">
      {message}
    </p>
  );
}

/** Settings › Documents › Defaults: the three rows inside the group's card. */
export function DocumentDefaultsRows(): JSX.Element {
  const services = useServices();
  const { bridge, store } = services;
  const settings = store.settings.value;
  const [templates, setTemplates] = useState<Template[]>([]);
  const [styles, setStyles] = useState<Style[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [managing, setManaging] = useState(false);
  const [reload, setReload] = useState(0);

  useEffect(() => {
    let live = true;
    Promise.all([bridge.call('templates.list'), bridge.call('styles.list')])
      .then(([t, s]) => {
        if (live) {
          setTemplates(t.templates);
          setStyles(s.styles);
        }
      })
      .catch((e: unknown) => {
        if (live) {
          setError(`Templates and styles could not be read. ${e instanceof Error ? e.message : ''}`.trim());
        }
      });
    return () => {
      live = false;
    };
  }, [bridge, reload]);

  useEffect(() => {
    const offs = [bridge.on('templates.changed', () => { setReload((n) => n + 1); }), bridge.on('styles.changed', () => { setReload((n) => n + 1); })];
    return () => {
      offs.forEach((off) => {
        off();
      });
    };
  }, [bridge]);

  if (settings === null) {
    return <></>;
  }
  const docs = settings.documents;
  const templateOptions = templates.length === 0 ? [{ value: docs.defaultTemplateId, label: docs.defaultTemplateId }] : templates.map((t) => ({ value: t.id, label: t.name }));
  const styleOptions = styles.length === 0 ? [{ value: docs.defaultStyleId, label: docs.defaultStyleId }] : styles.map((s) => ({ value: s.id, label: s.name }));
  return (
    <>
      <SettingsRow label="Default template" description="Used when you create a document from a recording.">
        <SelectMenu<string>
          label="Default template"
          value={docs.defaultTemplateId}
          options={templateOptions}
          onChange={(defaultTemplateId) => {
            void updateSettings(services, { documents: { defaultTemplateId } }).then(setError);
          }}
        />
      </SettingsRow>
      <SettingsRow label="Default style" description="How new documents look. Styles never change what is written.">
        <SelectMenu<string>
          label="Default style"
          value={docs.defaultStyleId}
          options={styleOptions}
          onChange={(defaultStyleId) => {
            void updateSettings(services, { documents: { defaultStyleId } }).then(setError);
          }}
        />
      </SettingsRow>
      <SettingsRow
        label="Manage templates and styles"
        description={`${templates.length} templates and ${styles.length} styles. Open, duplicate or delete them.`}
        below={<InlineMessage message={error} />}
      >
        <button
          class="btn ghost small-btn"
          type="button"
          aria-haspopup="dialog"
          onClick={() => {
            setManaging(true);
          }}
        >
          Manage
        </button>
      </SettingsRow>
      {managing ? (
        <TemplatesManager
          onClose={() => {
            setManaging(false);
          }}
        />
      ) : null}
    </>
  );
}

const NO_DEFAULT = 'first-ready';

/** Under the local model cards: which model writes documents now and where it runs, as providers.list says. */
export function LocalModelInUse({ provider }: { provider: ProviderInfo | null }): JSX.Element | null {
  if (provider === null) {
    return null;
  }
  return (
    <p class="model-in-use" role="status" data-local-ready={provider.ready ? 'true' : 'false'}>
      {provider.ready ? (
        <>
          Documents are written with <strong>{provider.modelLabel ?? 'the local model'}</strong>. {provider.detail ?? ''}
        </>
      ) : (
        <>
          <strong>{provider.reason ?? 'The local model is not ready'}.</strong> {provider.detail ?? ''}
        </>
      )}
    </p>
  );
}

/** Settings › AI and privacy: which provider the Builder starts with, and the local model. */
export function AiProviderDefaults(): JSX.Element {
  const services = useServices();
  const { bridge, store } = services;
  const settings = store.settings.value;
  const models = useModels(bridge);
  const [providers, setProviders] = useState<ProviderInfo[]>([]);
  const [error, setError] = useState<string | null>(null);
  const aiKey = JSON.stringify(settings?.ai ?? null);
  const installedLocal = models.state.models.filter((m) => m.engine === 'llm' && m.installed).length;

  useEffect(() => {
    let live = true;
    bridge
      .call('providers.list')
      .then((result) => {
        if (live) {
          setProviders(result.providers);
        }
      })
      .catch(() => undefined);
    return () => {
      live = false;
    };
  }, [bridge, aiKey, installedLocal]);

  // The local model in effect follows what is installed (BRIDGE.md: an installed one whenever any is), so the
  // snapshot is read again when a local model finishes installing or is removed.
  const loadedInstalls = useRef<number | null>(null);
  useEffect(() => {
    if (!models.state.loaded) {
      return undefined;
    }
    if (loadedInstalls.current === null || loadedInstalls.current === installedLocal) {
      loadedInstalls.current = installedLocal;
      return undefined;
    }
    loadedInstalls.current = installedLocal;
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
  }, [bridge, store, models.state.loaded, installedLocal]);

  if (settings === null) {
    return <></>;
  }
  const ai = settings.ai;
  const note = (p: ProviderInfo): string => (p.ready ? '' : ` (${(p.reason ?? 'not ready').toLocaleLowerCase()})`);
  const local = providers.find((p) => p.id === 'local') ?? null;
  return (
    <>
      <SettingsGroup label="Writing documents">
        <SettingsRow
          label="Default provider"
          description="Which AI writes documents unless a template names its own. The local model runs on this PC."
          below={<InlineMessage message={error} />}
        >
          <SelectMenu<string>
            label="Default provider"
            value={ai.defaultProviderId ?? NO_DEFAULT}
            options={[
              { value: NO_DEFAULT, label: 'The first one that is ready' },
              ...providers.map((p) => ({ value: p.id, label: `${p.name}${note(p)}` })),
            ]}
            onChange={(value) => {
              const defaultProviderId = value === NO_DEFAULT ? null : (value as ProviderId);
              void updateSettings(services, { ai: { defaultProviderId } }).then(setError);
            }}
          />
        </SettingsRow>
        <SettingsRow
          label="Local model"
          description="Writes documents on this PC without sending anything. Downloaded once and checked before it is used."
          below={
            <>
              <ModelCards
                api={models}
                engine="llm"
                defaultId={ai.localModelId}
                label="Local model"
                useLabel="Use this model"
                defaultStatus="Installed · selected"
                onDefault={(localModelId) => {
                  void updateSettings(services, { ai: { localModelId } }).then(setError);
                }}
              />
              <LocalModelInUse provider={local} />
            </>
          }
        />
      </SettingsGroup>
    </>
  );
}
