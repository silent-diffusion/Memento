import { afterEach, describe, expect, it } from 'vitest';
import { recordShortcut, type ShortcutEvent } from './recordShortcuts';

const key = (k: string, target: EventTarget | null, extra: Partial<ShortcutEvent> = {}): ShortcutEvent => ({
  key: k,
  ctrlKey: false,
  metaKey: false,
  altKey: false,
  shiftKey: false,
  repeat: false,
  target,
  ...extra,
});

const make = <K extends keyof HTMLElementTagNameMap>(tag: K, attrs: Record<string, string> = {}): HTMLElementTagNameMap[K] => {
  const el = document.createElement(tag);
  for (const [name, value] of Object.entries(attrs)) {
    el.setAttribute(name, value);
  }
  document.body.append(el);
  return el;
};

describe('Recording session keys', () => {
  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('Space pauses or resumes when nothing in particular has focus', () => {
    expect(recordShortcut(key(' ', document.body))).toBe('togglePause');
    expect(recordShortcut(key(' ', make('div')))).toBe('togglePause');
    expect(recordShortcut(key(' ', null))).toBe('togglePause');
  });

  it('Space types in text fields and presses focused controls instead', () => {
    expect(recordShortcut(key(' ', make('input', { type: 'text' })))).toBeNull();
    expect(recordShortcut(key(' ', make('input')))).toBeNull();
    expect(recordShortcut(key(' ', make('textarea')))).toBeNull();
    expect(recordShortcut(key(' ', make('button')))).toBeNull();
    expect(recordShortcut(key(' ', make('div', { role: 'switch' })))).toBeNull();
    expect(recordShortcut(key(' ', make('div', { role: 'slider' })))).toBeNull();
  });

  it('ignores Space with modifiers and swallows auto-repeat', () => {
    expect(recordShortcut(key(' ', document.body, { ctrlKey: true }))).toBeNull();
    expect(recordShortcut(key(' ', document.body, { shiftKey: true }))).toBeNull();
    expect(recordShortcut(key(' ', document.body, { repeat: true }))).toBe('swallow');
  });

  it('Ctrl+M marks a highlight, also while typing a note', () => {
    expect(recordShortcut(key('m', document.body, { ctrlKey: true }))).toBe('mark');
    expect(recordShortcut(key('M', make('input', { type: 'text' }), { ctrlKey: true }))).toBe('mark');
    expect(recordShortcut(key('m', make('textarea'), { ctrlKey: true }))).toBe('mark');
    expect(recordShortcut(key('m', document.body))).toBeNull();
    expect(recordShortcut(key('m', document.body, { ctrlKey: true, shiftKey: true }))).toBeNull();
    expect(recordShortcut(key('m', document.body, { ctrlKey: true, repeat: true }))).toBe('swallow');
  });

  it('Esc does nothing at all outside text fields', () => {
    expect(recordShortcut(key('Escape', document.body))).toBe('swallow');
    expect(recordShortcut(key('Escape', make('button')))).toBe('swallow');
    expect(recordShortcut(key('Escape', make('input', { type: 'text' })))).toBeNull();
  });
});
