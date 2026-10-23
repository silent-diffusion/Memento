import { render } from 'preact';
import { act } from 'preact/test-utils';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ghostSizeClass, withGhostSize } from './ghostSize';
import { MultiSelectMenu, SelectMenu } from './Menus';

describe('ghost sizes (DESIGN.md §2.3)', () => {
  it('names the modifier of each size; the 36 px toolbar size is the default and has none', () => {
    expect(ghostSizeClass('row')).toBe('ghost-row');
    expect(ghostSizeClass('main')).toBe('ghost-main');
    expect(ghostSizeClass('stage')).toBe('ghost-stage');
    expect(ghostSizeClass('toolbar')).toBe('');
    expect(withGhostSize('btn ghost', 'toolbar')).toBe('btn ghost');
    expect(withGhostSize('btn ghost select-btn', 'main')).toBe('btn ghost select-btn ghost-main');
  });
});

describe('select menus take the ghost size', () => {
  let container: HTMLDivElement;
  const options = [
    { value: 'a', label: 'One' },
    { value: 'b', label: 'Two' },
  ] as const;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.append(container);
  });

  afterEach(() => {
    render(null, container);
    container.remove();
  });

  const trigger = (): string => container.querySelector('button')?.className ?? '';

  it('draws a settings value at the 36 px default', async () => {
    await act(() => {
      render(<SelectMenu label="Choice" value="a" options={options} onChange={() => undefined} />, container);
    });
    expect(trigger()).toBe('btn ghost select-btn');
  });

  it('adds the row and main modifiers when asked, for the sort variant too', async () => {
    await act(() => {
      render(<SelectMenu label="Camera" variant="sort" size="row" value="a" options={options} onChange={() => undefined} />, container);
    });
    expect(trigger()).toBe('btn ghost sort-btn ghost-row');
    await act(() => {
      render(<MultiSelectMenu label="Formats" size="main" values={['a']} options={options} onChange={() => undefined} />, container);
    });
    expect(trigger()).toBe('btn ghost select-btn ghost-main');
  });

  it('keeps the chip variant the 32 px chip whatever the size', async () => {
    await act(() => {
      render(<SelectMenu label="Template" variant="chip" size="row" value="a" options={options} onChange={() => undefined} />, container);
    });
    expect(trigger()).toBe('btn ghost chip-select');
  });
});
