// Templates and styles manager (DESIGN.md §18: "list of templates with their module counts; opened
// from Settings › Documents"): open, duplicate, delete your own, reset a built-in. A side sheet.
import type { JSX } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import type { Style, Template } from '../../bridge/types';
import { SideSheet } from '../../components/Overlay';
import { moduleWords } from '../../format/documents';
import { useServices } from '../../state/context';
import { openStyleEditor, SETTINGS_RETURN } from './styleReturn';
import './styleeditor.css';

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

type Pending = { kind: 'template' | 'style'; id: string } | null;

export function TemplatesManager({ onClose }: { onClose: () => void }): JSX.Element {
  const services = useServices();
  const { bridge, store, router } = services;
  const [templates, setTemplates] = useState<Template[] | null>(null);
  const [styles, setStyles] = useState<Style[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<Pending>(null);
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
          setError(`Templates and styles could not be read. ${messageOf(e)}`);
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

  const run = (work: Promise<unknown>, done?: string): void => {
    setError(null);
    work
      .then(() => {
        setConfirmDelete(null);
        setReload((n) => n + 1);
        if (done !== undefined) {
          store.toasts.show({ tone: 'ok', title: done, body: 'Nothing else was changed.' });
        }
      })
      .catch((e: unknown) => {
        setConfirmDelete(null);
        setError(messageOf(e));
      });
  };

  const styleName = (id: string): string => styles?.find((s) => s.id === id)?.name ?? id;

  return (
    <SideSheet
      titleId="manager-title"
      title="Templates and styles"
      onClose={onClose}
      footer={
        <>
          <span class="sheet-footer-note">Built-in templates and styles stay as they are; duplicate one to make your own.</span>
          <button class="btn p" type="button" onClick={onClose}>
            Done
          </button>
        </>
      }
    >
      <div class="manager">
        {error === null ? null : (
          <p class="manager-error" role="alert">
            {error}
          </p>
        )}
        <div class="manager-group">
          <span class="lbl" id="manager-templates">
            Templates
          </span>
          {templates === null ? (
            <span class="manager-meta">Reading the templates…</span>
          ) : (
            <div class="manager-list" role="list" aria-labelledby="manager-templates">
              {templates.map((t) => {
                const count = t.rows.reduce((n, r) => n + r.modules.length, 0);
                const deleting = confirmDelete?.kind === 'template' && confirmDelete.id === t.id;
                return (
                  <div key={t.id} class="manager-row" role="listitem">
                    <span class="manager-text">
                      <span class="manager-name">{t.name}</span>
                      <span class="manager-meta">
                        {moduleWords(count)} · {styleName(t.styleId)} · {t.builtIn ? 'Built in' : 'Yours'}
                      </span>
                    </span>
                    <span class="manager-actions">
                      {deleting ? (
                        <>
                          <span class="manager-confirm">Delete {t.name}?</span>
                          <button class="btn g small-btn" type="button" ref={(el) => el?.focus()} onClick={() => { setConfirmDelete(null); }}>
                            Keep
                          </button>
                          <button class="btn d small-btn" type="button" onClick={() => { run(bridge.call('templates.delete', { templateId: t.id }), `${t.name} deleted`); }}>
                            Delete
                          </button>
                        </>
                      ) : (
                        <>
                          <button
                            class="btn g small-btn"
                            type="button"
                            aria-label={`Open ${t.name} in the builder`}
                            onClick={() => {
                              onClose();
                              router.navigate({ name: 'builder', recordingId: null, templateId: t.id, documentId: null });
                            }}
                          >
                            Open
                          </button>
                          <button class="btn g small-btn" type="button" aria-label={`Duplicate ${t.name}`} onClick={() => { run(bridge.call('templates.duplicate', { templateId: t.id }), `${t.name} duplicated`); }}>
                            Duplicate
                          </button>
                          {t.builtIn ? (
                            <button class="btn g small-btn" type="button" disabled={t.customized !== true && t.modifiedAt === null} aria-label={`Reset ${t.name}`} onClick={() => { run(bridge.call('templates.resetBuiltIn', { templateId: t.id }), `${t.name} is as it shipped`); }}>
                              Reset
                            </button>
                          ) : (
                            <button class="btn g small-btn" type="button" aria-label={`Delete ${t.name}`} onClick={() => { setConfirmDelete({ kind: 'template', id: t.id }); }}>
                              Delete
                            </button>
                          )}
                        </>
                      )}
                    </span>
                  </div>
                );
              })}
            </div>
          )}
        </div>
        <div class="manager-group">
          <span class="lbl" id="manager-styles">
            Styles
          </span>
          {styles === null ? (
            <span class="manager-meta">Reading the styles…</span>
          ) : (
            <div class="manager-list" role="list" aria-labelledby="manager-styles">
              {styles.map((s) => {
                const deleting = confirmDelete?.kind === 'style' && confirmDelete.id === s.id;
                return (
                  <div key={s.id} class="manager-row" role="listitem">
                    <span class="manager-text">
                      <span class="manager-name">{s.name}</span>
                      <span class="manager-meta">
                        Used by {s.usedByTemplates} {s.usedByTemplates === 1 ? 'template' : 'templates'} · {s.builtIn ? 'Built in' : 'Yours'}
                      </span>
                    </span>
                    <span class="manager-actions">
                      {deleting ? (
                        <>
                          <span class="manager-confirm">Delete {s.name}?</span>
                          <button class="btn g small-btn" type="button" ref={(el) => el?.focus()} onClick={() => { setConfirmDelete(null); }}>
                            Keep
                          </button>
                          <button class="btn d small-btn" type="button" onClick={() => { run(bridge.call('styles.delete', { styleId: s.id }), `${s.name} deleted`); }}>
                            Delete
                          </button>
                        </>
                      ) : (
                        <>
                          <button
                            class="btn g small-btn"
                            type="button"
                            aria-label={`Open ${s.name} in the style editor`}
                            onClick={() => {
                              onClose();
                              openStyleEditor(
                                store,
                                (route) => {
                                  router.navigate(route);
                                },
                                s.id,
                                SETTINGS_RETURN,
                              );
                            }}
                          >
                            Open
                          </button>
                          <button class="btn g small-btn" type="button" aria-label={`Duplicate ${s.name}`} onClick={() => { run(bridge.call('styles.duplicate', { styleId: s.id }), `${s.name} duplicated`); }}>
                            Duplicate
                          </button>
                          {s.builtIn ? (
                            <button class="btn g small-btn" type="button" disabled={s.customized !== true && s.modifiedAt === null} aria-label={`Reset ${s.name}`} onClick={() => { run(bridge.call('styles.resetBuiltIn', { styleId: s.id }), `${s.name} is as it shipped`); }}>
                              Reset
                            </button>
                          ) : (
                            <button class="btn g small-btn" type="button" aria-label={`Delete ${s.name}`} onClick={() => { setConfirmDelete({ kind: 'style', id: s.id }); }}>
                              Delete
                            </button>
                          )}
                        </>
                      )}
                    </span>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>
    </SideSheet>
  );
}
