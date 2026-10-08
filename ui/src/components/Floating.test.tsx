import { render } from 'preact';
import { act } from 'preact/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FloatingPopovers, floatingPosition } from './Floating';
import { ActionMenu, SelectMenu } from './Menus';

const VIEWPORT = { width: 1280, height: 800 };
const anchor = (top: number, left: number, width = 36, height = 32): { top: number; bottom: number; left: number; right: number } => ({
  top,
  bottom: top + height,
  left,
  right: left + width,
});

describe('floatingPosition', () => {
  it('puts the popover below the trigger, aligned to its end', () => {
    expect(floatingPosition(anchor(100, 400), { width: 200, height: 150 }, VIEWPORT, 'end')).toEqual({ top: 140, left: 236 });
  });

  it('aligns to the start when asked (the transcript speaker menu)', () => {
    expect(floatingPosition(anchor(100, 400), { width: 200, height: 150 }, VIEWPORT, 'start', 6)).toEqual({ top: 138, left: 400 });
  });

  it('flips above the trigger when there is no room below but there is above', () => {
    expect(floatingPosition(anchor(700, 400), { width: 200, height: 150 }, VIEWPORT, 'end')).toEqual({ top: 542, left: 236 });
  });

  it('stays inside the window when it fits on neither side, and keeps 8 px from the edges', () => {
    expect(floatingPosition(anchor(60, 10), { width: 200, height: 780 }, VIEWPORT, 'end')).toEqual({ top: 12, left: 8 });
    expect(floatingPosition(anchor(100, 1250), { width: 200, height: 100 }, VIEWPORT, 'start')).toEqual({ top: 140, left: 1072 });
  });
});

describe('menus inside a pane that scrolls on its own', () => {
  let container: HTMLDivElement;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.append(container);
  });

  afterEach(() => {
    render(null, container);
    container.remove();
  });

  const open = async (trigger: HTMLElement): Promise<void> => {
    await act(async () => {
      trigger.click();
      await Promise.resolve();
    });
  };

  const pointerDown = async (target: EventTarget): Promise<void> => {
    await act(async () => {
      target.dispatchEvent(new Event('pointerdown', { bubbles: true }));
      await Promise.resolve();
    });
  };

  it('renders the listbox in a layer on <body>, keeps it open for clicks inside and closes it outside', async () => {
    const onChange = vi.fn();
    await act(async () => {
      render(
        <FloatingPopovers.Provider value>
          <div class="pane" style={{ overflowY: 'auto' }}>
            <SelectMenu label="Speed" value="1" options={[{ value: '1', label: '1.0×' }, { value: '2', label: '2.0×' }]} onChange={onChange} />
          </div>
        </FloatingPopovers.Provider>,
        container,
      );
      await Promise.resolve();
    });
    const trigger = container.querySelector<HTMLButtonElement>('button');
    await open(trigger as HTMLElement);
    const listbox = document.querySelector<HTMLElement>('[role="listbox"]');
    expect(listbox?.parentElement?.classList.contains('popover-layer')).toBe(true);
    expect(listbox?.parentElement?.parentElement).toBe(document.body);
    expect(container.querySelector('[role="listbox"]')).toBeNull();
    // Placed beside the trigger with fixed coordinates.
    expect(listbox?.style.top).toMatch(/px$/);
    expect(listbox?.style.left).toMatch(/px$/);

    await pointerDown(listbox ?? document.body);
    expect(document.querySelector('[role="listbox"]')).not.toBeNull();
    const option = [...document.querySelectorAll<HTMLElement>('[role="option"]')].find((o) => o.textContent === '2.0×');
    if (option === undefined) {
      throw new Error('no 2.0× option');
    }
    await open(option);
    expect(onChange).toHaveBeenCalledWith('2');
    expect(document.querySelector('[role="listbox"]')).toBeNull();

    await open(trigger as HTMLElement);
    await pointerDown(document.body);
    expect(document.querySelector('[role="listbox"]')).toBeNull();
  });

  it('hands focus back to the trigger on Tab so focus moves on from the pane, not from the end of the page', async () => {
    await act(async () => {
      render(
        <FloatingPopovers.Provider value>
          <ActionMenu label="More" triggerClass="icon-btn" actions={[{ label: 'Rename', run: () => undefined }]}>
            ⋯
          </ActionMenu>
        </FloatingPopovers.Provider>,
        container,
      );
      await Promise.resolve();
    });
    const trigger = container.querySelector<HTMLButtonElement>('button');
    await open(trigger as HTMLElement);
    const item = document.querySelector<HTMLElement>('.popover-layer [role="menuitem"]');
    expect(document.activeElement).toBe(item);
    await act(async () => {
      item?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
      await Promise.resolve();
    });
    expect(document.querySelector('[role="menu"]')).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it('leaves menus elsewhere in place', async () => {
    await act(async () => {
      render(<SelectMenu label="Sort" value="a" options={[{ value: 'a', label: 'Newest' }]} onChange={() => undefined} />, container);
      await Promise.resolve();
    });
    await open(container.querySelector<HTMLButtonElement>('button') as HTMLElement);
    expect(container.querySelector('.menu-root [role="listbox"]')).not.toBeNull();
    expect(document.querySelector('.popover-layer')).toBeNull();
  });
});
