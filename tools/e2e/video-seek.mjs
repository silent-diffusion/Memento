// Video spike (ENGINE-NOTES.md §M.3): does an MP4 that Memento.Video wrote play and seek in the browser engine
// WebView2 uses? Serves the file over HTTP with range requests (as the app's virtual host does), opens it in headless
// Edge through the DevTools protocol, seeks to times spread over the whole file and reads the frame number that
// VideoCheck's synthetic source draws as a 24-bit barcode in the top-left corner (32 px cells, white = 1).
//
//   node tools/e2e/video-seek.mjs <file.mp4> [--fps 30] [--seeks 40] [--edge "C:\...\msedge.exe"]
//
// Prints one line per seek (target time, frame shown, error in frames, seek time) and a summary. Node 22+.

import { createServer } from 'node:http';
import { open, stat, mkdtemp, rm } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const args = process.argv.slice(2);
const file = resolve(args[0]);
const option = (name, fallback) => {
  const i = args.indexOf(`--${name}`);
  return i >= 0 ? args[i + 1] : fallback;
};
const fps = Number(option('fps', '30'));
const seeks = Number(option('seeks', '40'));
// Frames the barcode runs ahead of the file's time (files made before VideoCheck --fast used the source's own origin).
const offset = Number(option('offset', '0'));
const edge = option('edge', 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const page = `<!doctype html><meta charset="utf-8"><video id="v" muted preload="metadata" src="/video.mp4"></video><canvas id="c"></canvas>`;

const server = createServer(async (req, res) => {
  if (req.url === '/') {
    res.writeHead(200, { 'content-type': 'text/html' }).end(page);
    return;
  }
  if (req.url !== '/video.mp4') {
    res.writeHead(404).end();
    return;
  }
  const size = (await stat(file)).size;
  const range = /bytes=(\d*)-(\d*)/.exec(req.headers.range ?? '');
  let start = 0;
  let end = size - 1;
  if (range) {
    if (range[1] === '') {
      start = size - Number(range[2]);
    } else {
      start = Number(range[1]);
      if (range[2] !== '') end = Math.min(Number(range[2]), size - 1);
    }
  }
  const handle = await open(file, 'r');
  const length = end - start + 1;
  res.writeHead(range ? 206 : 200, {
    'content-type': 'video/mp4',
    'accept-ranges': 'bytes',
    'content-length': length,
    ...(range ? { 'content-range': `bytes ${start}-${end}/${size}` } : {}),
  });
  const stream = handle.createReadStream({ start, end, autoClose: true });
  stream.on('close', () => handle.close().catch(() => {}));
  stream.pipe(res);
  served += length;
});
let served = 0;
await new Promise((r) => server.listen(0, '127.0.0.1', r));
const port = server.address().port;

const profile = await mkdtemp(join(tmpdir(), 'video-seek-'));
const debugPort = 9300 + Math.floor(Math.random() * 500);
const browser = spawn(edge, [
  '--headless=new', `--remote-debugging-port=${debugPort}`, `--user-data-dir=${profile}`,
  '--no-first-run', '--autoplay-policy=no-user-gesture-required', `http://127.0.0.1:${port}/`,
], { stdio: 'ignore' });

let ws;
try {
  let target;
  for (let i = 0; i < 80 && !target; i++) {
    try {
      const list = await (await fetch(`http://127.0.0.1:${debugPort}/json/list`)).json();
      target = list.find((t) => t.type === 'page' && t.url.startsWith(`http://127.0.0.1:${port}`));
    } catch { /* starting */ }
    if (!target) await sleep(250);
  }
  if (!target) throw new Error('Edge did not open the page');
  ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });
  let id = 0;
  const pending = new Map();
  ws.onmessage = (m) => { const d = JSON.parse(m.data); if (d.id && pending.has(d.id)) { pending.get(d.id)(d); pending.delete(d.id); } };
  const evaluate = (expression) => new Promise((r) => {
    const n = ++id;
    pending.set(n, (d) => r(d.result?.exceptionDetails ? { exception: d.result.exceptionDetails.exception?.value ?? d.result.exceptionDetails.text } : d.result?.result?.value ?? d.error));
    ws.send(JSON.stringify({ id: n, method: 'Runtime.evaluate', params: { expression, awaitPromise: true, returnByValue: true } }));
  });

  await sleep(1500);
  const opened = await evaluate(`(async () => {
    while (!document.getElementById('v')) await new Promise((r) => setTimeout(r, 50));
    const v = document.getElementById('v');
    const t0 = performance.now();
    if (v.readyState < 1) await new Promise((r, j) => { v.onloadedmetadata = r; v.onerror = () => j(v.error?.message ?? 'error ' + v.error?.code); setTimeout(() => j('timeout'), 30000); });
    return { duration: v.duration, durationText: String(v.duration), seekableEnd: v.seekable.length ? String(v.seekable.end(0)) : "none", width: v.videoWidth, height: v.videoHeight, ms: performance.now() - t0 };
  })()`);
  console.log(`opened: ${JSON.stringify(opened)}`);
  if (!opened || typeof opened.duration !== 'number') throw new Error(`could not open: ${JSON.stringify(opened)}`);

  const read = `(() => {
    const v = document.getElementById('v'), c = document.getElementById('c');
    c.width = v.videoWidth; c.height = 32;
    const g = c.getContext('2d', { willReadFrequently: true });
    g.drawImage(v, 0, 0, v.videoWidth, 32, 0, 0, v.videoWidth, 32);
    let slot = 0;
    for (let bit = 0; bit < 24; bit++) {
      const p = g.getImageData(bit * 32 + 16, 16, 1, 1).data;
      if ((p[0] + p[1] + p[2]) / 3 > 128) slot |= 1 << bit;
    }
    return slot;
  })()`;

  const results = [];
  const duration = opened.duration;
  for (let i = 0; i < seeks; i++) {
    // Spread over the file, avoiding exact frame boundaries; the last one near the end.
    const t = i === seeks - 1 ? duration - 0.5 : (duration * (i + 0.37)) / seeks;
    const r = await evaluate(`(async () => {
      const v = document.getElementById('v');
      const t0 = performance.now();
      v.currentTime = ${t};
      await new Promise((r, j) => { v.onseeked = r; setTimeout(() => j('seek timeout'), 30000); });
      await new Promise((r) => { if (v.requestVideoFrameCallback) v.requestVideoFrameCallback(() => r()); setTimeout(r, 500); });
      return { ms: performance.now() - t0, slot: ${read}, current: v.currentTime };
    })()`);
    const expected = Math.floor(t * fps + 1e-6) + offset;
    const error = typeof r?.slot === 'number' ? r.slot - expected : null;
    results.push({ t, ...r, error });
    console.log(`seek ${t.toFixed(3)} s → frame ${r?.slot} (expected ${expected}, error ${error}) in ${r?.ms?.toFixed(0)} ms`);
  }

  const played = await evaluate(`(async () => {
    const v = document.getElementById('v');
    v.currentTime = ${Math.max(0, duration / 2)};
    await new Promise((r) => { v.onseeked = r; });
    const from = v.currentTime;
    await v.play();
    await new Promise((r) => setTimeout(r, 2000));
    v.pause();
    return { from, to: v.currentTime };
  })()`);
  console.log(`play from the middle for 2 s: ${JSON.stringify(played)}`);

  const ok = results.filter((r) => r.error !== null && Math.abs(r.error) <= 1);
  const times = results.map((r) => r.ms).filter((x) => typeof x === 'number').sort((a, b) => a - b);
  const errors = results.map((r) => r.error).filter((e) => e !== null);
  console.log(`errors: min ${Math.min(...errors)} max ${Math.max(...errors)} frames`);
  console.log(`summary: ${ok.length}/${results.length} seeks within one frame; seek time median ${times[Math.floor(times.length / 2)]?.toFixed(0)} ms, max ${times[times.length - 1]?.toFixed(0)} ms`);
} finally {
  ws?.close();
  browser.kill();
  server.close();
  await sleep(500);
  await rm(profile, { recursive: true, force: true }).catch(() => {});
}
