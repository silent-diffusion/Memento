import { describe, expect, it, vi } from 'vitest';
import type { BridgeClient } from '../../bridge/client';
import { updateFooterLine } from '../../format/footer';
import { createStore, showUpdateReady } from '../../state/store';
import { parseLicenses } from './about';

describe('About and updates (H1)', () => {
  it('reads THIRD-PARTY.md into paragraphs and table rows, without Markdown marks', () => {
    const blocks = parseLicenses(
      [
        '# Third-party components',
        '',
        'Every bundled dependency, its `license`.',
        '',
        '| Component | License | Used for |',
        '|---|---|---|',
        '| Preact | MIT | UI framework |',
        '| NtvLibs | [VS terms](https://example.org/terms) | `vcruntime140.dll` |',
        '',
        'Models:',
      ].join('\n'),
    );

    expect(blocks).toEqual([
      { kind: 'text', text: 'Every bundled dependency, its license.' },
      {
        kind: 'table',
        header: ['Component', 'License', 'Used for'],
        rows: [
          ['Preact', 'MIT', 'UI framework'],
          ['NtvLibs', 'VS terms (https://example.org/terms)', 'vcruntime140.dll'],
        ],
      },
      { kind: 'text', text: 'Models:' },
    ]);
  });

  it('names the update download in the footer', () => {
    expect(updateFooterLine({ downloading: true, percent: 41.6, version: '0.5.1' })).toBe('Downloading Memento 0.5.1 · 42%');
    expect(updateFooterLine({ downloading: true, percent: null, version: null })).toBe('Downloading an update');
  });

  it('offers the restart in a toast once the update is downloaded, and only applies on the click', () => {
    const store = createStore(false);
    const call = vi.fn(() => Promise.resolve({}));
    const bridge = { call } as unknown as BridgeClient;

    showUpdateReady(bridge, store, '0.5.1');

    const toast = store.toasts.items.value[0];
    expect(toast?.title).toBe('Memento 0.5.1 is ready to install');
    expect(toast?.body).toContain('installs the next time Memento starts');
    expect(call).not.toHaveBeenCalled();
    toast?.actions[0]?.run();
    expect(call).toHaveBeenCalledWith('updates.apply');
    expect(toast?.actions.map((a) => a.label)).toEqual(['Restart to update to 0.5.1', 'Later']);
  });
});
