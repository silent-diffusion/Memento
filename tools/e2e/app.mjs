// Launches the published Memento.exe with a private data root (LOCALAPPDATA pointed at a folder under artifacts/),
// so the real library, settings and WebView2 profile are never touched, and opens DevTools on a local port.

import { spawn } from 'node:child_process';
import { mkdir } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { Page, sleep } from './cdp.mjs';

export const repoRoot = resolve(import.meta.dirname, '..', '..');

export class App {
  constructor({ exe = join(repoRoot, 'artifacts', 'publish', 'win-x64', 'Memento.exe'), dataRoot, port = 9333, args = [] }) {
    this.exe = exe;
    this.dataRoot = dataRoot;
    this.port = port;
    this.args = args;
    this.process = null;
    this.page = null;
  }

  get memento() {
    return join(this.dataRoot, 'Memento');
  }

  async start() {
    await mkdir(this.dataRoot, { recursive: true });
    this.process = spawn(this.exe, this.args, {
      env: {
        ...process.env,
        LOCALAPPDATA: this.dataRoot,
        WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-port=${this.port}`,
      },
      stdio: 'ignore',
      detached: false,
    });
    this.exited = new Promise((done) => this.process.once('exit', (code) => done(code)));
    this.page = await Page.connect(this.port, { timeoutMs: 90_000 });
    await this.page.waitFor(`document.querySelector('#app, main, body')?.innerText?.length > 0`, 'the UI to render', 30_000);
    return this.page;
  }

  /** Closes the window the normal way (WM_CLOSE through taskkill without /F) and waits for the exit. */
  async close(timeoutMs = 30_000) {
    this.page?.close();
    spawn('taskkill', ['/PID', String(this.process.pid)], { stdio: 'ignore' });
    const code = await Promise.race([this.exited, sleep(timeoutMs).then(() => 'timeout')]);
    if (code === 'timeout') {
      await this.kill();
      throw new Error('Memento did not close within the timeout');
    }
    return code;
  }

  /** Terminates the process at once (TerminateProcess), like a crash or power cut for the app. */
  async kill() {
    this.page?.close();
    spawn('taskkill', ['/F', '/T', '/PID', String(this.process.pid)], { stdio: 'ignore' });
    return this.exited;
  }
}
