// The Who spoke editor's Speaking now row (2.6.0): while recording every name has the button and a
// clear once it has marks; outside a recording only names with marks are listed, read-only; a name
// taken off the list takes its marks with it.
import { render } from 'preact';
import { act } from 'preact/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { WhoSpoke } from '../bridge/types';
import { markCount, speakingNowHint, WhoSpokeEditor } from './WhoSpoke';

describe('Who spoke: Speaking now', () => {
  let container: HTMLDivElement;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.append(container);
  });

  afterEach(() => {
    render(null, container);
    container.remove();
  });

  const mount = async (value: WhoSpoke, onChange: (next: WhoSpoke) => void, speakingNow?: () => number): Promise<void> => {
    await act(async () => {
      render(<WhoSpokeEditor idPrefix="t" value={value} participants={[]} settingsDefault={null} onChange={onChange} speakingNow={speakingNow} />, container);
      await Promise.resolve();
    });
  };

  const button = (name: string): HTMLButtonElement | null =>
    [...container.querySelectorAll('button')].find((b) => (b.getAttribute('aria-label') ?? b.textContent.trim()) === name) ?? null;

  const rows = (): string[] => [...container.querySelectorAll('.who-spoke-mark')].map((r) => `${r.querySelector('.who-spoke-mark-name')?.textContent}:${r.querySelector('.who-spoke-mark-count')?.textContent}`);

  it('while recording, every name has Speaking now, which stamps the timer and saves the mark', async () => {
    const onChange = vi.fn<(next: WhoSpoke) => void>();
    await mount({ count: null, names: ['Ana', 'Ben'], marks: [] }, onChange, () => 12_499.6);

    expect(rows()).toEqual(['Ana:', 'Ben:']);
    expect(container.querySelector('.who-spoke-hint')?.textContent).toBe(speakingNowHint(2));
    await act(async () => {
      button('Mark Ben speaking now')?.click();
      await Promise.resolve();
    });

    expect(onChange).toHaveBeenCalledWith({ count: null, names: ['Ana', 'Ben'], marks: [{ name: 'Ben', atMs: 12_500 }] });
    expect(button("Clear Ben's marks")).toBeNull();
  });

  it('counts the marks beside the name and clears them with the ×', async () => {
    const onChange = vi.fn<(next: WhoSpoke) => void>();
    const marks = [{ name: 'Ana', atMs: 5 }, { name: 'ana', atMs: 9 }, { name: 'Ben', atMs: 20 }];
    await mount({ count: 2, names: ['Ana', 'Ben'], marks }, onChange, () => 30);

    expect(rows()).toEqual([`Ana:${markCount(2)}`, `Ben:${markCount(1)}`]);
    await act(async () => {
      button("Clear Ana's marks")?.click();
      await Promise.resolve();
    });

    expect(onChange).toHaveBeenCalledWith({ count: 2, names: ['Ana', 'Ben'], marks: [{ name: 'Ben', atMs: 20 }] });
  });

  it('outside a recording lists only the names with marks, read-only, and says nothing about pressing', async () => {
    await mount({ count: null, names: ['Ana', 'Ben'], marks: [{ name: 'Ben', atMs: 20 }] }, () => undefined);

    expect(rows()).toEqual(['Ben:1 mark']);
    expect(button('Mark Ben speaking now')).toBeNull();
    expect(button("Clear Ben's marks")).toBeNull();
    expect([...container.querySelectorAll('.who-spoke-hint')].map((p) => p.textContent)).toEqual(['Speakers are identified as 2 speakers and named in the order they first speak. Rename any that are off in Review.']);
  });

  it('a name taken off the list takes its marks with it', async () => {
    const onChange = vi.fn<(next: WhoSpoke) => void>();
    await mount({ count: null, names: ['Ana', 'Ben'], marks: [{ name: 'Ana', atMs: 5 }, { name: 'Ben', atMs: 20 }] }, onChange, () => 30);

    await act(async () => {
      button('Remove Ben')?.click();
      await Promise.resolve();
    });

    expect(onChange).toHaveBeenCalledWith({ count: null, names: ['Ana'], marks: [{ name: 'Ana', atMs: 5 }] });
  });

  it('before any name is listed, the hint says to add names first', async () => {
    await mount({ count: null, names: [], marks: [] }, () => undefined, () => 0);

    expect(container.querySelector('.who-spoke-marks')).toBeNull();
    expect(container.querySelector('.who-spoke-hint')?.textContent).toBe(speakingNowHint(0));
  });
});
