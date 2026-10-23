import { act } from 'preact/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import type { TemplateSaveParams } from '../../bridge/types';
import { navigationOf } from '../../state/navigation';
import { click, mountApp, settle, type, until, type Harness } from '../../testing/appHarness';

// Back from the Builder with unsaved template changes (2.4.0, usability review R1 option A): the question, its three
// answers, and no question when nothing changed. Esc, Alt+← and the mouse's back button reach the same back control
// (components/SpokeNavigation.test.tsx); the page-history path is the router's leave guard (state/router.test.ts).
const DESIGN = '20261005-160000-dsrev';

describe('leaving the document builder with unsaved template changes', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const open = async (): Promise<void> => {
    h = await mountApp({ name: 'builder', recordingId: DESIGN, templateId: null, documentId: null }, { m4: { ai: 'ready' } });
    await until(() => document.querySelector('.builder-paper article.paper') !== null && document.querySelector('.mod') !== null);
    await until(() => (document.querySelector<HTMLInputElement>('#tpl-name')?.value ?? '') !== '');
  };
  const nameField = (): HTMLInputElement | null => document.querySelector<HTMLInputElement>('#tpl-name');
  const backControl = (): HTMLButtonElement | null => document.querySelector<HTMLButtonElement>('[data-spoke-back]');
  const dialog = (): HTMLElement | null => document.querySelector<HTMLElement>('[role="dialog"][aria-labelledby="leave-template-title"]');
  const inDialog = (name: string): HTMLButtonElement | undefined => [...(dialog()?.querySelectorAll('button') ?? [])].find((b) => b.textContent.trim() === name);
  const rename = async (to: string): Promise<void> => {
    await type(nameField(), to);
    await settle(30);
  };

  it('goes straight back when nothing changed', async () => {
    await open();
    await click(backControl());
    await until(() => h.store.route.value.name === 'review');
    expect(dialog()).toBeNull();
    expect(h.callsOf('templates.save')).toEqual([]);
  });

  it('asks with the template’s name, and Keep editing stays with the changes', async () => {
    await open();
    await rename('Board minutes');
    await click(backControl());
    await until(() => dialog() !== null);
    expect(document.querySelector('#leave-template-title')?.textContent).toBe('Leave without saving the template?');
    expect(dialog()?.querySelector('.dialog-body')?.textContent).toContain('Your changes to “Board minutes” are only in this document builder.');
    expect([...(dialog()?.querySelectorAll('button') ?? [])].map((b) => b.textContent.trim())).toEqual(['Keep editing', 'Save template', 'Leave without saving']);
    // Keep editing is the default: Enter or a click there never loses anything.
    expect(document.activeElement).toBe(inDialog('Keep editing'));
    await click(inDialog('Keep editing'));
    await until(() => dialog() === null);
    expect(h.store.route.value.name).toBe('builder');
    expect(nameField()?.value).toBe('Board minutes');
    expect(h.callsOf('templates.save')).toEqual([]);
  });

  it('Save template saves through the template save path, then leaves', async () => {
    await open();
    await rename('Board minutes');
    await click(backControl());
    await until(() => dialog() !== null);
    await click(inDialog('Save template'));
    await until(() => h.store.route.value.name === 'review');
    const saved = h.callsOf('templates.save') as TemplateSaveParams[];
    expect(saved).toHaveLength(1);
    expect(saved[0]?.template.name).toBe('Board minutes');
    expect(h.store.route.value).toEqual({ name: 'review', recordingId: DESIGN });
    expect(h.store.toasts.items.value.map((t) => t.title)).toContain('Saved as “Board minutes”'); // Meeting minutes is built in: saved as a copy.
  });

  it('stays with the changes when Save template fails, and says so', async () => {
    await open();
    const call = h.bridge.call.bind(h.bridge);
    (h.bridge as { call: unknown }).call = (method: string, params?: unknown) =>
      method === 'templates.save' ? Promise.reject(new Error('The templates folder could not be written.')) : (call as (m: string, p?: unknown) => Promise<unknown>)(method, params);
    await rename('Board minutes');
    await click(backControl());
    await until(() => dialog() !== null);
    await click(inDialog('Save template'));
    await until(() => dialog() === null);
    expect(h.store.route.value.name).toBe('builder');
    expect(nameField()?.value).toBe('Board minutes');
    const toast = h.store.toasts.items.value.find((t) => t.title === 'The template was not saved');
    expect(toast?.body).toBe('The templates folder could not be written. Your arrangement is still here.');
  });

  it('asks the same question when the page’s own history would leave, and only while there are changes', async () => {
    await open();
    const nav = navigationOf(h.store);
    const moves: string[] = [];
    let held = false;
    await act(() => {
      held = nav.leaveGuard?.(() => moves.push('history')) ?? false;
    });
    expect(held).toBe(false);
    expect(dialog()).toBeNull();
    await rename('Board minutes');
    await act(() => {
      held = nav.leaveGuard?.(() => moves.push('history')) ?? false;
    });
    expect(held).toBe(true);
    await until(() => dialog() !== null);
    await click(inDialog('Leave without saving'));
    await until(() => dialog() === null);
    // The move the history asked for, not the back control's.
    expect(moves).toEqual(['history']);
  });

  it('Leave without saving drops the changes and leaves; the template stays as it was', async () => {
    await open();
    const before = nameField()?.value;
    await rename('Board minutes');
    await click(backControl());
    await until(() => dialog() !== null);
    await click(inDialog('Leave without saving'));
    await until(() => h.store.route.value.name === 'review');
    expect(h.callsOf('templates.save')).toEqual([]);
    // Opening the Builder again starts from the template, not the dropped draft.
    h.store.route.value = { name: 'builder', recordingId: DESIGN, templateId: null, documentId: null };
    await until(() => (nameField()?.value ?? '') !== '');
    expect(nameField()?.value).toBe(before);
    await click(backControl());
    await until(() => h.store.route.value.name === 'review');
    expect(dialog()).toBeNull();
  });
});
