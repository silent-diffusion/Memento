import { afterEach, describe, expect, it } from 'vitest';
import { applyTheme } from './theme';

describe('applyTheme', () => {
  const root = document.documentElement;

  afterEach(() => {
    root.className = '';
    root.removeAttribute('style');
  });

  it('always applies the app class and adds dark only when dark', () => {
    applyTheme(root, false);
    expect(root.classList.contains('app')).toBe(true);
    expect(root.classList.contains('dark')).toBe(false);
    expect(root.style.colorScheme).toBe('light');

    applyTheme(root, true);
    expect(root.classList.contains('app')).toBe(true);
    expect(root.classList.contains('dark')).toBe(true);
    expect(root.style.colorScheme).toBe('dark');
  });

  it('switches back without touching other classes', () => {
    root.classList.add('something-else');

    applyTheme(root, true);
    applyTheme(root, false);

    expect([...root.classList].sort()).toEqual(['app', 'something-else']);
  });
});
