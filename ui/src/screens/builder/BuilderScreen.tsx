// Document builder (DESIGN.md §10), transcribed from design/renders/Builder.dc.html: the modules
// palette, the structure as rows of module cards, and the live preview with the inputs and output.
// Generate is the only moment anything is sent; the preview of exactly what will be sent is one
// click away, and "ask before every send" confirms first (§5.19).
import type { JSX } from 'preact';
import { useEffect, useMemo, useReducer, useRef, useState } from 'preact/hooks';
import type {
  GenerationPreviewResult,
  InputSelection,
  ModuleInfo,
  Project,
  ProviderId,
  ProviderInfo,
  ProvidersListResult,
  Style,
  Template,
} from '../../bridge/types';
import { ArrowRightIcon } from '../../components/paper/icons';
import { SpokeHeader } from '../../components/SpokeHeader';
import { UndoButton } from '../../components/UndoButton';
import { documentWord, moduleWords, paperName } from '../../format/documents';
import { useServices } from '../../state/context';
import { undoOf } from '../../state/undo';
import type { Route } from '../../state/router';
import { openStyleEditor } from '../styleeditor/styleReturn';
import './builder.css';
import { draftKey, draftOf, type BuilderTab } from './draft';
import { cancelGeneration, confirmGeneration, dismissGeneration, generationOf, startGeneration, watchGeneration } from './generation';
import { holdLiveOutput, liveOutputOf, openLiveOutput, releaseLiveOutput, resetLiveOutput } from './liveOutput';
import { Palette } from './Palette';
import { PreviewPanel, type InputRow } from './PreviewPanel';
import { layoutOfDocument } from './regenerate';
import { bindBuilderUndo, recordBuilderStep, structureStep, templateStep, type BuilderSnapshot } from './builderUndo';
import { ConfirmSendDialog, PayloadSheet } from './SendDialogs';
import { initialStructure, moduleCount, modulesInUse, rowsOf, structureReducer, type DragPayload, type Rows, type StructureAction } from './structureState';
import { moduleName, Structure } from './Structure';
import { TemplateChooser } from './TemplateChooser';

/** The preview paper is asked for once changes pause this long. */
export const PREVIEW_DEBOUNCE_MS = 200;

/**
 * How often provider readiness is asked again while the chosen provider is not ready. The local model's readiness
 * depends on free video memory, which other apps (or a job that just ended) give back without any event.
 */
export const PROVIDER_RECHECK_MS = 5000;

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

/** The template as it stands: its settings and the rows in the structure. */
function composed(template: Template, rows: Rows): Template {
  return { ...template, rows: rows.map((modules) => ({ modules })) };
}

/** Which provider the Builder starts with: the template's, the Settings default, else the first ready one. */
export function chosenProvider(template: Template, defaultId: ProviderId | null, list: readonly ProviderInfo[]): ProviderInfo | null {
  const wanted = template.providerId ?? defaultId;
  return list.find((p) => p.id === wanted) ?? list.find((p) => p.ready) ?? null;
}

interface BuilderProps {
  recordingId: string | null;
  templateId: string | null;
  documentId: string | null;
}

export function BuilderScreen({ recordingId, templateId, documentId }: BuilderProps): JSX.Element {
  const services = useServices();
  const { bridge, store, router } = services;
  const key = draftKey(recordingId, templateId, documentId);
  const draftRef = draftOf(store);
  const restored = draftRef.current?.key === key ? draftRef.current : null;

  const [modules, setModules] = useState<ModuleInfo[] | null>(null);
  const [template, setTemplate] = useState<Template | null>(restored?.template ?? null);
  const [structure, dispatch] = useReducer(structureReducer, restored?.structure ?? initialStructure([]));
  const [tab, setTab] = useState<BuilderTab>(restored?.tab ?? 'preview');
  const [styles, setStyles] = useState<Style[]>([]);
  const [providers, setProviders] = useState<ProvidersListResult | null>(null);
  const [project, setProject] = useState<Project | null>(null);
  const [attachmentCount, setAttachmentCount] = useState(0);
  const [earlierCount, setEarlierCount] = useState(0);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [previewHtml, setPreviewHtml] = useState<string | null>(null);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const [drag, setDrag] = useState<DragPayload | null>(null);
  const [over, setOver] = useState<string | null>(null);
  const [sheet, setSheet] = useState<{ preview: GenerationPreviewResult | null; error: string | null } | null>(null);
  const [announcement, setAnnouncement] = useState('');
  const [savingTemplate, setSavingTemplate] = useState(false);
  const [reloadStyles, setReloadStyles] = useState(0);
  /** The template as opened or last saved (JSON), or null until the next render records it. */
  const clean = useRef<string | null>(null);

  const job = generationOf(store).value;
  const settings = store.settings.value;
  const catalog = useMemo(() => new Map((modules ?? []).map((m) => [m.id as string, m])), [modules]);

  // Leaving by Back (or after Generate) drops the draft; anything else (the Style editor) keeps it.
  const keepDraft = useRef(true);
  const latest = useRef({ template, structure, tab });
  latest.current = { template, structure, tab };
  useEffect(
    () => () => {
      const { template: t, structure: s, tab: currentTab } = latest.current;
      draftRef.current = keepDraft.current && t !== null ? { key, template: t, structure: s, tab: currentTab } : null;
    },
    [key],
  );

  // The catalog, the styles, the providers and what the recording holds; the template unless restored.
  useEffect(() => {
    // An object, so the checks after each await see the cleanup's change.
    const alive = { current: true };
    const isAlive = (): boolean => alive.current;
    const load = async (): Promise<void> => {
      const [catalogResult, providerResult, projectResult] = await Promise.all([
        bridge.call('modules.list'),
        bridge.call('providers.list'),
        recordingId === null ? Promise.resolve(null) : bridge.call('project.get', { recordingId }),
      ]);
      if (!isAlive()) {
        return;
      }
      setModules(catalogResult.modules);
      setProviders(providerResult);
      setProject(projectResult);
      if (recordingId !== null) {
        Promise.all([bridge.call('attachments.list', { recordingId }), bridge.call('documents.list', { recordingId })])
          .then(([a, d]) => {
            if (alive.current) {
              setAttachmentCount(a.attachments.length);
              setEarlierCount(d.documents.filter((doc) => doc.id !== documentId).length);
            }
          })
          .catch(() => undefined);
      }
      if (restored !== null) {
        return;
      }
      let chosen: Template;
      if (templateId !== null) {
        chosen = await bridge.call('templates.get', { templateId });
      } else {
        const { templates } = await bridge.call('templates.list');
        const defaultId = store.settings.value?.documents.defaultTemplateId ?? 'meeting-minutes';
        const type = projectResult?.details.type ?? null;
        const fallback = templates.find((t) => t.id === defaultId) ?? templates[0];
        const byType = type === null ? undefined : templates.find((t) => t.recordingTypes.includes(type));
        const pick = fallback !== undefined && (type === null || fallback.recordingTypes.length === 0 || fallback.recordingTypes.includes(type)) ? fallback : (byType ?? fallback);
        if (pick === undefined) {
          throw new Error('There are no templates in this library. Restore the built-in ones in Settings › Documents.');
        }
        chosen = pick;
      }
      // Regenerate: the inputs, provider and style the document was made with.
      if (documentId !== null && recordingId !== null) {
        const { document } = await bridge.call('documents.get', { recordingId, documentId });
        const record = document.record;
        if (record !== null) {
          chosen = { ...chosen, inputs: record.inputs, providerId: record.providerId, styleId: record.styleId, rows: layoutOfDocument(chosen, document) };
        }
      }
      if (!isAlive()) {
        return;
      }
      clean.current = null;
      setTemplate(chosen);
      dispatch({ type: 'load', rows: rowsOf(chosen) });
    };
    load().catch((e: unknown) => {
      if (alive.current) {
        setLoadError(messageOf(e));
      }
    });
    return () => {
      alive.current = false;
    };
  }, [bridge, recordingId, templateId, documentId]);

  // Styles: on open, and whenever the host says they changed (a built-in saved as a copy).
  useEffect(() => {
    let live = true;
    bridge
      .call('styles.list')
      .then(({ styles: list }) => {
        if (live) {
          setStyles(list);
        }
      })
      .catch(() => undefined);
    return () => {
      live = false;
    };
  }, [bridge, reloadStyles]);
  useEffect(() => bridge.on('styles.changed', () => { setReloadStyles((n) => n + 1); }), [bridge]);

  // Provider readiness follows Settings (a key added, external AI turned on) and model installs.
  const aiKey = JSON.stringify(settings?.ai ?? null);
  useEffect(() => {
    let live = true;
    bridge
      .call('providers.list')
      .then((result) => {
        if (live) {
          setProviders(result);
        }
      })
      .catch(() => undefined);
    return () => {
      live = false;
    };
  }, [bridge, aiKey]);

  // The live preview paper, after a short pause in changes.
  const current = template === null ? null : composed(template, structure.rows);
  // The template as it was opened or last saved, to tell whether switching templates would lose changes.
  const currentKey = current === null ? null : JSON.stringify(current);
  useEffect(() => {
    if (clean.current === null && currentKey !== null) {
      clean.current = currentKey;
    }
  });
  const dirty = currentKey !== null && clean.current !== null && clean.current !== currentKey;
  const previewKey = current === null ? null : JSON.stringify([current.name, current.styleId, current.rows]);
  const previewSequence = useRef(0);
  useEffect(() => {
    if (current === null) {
      return undefined;
    }
    const sequence = ++previewSequence.current;
    const timer = setTimeout(() => {
      bridge
        .call('generation.previewHtml', { recordingId, template: current, styleId: current.styleId })
        .then(({ html }) => {
          if (sequence === previewSequence.current) {
            setPreviewHtml(html);
            setPreviewError(null);
          }
        })
        .catch((e: unknown) => {
          if (sequence === previewSequence.current) {
            setPreviewError(`The preview could not be drawn. ${messageOf(e)}`);
          }
        });
    }, PREVIEW_DEBOUNCE_MS);
    return () => {
      clearTimeout(timer);
    };
  }, [bridge, recordingId, previewKey]);

  // This Builder opens what it generated; while it is on screen it watches the job.
  useEffect(() => {
    if (recordingId === null) {
      return undefined;
    }
    watchGeneration(store, true);
    return () => {
      watchGeneration(store, false);
    };
  }, [store, recordingId]);
  // The Live output of this recording's generation stays readable while this Builder is open.
  useEffect(() => {
    if (recordingId === null) {
      return undefined;
    }
    const holder = `builder:${recordingId}`;
    holdLiveOutput(store, holder);
    return () => {
      releaseLiveOutput(store, holder);
    };
  }, [store, recordingId]);
  const live = liveOutputOf(store).value;
  const mine = job !== null && job.recordingId === recordingId ? job : null;
  useEffect(() => {
    if (mine?.phase === 'done' && mine.resultId !== null && recordingId !== null) {
      const resultId = mine.resultId;
      dismissGeneration(store);
      keepDraft.current = false;
      router.navigate({ name: 'document', recordingId, documentId: resultId });
    } else if (mine?.phase === 'cancelled') {
      dismissGeneration(store);
    }
  }, [mine?.phase, mine?.resultId]);

  const providerList = providers?.providers ?? [];
  const provider = template === null ? null : chosenProvider(template, settings?.ai.defaultProviderId ?? null, providerList);
  const waitingForProvider = providers !== null && template !== null && provider?.ready !== true;
  useEffect(() => {
    if (!waitingForProvider) {
      return undefined;
    }
    let live = true;
    const timer = setInterval(() => {
      bridge
        .call('providers.list')
        .then((result) => {
          if (live) {
            setProviders(result);
          }
        })
        .catch(() => undefined);
    }, PROVIDER_RECHECK_MS);
    return () => {
      live = false;
      clearInterval(timer);
    };
  }, [bridge, waitingForProvider]);
  const word = documentWord(template?.name ?? 'document');
  const count = moduleCount(structure.rows);
  // The palette greys the modules already placed; it updates as modules are added or removed.
  const inUse = useMemo(() => modulesInUse(structure.rows), [structure.rows]);
  const style = styles.find((s) => s.id === template?.styleId) ?? null;
  const backRoute: Route = recordingId === null ? { name: 'settings', section: 'documents' } : { name: 'review', recordingId };
  const backLabel = recordingId === null ? 'Settings' : (project?.summary.title ?? store.library.value?.recordings.find((r) => r.id === recordingId)?.title ?? 'Recording');
  const busy = mine !== null && (mine.phase === 'starting' || mine.phase === 'running' || mine.phase === 'confirm');
  const otherBusy = job !== null && job.recordingId !== recordingId && (job.phase === 'running' || job.phase === 'confirm' || job.phase === 'starting');
  const canGenerate = recordingId !== null && template !== null && provider?.ready === true && count > 0 && !busy && !otherBusy;

  // Undo (state/undo.ts): structure and template changes are steps with the state before and after.
  const undo = undoOf(store);
  const undoScope = `builder:${key}`;
  /** What the screen holds, kept current between a change and the next render. */
  const now = useRef({ template, structure });
  now.current = { template, structure };
  useEffect(() => {
    undo.claim(undoScope);
    const unbind = bindBuilderUndo(undoScope, (snapshot: BuilderSnapshot) => {
      // Undo puts back structure and settings; what Save template gave the template (its id) stays.
      const t = now.current.template;
      const restored = t === null ? snapshot.template : { ...snapshot.template, id: t.id, builtIn: t.builtIn, modifiedAt: t.modifiedAt, ...(t.customized === undefined ? {} : { customized: t.customized }) };
      now.current = { template: restored, structure: structureReducer(now.current.structure, { type: 'restore', rows: snapshot.rows }) };
      setTemplate(restored);
      dispatch({ type: 'restore', rows: snapshot.rows });
    });
    return () => {
      unbind();
      // The Style editor keeps the draft, and with it this stack; Back and Generate drop both.
      undo.release(undoScope, { keep: keepDraft.current });
    };
  }, [undo, undoScope]);

  const structureDispatch = (action: StructureAction): void => {
    const { template: t, structure: before } = now.current;
    const after = structureReducer(before, action);
    const step = t === null ? null : structureStep(before.rows, action, (m) => moduleName(m, catalog));
    if (t !== null && step !== null && JSON.stringify(after.rows) !== JSON.stringify(before.rows)) {
      recordBuilderStep(undo, undoScope, { template: t, rows: before.rows }, { template: t, rows: after.rows }, step);
    }
    now.current = { template: t, structure: after };
    dispatch(action);
  };

  const patchTemplate = (patch: Partial<Template>): void => {
    const { template: t, structure: s } = now.current;
    if (t === null) {
      return;
    }
    const next = { ...t, ...patch };
    if (JSON.stringify(next) !== JSON.stringify(t)) {
      recordBuilderStep(undo, undoScope, { template: t, rows: s.rows }, { template: next, rows: s.rows }, templateStep(patch));
    }
    now.current = { template: next, structure: s };
    setTemplate(next);
  };

  const generate = (chosen: ProviderInfo | null = provider): void => {
    if (current === null || recordingId === null || chosen === null) {
      return;
    }
    resetLiveOutput(store);
    void startGeneration(services, { recordingId, template: current, provider: chosen, documentId });
  };

  /** Another template from the header's list: its structure, inputs, provider, style and output replace the current ones. */
  const openTemplate = (next: Template): void => {
    const { template: t, structure: s } = now.current;
    if (t !== null) {
      recordBuilderStep(undo, undoScope, { template: t, rows: s.rows }, { template: next, rows: rowsOf(next) }, { label: `open ${next.name}` });
    }
    now.current = { template: next, structure: structureReducer(s, { type: 'load', rows: rowsOf(next) }) };
    clean.current = null;
    setTemplate(next);
    dispatch({ type: 'load', rows: rowsOf(next) });
    setAnnouncement(`${next.name} opened, ${moduleWords(moduleCount(rowsOf(next)))}.`);
  };

  /** Save template (a built-in is saved as a copy), or Save as new template (an empty id: always a new one). */
  const saveTemplate = (asNew = false): void => {
    if (current === null || savingTemplate) {
      return;
    }
    setSavingTemplate(true);
    bridge
      .call('templates.save', { template: asNew ? { ...current, id: '' } : current })
      .then((saved) => {
        setSavingTemplate(false);
        const copied = saved.id !== current.id;
        clean.current = null;
        setTemplate({ ...current, id: saved.id, name: saved.name, builtIn: saved.builtIn, modifiedAt: saved.modifiedAt });
        store.toasts.show({
          tone: 'ok',
          title: copied ? `Saved as “${saved.name}”` : `Template “${saved.name}” saved`,
          body: asNew
            ? `A new template; “${current.name}” stays as it was. Choose either from the template list in the builder.`
            : copied && current.builtIn
              ? `${current.name} is built in, so it stays as it is; your arrangement is a template of its own.`
              : 'Choose it from the template list in the builder, for any recording.',
        });
      })
      .catch((e: unknown) => {
        setSavingTemplate(false);
        store.toasts.show({ tone: 'danger', title: 'The template was not saved', body: `${messageOf(e)} Your arrangement is still here.` });
      });
  };

  const openPayload = (): void => {
    if (current === null || recordingId === null) {
      return;
    }
    setSheet({ preview: null, error: null });
    bridge
      .call('generation.preview', { recordingId, template: current })
      .then((preview) => {
        setSheet((s) => (s === null ? s : { preview, error: null }));
      })
      .catch((e: unknown) => {
        setSheet((s) => (s === null ? s : { preview: null, error: `What would be sent could not be put together. ${messageOf(e)} Nothing was sent.` }));
      });
  };

  const editStyle = (styleId: string): void => {
    if (template === null) {
      return;
    }
    const builderRoute: Route = { name: 'builder', recordingId, templateId, documentId };
    openStyleEditor(
      store,
      (route) => {
        router.navigate(route);
      },
      styleId,
      {
      label: template.name,
      route: builderRoute,
      onSaved: (savedStyle) => {
        const draft = draftOf(store).current;
        if (draft?.key === key) {
          draft.template = { ...draft.template, styleId: savedStyle.id };
        }
      },
      },
    );
  };

  const inputRows = useMemo((): InputRow[] => {
    const share = settings?.ai.share;
    const cloud = provider?.kind !== 'local';
    const off = (allowed: boolean | undefined): boolean => cloud && allowed === false;
    const p = project;
    const transcribed = p?.summary.stages.some((s) => s.stage === 'transcript' && s.state === 'done') ?? false;
    const highlights = p?.highlights.length ?? 0;
    const notes = p?.highlights.filter((h) => h.note.trim() !== '').length ?? 0;
    const row = (key: keyof InputSelection, note: string, allowed: boolean | undefined): InputRow => ({
      key,
      note: off(allowed) ? 'off in Settings' : note,
      locked: off(allowed),
    });
    if (p === null) {
      return (['transcript', 'details', 'participants', 'agenda', 'highlights', 'attachments', 'previousDocuments'] as const).map((k) =>
        row(k, '', k === 'previousDocuments' ? true : share?.[k]),
      );
    }
    return [
      row('transcript', transcribed ? 'with speakers' : 'not transcribed yet', share?.transcript),
      row('details', p.details.purpose === '' ? 'title, date, type' : 'title, date, purpose', share?.details),
      row('participants', p.details.participants.length === 0 ? 'none listed' : `${p.details.participants.length} listed`, share?.participants),
      row('agenda', p.details.agenda.items.length === 0 ? 'none imported' : (p.details.agenda.source ?? 'typed in'), share?.agenda),
      row('highlights', highlights === 0 ? 'none marked' : `${highlights} + ${notes}`, share?.highlights),
      row('attachments', attachmentCount === 0 ? 'none attached' : `${attachmentCount} attached`, share?.attachments),
      row('previousDocuments', earlierCount === 0 ? 'none yet' : `${earlierCount} in this recording`, true),
    ];
  }, [settings?.ai.share, provider?.kind, project, attachmentCount, earlierCount]);

  const alternative = mine?.phase === 'failed' ? (providerList.find((p) => p.ready && p.id !== mine.provider.id && (p.kind === 'local' || providers?.externalAiEnabled === true)) ?? null) : null;

  const header = (
    <SpokeHeader
      backLabel={backLabel}
      onBack={() => {
        keepDraft.current = false;
        router.navigate(backRoute);
      }}
      titleEditor={
        <>
          <label class="sr" for="tpl-name">
            Template name
          </label>
          <input
            id="tpl-name"
            class="field rec-title-input builder-title"
            type="text"
            value={template?.name ?? ''}
            maxLength={80}
            autocomplete="off"
            disabled={template === null}
            onInput={(event) => {
              patchTemplate({ name: event.currentTarget.value });
            }}
          />
          <span class="pill done">Template · {moduleWords(count)}</span>
          <TemplateChooser currentId={template?.id ?? null} currentName={template?.name ?? ''} dirty={dirty} disabled={template === null || busy} onOpen={openTemplate} />
        </>
      }
      actions={
        <>
          <UndoButton />
          <button class="btn ghost spoke-ghost" type="button" disabled={template === null || savingTemplate} onClick={() => { saveTemplate(); }}>
            Save template
          </button>
          <button class="btn ghost spoke-ghost" type="button" disabled={template === null || savingTemplate} onClick={() => { saveTemplate(true); }}>
            Save as new template
          </button>
          {recordingId === null ? null : (
            <button
              class="btn primary spoke-primary"
              type="button"
              disabled={!canGenerate}
              title={provider?.ready === false ? `${provider.name}: ${provider.reason ?? 'not ready'}` : undefined}
              onClick={() => {
                generate();
              }}
            >
              Generate {word}
              <ArrowRightIcon size={16} />
            </button>
          )}
        </>
      }
    />
  );

  if (loadError !== null) {
    return (
      <>
        {header}
        <main class="spoke-main">
          <div class="placeholder-card">
            <p class="placeholder-text" role="alert">
              {loadError}
            </p>
          </div>
        </main>
      </>
    );
  }

  return (
    <>
      {header}
      <main class="builder-main">
        <div class="builder-layout">
          {modules === null ? (
            <section class="palette" aria-label="Modules" aria-busy="true">
              <p class="palette-empty">Reading the modules…</p>
            </section>
          ) : (
            <Palette
              inUse={inUse}
              modules={modules}
              onAdd={(m) => {
                structureDispatch({ type: 'append', module: m });
                setAnnouncement(`${m.name} added at the end.`);
              }}
              onDragStart={setDrag}
              onDragEnd={() => {
                setDrag(null);
                setOver(null);
              }}
            />
          )}
          <Structure
            rows={structure.rows}
            selectedId={structure.selectedId}
            catalog={catalog}
            drag={drag}
            over={over}
            dispatch={structureDispatch}
            onDragStart={setDrag}
            onDragEnd={() => {
              setDrag(null);
              setOver(null);
            }}
            onOver={setOver}
            onAddModule={() => {
              const custom = catalog.get('customAi');
              if (custom !== undefined) {
                structureDispatch({ type: 'append', module: custom });
                setAnnouncement(`${custom.name} added at the end.`);
              }
            }}
            announce={setAnnouncement}
          />
          {template === null ? (
            <section class="builder-side" aria-label="Preview and inputs" aria-busy="true">
              <p class="palette-empty">Opening the template…</p>
            </section>
          ) : (
            <PreviewPanel
              tab={tab}
              onTab={setTab}
              documentWord={word}
              previewHtml={previewHtml}
              previewError={previewError}
              styleName={style?.name ?? template.styleId}
              paper={paperName(style?.settings.paper ?? 'letter')}
              onEditStyle={() => {
                editStyle(template.styleId);
              }}
              job={mine}
              moduleName={(id) => {
                const m = structure.rows.flat().find((x) => x.id === id) ?? mine?.template.rows.flatMap((r) => r.modules).find((x) => x.id === id);
                return m === undefined ? null : moduleName(m, catalog);
              }}
              onCancel={() => {
                void cancelGeneration(bridge, store);
              }}
              onShowLiveOutput={
                busy || (mine?.phase === 'failed' && live !== null && live.recordingId === recordingId)
                  ? () => {
                      openLiveOutput(store);
                    }
                  : undefined
              }
              onRetry={() => {
                generate(mine?.provider ?? provider);
              }}
              alternative={alternative}
              onSwitch={(next) => {
                patchTemplate({ providerId: next.id });
                generate(next);
              }}
              onDismissFailure={() => {
                dismissGeneration(store);
              }}
              inputs={template.inputs}
              inputRows={inputRows}
              onInputs={(inputs) => {
                patchTemplate({ inputs });
              }}
              providers={providers?.providers ?? null}
              externalAiEnabled={providers?.externalAiEnabled ?? false}
              providerId={provider?.id ?? null}
              onProvider={(id) => {
                patchTemplate({ providerId: id });
              }}
              onOpenAiSettings={() => {
                router.navigate({ name: 'settings', section: 'ai-privacy' });
              }}
              styles={styles}
              styleId={template.styleId}
              onStyle={(styleId) => {
                patchTemplate({ styleId });
              }}
              onEditStyles={() => {
                editStyle(template.styleId);
              }}
              output={template.output}
              onOutput={(output) => {
                patchTemplate({ output });
              }}
              canPreviewPayload={recordingId !== null && provider !== null}
              onPreviewPayload={openPayload}
            />
          )}
        </div>
        <div class="sr" role="status" aria-live="polite">
          {announcement}
        </div>
      </main>
      {sheet !== null && provider !== null ? (
        <PayloadSheet
          provider={provider}
          preview={sheet.preview}
          error={sheet.error}
          onClose={() => {
            setSheet(null);
          }}
        />
      ) : null}
      {mine?.phase === 'confirm' && mine.summary !== null ? (
        <ConfirmSendDialog
          summary={mine.summary}
          documentWord={word}
          onAnswer={(approved) => {
            void confirmGeneration(bridge, store, approved);
          }}
        />
      ) : null}
    </>
  );
}
