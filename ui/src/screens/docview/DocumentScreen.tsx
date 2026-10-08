// Document viewer (DESIGN.md §12), transcribed from design/renders/DocView.dc.html: the paper from
// documents.renderHtml, editable in place and saved as you type; timestamp chips open Review at
// that moment; "How this was made", versions with Restore, Regenerate, Export and More.
import type { JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import type { DocumentSummary, DocumentVersion, GenerationRecord, Project, Style } from '../../bridge/types';
import { MoreIcon } from '../../components/icons';
import { SpokeHeader } from '../../components/SpokeHeader';
import { UndoButton } from '../../components/UndoButton';
import { ActionMenu } from '../../components/Menus';
import { Dialog } from '../../components/Overlay';
import { RegenerateIcon } from '../../components/paper/icons';
import { Paper } from '../../components/paper/Paper';
import { serializePaper } from '../../components/paper/paperDom';
import { PROVIDER_SHORT, whenWords } from '../../format/documents';
import { useServices } from '../../state/context';
import { copyDocument } from '../../state/copy';
import { MERGE_WINDOW_MS, undoOf } from '../../state/undo';
import { cancelGeneration, generationOf } from '../builder/generation';
import { holdLiveOutput, liveOutputOf, openLiveOutput, releaseLiveOutput } from '../builder/liveOutput';
import { ProgressCard } from '../builder/PreviewPanel';
import '../builder/builder.css';
import './docview.css';
import { blockKind, currentBlock, insertTable, insertTimestamp, selectionRange, setBlock, toggleInline, toggleList, type BlockKind } from './editing';
import { playheadOf } from './playhead';
import { createEditSaver, type EditSaver, type SaveState } from './saver';
import { HowMade, numberedVersions, versionMeta, Versions } from './SideColumn';
import { restoreDocumentVersion, VersionBanner, type ViewedVersion } from './VersionBanner';
import { Toolbar, type ToolbarCommand } from './Toolbar';

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

type Confirm =
  | { kind: 'restore'; version: DocumentVersion; number: number }
  | { kind: 'delete' }
  | { kind: 'makeTemplate' };

/** `versionId` (after 1.2.0): open that kept version read-only first (Review's History). */
export function DocumentScreen({ recordingId, documentId, versionId }: { recordingId: string; documentId: string; versionId?: string }): JSX.Element {
  const services = useServices();
  const { bridge, store, router } = services;
  const now = services.now();
  const [summary, setSummary] = useState<DocumentSummary | null>(null);
  const [record, setRecord] = useState<GenerationRecord | null>(null);
  const [html, setHtml] = useState<string | null>(null);
  const [project, setProject] = useState<Project | null>(null);
  const [styles, setStyles] = useState<Style[]>([]);
  const [versions, setVersions] = useState<DocumentVersion[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [save, setSave] = useState<SaveState>({ kind: 'saved', version: null });
  const [kind, setKind] = useState<BlockKind | null>(null);
  const [marks, setMarks] = useState({ bold: false, italic: false });
  const [confirm, setConfirm] = useState<Confirm | null>(null);
  const [confirmError, setConfirmError] = useState<string | null>(null);
  const [templateName, setTemplateName] = useState('');
  const [reload, setReload] = useState(0);
  const [versionsReload, setVersionsReload] = useState(0);
  // After 1.2.0: a kept version open read-only in place of the paper, with a banner to restore it or go back.
  const [viewing, setViewing] = useState<ViewedVersion | null>(versionId === undefined ? null : { versionId, html: null, error: null });
  const [restoringVersion, setRestoringVersion] = useState(false);
  const article = useRef<HTMLElement | null>(null);
  const lastRange = useRef<Range | null>(null);
  const saverRef = useRef<EditSaver | null>(null);
  const history = store.settings.value?.history ?? null;
  // Undo (state/undo.ts): a run of typing in the paper is one step; Undo puts the paper back as it was and saves it.
  const undo = undoOf(store);
  const undoScope = `document:${recordingId}/${documentId}`;
  const [paperKey, setPaperKey] = useState(0);
  const burst = useRef<{ before: string; after: string | null } | null>(null);
  const lastEdit = useRef(0);
  useEffect(() => {
    undo.claim(undoScope);
    return () => {
      undo.release(undoScope);
    };
  }, [undo, undoScope]);

  // Live output: a regeneration into this document (its progress card), and the exchange that wrote it, kept
  // readable until this viewer is left.
  useEffect(() => {
    const holder = `viewer:${documentId}`;
    holdLiveOutput(store, holder);
    return () => {
      releaseLiveOutput(store, holder);
    };
  }, [store, documentId]);
  const job = generationOf(store).value;
  const live = liveOutputOf(store).value;
  const regenerating = job !== null && job.recordingId === recordingId && job.documentId === documentId && (job.phase === 'starting' || job.phase === 'running' || job.phase === 'confirm') ? job : null;
  const finishedLive = live !== null && live.ended && live.recordingId === recordingId && live.documentId === documentId ? live : null;

  // The document, its paper and the recording it belongs to.
  useEffect(() => {
    let live = true;
    Promise.all([
      bridge.call('documents.get', { recordingId, documentId }),
      bridge.call('documents.renderHtml', { recordingId, documentId, mode: 'view' }),
      bridge.call('project.get', { recordingId }),
      bridge.call('styles.list'),
    ])
      .then(([doc, rendered, p, s]) => {
        if (!live) {
          return;
        }
        setSummary(doc.summary);
        setRecord(doc.document.record);
        setName(doc.summary.name);
        setHtml(rendered.html);
        setProject(p);
        setStyles(s.styles);
        setError(null);
      })
      .catch((e: unknown) => {
        if (live) {
          setError(messageOf(e));
        }
      });
    return () => {
      live = false;
    };
  }, [bridge, recordingId, documentId, reload]);

  useEffect(() => {
    if (history?.keepVersions !== true) {
      setVersions(null);
      return undefined;
    }
    let live = true;
    bridge
      .call('documents.versions', { recordingId, documentId })
      .then(({ versions: list }) => {
        if (live) {
          setVersions(list);
        }
      })
      .catch(() => {
        if (live) {
          setVersions([]);
        }
      });
    return () => {
      live = false;
    };
  }, [bridge, recordingId, documentId, history?.keepVersions, versionsReload]);

  // A new version written elsewhere (regenerated, restored) redraws the paper; our own edits only refresh the side.
  useEffect(
    () =>
      bridge.on('documents.changed', (payload) => {
        if (payload.recordingId !== recordingId || payload.documentId !== documentId) {
          return;
        }
        if (payload.reason === 'generated' || payload.reason === 'restored') {
          setReload((n) => n + 1);
        }
        setVersionsReload((n) => n + 1);
      }),
    [bridge, recordingId, documentId],
  );

  // Edits save as you type.
  useEffect(() => {
    const saver = createEditSaver({
      bridge,
      recordingId,
      documentId,
      read: () => (article.current === null ? null : serializePaper(article.current)),
      onState: setSave,
    });
    saverRef.current = saver;
    return () => {
      void saver.flush().finally(() => {
        saver.dispose();
      });
    };
  }, [bridge, recordingId, documentId]);

  // The toolbar follows the caret; the last place in the paper is kept for commands from the toolbar.
  useEffect(() => {
    const onSelection = (): void => {
      const paper = article.current;
      if (paper === null) {
        return;
      }
      const range = selectionRange(paper);
      if (range === null) {
        return;
      }
      lastRange.current = range.cloneRange();
      const block = currentBlock(paper, range);
      setKind(blockKind(block));
      const at = range.commonAncestorContainer;
      const element = at instanceof Element ? at : at.parentElement;
      setMarks({ bold: (element?.closest('strong, b') ?? null) !== null, italic: (element?.closest('em, i') ?? null) !== null });
    };
    document.addEventListener('selectionchange', onSelection);
    return () => {
      document.removeEventListener('selectionchange', onSelection);
    };
  }, []);

  const title = project?.summary.title ?? store.library.value?.recordings.find((r) => r.id === recordingId)?.title ?? 'Recording';
  const styleName = styles.find((s) => s.id === summary?.styleId)?.name ?? summary?.styleId ?? '';
  const pill =
    summary === null
      ? ''
      : [styleName, summary.providerId === null ? 'Written by you' : PROVIDER_SHORT[summary.providerId], whenWords(summary.generatedAt ?? summary.modifiedAt, now)].join(' · ');

  const edited = (): void => {
    lastEdit.current = Date.now();
    saverRef.current?.changed();
  };

  /** The paper as `markup` again (Undo, Redo): drawn afresh and saved like an edit. */
  const restorePaper = (markup: string): void => {
    burst.current = null;
    setHtml(markup);
    setPaperKey((k) => k + 1);
    saverRef.current?.changed();
  };

  /** Before the paper changes: a new step unless typing goes on from the last change. */
  const beginEdit = (): void => {
    const paper = article.current;
    if (paper === null) {
      return;
    }
    const now = Date.now();
    if (burst.current !== null && now - lastEdit.current <= MERGE_WINDOW_MS) {
      return;
    }
    lastEdit.current = now;
    const step = { before: serializePaper(paper), after: null as string | null };
    burst.current = step;
    undo.push({
      label: 'edit document',
      undo: () => {
        step.after = article.current === null ? step.after : serializePaper(article.current);
        restorePaper(step.before);
      },
      redo: () => {
        if (step.after !== null) {
          restorePaper(step.after);
        }
      },
    });
  };

  const range = (): Range | null => {
    const paper = article.current;
    if (paper === null) {
      return null;
    }
    const live = selectionRange(paper);
    if (live !== null) {
      return live;
    }
    const kept = lastRange.current;
    if (kept === null || !paper.contains(kept.commonAncestorContainer)) {
      return null;
    }
    const selection = document.getSelection();
    selection?.removeAllRanges();
    selection?.addRange(kept);
    return kept;
  };

  /** Focus back in the paper with the caret where the command left it (focus alone may move it). */
  const refocus = (paper: HTMLElement): void => {
    const at = selectionRange(paper)?.cloneRange() ?? null;
    paper.focus({ preventScroll: true });
    if (at !== null) {
      const selection = document.getSelection();
      selection?.removeAllRanges();
      selection?.addRange(at);
      lastRange.current = at.cloneRange();
    }
  };

  const command = (cmd: ToolbarCommand): void => {
    const paper = article.current;
    if (paper === null) {
      return;
    }
    const r = range();
    beginEdit();
    const done =
      cmd === 'bold'
        ? toggleInline(paper, r, 'strong')
        : cmd === 'italic'
          ? toggleInline(paper, r, 'em')
          : cmd === 'heading' || cmd === 'paragraph'
            ? setBlock(paper, r, cmd)
            : cmd === 'bulleted' || cmd === 'numbered'
              ? toggleList(paper, r, cmd === 'numbered')
              : insertTable(paper, r);
    if (done) {
      refocus(paper);
      setKind(blockKind(currentBlock(paper, selectionRange(paper))));
      edited();
    }
  };

  const openReview = (seconds: number): void => {
    void saverRef.current?.flush().finally(() => {
      router.navigate({ name: 'review', recordingId, atMs: Math.round(seconds * 1000) });
    });
  };

  const rename = (): void => {
    const trimmed = name.trim();
    if (summary === null || trimmed === summary.name) {
      return;
    }
    if (trimmed === '') {
      setName(summary.name);
      return;
    }
    const before = summary.name;
    const renameTo = async (to: string): Promise<void> => {
      const next = await bridge.call('documents.rename', { recordingId, documentId, name: to });
      setSummary(next);
      setName(next.name);
    };
    renameTo(trimmed)
      .then(() => {
        undo.push({
          label: 'rename document',
          undo: () => renameTo(before),
          redo: () => renameTo(trimmed),
        });
      })
      .catch((e: unknown) => {
        setName(summary.name);
        store.toasts.show({ tone: 'warning', title: 'The document was not renamed', body: `${messageOf(e)} The old name was kept.` });
      });
  };

  const regenerate =
    record === null
      ? null
      : (): void => {
          void saverRef.current?.flush().finally(() => {
            router.navigate({ name: 'builder', recordingId, templateId: record.templateId, documentId });
          });
        };

  const closeConfirm = (): void => {
    setConfirm(null);
    setConfirmError(null);
  };

  const runConfirm = (): void => {
    if (confirm === null) {
      return;
    }
    setConfirmError(null);
    if (confirm.kind === 'restore') {
      bridge
        .call('documents.restoreVersion', { recordingId, documentId, versionId: confirm.version.id })
        .then(() => {
          closeConfirm();
          setReload((n) => n + 1);
          setVersionsReload((n) => n + 1);
          store.toasts.show({ tone: 'ok', title: `Version ${confirm.number} restored`, body: 'The text before it is kept as a version too.' });
        })
        .catch((e: unknown) => {
          setConfirmError(messageOf(e));
        });
    } else if (confirm.kind === 'delete') {
      bridge
        .call('documents.delete', { recordingId, documentId })
        .then(() => {
          saverRef.current?.dispose();
          closeConfirm();
          router.navigate({ name: 'review', recordingId });
        })
        .catch((e: unknown) => {
          setConfirmError(messageOf(e));
        });
    } else {
      bridge
        .call('documents.makeTemplate', { recordingId, documentId, name: templateName.trim() })
        .then((template) => {
          closeConfirm();
          store.toasts.show({
            tone: 'ok',
            title: `Template “${template.name}” saved`,
            body: 'It is in Create document for every recording.',
            actions: [{ label: 'Open in the builder', run: () => { router.navigate({ name: 'builder', recordingId, templateId: template.id, documentId: null }); } }],
          });
        })
        .catch((e: unknown) => {
          setConfirmError(messageOf(e));
        });
    }
  };

  // A version opened from Review's History or the Versions list: its paper, read once.
  useEffect(() => {
    setViewing(versionId === undefined ? null : { versionId, html: null, error: null });
  }, [versionId]);
  const viewingId = viewing?.versionId ?? null;
  useEffect(() => {
    if (viewingId === null) {
      return undefined;
    }
    let live = true;
    bridge
      .call('documents.getVersion', { recordingId, documentId, versionId: viewingId })
      .then((result) => {
        if (live) {
          setViewing((v) => (v?.versionId === viewingId ? { ...v, html: result.html } : v));
        }
      })
      .catch((e: unknown) => {
        if (live) {
          setViewing((v) => (v?.versionId === viewingId ? { ...v, error: messageOf(e) } : v));
        }
      });
    return () => {
      live = false;
    };
  }, [bridge, recordingId, documentId, viewingId]);
  const openVersion = (version: DocumentVersion): void => {
    void saverRef.current?.flush().finally(() => {
      setViewing(version.id === 'current' ? null : { versionId: version.id, html: null, error: null });
    });
  };
  const viewed = viewing === null || versions === null ? null : (numberedVersions(versions).find((v) => v.version.id === viewing.versionId) ?? null);
  const restoreViewed = (): void => {
    if (viewing === null) {
      return;
    }
    const number = viewed?.number ?? null;
    const label = number === null ? 'restore an earlier version' : `restore version ${number}`;
    setRestoringVersion(true);
    restoreDocumentVersion(bridge, undo, recordingId, documentId, viewing.versionId, label)
      .then(() => {
        setViewing(null);
        setReload((n) => n + 1);
        setVersionsReload((n) => n + 1);
        undo.announce(number === null ? 'Version restored' : `Version ${number} restored`);
      })
      .catch((e: unknown) => {
        store.toasts.show({ tone: 'warning', title: 'The version was not restored', body: `${messageOf(e)} Nothing was changed.` });
      })
      .finally(() => {
        setRestoringVersion(false);
      });
  };

  const saveWords = save.kind === 'saving' ? 'Saving…' : save.kind === 'pending' ? 'Edited' : save.kind === 'failed' ? 'Not saved' : 'Saved';
  const playhead = playheadOf(store, recordingId);

  return (
    <>
      <SpokeHeader
        backLabel={title}
        onBack={() => {
          void saverRef.current?.flush().finally(() => {
            router.navigate({ name: 'review', recordingId });
          });
        }}
        titleEditor={
          <>
          <label class="sr" for="doc-name">
            Document name
          </label>
          <input
            id="doc-name"
            class="field rec-title-input doc-name"
            type="text"
            value={name}
            maxLength={120}
            autocomplete="off"
            disabled={summary === null}
            onInput={(event) => {
              setName(event.currentTarget.value);
            }}
            onBlur={rename}
            onKeyDown={(event) => {
              if (event.key === 'Enter') {
                event.preventDefault();
                rename();
              } else if (event.key === 'Escape' && summary !== null) {
                setName(summary.name);
              }
            }}
          />
          {summary === null ? null : <span class="pill done">{pill}</span>}
          <span class={save.kind === 'failed' ? 'doc-saved doc-saved--failed' : 'doc-saved'} role="status" aria-live="polite">
            {saveWords}
          </span>
          </>
        }
        actions={
          <>
          <UndoButton />
          <button class="btn ghost spoke-ghost doc-action" type="button" disabled={regenerate === null} title={regenerate === null ? 'Written by hand: there is nothing to regenerate' : undefined} onClick={() => regenerate?.()}>
            <RegenerateIcon size={16} />
            Regenerate
          </button>
          <button
            class="btn ghost spoke-ghost"
            type="button"
            aria-haspopup="dialog"
            onClick={() => {
              void saverRef.current?.flush().finally(() => {
                store.dialog.value = { kind: 'export', recordingId, documentIds: [documentId] };
              });
            }}
          >
            Export
          </button>
          <ActionMenu
            label="More actions"
            triggerClass="icon-btn spoke-more"
            actions={[
              {
                label: 'Duplicate',
                run: () => {
                  void saverRef.current?.flush().finally(() => {
                    bridge
                      .call('documents.duplicate', { recordingId, documentId })
                      .then((copy) => {
                        router.navigate({ name: 'document', recordingId, documentId: copy.id });
                      })
                      .catch((e: unknown) => {
                        store.toasts.show({ tone: 'warning', title: 'The document was not duplicated', body: `${messageOf(e)} Nothing was changed.` });
                      });
                  });
                },
              },
              {
                // After 1.2.0: Markdown for any program and the formatted page for Word and Outlook, written by the host.
                label: 'Copy to clipboard',
                run: () => {
                  void saverRef.current?.flush().finally(() => {
                    void copyDocument(bridge, store, recordingId, documentId, summary?.name ?? 'The document').then((done) => {
                      if (done !== null) {
                        undo.announce(done);
                      }
                    });
                  });
                },
              },
              {
                label: 'Make template',
                disabled: record === null,
                ...(record === null ? { note: 'Only for generated documents' } : {}),
                run: () => {
                  setTemplateName(`${summary?.name ?? 'Document'} template`);
                  setConfirm({ kind: 'makeTemplate' });
                },
              },
              {
                label: 'Delete',
                run: () => {
                  setConfirm({ kind: 'delete' });
                },
              },
            ]}
          >
            <MoreIcon size={18} />
          </ActionMenu>
          </>
        }
      />

      {error !== null ? (
        <main class="spoke-main">
          <div class="placeholder-card">
            <p class="placeholder-text" role="alert">
              {error}
            </p>
          </div>
        </main>
      ) : (
        <main class="doc-main">
          <div class="doc-layout">
            <section class="doc-column" aria-label="Document">
              {viewing !== null ? (
                <VersionBanner
                  name={summary?.name ?? 'The document'}
                  number={viewed?.number ?? null}
                  meta={viewed === null ? null : versionMeta(viewed.version, now)}
                  error={viewing.error}
                  ready={viewing.html !== null}
                  restoring={restoringVersion}
                  onRestore={restoreViewed}
                  onBack={() => {
                    setViewing(null);
                  }}
                />
              ) : (
              <Toolbar
                kind={kind}
                bold={marks.bold}
                italic={marks.italic}
                onCommand={command}
                onTimestamp={(seconds) => {
                  const paper = article.current;
                  beginEdit();
                  if (paper !== null && insertTimestamp(paper, range(), seconds)) {
                    refocus(paper);
                    edited();
                  }
                }}
                playheadSeconds={playhead === null ? null : playhead / 1000}
                durationSeconds={(project?.summary.durationMs ?? 0) / 1000}
              />
              )}
              {save.kind === 'failed' ? (
                <p class="doc-save-error" role="alert">
                  {save.message}
                </p>
              ) : null}
              <div
                class="doc-paper-undo"
                onBeforeInput={() => {
                  beginEdit();
                }}
              >
                {viewing !== null ? (
                  <Paper key={`version-${viewing.versionId}`} html={viewing.html} label={`${summary?.name ?? 'Document'}, an earlier version (read only)`} onTimestamp={openReview} loadingText="Opening this version…" class="doc-paper doc-paper--version" />
                ) : (
                  <Paper key={paperKey} html={html} label={summary?.name ?? 'Document'} editable articleRef={article} onEdit={edited} onTimestamp={openReview} loadingText="Opening the document…" class="doc-paper" />
                )}
              </div>
            </section>
            <aside class="doc-side" aria-label="About this document">
              {regenerating === null ? null : (
                <ProgressCard
                  job={regenerating}
                  moduleName={(id) => regenerating.template.rows.flatMap((r) => r.modules).find((m) => m.id === id)?.customTitle ?? null}
                  onCancel={() => {
                    void cancelGeneration(bridge, store);
                  }}
                  onShowLiveOutput={() => {
                    openLiveOutput(store);
                  }}
                />
              )}
              {summary === null ? null : <HowMade summary={summary} record={record} styleName={styleName} now={now} onRegenerate={regenerate} />}
              {finishedLive === null || regenerating !== null ? null : (
                <div class="doc-live">
                  <button
                    class="btn ghost side-btn"
                    type="button"
                    aria-haspopup="dialog"
                    onClick={() => {
                      openLiveOutput(store);
                    }}
                  >
                    Show live output
                  </button>
                  <p class="doc-live-note">The exchange with {finishedLive.provider.kind === 'local' ? 'the local model' : finishedLive.provider.name} that wrote this version. It is not saved and goes when you leave this document.</p>
                </div>
              )}
              <Versions
                history={history}
                versions={versions}
                now={now}
                onRestore={(version, number) => {
                  setConfirm({ kind: 'restore', version, number });
                }}
                onOpen={openVersion}
                openId={viewing?.versionId ?? null}
              />
              <p class="doc-footnote">Timestamps link back to the transcript. Edits save as you type and stay inside this recording.</p>
            </aside>
          </div>
        </main>
      )}

      {confirm?.kind === 'restore' ? (
        <Dialog
          titleId="restore-doc-title"
          title={`Restore version ${confirm.number}?`}
          onEscape={closeConfirm}
          actions={
            <>
              <button class="btn g" type="button" onClick={closeConfirm}>
                Cancel
              </button>
              <button class="btn p" type="button" data-autofocus onClick={runConfirm}>
                Restore
              </button>
            </>
          }
        >
          <p class="dialog-body">
            {summary?.name ?? 'The document'} goes back to version {confirm.number} ({versionMeta(confirm.version, now)}). The text you have now is kept as a version, so nothing is
            lost.
          </p>
          {confirmError === null ? null : (
            <p class="dialog-error" role="alert">
              {confirmError}
            </p>
          )}
        </Dialog>
      ) : null}
      {confirm?.kind === 'delete' ? (
        <Dialog
          titleId="delete-doc-title"
          title={`Delete “${summary?.name ?? 'this document'}”?`}
          onEscape={closeConfirm}
          actions={
            <>
              <button class="btn g" type="button" data-autofocus onClick={closeConfirm}>
                Cancel
              </button>
              <button class="btn d" type="button" onClick={runConfirm}>
                Delete
              </button>
            </>
          }
        >
          <p class="dialog-body">
            The document and its {versions === null || versions.length <= 1 ? 'history' : `${versions.length} versions`} are removed from {title}. The recording, its transcript and
            copies you exported are not touched. This cannot be undone.
          </p>
          {confirmError === null ? null : (
            <p class="dialog-error" role="alert">
              {confirmError}
            </p>
          )}
        </Dialog>
      ) : null}
      {confirm?.kind === 'makeTemplate' ? (
        <Dialog
          titleId="make-template-title"
          title="Make a template"
          onEscape={closeConfirm}
          actions={
            <>
              <button class="btn g" type="button" onClick={closeConfirm}>
                Cancel
              </button>
              <button class="btn p" type="submit" form="make-template-form" disabled={templateName.trim() === ''}>
                Save template
              </button>
            </>
          }
        >
          <p class="dialog-body">The structure, instructions, inputs and style of {summary?.name ?? 'this document'} become a template you can use for any recording.</p>
          <form
            id="make-template-form"
            class="dialog-form"
            onSubmit={(event) => {
              event.preventDefault();
              runConfirm();
            }}
          >
            <label class="field-label" for="make-template-name">
              Template name
            </label>
            <input
              id="make-template-name"
              class="field dialog-field"
              type="text"
              value={templateName}
              maxLength={80}
              data-autofocus
              onInput={(event) => {
                setTemplateName(event.currentTarget.value);
              }}
            />
          </form>
          {confirmError === null ? null : (
            <p class="dialog-error" role="alert">
              {confirmError}
            </p>
          )}
        </Dialog>
      ) : null}
    </>
  );
}
