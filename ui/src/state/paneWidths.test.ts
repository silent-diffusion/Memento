import { afterEach, describe, expect, it } from 'vitest';
import { clampPaneWidth, DEFAULT_PANE_WIDTHS, PANE_BOUNDS, paneWidthForKey, parsePaneWidths, readPaneWidths, writePaneWidths } from './paneWidths';

describe('Review pane widths (2.7.0)', () => {
  afterEach(() => {
    localStorage.removeItem('memento.ui.review.paneWidths');
  });

  it('keeps each pane within its bounds', () => {
    expect(clampPaneWidth('outline', 100)).toBe(PANE_BOUNDS.outline.min);
    expect(clampPaneWidth('outline', 9999)).toBe(PANE_BOUNDS.outline.max);
    expect(clampPaneWidth('details', 300.4)).toBe(300);
  });

  it('reads back what was written, and the defaults for anything it does not recognise', () => {
    expect(readPaneWidths()).toEqual(DEFAULT_PANE_WIDTHS);
    writePaneWidths({ outline: 320, details: 400 });
    expect(readPaneWidths()).toEqual({ outline: 320, details: 400 });
    localStorage.setItem('memento.ui.review.paneWidths', '{"outline":"wide"}');
    expect(readPaneWidths()).toEqual(DEFAULT_PANE_WIDTHS);
    expect(parsePaneWidths({ outline: 10, details: 10_000 })).toEqual({ outline: PANE_BOUNDS.outline.min, details: PANE_BOUNDS.details.max });
    expect(parsePaneWidths(null)).toBeNull();
  });

  it('moves by arrow keys (Shift further), Home and End to the bounds, Enter to the default', () => {
    expect(paneWidthForKey('outline', 260, 'ArrowRight', false, 'ArrowRight')).toBe(276);
    expect(paneWidthForKey('outline', 260, 'ArrowLeft', true, 'ArrowRight')).toBe(PANE_BOUNDS.outline.min);
    // The details pane grows to the left.
    expect(paneWidthForKey('details', 320, 'ArrowLeft', false, 'ArrowLeft')).toBe(336);
    expect(paneWidthForKey('details', 320, 'End', false, 'ArrowLeft')).toBe(PANE_BOUNDS.details.max);
    expect(paneWidthForKey('details', 500, 'Enter', false, 'ArrowLeft')).toBe(PANE_BOUNDS.details.initial);
    expect(paneWidthForKey('outline', 260, 'a', false, 'ArrowRight')).toBeNull();
  });
});
