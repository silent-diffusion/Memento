// Shared helpers for the H1 hardening scripts (h1-*.mjs): start the published app with a private data root and extra
// switches, read the bridge's events in the page, look at project files and the log, and save screenshots.
// The scripts drive the UI through the DevTools protocol like m1/m2/m3-flow; `bridge()` is used only to read state
// (and where a script says so explicitly, to set up a precondition that has no UI, such as a long import).

import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { cpSync, existsSync, mkdirSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { App, repoRoot } from './app.mjs';
import { sleep } from './cdp.mjs';

export { repoRoot, sleep };

export const option = (args, name, fallback) => (args.includes(name) ? args[args.indexOf(name) + 1] : fallback);

export class Run {
  constructor({ name, dataRoot, out, port = 9420, exe, models }) {
    this.name = name;
    this.dataRoot = resolve(dataRoot);
    this.out = resolve(out);
    this.port = port;
    this.exe = exe ? resolve(exe) : undefined;
    this.models = models ? resolve(models) : undefined;
    this.t0 = Date.now();
    this.shots = 0;
    this.results = [];
    this.app = null;
    this.page = null;
    mkdirSync(this.out, { recursive: true });
  }

  get memento() {
    return join(this.dataRoot, 'Memento');
  }

  get library() {
    return join(this.memento, 'Library', 'projects');
  }

  log(step, detail = '') {
    console.log(`[${((Date.now() - this.t0) / 1000).toFixed(0).padStart(5)} s] ${step}${detail ? ' · ' + detail : ''}`);
  }

  /** Records a check: name, passed, detail. Never throws, so one failing case does not hide the others. */
  check(name, passed, detail = '') {
    this.results.push({ name, passed: !!passed, detail });
    this.log(passed ? 'PASS' : 'FAIL', `${name}${detail ? ' — ' + detail : ''}`);
    return !!passed;
  }

  copyModels() {
    if (this.models) cpSync(this.models, join(this.memento, 'models'), { recursive: true });
  }

  async start(args = []) {
    this.app = new App({ dataRoot: this.dataRoot, port: this.port, args, ...(this.exe ? { exe: this.exe } : {}) });
    this.page = await this.app.start();
    await this.installEventTap();
    return this.page;
  }

  /** Keeps every bridge event the page receives in window.__h1.events (the page's own listeners are untouched). */
  async installEventTap() {
    await this.page.eval(`(() => {
      if (window.__h1) return true;
      window.__h1 = { next: 800000, pending: new Map(), events: [] };
      window.chrome.webview.addEventListener('message', (e) => {
        const data = typeof e.data === 'string' ? JSON.parse(e.data) : e.data;
        if (data && data.id !== undefined && window.__h1.pending.has(data.id)) {
          window.__h1.pending.get(data.id)(data);
          window.__h1.pending.delete(data.id);
        } else if (data && data.event) {
          window.__h1.events.push({ at: Date.now(), event: data.event, payload: data.payload });
          if (window.__h1.events.length > 5000) window.__h1.events.splice(0, 1000);
        }
      });
      window.__h1.call = (method, params) => new Promise((done) => {
        const id = window.__h1.next++;
        window.__h1.pending.set(id, done);
        window.chrome.webview.postMessage({ id, method, params: params ?? {} });
      });
      return true;
    })()`);
  }

  async bridge(method, params = {}) {
    const response = await this.page.eval(`window.__h1.call(${JSON.stringify(method)}, ${JSON.stringify(params)})`);
    if (response.error) {
      const error = new Error(`${method}: ${response.error.code}: ${response.error.message}`);
      error.code = response.error.code;
      throw error;
    }
    return response.result;
  }

  events(name) {
    return this.page.eval(`window.__h1.events.filter((e) => e.event === ${JSON.stringify(name)})`);
  }

  async shot(label) {
    await sleep(400);
    this.shots += 1;
    const file = join(this.out, `${String(this.shots).padStart(2, '0')}-${label}.png`);
    await this.page.screenshot(file);
    return file;
  }

  hasText(text, timeoutMs = 20_000) {
    return this.page.waitFor(`document.body.innerText.includes(${JSON.stringify(text)})`, `"${text}"`, timeoutMs);
  }

  text(selector = 'body') {
    return this.page.text(selector);
  }

  projectIds() {
    return existsSync(this.library) ? readdirSync(this.library).sort() : [];
  }

  folder(id) {
    return join(this.library, id);
  }

  manifest(id) {
    return JSON.parse(readFileSync(join(this.folder(id), 'project.json'), 'utf8'));
  }

  history(id) {
    const file = join(this.folder(id), 'history.jsonl');
    return existsSync(file) ? readFileSync(file, 'utf8').trim().split('\n').filter(Boolean).map((l) => JSON.parse(l)) : [];
  }

  logText() {
    const logs = join(this.memento, 'logs');
    if (!existsSync(logs)) return '';
    return readdirSync(logs).filter((f) => f.endsWith('.log')).sort().map((f) => readFileSync(join(logs, f), 'utf8')).join('');
  }

  logLines(pattern) {
    return this.logText().split('\n').filter((l) => pattern.test(l));
  }

  crashReports() {
    const logs = join(this.memento, 'logs');
    return existsSync(logs) ? readdirSync(logs).filter((f) => f.startsWith('crash-')) : [];
  }

  /** Waits until `predicate()` (sync or async) is truthy; returns its value. */
  async until(predicate, what, timeoutMs = 30_000, everyMs = 100) {
    const deadline = Date.now() + timeoutMs;
    for (;;) {
      let value;
      try {
        value = await predicate();
      } catch {
        value = undefined;
      }
      if (value) return value;
      if (Date.now() > deadline) throw new Error(`Timed out after ${timeoutMs} ms waiting for ${what}`);
      await sleep(everyMs);
    }
  }

  /** Recorded seconds from the Record screen's timer. */
  async elapsedSeconds() {
    const text = await this.page.text('[aria-label="Recording"]');
    const match = /(\d\d):(\d\d):(\d\d)/.exec(text);
    return match ? Number(match[1]) * 3600 + Number(match[2]) * 60 + Number(match[3]) : 0;
  }

  async waitElapsed(seconds) {
    await this.page.waitFor(
      `(() => { const m = /(\\d\\d):(\\d\\d):(\\d\\d)/.exec(document.querySelector('[aria-label="Recording"]')?.innerText ?? ''); return m && (+m[1]*3600 + +m[2]*60 + +m[3]) >= ${seconds}; })()`,
      `${seconds} s recorded`,
      (seconds + 90) * 1000,
    );
  }

  /** Library › New recording with a title; the remembered or default sources stay selected. */
  async newRecording(title) {
    if (!(await this.page.locate({ name: 'New recording' }))) {
      await this.page.click({ name: 'Library' });
    }
    await this.page.click({ name: 'New recording' });
    await this.hasText('AUDIO SOURCES');
    await this.page.click({ role: 'textbox', name: 'Untitled meeting', exact: false });
    await this.page.key('a', ['ctrl']);
    await this.page.type(title);
    await this.page.key('Enter');
  }

  async killApp() {
    await this.app.kill();
    // The worker belongs to the kill-on-close job object; give Windows a moment to end it.
    await sleep(1500);
  }

  workerPids() {
    try {
      const text = execFileSync('tasklist', ['/FI', 'IMAGENAME eq Memento.Worker.exe', '/FO', 'CSV', '/NH'], { encoding: 'utf8' });
      return text.split('\n').map((l) => l.split('","')[1]).filter(Boolean).map(Number);
    } catch {
      return [];
    }
  }

  summary() {
    const failed = this.results.filter((r) => !r.passed);
    return { name: this.name, passed: this.results.length - failed.length, failed: failed.length, results: this.results, crashReports: this.crashReports() };
  }
}

export const sha256 = (file) => createHash('sha256').update(readFileSync(file)).digest('hex');

export function filesOf(folder, prefix = '') {
  return readdirSync(folder).flatMap((name) => {
    const full = join(folder, name);
    return statSync(full).isDirectory() ? filesOf(full, `${prefix}${name}/`) : [{ file: `${prefix}${name}`, bytes: statSync(full).size }];
  });
}
