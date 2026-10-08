import { afterEach, describe, expect, it, vi } from 'vitest';
import { connectUndoKeys, createUndoManager, MERGE_WINDOW_MS, UNDO_LIMIT, undoKeyOf, type UndoManager } from './undo';

/** A counter the entries change, so do → undo → redo can be checked. */
function counter(manager: UndoManager): { value: number; add: (n: number, label?: string, mergeKey?: string) => void } {
  const state = {
    value: 0,
    add: (n: number, label = 'add', mergeKey?: string): void => {
      state.value += n;
      manager.push(
        {
          label,
          undo: () => {
            state.value -= n;
          },
          redo: () => {
            state.value += n;
          },
        },
        mergeKey === undefined ? {} : { mergeKey },
      );
    },
  };
  return state;
}

describe('undo manager', () => {
  it('undoes and redoes in order, and a new step clears what could be redone', async () => {
    const manager = createUndoManager();
    manager.claim('review:a');
    const c = counter(manager);
    c.add(1, 'add one');
    c.add(10, 'add ten');
    expect(manager.undoLabel.value).toBe('add ten');

    expect(await manager.undo()).toBe(true);
    expect(c.value).toBe(1);
    expect(manager.status.value?.text).toBe('Undone: add ten');
    expect(manager.redoLabel.value).toBe('add ten');
    expect(await manager.redo()).toBe(true);
    expect(c.value).toBe(11);
    expect(manager.status.value?.text).toBe('Redone: add ten');

    await manager.undo();
    c.add(100, 'add a hundred');
    expect(manager.redoLabel.value).toBeNull();
    expect(await manager.redo()).toBe(false);
    expect(c.value).toBe(101);
  });

  it('keeps at most UNDO_LIMIT steps, dropping the oldest', async () => {
    const manager = createUndoManager();
    manager.claim('builder:x');
    const c = counter(manager);
    for (let i = 0; i < UNDO_LIMIT + 5; i++) {
      c.add(1);
    }
    expect(manager.depth.value).toBe(UNDO_LIMIT);
    while (await manager.undo()) {
      // undo everything there is
    }
    expect(c.value).toBe(5);
  });

  it('merges pushes with the same key within the window into one step', async () => {
    let time = 0;
    const manager = createUndoManager({ now: () => time });
    manager.claim('builder:x');
    // Merged steps are snapshots: undo puts back the value before the first, redo the value after the last.
    let text = '';
    const type = (next: string): void => {
      const before = text;
      text = next;
      manager.push(
        {
          label: 'change instructions',
          undo: () => {
            text = before;
          },
          redo: () => {
            text = next;
          },
        },
        { mergeKey: 'instr:m01' },
      );
    };
    type('S');
    time += 500;
    type('Su');
    time += MERGE_WINDOW_MS + 1;
    type('Sum');
    expect(manager.depth.value).toBe(2);
    await manager.undo();
    expect(text).toBe('Su');
    await manager.undo();
    expect(text).toBe('');
    await manager.redo();
    expect(text).toBe('Su');
  });

  it('starts an empty stack when another scope is claimed, and keeps it for the same scope', async () => {
    const manager = createUndoManager();
    manager.claim('builder:a');
    counter(manager).add(1);
    manager.release('builder:a', { keep: true });
    expect(manager.undoLabel.value).toBeNull();
    expect(await manager.undo()).toBe(false);
    manager.claim('builder:a');
    expect(manager.undoLabel.value).toBe('add');

    manager.claim('review:b');
    expect(manager.undoLabel.value).toBeNull();
    expect(manager.depth.value).toBe(0);

    counter(manager).add(1);
    manager.release('review:b');
    manager.claim('review:b');
    expect(manager.depth.value).toBe(0);
  });

  it('keeps the step and reports when the host refuses, and does not register what an undo itself does', async () => {
    const report = vi.fn();
    const manager = createUndoManager({ report });
    manager.claim('review:a');
    let fail = true;
    manager.push({
      label: 'merge speakers',
      undo: () => {
        if (fail) {
          throw new Error('That speaker is not in this transcript any more.');
        }
        // An action called while undoing would register a step of its own.
        manager.push({ label: 'nested', undo: () => undefined, redo: () => undefined });
      },
      redo: () => undefined,
    });
    expect(await manager.undo()).toBe(false);
    expect(manager.undoLabel.value).toBe('merge speakers');
    expect(manager.status.value).toMatchObject({ text: 'Not undone: merge speakers', tone: 'failed' });
    expect(report).toHaveBeenCalledWith('Merge speakers was not undone', 'That speaker is not in this transcript any more. Nothing else changed; the step is still there to try again.');

    fail = false;
    expect(await manager.undo()).toBe(true);
    expect(manager.undoLabel.value).toBeNull();
    expect(manager.redoLabel.value).toBe('merge speakers');
  });

  it('runs steps one after another when Undo is pressed quickly', async () => {
    const manager = createUndoManager();
    manager.claim('review:a');
    const order: string[] = [];
    for (const name of ['first', 'second']) {
      manager.push({
        label: name,
        undo: async () => {
          await new Promise((resolve) => setTimeout(resolve, name === 'second' ? 20 : 0));
          order.push(name);
        },
        redo: () => undefined,
      });
    }
    await Promise.all([manager.undo(), manager.undo()]);
    expect(order).toEqual(['second', 'first']);
  });

  it('follows ids an undo gave out again', () => {
    const manager = createUndoManager();
    manager.claim('review:a');
    manager.aliasId('h1', 'h7');
    manager.aliasId('h1', 'h9');
    expect(manager.resolveId('h1')).toBe('h9');
    expect(manager.resolveId('h2')).toBe('h2');
    manager.claim('review:other');
    expect(manager.resolveId('h1')).toBe('h1');
  });
});

describe('undo keys', () => {
  let disconnect: (() => void) | null = null;

  afterEach(() => {
    disconnect?.();
    disconnect = null;
    document.body.innerHTML = '';
  });

  const key = (target: EventTarget, init: KeyboardEventInit): KeyboardEvent => {
    const event = new KeyboardEvent('keydown', { bubbles: true, cancelable: true, ...init });
    target.dispatchEvent(event);
    return event;
  };

  it('reads Ctrl+Z as undo and Ctrl+Y or Ctrl+Shift+Z as redo', () => {
    const base = { ctrlKey: true, metaKey: false, shiftKey: false, altKey: false };
    expect(undoKeyOf({ ...base, key: 'z' })).toBe('undo');
    expect(undoKeyOf({ ...base, key: 'Z', shiftKey: true })).toBe('redo');
    expect(undoKeyOf({ ...base, key: 'y' })).toBe('redo');
    expect(undoKeyOf({ ...base, key: 'z', altKey: true })).toBeNull();
    expect(undoKeyOf({ ...base, key: 'z', ctrlKey: false })).toBeNull();
    expect(undoKeyOf({ ...base, key: 'z', ctrlKey: false, metaKey: true })).toBe('undo');
  });

  it('undoes anywhere, but leaves Ctrl+Z to a text field that was changed since it was focused', async () => {
    const manager = createUndoManager();
    manager.claim('review:a');
    const c = counter(manager);
    c.add(1);
    c.add(2);
    disconnect = connectUndoKeys(manager);
    const field = document.createElement('input');
    const box = document.createElement('input');
    box.type = 'checkbox';
    document.body.append(field, box);

    // On the page: the app undoes.
    const first = key(document.body, { key: 'z', ctrlKey: true });
    expect(first.defaultPrevented).toBe(true);
    await Promise.resolve();
    await Promise.resolve();
    expect(c.value).toBe(1);

    // In a field that was typed in: the field's own undo.
    field.focus();
    field.value = 'typed';
    field.dispatchEvent(new Event('input', { bubbles: true }));
    const inField = key(field, { key: 'z', ctrlKey: true });
    expect(inField.defaultPrevented).toBe(false);
    expect(c.value).toBe(1);
    // Back to what it held when focused: nothing left for the field, so the app undoes.
    field.value = '';
    const emptied = key(field, { key: 'z', ctrlKey: true });
    expect(emptied.defaultPrevented).toBe(true);
    // Redo stays with the field once it was typed in.
    expect(key(field, { key: 'y', ctrlKey: true }).defaultPrevented).toBe(false);

    // A checkbox is not a text field.
    box.focus();
    expect(key(box, { key: 'y', ctrlKey: true }).defaultPrevented).toBe(true);
  });

  it('does nothing while a dialog blocks it or no screen holds the stack', () => {
    const manager = createUndoManager();
    manager.claim('review:a');
    const c = counter(manager);
    c.add(1);
    let blocked = true;
    disconnect = connectUndoKeys(manager, () => blocked);
    expect(key(document.body, { key: 'z', ctrlKey: true }).defaultPrevented).toBe(false);
    blocked = false;
    manager.release('review:a', { keep: true });
    expect(key(document.body, { key: 'z', ctrlKey: true }).defaultPrevented).toBe(false);
  });
});
