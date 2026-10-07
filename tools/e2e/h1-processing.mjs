// H1 processing checks through the real app and Memento.Worker.exe:
//  A. A 2-hour public-domain recording transcribed end to end on the graphics card, with a busy pause (a recording
//     starts mid-window and stops again) and a cancel mid-window (then "Continue transcribing"); then transcribed
//     again, the worker killed inside its first window, and "Retry on CPU" carries the whole pass on the processor.
//  B. A model download interrupted at 30% by killing Memento, resumed after relaunch (Range request), verified; then an
//     installed model with flipped bytes is detected before use, reported, and "Download … again" replaces it.
// Models come from a throttled mirror on this PC (--model-mirror; same sizes and SHA-256 as the catalog). The import
// is started with library.importMedia { path } (the picker is Windows' own dialog) and processing.cancel is called
// on the bridge because the UI has no cancel control for a running stage (reported). Speakers are off in A, so the
// numbers are the transcription pass alone.
//
//   node tools/e2e/h1-processing.mjs --audio2h <wav> --short <wav> --models <dir> [--part A|B] [--data <dir>] [--out <dir>]
import { createReadStream, existsSync, mkdirSync, readFileSync, rmSync, statSync, writeFileSync, copyFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { execFileSync } from 'node:child_process';
import { join, resolve } from 'node:path';
import { Run, option, repoRoot, sha256, sleep } from './h1-lib.mjs';

const args = process.argv.slice(2);
const base = resolve(option(args, '--data', join(repoRoot, 'artifacts', 'e2e-data-h1-processing')));
const out = resolve(option(args, '--out', join(repoRoot, 'artifacts', 'e2e', 'h1-processing')));
const audio2h = resolve(option(args, '--audio2h', ''));
const shortAudio = resolve(option(args, '--short', ''));
const models = resolve(option(args, '--models', ''));
const part = option(args, '--part', null);
const summaries = [];

const transcriptStage = (run, id) => {
  try {
    return run.manifest(id).stages.find((s) => s.stage === 'transcript') ?? null;
  } catch {
    return null;
  }
};
const partial = (run, id) => {
  try {
    return JSON.parse(readFileSync(join(run.folder(id), 'transcript.partial.json'), 'utf8'));
  } catch {
    return null;
  }
};
const windowsDone = (run, id) => (partial(run, id)?.tracks ?? []).reduce((n, t) => n + (t.windowsDone ?? 0), 0);
const failureCard = (run) => run.page.eval(`document.querySelector('.tx-failed')?.innerText.replace(/\\s+/g, ' ') ?? null`);
const toSeconds = (clock) => clock.split(':').map(Number).reduce((s, v) => s * 60 + v, 0);

/** "… · 1:59:37 of speech tracks in 412.3 s · …" from the last completed transcript line. */
function passNumbers(run, id) {
  const line = run.history(id).filter((h) => h.stage === 'transcript' && h.event === 'completed').at(-1);
  const m = line && /([\d:]+) of speech tracks in ([\d.]+) s/.exec(line.detail ?? '');
  return m ? { audioSeconds: toSeconds(m[1]), seconds: Number(m[2]), rtf: Math.round((Number(m[2]) / toSeconds(m[1])) * 1000) / 1000, detail: line.detail } : { detail: line?.detail };
}

async function partA() {
  const run = new Run({ name: 'A. 2-hour file, busy pause, cancel, crash, CPU', dataRoot: join(base, 'A'), out: join(out, 'A'), port: 9460, models });
  rmSync(run.dataRoot, { recursive: true, force: true });
  run.copyModels();
  const numbers = {};
  try {
    await run.start(['--simulate-audio', '--update-feed=off']);
    await run.bridge('settings.set', { speakers: { identify: false } });
    await run.bridge('library.importMedia', { path: audio2h, title: 'Two hours of readings' });
    const [id] = run.projectIds();
    await run.until(() => transcriptStage(run, id)?.state === 'active', 'transcription to start', 10 * 60_000, 500);
    const gpuStarted = Date.now();
    await run.page.click({ name: 'Two hours of readings', exact: false });
    await sleep(2000);
    await run.shot('gpu-transcribing');

    // Busy pause: a recording starts while window 3 runs.
    await run.until(() => windowsDone(run, id) >= 2, 'two windows', 20 * 60_000, 500);
    await sleep(4000);
    const beforePause = windowsDone(run, id);
    await run.newRecording('Busy pause');
    await run.page.click({ name: 'Start recording' });
    await run.hasText('RECORDING');
    const paused = await run.until(() => /Paused/.test(transcriptStage(run, id)?.label ?? '') && transcriptStage(run, id), 'the pause', 60_000, 200).catch(() => null);
    await sleep(3000);
    const workersWhilePaused = run.workerPids().length;
    await run.page.click({ name: 'Library' });
    await sleep(800);
    await run.shot('busy-paused-library');
    const footer = await run.text('footer');
    run.check('busy pause: transcription waits while recording, says why', !!paused && /PC is busy/.test(paused.label) && /Transcription paused · PC is busy/.test(footer), `${paused?.label ?? '(not paused)'} | footer: ${footer.replace(/\s+/g, ' ')}`);
    run.check('busy pause: the worker is stopped while it waits', workersWhilePaused === 0, `${workersWhilePaused} worker(s)`);
    await sleep(15_000);
    await run.page.click({ name: 'Back to recording' }).catch(() => run.page.click({ name: 'Busy pause', exact: false }));
    await run.page.click({ name: 'Stop and open review' });
    await run.until(() => transcriptStage(run, id)?.state === 'active' && !/Paused/.test(transcriptStage(run, id)?.label ?? ''), 'transcription to resume', 120_000, 500);
    await run.until(() => windowsDone(run, id) > beforePause, 'a window after the pause', 10 * 60_000, 500);
    const resumedLine = run.history(id).filter((h) => h.stage === 'transcript' && h.event === 'started').at(-1);
    run.check('busy pause: the pass continues from the finished windows', /continuing where it stopped/.test(resumedLine?.summary ?? '') && windowsDone(run, id) >= beforePause, `${resumedLine?.summary}; windows ${beforePause} → ${windowsDone(run, id)}`);

    // Cancel mid-window, then Continue transcribing (Review's failure card).
    await run.until(() => windowsDone(run, id) >= beforePause + 2, 'two more windows', 20 * 60_000, 500);
    await sleep(5000);
    const beforeCancel = windowsDone(run, id);
    await run.bridge('processing.cancel', { recordingId: id, stage: 'transcript' });
    await run.page.click({ name: 'Library' });
    await run.page.click({ name: 'Two hours of readings', exact: false });
    const card = await run.until(() => failureCard(run), 'the cancelled card', 30_000).catch(() => null);
    await run.shot('cancelled-card');
    run.check('cancel: the card says where it stopped and offers to continue', !!card && /Transcription was cancelled at [\d:]+\./.test(card) && card.includes('Continue transcribing'), card ?? '(none)');
    await run.page.click({ name: 'Continue transcribing' });
    await run.until(() => transcriptStage(run, id)?.state === 'active', 'transcription to continue', 60_000, 300);
    await run.until(() => windowsDone(run, id) > beforeCancel || transcriptStage(run, id)?.state === 'done', 'a window after continuing', 10 * 60_000, 500);
    run.check('cancel: continuing keeps the windows already done', windowsDone(run, id) >= beforeCancel || transcriptStage(run, id)?.state === 'done', `windows ${beforeCancel} → ${windowsDone(run, id)}`);

    await run.until(() => transcriptStage(run, id)?.state === 'done', 'the GPU pass to finish', 60 * 60_000, 2000);
    numbers.gpu = { ...passNumbers(run, id), wallSeconds: Math.round((Date.now() - gpuStarted) / 1000) };
    const transcript = JSON.parse(readFileSync(join(run.folder(id), 'transcript.json'), 'utf8'));
    numbers.gpu.segments = transcript.segments.length;
    numbers.gpu.lastEnd = Math.round(transcript.segments.at(-1)?.end ?? 0);
    numbers.gpu.coverageGaps = transcript.coverageGaps?.length ?? 0;
    await run.shot('gpu-done');
    run.check('GPU: the whole 2-hour file is transcribed', numbers.gpu.lastEnd > 7000 && numbers.gpu.segments > 500, JSON.stringify(numbers.gpu));

    // Again, the worker killed inside its first window: Retry on CPU.
    await run.bridge('transcript.retranscribe', { recordingId: id });
    await run.until(() => transcriptStage(run, id)?.state === 'active' && run.workerPids().length > 0 && (transcriptStage(run, id)?.percent ?? 0) >= 1, 'the second pass', 10 * 60_000, 300);
    const killed = run.workerPids();
    execFileSync('taskkill', ['/F', '/PID', String(killed[0])]);
    const crash = await run.until(() => failureCard(run), 'the crash card', 60_000).catch(() => null);
    await run.shot('worker-crash-card');
    run.check('crash: the card names the failure, the code, what is kept, and offers Retry on CPU first', !!crash && /Transcription stopped/.test(crash) && /0x[0-9A-F]{8}/.test(crash) && /Retry on CPU/.test(crash), crash ?? '(none)');
    const cpuStarted = Date.now();
    await run.page.click({ name: 'Retry on CPU' });
    await run.until(() => transcriptStage(run, id)?.state === 'active', 'the CPU pass', 60_000, 300);
    await sleep(5000);
    await run.shot('cpu-transcribing');
    run.check('CPU: the pass runs on the processor', /CPU/.test(transcriptStage(run, id)?.label ?? ''), transcriptStage(run, id)?.label);
    await run.until(() => ['done', 'failed'].includes(transcriptStage(run, id)?.state), 'the CPU pass to finish', 6 * 60 * 60_000, 10_000);
    numbers.cpu = { ...passNumbers(run, id), wallSeconds: Math.round((Date.now() - cpuStarted) / 1000), state: transcriptStage(run, id)?.state };
    const cpuTranscript = JSON.parse(readFileSync(join(run.folder(id), 'transcript.json'), 'utf8'));
    numbers.cpu.segments = cpuTranscript.segments.length;
    numbers.cpu.lastEnd = Math.round(cpuTranscript.segments.at(-1)?.end ?? 0);
    numbers.cpu.device = cpuTranscript.engine.device;
    await run.shot('cpu-done');
    run.check('CPU: the whole 2-hour file is transcribed on the processor', numbers.cpu.state === 'done' && numbers.cpu.lastEnd > 7000 && /CPU/.test(numbers.cpu.device), JSON.stringify(numbers.cpu));
    run.log('history', run.history(id).filter((h) => h.stage === 'transcript').map((h) => `${h.event}: ${h.summary}`).join(' | '));
    await run.app.close();
  } catch (error) {
    run.check('part A ran to the end', false, error.stack);
    await run.shot('A-failure').catch(() => null);
    await run.app?.kill();
  }
  summaries.push({ ...run.summary(), numbers });
}

/** A throttled HTTP mirror of <models>/<engine>/<file> with Range support; logs every request. */
function startMirror(root, bytesPerSecond) {
  const requests = [];
  const server = createServer((request, response) => {
    const path = join(root, decodeURIComponent(new URL(request.url, 'http://x').pathname));
    requests.push({ url: request.url, range: request.headers.range ?? null });
    if (!existsSync(path)) {
      response.writeHead(404).end();
      return;
    }
    const size = statSync(path).size;
    const m = /bytes=(\d+)-/.exec(request.headers.range ?? '');
    const start = m ? Number(m[1]) : 0;
    response.writeHead(m ? 206 : 200, { 'Content-Length': size - start, ...(m ? { 'Content-Range': `bytes ${start}-${size - 1}/${size}` } : {}), 'Accept-Ranges': 'bytes' });
    // Paced by hand: each 256 KiB chunk waits until the rate allows it.
    const stream = createReadStream(path, { start, highWaterMark: 1 << 18 });
    let closed = false;
    response.on('close', () => {
      closed = true;
      stream.destroy();
    });
    (async () => {
      const began = Date.now();
      let sent = 0;
      for await (const chunk of stream) {
        if (closed) return;
        sent += chunk.length;
        const due = (sent / bytesPerSecond) * 1000 - (Date.now() - began);
        if (due > 0) await sleep(due);
        if (!response.write(chunk)) await new Promise((done) => response.once('drain', done));
      }
      response.end();
    })().catch(() => response.destroy());
  });
  return new Promise((done) => server.listen(0, '127.0.0.1', () => done({ server, port: server.address().port, requests })));
}

async function partB() {
  const run = new Run({ name: 'B. interrupted download, damaged model', dataRoot: join(base, 'B'), out: join(out, 'B'), port: 9461 });
  rmSync(run.dataRoot, { recursive: true, force: true });
  const mirror = await startMirror(models, 20 * 1024 * 1024);
  const appArgs = ['--simulate-audio', '--update-feed=off', `--model-mirror=http://127.0.0.1:${mirror.port}/`];
  const small = join(run.memento, 'models', 'whisper', 'ggml-small.bin');
  const result = {};
  try {
    // Only the speaker models are installed; Small is downloaded through Settings.
    mkdirSync(join(run.memento, 'models', 'sherpa-onnx'), { recursive: true });
    for (const f of ['nemo_en_titanet_small.onnx', 'pyannote-segmentation-3-0.onnx']) copyFileSync(join(models, 'sherpa-onnx', f), join(run.memento, 'models', 'sherpa-onnx', f));
    await run.start(appArgs);
    await run.page.click({ name: 'Settings' });
    await run.page.click({ name: 'Transcription' });
    await run.page.click({ name: 'Install Small', exact: false });
    const at30 = await run.until(async () => (await run.events('models.progress')).find((e) => e.payload.modelId === 'whisper-small' && e.payload.percent >= 30)?.payload, 'the download at 30%', 120_000, 100);
    await run.shot('download-30');
    await run.killApp();
    const kept = existsSync(small + '.part') ? statSync(small + '.part').size : 0;
    result.killedAt = `${at30.percent}% (${at30.bytesDone} bytes)`;
    result.partKept = kept;
    run.check('download: the partial file is kept when Memento is killed', kept > 0.25 * at30.bytesTotal, `${kept} of ${at30.bytesTotal} bytes`);

    await run.start(appArgs);
    await run.page.click({ name: 'Settings' });
    await run.page.click({ name: 'Transcription' });
    await sleep(1000);
    await run.shot('after-relaunch-settings');
    const before = mirror.requests.length;
    await run.page.click({ name: 'Install Small', exact: false });
    const done = await run.until(async () => (await run.events('models.progress')).find((e) => e.payload.modelId === 'whisper-small' && ['done', 'failed'].includes(e.payload.state))?.payload, 'the download to finish', 5 * 60_000, 200);
    const resumed = mirror.requests.slice(before).find((r) => r.url.endsWith('ggml-small.bin'));
    result.resumeRequest = resumed;
    result.finished = done;
    run.check('download: resumed with a Range request from the kept bytes', resumed?.range === `bytes=${kept}-`, JSON.stringify(resumed));
    run.check('download: verified and installed', done.state === 'done' && existsSync(small) && sha256(small) === sha256(join(models, 'whisper', 'ggml-small.bin')), JSON.stringify(done));
    run.check('download: the log says it was verified by SHA-256', run.logLines(/Model whisper-small verified: SHA-256/).length > 0);

    // Damage it: same size, a few bytes flipped. Small is the model in use.
    await run.bridge('settings.set', { transcription: { modelId: 'whisper-small' } });
    await run.app.close();
    const bytes = readFileSync(small);
    for (const offset of [4096, bytes.length >> 1, bytes.length - 4096]) bytes[offset] ^= 0xff;
    writeFileSync(small, bytes);
    await run.start(appArgs);
    await run.bridge('library.importMedia', { path: shortAudio, title: 'Damaged model check' });
    const [id] = run.projectIds();
    await run.until(() => transcriptStage(run, id)?.state === 'failed', 'the damaged-model failure', 5 * 60_000, 500);
    await run.page.click({ name: 'Damaged model check', exact: false });
    const card = await run.until(() => failureCard(run), 'the failure card', 30_000).catch(() => null);
    await run.shot('damaged-model-card');
    result.card = card;
    run.check('damaged: reported specifically, the recording safe, re-download offered', !!card && card.includes('model file is damaged') && card.includes('SHA-256') && card.includes('Download Small again'), card ?? '(none)');
    run.check('damaged: set aside, never used', existsSync(small + '.damaged') && !existsSync(small) && run.workerPids().length === 0);
    run.check('damaged: the log names the checksums', run.logLines(/Model whisper-small is damaged: SHA-256 \w+ instead of \w+/).length > 0);
    await run.page.click({ name: 'Download Small again' });
    await run.until(() => transcriptStage(run, id)?.state === 'done', 'transcription after the re-download', 10 * 60_000, 1000);
    await run.shot('after-redownload');
    run.check('damaged: after the download the transcript is made by itself', existsSync(join(run.folder(id), 'transcript.json')) && sha256(small) === sha256(join(models, 'whisper', 'ggml-small.bin')) && !existsSync(small + '.damaged'));
    result.history = run.history(id).map((h) => `${h.stage}/${h.event}: ${h.summary}`);
    await run.app.close();
  } catch (error) {
    run.check('part B ran to the end', false, error.stack);
    await run.shot('B-failure').catch(() => null);
    await run.app?.kill();
  } finally {
    mirror.server.close();
  }
  summaries.push({ ...run.summary(), result, mirrorRequests: mirror.requests });
}

if (!part || part === 'B') await partB();
if (!part || part === 'A') await partA();
mkdirSync(out, { recursive: true });
writeFileSync(join(out, `summary-${part ?? 'all'}.json`), JSON.stringify(summaries, null, 2));
console.log(JSON.stringify(summaries.map((s) => ({ name: s.name, passed: s.passed, failed: s.failed, failures: s.results.filter((r) => !r.passed), numbers: s.numbers })), null, 2));
