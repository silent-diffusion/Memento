// Minimal Chrome DevTools Protocol client for driving Memento's WebView2 page like a user: real mouse clicks at
// element centres, typed text and key presses through Input.*, and screenshots. Nothing here calls the bridge.
// Node 22+ (global WebSocket and fetch). No dependencies.

import { mkdir, writeFile } from 'node:fs/promises';
import { dirname } from 'node:path';

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

export class Page {
  #ws;
  #nextId = 1;
  #pending = new Map();
  #listeners = [];

  static async connect(port, { timeoutMs = 60_000, origin = 'https://app.memento' } = {}) {
    const deadline = Date.now() + timeoutMs;
    for (;;) {
      try {
        const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
        const target = targets.find((t) => t.type === 'page' && t.url.startsWith(origin));
        if (target) {
          const page = new Page();
          await page.#open(target.webSocketDebuggerUrl);
          await page.send('Runtime.enable');
          await page.send('Page.enable');
          return page;
        }
      } catch {
        // The app is still starting.
      }
      if (Date.now() > deadline) throw new Error(`No ${origin} page on DevTools port ${port}`);
      await sleep(250);
    }
  }

  #open(url) {
    return new Promise((resolve, reject) => {
      this.#ws = new WebSocket(url);
      this.#ws.onopen = () => resolve();
      this.#ws.onerror = (e) => reject(new Error(`DevTools socket: ${e.message ?? e.type}`));
      this.#ws.onclose = () => {
        for (const { reject: fail } of this.#pending.values()) fail(new Error('DevTools socket closed'));
        this.#pending.clear();
      };
      this.#ws.onmessage = (message) => {
        const data = JSON.parse(message.data);
        if (data.id !== undefined) {
          const waiter = this.#pending.get(data.id);
          this.#pending.delete(data.id);
          if (data.error) waiter?.reject(new Error(`${data.error.message} ${data.error.data ?? ''}`));
          else waiter?.resolve(data.result);
        } else {
          for (const listener of this.#listeners) listener(data);
        }
      };
    });
  }

  send(method, params = {}) {
    const id = this.#nextId++;
    return new Promise((resolve, reject) => {
      this.#pending.set(id, { resolve, reject });
      this.#ws.send(JSON.stringify({ id, method, params }));
    });
  }

  on(listener) {
    this.#listeners.push(listener);
  }

  close() {
    try {
      this.#ws.close();
    } catch {
      // Already gone (the app was killed).
    }
  }

  /** Evaluates an expression in the page and returns its JSON value. */
  async eval(expression) {
    const result = await this.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
    if (result.exceptionDetails) {
      throw new Error(`Page threw: ${result.exceptionDetails.exception?.description ?? result.exceptionDetails.text}`);
    }
    return result.result.value;
  }

  /** Waits until `expression` is truthy; returns its value. */
  async waitFor(expression, what, timeoutMs = 20_000) {
    const deadline = Date.now() + timeoutMs;
    for (;;) {
      let value;
      try {
        value = await this.eval(expression);
      } catch {
        value = undefined;
      }
      if (value) return value;
      if (Date.now() > deadline) throw new Error(`Timed out after ${timeoutMs} ms waiting for ${what}`);
      await sleep(150);
    }
  }

  /**
   * Finds a visible element and returns its centre. `target` is a CSS selector string, or
   * `{ role?, name, exact?, within? }` matching buttons, links, inputs and ARIA roles by accessible name
   * (aria-label, else text, else placeholder).
   */
  async locate(target) {
    const spec = typeof target === 'string' ? { selector: target } : target;
    const found = await this.eval(`(() => {
      const spec = ${JSON.stringify(spec)};
      const visible = (el) => { const r = el.getBoundingClientRect(); const s = getComputedStyle(el); return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && s.display !== 'none'; };
      const nameOf = (el) => (el.getAttribute('aria-label') || el.innerText || el.value || el.getAttribute('placeholder') || el.getAttribute('title') || '').trim().replace(/\\s+/g, ' ');
      const scope = spec.within ? document.querySelector(spec.within) : document;
      if (!scope) return null;
      let candidates;
      if (spec.selector) candidates = [...scope.querySelectorAll(spec.selector)];
      else {
        const roles = spec.role ? [spec.role] : ['button', 'link', 'menuitem', 'tab', 'checkbox', 'switch', 'radio', 'option', 'textbox', 'searchbox', 'slider'];
        const css = roles.map((r) => ({ button: 'button,[role=button]', link: 'a,[role=link]', textbox: 'input:not([type=checkbox]):not([type=radio]),textarea,[role=textbox]', searchbox: 'input[type=search],[role=searchbox]', checkbox: 'input[type=checkbox],[role=checkbox]', radio: 'input[type=radio],[role=radio]' }[r] ?? '[role=' + r + ']')).join(',');
        candidates = [...scope.querySelectorAll(css)].filter((el) => {
          const n = nameOf(el);
          return spec.exact === false ? n.toLowerCase().includes(spec.name.toLowerCase()) : n === spec.name;
        });
      }
      const el = candidates.find(visible);
      if (!el) return null;
      el.scrollIntoView({ block: 'center', inline: 'center' });
      const r = el.getBoundingClientRect();
      return { x: r.left + r.width / 2, y: r.top + r.height / 2, name: nameOf(el), disabled: !!el.disabled || el.getAttribute('aria-disabled') === 'true' };
    })()`);
    return found;
  }

  async click(target, { timeoutMs = 15_000, allowDisabled = false } = {}) {
    const deadline = Date.now() + timeoutMs;
    for (;;) {
      const at = await this.locate(target);
      if (at && (!at.disabled || allowDisabled)) {
        await this.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: at.x, y: at.y });
        await this.send('Input.dispatchMouseEvent', { type: 'mousePressed', x: at.x, y: at.y, button: 'left', clickCount: 1 });
        await this.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: at.x, y: at.y, button: 'left', clickCount: 1 });
        return at;
      }
      if (Date.now() > deadline) throw new Error(`Nothing clickable for ${JSON.stringify(target)}${at ? ' (disabled)' : ''}`);
      await sleep(150);
    }
  }

  /** Clicks at a fraction (0..1) of an element's width, e.g. to seek on a waveform. */
  async clickAt(selector, fractionX) {
    const rect = await this.eval(`(() => { const el = document.querySelector(${JSON.stringify(selector)}); if (!el) return null; el.scrollIntoView({block:'center'}); const r = el.getBoundingClientRect(); return { left: r.left, top: r.top, width: r.width, height: r.height }; })()`);
    if (!rect) throw new Error(`No ${selector}`);
    const x = rect.left + rect.width * fractionX;
    const y = rect.top + rect.height / 2;
    await this.send('Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button: 'left', clickCount: 1 });
    await this.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x, y, button: 'left', clickCount: 1 });
  }

  async type(text) {
    await this.send('Input.insertText', { text });
  }

  /** Presses a key: 'Enter', 'Escape', 'Space', 'Tab', 'Backspace', a letter, with optional modifiers ['ctrl'|'shift'|'alt']. */
  async key(key, modifiers = []) {
    const codes = { Enter: [13, 'Enter', '\r'], Escape: [27, 'Escape'], Space: [32, 'Space', ' '], Tab: [9, 'Tab'], Backspace: [8, 'Backspace'], ArrowLeft: [37, 'ArrowLeft'], ArrowRight: [39, 'ArrowRight'] };
    const [vk, code, text] = codes[key] ?? [key.toUpperCase().charCodeAt(0), `Key${key.toUpperCase()}`, key];
    const keyName = key === 'Space' ? ' ' : key;
    const mask = (modifiers.includes('alt') ? 1 : 0) | (modifiers.includes('ctrl') ? 2 : 0) | (modifiers.includes('shift') ? 8 : 0);
    const base = { key: keyName, code, windowsVirtualKeyCode: vk, nativeVirtualKeyCode: vk, modifiers: mask };
    await this.send('Input.dispatchKeyEvent', { type: mask ? 'rawKeyDown' : 'keyDown', ...base, ...(text && !mask ? { text } : {}) });
    await this.send('Input.dispatchKeyEvent', { type: 'keyUp', ...base });
  }

  async screenshot(path) {
    const { data } = await this.send('Page.captureScreenshot', { format: 'png' });
    await mkdir(dirname(path), { recursive: true });
    await writeFile(path, Buffer.from(data, 'base64'));
  }

  /** Visible text of the page (or of one element). */
  text(selector = 'body') {
    return this.eval(`(document.querySelector(${JSON.stringify(selector)})?.innerText ?? '')`);
  }

  /** Every visible control with its accessible name, for discovering what to click. */
  controls() {
    return this.eval(`[...document.querySelectorAll('button,a,input,textarea,select,[role=button],[role=tab],[role=switch],[role=checkbox],[role=menuitem],[role=slider]')]
      .filter((el) => { const r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0; })
      .map((el) => el.tagName.toLowerCase() + (el.type ? '[' + el.type + ']' : '') + ': ' + (el.getAttribute('aria-label') || el.innerText || el.value || el.getAttribute('placeholder') || '').trim().replace(/\\s+/g, ' ').slice(0, 80) + (el.disabled ? ' (disabled)' : '') + (el.getAttribute('aria-pressed') ? ' pressed=' + el.getAttribute('aria-pressed') : '') + (el.checked ? ' (checked)' : ''))`);
  }
}

export { sleep };
