/**
 * Applies the theme to the root element: `app` always, `dark` when dark (tokens.css swaps every
 * token on `.app.dark`). `color-scheme` follows so native scrollbars and form controls match.
 */
export function applyTheme(root: HTMLElement, isDark: boolean): void {
  root.classList.add('app');
  root.classList.toggle('dark', isDark);
  root.style.colorScheme = isDark ? 'dark' : 'light';
}

/** The OS preference as the page sees it; the host keeps WebView2's preference in step with Windows and Settings. */
export function systemPrefersDark(): boolean {
  return typeof window.matchMedia === 'function' && window.matchMedia('(prefers-color-scheme: dark)').matches;
}
