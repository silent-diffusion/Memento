// Starts the published app with a private data root and keeps it running until it exits (for exploring the UI
// with other CDP clients on the same port). Usage: node tools/e2e/launch.mjs <dataRoot> [--simulate-audio]
import { resolve } from 'node:path';
import { App } from './app.mjs';

const [dataRoot, ...args] = process.argv.slice(2);
if (!dataRoot) {
  console.error('Usage: node tools/e2e/launch.mjs <dataRoot> [Memento arguments]');
  process.exit(2);
}

const app = new App({ dataRoot: resolve(dataRoot), args });
await app.start();
console.log(`Memento running (pid ${app.process.pid}), DevTools on port ${app.port}, data under ${app.memento}`);
app.page.close();
console.log(`Memento exited with ${await app.exited}`);
