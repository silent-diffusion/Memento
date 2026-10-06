import type { JSX } from 'preact';
import type { FooterStatusPayload } from '../bridge/types';
import { engineLine, storageLine } from '../format/footer';

interface StatusFooterProps {
  /** Latest `status.footer` from the host; `null` until the first one arrives. */
  status: FooterStatusPayload | null;
}

/** The 36 px status footer (DESIGN.md §3): engine state left, storage statement right. */
export function StatusFooter({ status }: StatusFooterProps): JSX.Element {
  const engine = engineLine(status?.engine ?? null);
  const storage = storageLine(status?.storage ?? null);
  return (
    <footer class="app-footer">
      <span class="footer-engine">
        <span class={`status-dot status-dot--${engine.tone}`} aria-hidden="true" />
        {engine.text}
      </span>
      <span class={storage.low ? 'footer-storage footer-storage--low' : 'footer-storage'}>{storage.text}</span>
    </footer>
  );
}
