import { render } from 'preact';
import { act } from 'preact/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { BridgeClient } from '../bridge/client';
import { AppContext, createMemoryRouter, type AppServices } from '../state/context';
import { createStore } from '../state/store';
import { focusableWithin, moveFocus, trapTab } from './keyboard';
import { Dialog, initialFocusTarget } from './Overlay';

const bridge: BridgeClient = { isHosted: false, call: vi.fn(() => new Promise<never>(() => undefined)), on: vi.fn(() => () => undefined) };

function key(target: EventTarget, init: KeyboardEventInit): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { bubbles: true, cancelable: true, ...init });
  target.dispatchEvent(event);
  return event;
}

describe('focus helpers', () => {
  let box: HTMLDivElement;

  beforeEach(() => {
    box = document.createElement('div');
    box.innerHTML = `
      <button id="a">A</button>
      <button id="b" disabled>B</button>
      <input id="c" />
      <span tabindex="-1" id="d">D</span>
      <a href="#x" id="e">E</a>`;
    document.body.append(box);
  });

  afterEach(() => {
    box.remove();
  });

  it('lists tabbable elements in order, skipping disabled and tabindex -1', () => {
    expect(focusableWithin(box).map((el) => el.id)).toEqual(['a', 'c', 'e']);
  });

  it('wraps Tab from the last element to the first and Shift+Tab back', () => {
    const last = box.querySelector<HTMLElement>('#e');
    last?.focus();
    const forward = new KeyboardEvent('keydown', { key: 'Tab', cancelable: true });
    expect(trapTab(forward, box)).toBe(true);
    expect(forward.defaultPrevented).toBe(true);
    expect(document.activeElement?.id).toBe('a');

    const back = new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, cancelable: true });
    expect(trapTab(back, box)).toBe(true);
    expect(document.activeElement?.id).toBe('e');
  });

  it('lets Tab move normally in the middle and pulls focus back in from outside', () => {
    box.querySelector<HTMLElement>('#a')?.focus();
    expect(trapTab(new KeyboardEvent('keydown', { key: 'Tab' }), box)).toBe(false);

    const outside = document.createElement('button');
    document.body.append(outside);
    outside.focus();
    trapTab(new KeyboardEvent('keydown', { key: 'Tab', cancelable: true }), box);
    expect(document.activeElement?.id).toBe('a');
    outside.remove();
  });

  it('moves with arrow keys inside a group, wrapping, with Home and End', () => {
    const group = document.createElement('div');
    group.innerHTML = '<button class="chip" id="x">X</button><button class="chip" id="y">Y</button><button class="chip" id="z">Z</button>';
    document.body.append(group);
    group.querySelector<HTMLElement>('#x')?.focus();
    moveFocus(new KeyboardEvent('keydown', { key: 'ArrowRight' }), group, '.chip');
    expect(document.activeElement?.id).toBe('y');
    moveFocus(new KeyboardEvent('keydown', { key: 'ArrowLeft' }), group, '.chip');
    moveFocus(new KeyboardEvent('keydown', { key: 'ArrowLeft' }), group, '.chip');
    expect(document.activeElement?.id).toBe('z');
    moveFocus(new KeyboardEvent('keydown', { key: 'Home' }), group, '.chip');
    expect(document.activeElement?.id).toBe('x');
    expect(moveFocus(new KeyboardEvent('keydown', { key: 'Enter' }), group, '.chip')).toBeNull();
    group.remove();
  });
});

describe('Dialog', () => {
  let container: HTMLDivElement;
  let services: AppServices;
  let opener: HTMLButtonElement;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.append(container);
    opener = document.createElement('button');
    opener.textContent = 'Delete…';
    document.body.append(opener);
    opener.focus();
    const store = createStore(false);
    services = { bridge, store, router: createMemoryRouter(store), now: () => new Date() };
  });

  afterEach(() => {
    render(null, container);
    container.remove();
    opener.remove();
  });

  const mount = (onEscape: () => void, show = true): void => {
    void act(() => {
      render(
        <AppContext.Provider value={services}>
          {show ? (
            <Dialog
              titleId="t"
              title='Delete "Weekly 1:1 with Sam"?'
              onEscape={onEscape}
              actions={
                <>
                  <button class="btn g" type="button">
                    Cancel
                  </button>
                  <button class="btn d" type="button">
                    Delete
                  </button>
                </>
              }
            >
              <p>Removes the recording.</p>
            </Dialog>
          ) : null}
        </AppContext.Provider>,
        container,
      );
    });
  };

  const dialog = (): HTMLElement => {
    const el = document.querySelector<HTMLElement>('[role="dialog"]');
    if (el === null) {
      throw new Error('no dialog');
    }
    return el;
  };

  it('is a labelled modal that focuses Cancel, never the destructive button', () => {
    mount(vi.fn());
    expect(dialog().getAttribute('aria-modal')).toBe('true');
    expect(document.getElementById(dialog().getAttribute('aria-labelledby') ?? '')?.textContent).toBe('Delete "Weekly 1:1 with Sam"?');
    expect(document.activeElement?.textContent).toBe('Cancel');
    expect(services.store.overlays.value).toBe(1);
  });

  it('keeps Tab inside the dialog', () => {
    mount(vi.fn());
    const buttons = [...dialog().querySelectorAll('button')];
    buttons.at(-1)?.focus();
    key(dialog(), { key: 'Tab' });
    expect(document.activeElement).toBe(buttons[0]);
    key(dialog(), { key: 'Tab', shiftKey: true });
    expect(document.activeElement).toBe(buttons.at(-1));
  });

  it('runs the non-destructive outcome on Esc', () => {
    const onEscape = vi.fn();
    mount(onEscape);
    key(dialog(), { key: 'Escape' });
    expect(onEscape).toHaveBeenCalledTimes(1);
  });

  it('gives focus back to the opener and releases the shell when it closes', async () => {
    mount(vi.fn());
    mount(vi.fn(), false);
    await Promise.resolve();
    expect(services.store.overlays.value).toBe(0);
    expect(document.activeElement).toBe(opener);
  });

  it('prefers an element marked data-autofocus', () => {
    const box = document.createElement('div');
    box.innerHTML = '<button>One</button><input data-autofocus id="f" /><button class="d">Delete</button>';
    expect(initialFocusTarget(box)?.id).toBe('f');
    const onlyDestructive = document.createElement('div');
    onlyDestructive.innerHTML = '<button class="d">Delete</button>';
    expect(initialFocusTarget(onlyDestructive)).toBeNull();
  });
});
