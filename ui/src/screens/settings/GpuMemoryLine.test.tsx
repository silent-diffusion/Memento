import { render } from 'preact';
import { act } from 'preact/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { GpuMemoryInfo } from '../../bridge/types';
import { button, click, mountApp, until, type Harness } from '../../testing/appHarness';
import { GpuMemoryLine, withoutGpuNote } from './GpuMemoryLine';

const GIB = 1024 ** 3;

/** The product owner's PC: an RTX 3060 whose memory Ollama's model server holds. */
const OWNERS_CARD: GpuMemoryInfo = {
  gpuName: 'NVIDIA GeForce RTX 3060 Laptop GPU',
  totalBytes: 5.85 * GIB,
  freeBytes: 0.8 * GIB,
  usedBytes: 5.05 * GIB,
  holders: [{ processName: 'llama-server.exe', description: 'Ollama (llama-server.exe)', bytes: 5 * GIB, memento: false, startedBy: null }],
  summary: 'The graphics card has 0.8 GB of 6 GB free. Ollama (llama-server.exe) is using 5.0 GB.',
};
const OWNERS_NOTE =
  'The graphics card has 0.8 GB of 6 GB free. Ollama (llama-server.exe) is using 5.0 GB. Qwen3.5 4B needs 3.6 GB on the card, so it runs on the processor (several times slower) until that memory is free. '
  + 'To use the card, close Ollama (llama-server.exe) or wait until it lets go of the memory, then check again.';

describe('GpuMemoryLine (DESIGN §17: the card, the amount, who holds it, the fix)', () => {
  let container: HTMLDivElement | null = null;

  afterEach(() => {
    if (container !== null) {
      render(null, container);
      container.remove();
      container = null;
    }
  });

  const mount = async (props: Parameters<typeof GpuMemoryLine>[0]): Promise<HTMLDivElement> => {
    const target = document.createElement('div');
    document.body.append(target);
    container = target;
    await act(async () => {
      render(<GpuMemoryLine {...props} />, target);
      await Promise.resolve();
    });
    return target;
  };

  it('says why the model is on the processor, as a warning, with Check again', async () => {
    const onCheck = vi.fn();
    const root = await mount({ memory: OWNERS_CARD, note: OWNERS_NOTE, checking: false, onCheck });

    expect(root.querySelector('.gpu-memory-text')?.textContent).toBe(OWNERS_NOTE);
    expect(root.querySelector('.gpu-memory--short')).not.toBeNull();
    expect(root.querySelector('[role="status"]')).not.toBeNull();
    await click(root.querySelector('button'));
    expect(onCheck).toHaveBeenCalledTimes(1);
  });

  it('shows the summary when the model fits and nothing while checking twice', async () => {
    const root = await mount({ memory: OWNERS_CARD, note: null, checking: true, onCheck: () => undefined });

    expect(root.querySelector('.gpu-memory-text')?.textContent).toBe('The graphics card has 0.8 GB of 6 GB free. Ollama (llama-server.exe) is using 5.0 GB.');
    expect(root.querySelector('.gpu-memory--short')).toBeNull();
    const check = root.querySelector('button');
    expect(check?.textContent).toBe('Checking…');
    expect(check?.disabled).toBe(true);
  });

  it('shows nothing on a PC without a card', async () => {
    const root = await mount({ memory: null, note: null, checking: false, onCheck: () => undefined });

    expect(root.innerHTML).toBe('');
  });

  it('moves the card sentence out of the provider detail', () => {
    const detail = `${OWNERS_NOTE} Runs on the processor with an 8k context. Nothing leaves this PC.`;

    expect(withoutGpuNote(detail, OWNERS_NOTE)).toBe('Runs on the processor with an 8k context. Nothing leaves this PC.');
    expect(withoutGpuNote(detail, null)).toBe(detail);
    expect(withoutGpuNote(null, OWNERS_NOTE)).toBeNull();
    expect(withoutGpuNote('Something else.', OWNERS_NOTE)).toBe('Something else.');
    expect(withoutGpuNote(OWNERS_NOTE, OWNERS_NOTE)).toBeNull();
  });
});

describe('Settings › Transcription explains the card (against the browser-preview host)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const gpuLine = (): string => (document.querySelector('[data-testid="gpu-memory"] .gpu-memory-text')?.textContent ?? '').replace(/\s+/g, ' ');
  const engineValue = (): string => document.querySelector('[data-testid="engine-value"]')?.textContent ?? '';

  it('names Ollama, the amounts and why Large v3 Turbo transcribes on the processor', async () => {
    h = await mountApp({ name: 'settings', section: 'transcription' }, { m4: { vram: 'low' } });
    await until(() => gpuLine() !== '' && engineValue() !== 'Checking…');

    expect(engineValue()).toBe('Local · CPU');
    expect(gpuLine()).toBe(
      'The graphics card has 2.0 GB of 12 GB free. Ollama (llama-server.exe) is using 9.4 GB and Windows desktop (dwm.exe) 0.4 GB. Large v3 Turbo needs 2.5 GB on the card, '
        + 'so it transcribes on the processor (slower) until that memory is free. To use the card, close Ollama (llama-server.exe) or wait until it lets go of the memory, then check again.',
    );
    expect(document.querySelector('.gpu-memory--short')).not.toBeNull();
  });

  it('shows the free memory with the card in use, reads Settings again on opening, and Check again samples anew', async () => {
    h = await mountApp({ name: 'settings', section: 'transcription' });
    await until(() => gpuLine() !== '');

    expect(engineValue()).toBe('Local · GPU (NVIDIA GeForce RTX 4070)');
    expect(gpuLine()).toBe('The graphics card has 9.2 GB of 12 GB free. Windows desktop (dwm.exe) is using 0.4 GB.');
    // The harness records calls after start-up has read the snapshot; opening Settings reads it again.
    expect(h.callsOf('settings.get').length).toBeGreaterThanOrEqual(1);

    const refreshes = h.callsOf('engine.refresh').length;
    await click(button('Check again'));
    await until(() => h.callsOf('engine.refresh').length === refreshes + 1 && document.querySelector<HTMLButtonElement>('.gpu-memory-check')?.disabled === false);
    expect(gpuLine()).toBe('The graphics card has 9.2 GB of 12 GB free. Windows desktop (dwm.exe) is using 0.4 GB.');
  });
});
