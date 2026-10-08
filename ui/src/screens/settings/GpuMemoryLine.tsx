// The graphics card's memory under a model line in Settings (DESIGN.md §17 and §11): what is free, who holds the
// rest and, when a model cannot use the card, why and what to do; with "Check again" so the owner can close the
// other program and see the number change without restarting Memento (engine.refresh).
import type { JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import type { EngineStatusResult, GpuMemoryInfo } from '../../bridge/types';
import { useServices } from '../../state/context';

/**
 * "Check again": engine.refresh reads the card with nothing cached, then the Settings snapshot is read again (the model
 * in effect follows the card's free memory) and the section re-reads what it shows (`onFresh`).
 */
export function useCheckAgain(onFresh: (status: EngineStatusResult) => Promise<void> | void): { checking: boolean; check: () => void } {
  const { bridge, store } = useServices();
  const [checking, setChecking] = useState(false);
  const mounted = useRef(true);
  useEffect(
    () => () => {
      mounted.current = false;
    },
    [],
  );
  const check = (): void => {
    if (checking) {
      return;
    }
    setChecking(true);
    bridge
      .call('engine.refresh')
      .then(async (status) => {
        const fresh = await bridge.call('settings.get');
        if (mounted.current) {
          store.settings.value = fresh;
          await onFresh(status);
        }
      })
      .catch((e: unknown) => {
        console.warn('[settings] engine.refresh failed', e);
      })
      .finally(() => {
        if (mounted.current) {
          setChecking(false);
        }
      });
  };
  return { checking, check };
}

interface GpuMemoryLineProps {
  memory: GpuMemoryInfo | null;
  /** The host's sentence when the model cannot use the card (`note` / `gpuNote`); the summary is shown otherwise. */
  note: string | null;
  checking: boolean;
  onCheck: () => void;
}

export function GpuMemoryLine({ memory, note, checking, onCheck }: GpuMemoryLineProps): JSX.Element | null {
  if (memory === null) {
    return null;
  }
  return (
    <div class={note === null ? 'gpu-memory' : 'gpu-memory gpu-memory--short'} data-testid="gpu-memory">
      <p class="gpu-memory-text" role="status" aria-live="polite">
        {note ?? memory.summary}
      </p>
      <button class="btn ghost small-btn gpu-memory-check" type="button" disabled={checking} aria-busy={checking} onClick={onCheck}>
        {checking ? 'Checking…' : 'Check again'}
      </button>
    </div>
  );
}

/** The provider's detail without the card sentence it starts with, which the line beneath shows instead. */
export function withoutGpuNote(detail: string | null, gpuNote: string | null): string | null {
  if (detail === null || gpuNote === null || !detail.startsWith(gpuNote)) {
    return detail;
  }
  const rest = detail.slice(gpuNote.length).trim();
  return rest === '' ? null : rest;
}
