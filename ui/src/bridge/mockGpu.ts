// The browser-preview host's graphics card (docs/BRIDGE.md, GpuMemoryInfo): a 12 GB card with the desktop's own
// memory, or with `?vram=low` the same card while Ollama's model server holds most of it, worded as the host's
// GpuMemoryWording words it (DESIGN.md §17: the thing, the amount, the fix).
import type { GpuMemoryHolder, GpuMemoryInfo } from './types';

/** `?vram=`: `low` stands for another app (Ollama) holding the card's memory. */
export type VramFlag = 'ok' | 'low';

const GIB = 1024 ** 3;
const DESKTOP: GpuMemoryHolder = { processName: 'dwm.exe', description: 'Windows desktop (dwm.exe)', bytes: 0.4 * GIB, memento: false, startedBy: null };
const OLLAMA: GpuMemoryHolder = { processName: 'llama-server.exe', description: 'Ollama (llama-server.exe)', bytes: 9.4 * GIB, memento: false, startedBy: null };

/** The card as `engine.status` and `providers.list` describe it. */
export function mockGpuMemory(vram: VramFlag): GpuMemoryInfo {
  const low = vram === 'low';
  const free = (low ? 2.0 : 9.2) * GIB;
  return {
    gpuName: 'NVIDIA GeForce RTX 4070',
    totalBytes: 11.99 * GIB,
    freeBytes: free,
    usedBytes: 12 * GIB - free,
    holders: low ? [OLLAMA, DESKTOP] : [DESKTOP],
    summary: low
      ? 'The graphics card has 2.0 GB of 12 GB free. Ollama (llama-server.exe) is using 9.4 GB and Windows desktop (dwm.exe) 0.4 GB.'
      : 'The graphics card has 9.2 GB of 12 GB free. Windows desktop (dwm.exe) is using 0.4 GB.',
  };
}

/** Why a model does not run on the card with `?vram=low`, e.g. "… Qwen3.5 4B needs 3.4 GB on the card, so it runs on the processor …". */
export function mockGpuNote(vram: VramFlag, modelName: string, neededGb: string, instead: string): string | null {
  return vram === 'low'
    ? `${mockGpuMemory(vram).summary} ${modelName} needs ${neededGb} on the card, so ${instead} until that memory is free. To use the card, close Ollama (llama-server.exe) or wait until it lets go of the memory, then check again.`
    : null;
}
