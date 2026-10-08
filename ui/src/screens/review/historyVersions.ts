// Review's History opening versions (after 1.2.0): history.links while the History tab shows, and restoring a
// transcript version from its banner through the existing transcript.restoreVersion, as one Undo step.
import { useEffect, useState } from 'preact/hooks';
import type { BridgeClient } from '../../bridge/client';
import type { HistoryLink } from '../../bridge/types';
import type { UndoManager } from '../../state/undo';

/** history.links for the recording while `active`; read again whenever `revision` changes. Null while reading. */
export function useHistoryLinks(bridge: BridgeClient, recordingId: string, active: boolean, revision: unknown): HistoryLink[] | null {
  const [links, setLinks] = useState<HistoryLink[] | null>(null);
  useEffect(() => {
    if (!active) {
      return undefined;
    }
    let live = true;
    bridge
      .call('history.links', { recordingId })
      .then(({ links: list }) => {
        if (live) {
          setLinks(list);
        }
      })
      .catch((e: unknown) => {
        console.warn('[review] history.links failed', e);
        if (live) {
          setLinks([]);
        }
      });
    return () => {
      live = false;
    };
  }, [bridge, recordingId, active, revision]);
  return links;
}

/**
 * Restores a kept transcript version. The transcript it replaces is kept as a version by the host (when version
 * history is on); that new version is what Undo restores, and Redo restores this one again. Without version history
 * nothing is kept, so there is nothing for Undo to bring back and no step is added.
 */
export async function restoreTranscriptVersion(bridge: BridgeClient, undo: UndoManager, recordingId: string, versionId: string, label: string): Promise<void> {
  const restore = (id: string) => bridge.call('transcript.restoreVersion', { recordingId, versionId: id });
  const before = new Set((await bridge.call('transcript.versions', { recordingId })).versions.map((v) => v.id));
  await restore(versionId);
  const kept = (await bridge.call('transcript.versions', { recordingId })).versions.find((v) => !before.has(v.id)) ?? null;
  if (kept !== null) {
    undo.push({
      label,
      undo: () => restore(kept.id),
      redo: () => restore(versionId),
    });
  }
}
