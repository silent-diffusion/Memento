import type { JSX } from 'preact';
import type { FooterStatusPayload } from '../bridge/types';
import { footerStorageLine, statusLine } from '../format/footer';

interface StatusFooterProps {
  /** Latest `status.footer` from the host; `null` until the first one arrives. */
  status: FooterStatusPayload | null;
  /** When the lost source's recording.sourceLost event carried a time, the footer names it. */
  lostAtMs?: number | null;
}

/** The 36 px status footer (DESIGN.md §3, §17 variants): engine or warning left, storage right. */
export function StatusFooter({ status, lostAtMs = null }: StatusFooterProps): JSX.Element {
  const left = statusLine(status, lostAtMs);
  const storage = footerStorageLine(status);
  return (
    <footer class="app-footer">
      <span class="footer-engine">
        <span class={`status-dot status-dot--${left.tone}`} aria-hidden="true" />
        <span>
          {left.strong === undefined ? null : <span class="footer-strong">{left.strong}</span>}
          {left.text}
        </span>
      </span>
      <span class={storage.low ? 'footer-storage footer-storage--low' : 'footer-storage'}>{storage.text}</span>
    </footer>
  );
}
