// Review › Documents (DESIGN.md §9, renders/Review.dc.html): one card per document saved inside the
// recording, Create document (the Builder with the default template) and Write a document.
import type { JSX } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import type { DocumentSummary, Style } from '../../bridge/types';
import { DocumentIcon } from '../../components/icons';
import { PROVIDER_SHORT, whenInline } from '../../format/documents';
import { useServices } from '../../state/context';
import './docview.css';

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

/** "Corporate style · generated yesterday, 5:14 PM · Claude" or "Written by you · Oct 5, 9:12 AM". */
export function documentCardMeta(doc: DocumentSummary, styleName: string, now: Date): string {
  if (doc.kind === 'written' || doc.generatedAt === null || doc.providerId === null) {
    return `${styleName} style · written by you · ${whenInline(doc.modifiedAt, now)}`;
  }
  return `${styleName} style · generated ${whenInline(doc.generatedAt, now)} · ${PROVIDER_SHORT[doc.providerId]}`;
}

/** "Edited by you · 2 versions", "1 version". */
export function documentCardVersions(doc: DocumentSummary): string {
  const versions = `${doc.versions} ${doc.versions === 1 ? 'version' : 'versions'}`;
  return doc.versions > 1 ? `Edited · ${versions}` : versions;
}

export function DocumentsTab({ recordingId, onCreate }: { recordingId: string; onCreate?: () => void }): JSX.Element {
  const services = useServices();
  const { bridge, store, router } = services;
  const now = services.now();
  const [documents, setDocuments] = useState<DocumentSummary[] | null>(null);
  const [styles, setStyles] = useState<Style[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [reload, setReload] = useState(0);
  const [writing, setWriting] = useState(false);

  useEffect(() => {
    let live = true;
    Promise.all([bridge.call('documents.list', { recordingId }), bridge.call('styles.list')])
      .then(([list, s]) => {
        if (live) {
          setDocuments(list.documents);
          setStyles(s.styles);
          setError(null);
        }
      })
      .catch((e: unknown) => {
        if (live) {
          setError(`The documents could not be listed. ${messageOf(e)}`);
        }
      });
    return () => {
      live = false;
    };
  }, [bridge, recordingId, reload]);

  useEffect(
    () =>
      bridge.on('documents.changed', (payload) => {
        if (payload.recordingId === recordingId) {
          setReload((n) => n + 1);
        }
      }),
    [bridge, recordingId],
  );

  const write = (): void => {
    if (writing) {
      return;
    }
    setWriting(true);
    const styleId = store.settings.value?.documents.defaultStyleId ?? 'corporate';
    const taken = new Set((documents ?? []).map((d) => d.name));
    let name = 'Notes';
    for (let n = 2; taken.has(name); n++) {
      name = `Notes ${n}`;
    }
    bridge
      .call('documents.create', { recordingId, name, styleId })
      .then((doc) => {
        setWriting(false);
        router.navigate({ name: 'document', recordingId, documentId: doc.id });
      })
      .catch((e: unknown) => {
        setWriting(false);
        store.toasts.show({ tone: 'warning', title: 'The document was not created', body: `${messageOf(e)} Nothing was changed.` });
      });
  };

  const styleName = (id: string): string => styles.find((s) => s.id === id)?.name ?? id;

  return (
    <div class="docs">
      {error !== null ? (
        <p class="docs-note" role="alert">
          {error}
        </p>
      ) : documents === null ? (
        <p class="docs-note">Reading the documents…</p>
      ) : documents.length === 0 ? (
        <div class="docs-empty">
          <span class="docs-empty-tile" aria-hidden="true">
            <DocumentIcon size={18} />
          </span>
          <span class="docs-empty-title">No documents yet</span>
          <span class="docs-empty-text">Minutes, summaries and notes are built from the transcript, or written by you.</span>
        </div>
      ) : (
        <div class="doc-cards">
          {documents.map((doc) => (
            <button
              key={doc.id}
              class="card doc-card"
              type="button"
              onClick={() => {
                router.navigate({ name: 'document', recordingId, documentId: doc.id });
              }}
            >
              <span class="doc-card-name">{doc.name}</span>
              <span class="doc-card-meta">{documentCardMeta(doc, styleName(doc.styleId), now)}</span>
              <span class="doc-card-versions">{documentCardVersions(doc)}</span>
            </button>
          ))}
        </div>
      )}
      <div class="docs-actions">
        <button
          class="btn primary docs-create"
          type="button"
          onClick={() => {
            if (onCreate === undefined) {
              router.navigate({ name: 'builder', recordingId, templateId: null, documentId: null });
            } else {
              onCreate();
            }
          }}
        >
          Create document
        </button>
        <button class="btn ghost docs-write" type="button" disabled={writing} onClick={write}>
          Write a document
        </button>
      </div>
      <p class="docs-note">Documents are saved inside this recording and can be exported on their own.</p>
    </div>
  );
}
