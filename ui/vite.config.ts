/// <reference types="vitest/config" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
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

// Settings › General › About lists the bundled licenses from docs/THIRD-PARTY.md, read when the page is built (and
// on every change during development), so the page never fetches anything for it.
const THIRD_PARTY_ID = 'virtual:third-party-licenses';
const THIRD_PARTY_FILE = resolve(import.meta.dirname, '..', 'docs', 'THIRD-PARTY.md');

function thirdPartyLicensesPlugin(): Plugin {
  const resolved = `\0${THIRD_PARTY_ID}`;
  return {
    name: 'memento-third-party-licenses',
    resolveId: (source) => (source === THIRD_PARTY_ID ? resolved : undefined),
    load(id) {
      if (id !== resolved) {
        return undefined;
      }
      this.addWatchFile(THIRD_PARTY_FILE);
      return `export const THIRD_PARTY = ${JSON.stringify(readFileSync(THIRD_PARTY_FILE, 'utf8'))};`;
    },
  };
}

export default defineConfig({
  base: '/',
  oxc: {
    jsx: { runtime: 'automatic', importSource: 'preact' },
  },
  plugins: [contentSecurityPolicyPlugin(), thirdPartyLicensesPlugin()],
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
    // The screen tests drive the whole preview host in jsdom; under a full parallel run they need room.
    testTimeout: 20_000,
  },
});
