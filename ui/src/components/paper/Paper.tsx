// The document paper (DESIGN.md §5.18): the host's paper HTML shown as it is, in the Builder
// preview (skeletons), the Style editor (sample page) and the Document viewer (editable in place).
// The DOM inside is managed here, not by Preact, so typing in the viewer is never re-rendered away.
import type { JSX } from 'preact';
import { useEffect, useLayoutEffect, useRef } from 'preact/hooks';
import './paper.css';
import './paper-host.css';
import { importPaper, timestampOf } from './paperDom';

interface PaperProps {
  /** The host's HTML; null while it loads. */
  html: string | null;
  /** Accessible name of the page ("Meeting minutes", "Preview of the layout"). */
  label: string;
  /** The viewer: the paper is a rich-text field; data blocks and chips stay locked (contenteditable=false). */
  editable?: boolean;
  /** Every edit inside the paper (the viewer saves after a pause). */
  onEdit?: () => void;
  /** A timestamp chip, timeline time or quote time was clicked: open Review there. */
  onTimestamp?: (seconds: number) => void;
  /** The live article, for the formatting toolbar and saving. */
  articleRef?: { current: HTMLElement | null };
  /** Shown while `html` is null. */
  loadingText?: string;
  class?: string;
}

export function Paper({ html, label, editable = false, onEdit, onTimestamp, articleRef, loadingText = 'Drawing the page…', class: extra }: PaperProps): JSX.Element {
  const host = useRef<HTMLDivElement | null>(null);
  const handlers = useRef({ onEdit, onTimestamp });
  handlers.current = { onEdit, onTimestamp };

  useLayoutEffect(() => {
    const el = host.current;
    if (el === null) {
      return;
    }
    el.replaceChildren();
    if (articleRef !== undefined) {
      articleRef.current = null;
    }
    if (html === null) {
      return;
    }
    const article = importPaper(html);
    if (article === null) {
      const p = document.createElement('p');
      p.className = 'paper-host-error';
      p.textContent = 'This page could not be shown: the host sent no document paper.';
      el.appendChild(p);
      return;
    }
    if (editable) {
      article.setAttribute('contenteditable', 'true');
      article.setAttribute('role', 'textbox');
      article.setAttribute('aria-multiline', 'true');
      article.setAttribute('spellcheck', 'true');
    }
    article.setAttribute('aria-label', label);
    el.appendChild(article);
    if (articleRef !== undefined) {
      articleRef.current = article;
    }
  }, [html, editable]);

  // The accessible name can change (a rename) without redrawing the page.
  useEffect(() => {
    host.current?.querySelector('article.paper')?.setAttribute('aria-label', label);
  }, [label]);

  if (html === null) {
    return (
      <div key="loading" class={['paper-host', 'paper-host--loading', extra ?? ''].filter((c) => c !== '').join(' ')} aria-busy="true">
        {loadingText}
      </div>
    );
  }
  // Preact renders no children here: the article is put in by the layout effect above.
  return (
    <div
      key="paper"
      ref={host}
      class={['paper-host', extra ?? ''].filter((c) => c !== '').join(' ')}
      onClick={(event) => {
        const seconds = timestampOf(event.target);
        if (seconds !== null) {
          // Chips are in-page links (#t=…); they open Review rather than move the page.
          event.preventDefault();
          handlers.current.onTimestamp?.(seconds);
        }
      }}
      onInput={() => {
        handlers.current.onEdit?.();
      }}
    />
  );
}
