// 2.7.0: Review's resizable side panes and Jump to playing, against the browser-preview host.
import { act } from 'preact/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import { PANE_BOUNDS } from '../../state/paneWidths';
import { button, click, mountApp, press, settle, until, type Harness } from '../../testing/appHarness';

const DESIGN_REVIEW = '20261005-160000-dsrev';

describe('Review layout (2.7.0)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
    localStorage.removeItem('memento.ui.review.paneWidths');
  });

  const open = async (): Promise<void> => {
    h = await mountApp({ name: 'review', recordingId: DESIGN_REVIEW });
    await until(() => document.querySelector('.review-panes') !== null);
  };
  const splitter = (label: string): HTMLElement => {
    const el = document.querySelector<HTMLElement>(`.pane-splitter[aria-label="${label}"]`);
    if (el === null) {
      throw new Error(`no splitter ${label}`);
    }
    return el;
  };
  const panes = (): HTMLElement | null => document.querySelector<HTMLElement>('.review-panes');

  it('resizes the outline and the details from the keyboard, remembers the widths, and double-click resets', async () => {
    await open();
    const outline = splitter('Resize the outline');
    expect(outline.getAttribute('role')).toBe('separator');
    expect(outline.tabIndex).toBe(0);
    expect(outline.getAttribute('aria-valuenow')).toBe(String(PANE_BOUNDS.outline.initial));
    await press(outline, 'ArrowRight');
    expect(outline.getAttribute('aria-valuenow')).toBe(String(PANE_BOUNDS.outline.initial + 16));
    expect(panes()?.style.getPropertyValue('--review-outline-w')).toBe(`${PANE_BOUNDS.outline.initial + 16}px`);
    // The details pane widens to the left; End takes it to its widest.
    await press(splitter('Resize the details'), 'End');
    expect(splitter('Resize the details').getAttribute('aria-valuenow')).toBe(String(PANE_BOUNDS.details.max));
    h.unmount();
    await open();
    expect(splitter('Resize the outline').getAttribute('aria-valuenow')).toBe(String(PANE_BOUNDS.outline.initial + 16));
    expect(splitter('Resize the details').getAttribute('aria-valuenow')).toBe(String(PANE_BOUNDS.details.max));
    await act(async () => {
      splitter('Resize the outline').dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
      await Promise.resolve();
    });
    await settle(10);
    expect(splitter('Resize the outline').getAttribute('aria-valuenow')).toBe(String(PANE_BOUNDS.outline.initial));
  });

  it('offers Jump to playing beside Skip silences', async () => {
    await open();
    await until(() => document.querySelector('.segm') !== null);
    const jump = button('Jump to playing');
    expect(jump.previousElementSibling?.textContent).toBe('Skip silences');
    await click(jump);
    expect(document.querySelector('.segm')).not.toBeNull();
  });
});
