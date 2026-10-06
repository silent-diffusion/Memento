/// <reference types="vitest/config" />
import { defineConfig, type Plugin } from 'vite';

// The built page loads its own files from https://app.memento/, talks to the host through
// window.chrome.webview, and reads recordings from the library virtual host (BRIDGE.md): the mix
// through <audio> and peaks.json through fetch. blob: covers the browser-preview host's generated
// media. Nothing else is allowed. The dev server is left alone because Vite injects inline styles
// and a websocket for hot reload.
const LIBRARY_HOST = 'https://library.memento';
const contentSecurityPolicy = [
  "default-src 'none'",
  "script-src 'self'",
  "style-src 'self'",
  "font-src 'self'",
  "img-src 'self' data:",
  `media-src ${LIBRARY_HOST} blob:`,
  `connect-src 'self' ${LIBRARY_HOST} blob:`,
  "base-uri 'none'",
  "form-action 'none'",
].join('; ');

function contentSecurityPolicyPlugin(): Plugin {
  return {
    name: 'memento-csp',
    apply: 'build',
    // Placed straight after <meta charset> so it governs every script and stylesheet that follows.
    transformIndexHtml: (html) => {
      const charset = '<meta charset="utf-8" />';
      if (!html.includes(charset)) {
        throw new Error('index.html must declare <meta charset="utf-8" /> for the CSP to be placed after it.');
      }
      return html.replace(
        charset,
        `${charset}\n    <meta http-equiv="Content-Security-Policy" content="${contentSecurityPolicy}" />`,
      );
    },
  };
}

export default defineConfig({
  base: '/',
  oxc: {
    jsx: { runtime: 'automatic', importSource: 'preact' },
  },
  plugins: [contentSecurityPolicyPlugin()],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: false,
    // Fonts stay separate files so the CSP and caching treat them like any other asset.
    assetsInlineLimit: 0,
  },
  server: {
    port: 5173,
    strictPort: true,
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.{ts,tsx}'],
    restoreMocks: true,
  },
});
