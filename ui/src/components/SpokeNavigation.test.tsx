import { act } from 'preact/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import { button, click, mountApp, press, settle, type, until, type Harness } from '../testing/appHarness';

// Back, Esc, Alt+← and the mouse's back button across the spokes (DESIGN.md §3, state/navigation.ts, state/back.ts),
// against the browser-preview host.
const DESIGN = '20261005-160000-dsrev';
const MINUTES = 'doc-20261005-minutes';

const backControl = (): HTMLButtonElement => {
  const el = document.querySelector<HTMLButtonElement>('[data-spoke-back]');
  if (el === null) {
    throw new Error('no back control');
  }
  return el;
};

const key = async (target: Element | Window, init: KeyboardEventInit): Promise<KeyboardEvent> => {
  const event = new KeyboardEvent('keydown', { bubbles: true, cancelable: true, ...init });
  await act(async () => {
    target.dispatchEvent(event);
    await Promise.resolve();
  });
  await settle(10);
  return event;
};

const mouseBack = async (): Promise<MouseEvent> => {
  const down = new MouseEvent('mousedown', { bubbles: true, cancelable: true, button: 3 });
  const up = new MouseEvent('mouseup', { bubbles: true, cancelable: true, button: 3 });
  await act(async () => {
    document.body.dispatchEvent(down);
    document.body.dispatchEvent(up);
    await Promise.resolve();
  });
  await settle(10);
  return up;
};

describe('spoke navigation', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  it('Settings remembers its section: the gear opens it again, and Back returns to the Library', async () => {
    h = await mountApp({ name: 'library' });
    await until(() => document.querySelector('[aria-label="Settings"]') !== null);
    await click(document.querySelector('[aria-label="Settings"]'));
    await until(() => document.querySelector('.settings-main') !== null);
    expect(h.store.route.value).toEqual({ name: 'settings', section: 'general' });
    await click(button('Storage and history'));
    expect(backControl().textContent).toBe('Library');
    await click(backControl());
    await until(() => h.store.route.value.name === 'library');
    await until(() => document.querySelector('[aria-label="Settings"]') !== null);
    await click(document.querySelector('[aria-label="Settings"]'));
    expect(h.store.route.value).toEqual({ name: 'settings', section: 'storage' });
  });

  it('Esc closes an open menu first and only then goes back', async () => {
    h = await mountApp({ name: 'settings', section: 'documents' });
    await until(() => document.querySelector('.settings-row .select-btn') !== null);
    const select = document.querySelector<HTMLButtonElement>('.settings-row .select-btn');
    await click(select);
    await until(() => document.querySelector('[role="menu"], [role="listbox"]') !== null);
    const item = document.querySelector<HTMLElement>('[role="menu"] [role="menuitem"], [role="menu"] [role="menuitemradio"], [role="listbox"] [role="option"]');
    item?.focus();
    await key(document.activeElement ?? document.body, { key: 'Escape' });
    expect(document.querySelector('[role="menu"], [role="listbox"]')).toBeNull();
    expect(h.store.route.value.name).toBe('settings');
    await key(document.activeElement ?? document.body, { key: 'Escape' });
    expect(h.store.route.value).toEqual({ name: 'library' });
  });

  it('Esc never goes back while typing in a field, nor while a dialog is open', async () => {
    h = await mountApp({ name: 'review', recordingId: DESIGN });
    await until(() => document.querySelector('.segm') !== null);
    const search = document.querySelector<HTMLInputElement>('input[type="search"], .player-search input');
    if (search === null) {
      throw new Error('no transcript search field');
    }
    search.focus();
    await key(search, { key: 'Escape' });
    expect(h.store.route.value.name).toBe('review');

    await click(button('Export'));
    await until(() => document.querySelector('[role="dialog"]') !== null);
    // The dialog takes Esc (it closes); the screen stays.
    await key(document.activeElement ?? document.body, { key: 'Escape' });
    await until(() => document.querySelector('[role="dialog"]') === null);
    expect(h.store.route.value.name).toBe('review');
  });

  it('Esc in Review clears the transcript filter first, then goes back to the Library', async () => {
    h = await mountApp({ name: 'review', recordingId: DESIGN });
    await until(() => document.querySelector('.person-filter') !== null);
    await click(document.querySelector('.person-filter'));
    await until(() => document.querySelector('.tx-filter-line, .tx-filtered') !== null || document.body.textContent.includes('Showing'));
    (document.activeElement as HTMLElement | null)?.blur();
    await key(document.body, { key: 'Escape' });
    expect(h.store.route.value.name).toBe('review');
    expect(document.body.textContent.includes('Showing')).toBe(false);
    await key(document.body, { key: 'Escape' });
    expect(h.store.route.value).toEqual({ name: 'library' });
  });

  it('the Recording session ignores Esc (DESIGN.md §8); Alt+← still goes back', async () => {
    h = await mountApp({ name: 'record', sessionId: null });
    await until(() => document.querySelector('[data-spoke-back]') !== null);
    await key(document.body, { key: 'Escape' });
    expect(h.store.route.value.name).toBe('record');
    const event = await key(document.body, { key: 'ArrowLeft', altKey: true });
    expect(event.defaultPrevented).toBe(true);
    expect(h.store.route.value).toEqual({ name: 'library' });
  });

  it('the mouse’s back button presses the back control instead of stepping through page history', async () => {
    h = await mountApp({ name: 'settings', section: 'general' });
    await until(() => document.querySelector('[data-spoke-back]') !== null);
    const up = await mouseBack();
    expect(up.defaultPrevented).toBe(true);
    expect(h.store.route.value).toEqual({ name: 'library' });
    // On the Library (no back control) it does nothing.
    await mouseBack();
    expect(h.store.route.value).toEqual({ name: 'library' });
  });

  it('a timestamp chip: Review’s Back names the document and returns to it; the document’s Back returns to Review', async () => {
    h = await mountApp({ name: 'document', recordingId: DESIGN, documentId: MINUTES }, { m4: { ai: 'ready' } });
    await until(() => document.querySelector('.doc-paper article.paper a.ts') !== null);
    await click(document.querySelector('.doc-paper article.paper a.ts'));
    await until(() => h.store.route.value.name === 'review' && document.querySelector('[data-spoke-back]') !== null);
    expect(backControl().textContent).toBe('Meeting minutes');
    await click(backControl());
    await until(() => h.store.route.value.name === 'document');
    expect(h.store.route.value).toEqual({ name: 'document', recordingId: DESIGN, documentId: MINUTES });
    await until(() => document.querySelector('.doc-paper article.paper') !== null);
    await click(backControl());
    await until(() => h.store.route.value.name === 'review');
    await until(() => backControl().textContent === 'Library');
  });

  it('a cross-screen link passes a return target: the Builder’s AI settings link opens Settings, Back returns with the draft', async () => {
    h = await mountApp({ name: 'builder', recordingId: DESIGN, templateId: null, documentId: null }, { m4: { ai: 'off' } });
    await until(() => document.querySelector('.builder-paper article.paper') !== null && document.querySelector('.mod') !== null);
    const name = document.querySelector<HTMLInputElement>('#tpl-name');
    await type(name, 'Minutes for the board');
    await settle(30);
    await click(document.querySelector('#builder-tab-inputs'));
    await click(button('Open AI and privacy settings'));
    await until(() => document.querySelector('.settings-main') !== null);
    expect(h.store.route.value).toEqual({ name: 'settings', section: 'ai-privacy' });
    expect(backControl().textContent).toBe('Minutes for the board');
    await click(button('General'));
    expect(backControl().textContent).toBe('Minutes for the board');
    await click(backControl());
    await until(() => h.store.route.value.name === 'builder' && document.querySelector<HTMLInputElement>('#tpl-name') !== null);
    // The unsaved name came back with the Builder (kept while it was away, as for the Style editor).
    await until(() => document.querySelector<HTMLInputElement>('#tpl-name')?.value === 'Minutes for the board');
    expect(backControl().textContent).toBe('Design review: library screen');
  });

  it('the Builder ignores Esc while the template has unsaved changes', async () => {
    h = await mountApp({ name: 'builder', recordingId: DESIGN, templateId: null, documentId: null }, { m4: { ai: 'ready' } });
    await until(() => document.querySelector<HTMLInputElement>('#tpl-name')?.value !== '' && document.querySelector('#tpl-name') !== null);
    const name = document.querySelector<HTMLInputElement>('#tpl-name');
    await type(name, 'Minutes, shorter');
    name?.blur();
    await settle(30);
    await key(document.body, { key: 'Escape' });
    expect(h.store.route.value.name).toBe('builder');
    // Alt+← is a deliberate Back and still works.
    await key(document.body, { key: 'ArrowLeft', altKey: true });
    expect(h.store.route.value).toEqual({ name: 'review', recordingId: DESIGN });
  });

  it('the back control says how to use it from the keyboard', async () => {
    h = await mountApp({ name: 'settings', section: 'general' });
    await until(() => document.querySelector('[data-spoke-back]') !== null);
    expect(backControl().getAttribute('title')).toBe('Back to Library (Esc or Alt+←)');
    expect(backControl().getAttribute('aria-keyshortcuts')).toBe('Escape Alt+ArrowLeft');
    await press(backControl(), 'Enter');
  });
});
