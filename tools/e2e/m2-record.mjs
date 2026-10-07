// M2 end-to-end check on real devices: starts the published app with a private data root, records the default
// microphone and "everything this PC plays" for a while (play a public-domain speech file through the speakers
// meanwhile), stops, waits for transcript, speakers and topics, and prints what the transcript holds. The bridge is
// called directly here (the M2 UI is checked separately). The recording is deleted through the Delete flow at the end.
//
//   node tools/e2e/m2-record.mjs <dataRoot> [--seconds 120] [--play <wav>] [--keep]
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { App } from './app.mjs';
import { sleep } from './cdp.mjs';

const args = process.argv.slice(2);
const dataRoot = resolve(args[0] ?? '');
const option = (name, fallback) => (args.includes(name) ? args[args.indexOf(name) + 1] : fallback);
const seconds = Number(option('--seconds', '120'));
const play = option('--play', null);

const app = new App({ dataRoot });
const page = await app.start();
await page.eval(`(() => {
  window.__m2 = { next: 900000, pending: new Map(), events: [] };
  window.chrome.webview.addEventListener('message', (e) => {
    const data = typeof e.data === 'string' ? JSON.parse(e.data) : e.data;
    if (data && data.id !== undefined && window.__m2.pending.has(data.id)) {
      window.__m2.pending.get(data.id)(data);
      window.__m2.pending.delete(data.id);
    } else if (data && data.event) {
      window.__m2.events.push(data.event);
    }
  });
  window.__m2.call = (method, params) => new Promise((done) => {
    const id = window.__m2.next++;
    window.__m2.pending.set(id, done);
    window.chrome.webview.postMessage({ id, method, params: params ?? {} });
  });
  return true;
})()`);

async function call(method, params = {}) {
  const response = await page.eval(`window.__m2.call(${JSON.stringify(method)}, ${JSON.stringify(params)})`);
  if (response.error) throw new Error(`${method}: ${response.error.code}: ${response.error.message}`);
  return response.result;
}

const sources = (await call('sources.list')).audio;
const mic = sources.find((s) => s.kind === 'microphone' && s.isDefault) ?? sources.find((s) => s.kind === 'microphone');
const system = sources.find((s) => s.kind === 'system');
console.log(`sources: mic "${mic?.name}", system "${system?.name}"`);
const engine = await call('engine.status');
console.log(`engine: ${JSON.stringify(engine.transcription)}`);

let player = null;
if (play) {
  // Plays through the default output, so the system track hears it (and the microphone, faintly, too).
  player = spawn('powershell', ['-NoProfile', '-Command', `(New-Object Media.SoundPlayer '${play}').PlaySync()`], { stdio: 'ignore' });
  await sleep(500);
}

const started = await call('recording.start', { title: 'M2 real-device check', type: 'meeting', sourceIds: [mic.id, system.id] });
console.log(`recording ${started.recordingId} for ${seconds} s`);
await sleep(seconds * 1000);
await call('recording.stop', { sessionId: started.sessionId });
player?.kill();
const id = started.recordingId;

const begun = Date.now();
let last = '';
for (;;) {
  const processing = await call('library.processing');
  const got = await call('transcript.get', { recordingId: id });
  const stages = processing.current?.recordingId === id ? processing.current.stages.map((s) => `${s.stage}=${s.state}(${s.label})`).join(' ') : 'idle';
  const line = `${got.status} | ${stages}`;
  if (line !== last) {
    console.log(`  +${Math.round((Date.now() - begun) / 1000)} s ${line}`);
    last = line;
  }
  if (processing.current?.recordingId !== id && got.status !== 'queued' && got.status !== 'running') break;
  if (Date.now() - begun > 15 * 60_000) throw new Error('processing did not finish in 15 minutes');
  await sleep(2000);
}

const { transcript, status, failure } = await call('transcript.get', { recordingId: id });
console.log(`status ${status}${failure ? `, failure: ${failure.message}` : ''}`);
const project = await call('project.get', { recordingId: id });
const folder = join(app.memento, 'Library', 'projects', id);
console.log(`transcript.json exists: ${existsSync(join(folder, 'transcript.json'))}`);
if (transcript) {
  const byTrack = {};
  for (const s of transcript.segments) byTrack[s.track] = (byTrack[s.track] ?? 0) + 1;
  console.log(`engine ${transcript.engine.model} on ${transcript.engine.device} in ${transcript.engine.durationMs} ms; language ${transcript.language}`);
  console.log(`${transcript.segments.length} segments by track ${JSON.stringify(byTrack)}; speakers ${transcript.speakers.map((s) => `${s.id} ${s.talkTimeMs} ms`).join(', ')}`);
  console.log(`with a speaker: ${transcript.segments.filter((s) => s.speaker).length}; coverage gaps ${transcript.coverageGaps.length}`);
  for (const s of transcript.segments.slice(0, 5)) console.log(`  [${s.start.toFixed(1)}] ${s.track} ${s.speaker} ${s.text.slice(0, 90)}`);
}
console.log(`topics: ${project.topics.map((t) => t.label).join(', ')}`);
console.log(`history: ${project.history.map((h) => `${h.stage}/${h.event}`).join(', ')}`);

if (!args.includes('--keep')) {
  await call('project.delete', { recordingId: id });
  console.log(`deleted ${id} through the Delete flow; folder exists: ${existsSync(folder)}`);
}

await app.close();
