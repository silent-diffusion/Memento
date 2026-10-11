// A camera Memento reaches through DirectShow, end to end on the published app (ENGINE-NOTES.md §R): the OBS Virtual
// Camera when OBS has it running. Nothing is played through the speakers: the audio is the simulated engine's.
//
//   node tools/e2e/m8-camera.mjs [--seconds 20] [--data <dir>] [--out <dir>] [--port 9393] [--keep]
//                               [--stop-obs-after <s> --obs-pid <pid>]
//
// Checks video.listSources (every camera once, each with its backend, the OBS Virtual Camera as "dshow"), its
// thumbnails (first and while polled), the Camera row's "via DirectShow" note and its live thumbnail on the Recording
// screen, then records the camera with the simulated microphone for --seconds through the page's Start recording and
// checks the camera track in project.json, the video stage and playback through https://library.memento/ in WebView2.
// Without the OBS Virtual Camera the camera steps are reported as skipped; with it installed but OBS not sending, the
// run checks the "not started in OBS" refusal (the thumbnail then shows OBS's placeholder) and skips the recording.
//
// --stop-obs-after <s> --obs-pid <pid> closes that OBS (tools/e2e/obs-close.ps1, its own WM_CLOSE) <s> seconds into
// the recording, so its virtual camera stops: the camera track must end there with recording.videoProblem and the
// "stopped" sentence while the audio records on. Use it only with an OBS started for the test (a portable copy with
// its own settings), never with someone's own OBS.
//
// The data root is deleted at the end (--keep leaves it). Results and measurements go to <out>/summary.json.

import { existsSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { join, resolve } from 'node:path';
import { Run, option, repoRoot, sleep } from './h1-lib.mjs';

const args = process.argv.slice(2);
const seconds = Number(option(args, '--seconds', '20'));
const stopObsAfter = args.includes('--stop-obs-after') ? Number(option(args, '--stop-obs-after', '8')) : null;
const obsPid = Number(option(args, '--obs-pid', '0'));
const run = new Run({
  name: 'm8-camera',
  dataRoot: option(args, '--data', join(repoRoot, 'artifacts', 'e2e-data-m8-camera')),
  out: option(args, '--out', join(repoRoot, 'artifacts', 'e2e', 'm8-camera')),
  port: Number(option(args, '--port', '9393')),
});
const keep = args.includes('--keep');
const measurements = {};
const OBS = 'OBS Virtual Camera';

/** Ends the run early when the camera can't be recorded here, after what could be checked was. */
class SkipRest extends Error {}

const text = (selector) => run.page.eval(`document.querySelector(${JSON.stringify(selector)})?.textContent ?? ''`);

/** Processor time (ms) of this run's Memento, from Windows. */
function cpuMs(pid) {
  const out = execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', `[int64](Get-Process -Id ${pid}).TotalProcessorTime.TotalMilliseconds`], { encoding: 'utf8', windowsHide: true });
  return Number(out.trim());
}

/** A muted, never-played <video> on the page: the track's length and size, and one seek to its middle. */
async function playable(url) {
  return run.page.eval(`(async () => {
    const video = document.createElement('video');
    video.muted = true;
    video.preload = 'auto';
    video.style.position = 'fixed'; video.style.width = '64px'; video.style.left = '-200px';
    document.body.appendChild(video);
    const once = (name) => new Promise((done, fail) => {
      const timer = setTimeout(() => fail(new Error(name + ' timed out')), 15000);
      video.addEventListener(name, () => { clearTimeout(timer); done(); }, { once: true });
      video.addEventListener('error', () => { clearTimeout(timer); fail(new Error('media error ' + (video.error && video.error.code))); }, { once: true });
    });
    const loaded = once('loadedmetadata');
    video.src = ${JSON.stringify(url)};
    await loaded;
    const seeked = once('seeked');
    video.currentTime = video.duration / 2;
    await seeked;
    const result = { duration: video.duration, width: video.videoWidth, height: video.videoHeight, landed: video.currentTime, readyState: video.readyState };
    video.removeAttribute('src'); video.load(); video.remove();
    return result;
  })()`);
}

let recordingId = null;
try {
  await run.start(['--simulate-audio', '--update-feed=off']);
  run.log('started', run.dataRoot);

  // --- The list: every camera once, each with its backend ------------------------------------------------------------
  const sources = await run.bridge('video.listSources');
  const cameras = sources.cameras;
  measurements.cameras = cameras.map((c) => ({ name: c.name, backend: c.backend }));
  run.check('every camera has a backend, displays and windows none', cameras.every((c) => c.backend === 'mf' || c.backend === 'dshow') && [...sources.displays, ...sources.windows].every((s) => s.backend === null), cameras.map((c) => `${c.name} (${c.backend})`).join(', ') || 'no camera');
  run.check('no camera is listed twice', new Set(cameras.map((c) => c.id)).size === cameras.length && new Set(cameras.map((c) => c.name)).size === cameras.length);
  const obs = cameras.find((c) => c.name === OBS && c.backend === 'dshow');
  if (!obs) {
    run.check('skipped: the OBS Virtual Camera is not installed here', true);
  } else {
    run.check('the OBS Virtual Camera is listed through DirectShow', obs.id.startsWith('camera:dshow:'), obs.id);

    // --- Thumbnails: the first opens the graph, the next ones reuse it ------------------------------------------------
    let started = Date.now();
    let thumb;
    try {
      thumb = await run.bridge('video.thumbnail', { sourceId: obs.id, width: 320 });
    } catch (error) {
      thumb = { error: String(error) };
    }
    measurements.firstThumbnailMs = Date.now() - started;
    if (thumb.error && /not started in OBS|no picture/.test(thumb.error)) {
      run.check('skipped: the OBS Virtual Camera is not running (Start Virtual Camera in OBS)', true, thumb.error);
    } else {
      run.check('video.thumbnail answers a JPEG of the camera', thumb.dataUrl?.startsWith('data:image/jpeg;base64,') && thumb.width === 320, `${thumb.width} × ${thumb.height} in ${measurements.firstThumbnailMs} ms`);
      const polled = [];
      for (let i = 0; i < 5; i++) {
        await sleep(1000);
        started = Date.now();
        await run.bridge('video.thumbnail', { sourceId: obs.id, width: 144 });
        polled.push(Date.now() - started);
      }
      measurements.polledThumbnailMs = polled;
      run.check('polled once a second, the thumbnails stay cheap', Math.max(...polled) < 250, `${polled.join(', ')} ms`);

      // --- The Recording screen: the Camera row says "via DirectShow" and shows the camera ----------------------------
      await run.page.click({ name: 'New recording' });
      await run.page.waitFor(`document.querySelectorAll('.rec-video-src').length === 3`, 'the VIDEO section', 30_000);
      await run.page.click({ role: 'switch', name: 'Camera' });
      await run.page.waitFor(`document.querySelector('[data-row="camera"] .rec-thumb-img')?.src?.startsWith('data:image/jpeg')`, 'the camera row thumbnail', 20_000);
      const row = await text('[data-row="camera"] .src-sub');
      run.check('the Camera row names the camera with "via DirectShow"', row === `${OBS} · via DirectShow`, row);
      run.check('the Camera row shows the camera\'s live thumbnail', true);

      // --- Record: the camera and the simulated microphone, through the page -------------------------------------------
      await run.page.click({ name: 'Start recording' });
      const outcome = await run.until(
        async () => {
          const session = (await run.bridge('recording.current')).session;
          if (session?.videoTracks?.some((t) => t.trackId === 'camera' && t.state === 'recording')) return { recording: true };
          const refused = (await run.events('recording.videoProblem')).find((e) => e.payload.trackId === 'camera')?.payload;
          return refused ? { recording: false, refused } : null;
        },
        'the camera track to record or be refused',
        30_000,
        250,
      );
      const current = await run.bridge('recording.current');
      recordingId = current.session.recordingId;
      const sessionId = current.session.sessionId;
      if (!outcome.recording) {
        // The OBS Virtual Camera is installed but OBS is not sending: its filter shows a placeholder, and Memento says so.
        run.check('while OBS is not sending, the camera refuses with the "not started in OBS" sentence and the audio records', /is not started in OBS/.test(outcome.refused.message) && current.session.state === 'recording', outcome.refused.message);
        run.check('skipped: recording the camera needs OBS\'s virtual camera running', true);
        await run.bridge('recording.stop', { sessionId });
        await run.until(async () => (await run.events('recording.state')).some((e) => e.payload.recordingId === recordingId && e.payload.state === 'ready'), 'the recording to be ready', 120_000);
        throw new SkipRest();
      }
      const pid = run.app.process.pid;
      const cpuBefore = cpuMs(pid);
      const wallBefore = Date.now();
      run.log('recording', `${recordingId} · ${OBS}`);

      if (stopObsAfter !== null && obsPid > 0) {
        await sleep(stopObsAfter * 1000);
        const closedAt = Date.now();
        const closed = execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', join(repoRoot, 'tools', 'e2e', 'obs-close.ps1'), '-ProcessId', String(obsPid)], { encoding: 'utf8', windowsHide: true }).trim();
        run.log('obs', closed);
        const problem = await run.until(
          async () => (await run.events('recording.videoProblem')).find((e) => e.payload.sessionId === sessionId && e.payload.trackId === 'camera')?.payload,
          'the camera track to end',
          15_000,
          100,
        );
        measurements.obsStop = { reportedAfterMs: Date.now() - closedAt, code: problem.code, state: problem.state, message: problem.message, atMs: problem.atMs };
        run.check('OBS stopping its virtual camera ends the camera track with the "stopped" sentence', problem.state === 'ended' && problem.message.startsWith(`${OBS} stopped at `) && problem.detail === 'OBS', `${problem.code} ${problem.state} after ${measurements.obsStop.reportedAfterMs} ms: ${problem.message}`);
        await sleep(1500);
        const note = await text('[data-row="camera"] .src-sub');
        run.check('the audio records on after the camera ended', (await run.bridge('recording.current')).session?.state === 'recording', note);
      }

      await sleep(Math.max(0, seconds * 1000 - (Date.now() - wallBefore)));
      const statuses = (await run.events('recording.videoStatus')).filter((e) => e.payload.sessionId === sessionId);
      const last = statuses.map((e) => e.payload.videoTracks.find((t) => t.trackId === 'camera')).filter(Boolean).at(-1);
      measurements.recordingCpuPercentOfPc = Number(((100 * (cpuMs(pid) - cpuBefore)) / (Date.now() - wallBefore) / (await run.page.eval('navigator.hardwareConcurrency'))).toFixed(2));
      measurements.encoder = last && { name: last.encoder, hardware: last.hardware, width: last.width, height: last.height, frameRate: last.frameRate, framesWritten: last.framesWritten, framesDropped: last.framesDropped, actualMbps: Number((last.actualBitsPerSecond / 1e6).toFixed(2)) };
      run.check('recording.videoStatus has the camera track\'s figures', !!last && last.sourceId === obs.id && last.framesWritten > 0, last ? `${last.encoder}, ${last.width} × ${last.height} @ ${last.frameRate}, ${last.framesWritten} written, ${last.framesDropped} dropped` : 'none');

      await run.bridge('recording.stop', { sessionId });
      await run.until(async () => (await run.events('recording.state')).some((e) => e.payload.recordingId === recordingId && e.payload.state === 'ready'), 'the recording to be ready', 120_000);
      await run.until(() => run.manifest(recordingId).stages?.find((s) => s.stage === 'video')?.state === 'done', 'the video stage', 180_000, 500);

      // --- project.json and playback -------------------------------------------------------------------------------
      const manifest = run.manifest(recordingId);
      const track = manifest.videoTracks.find((t) => t.role === 'camera');
      const file = join(run.folder(recordingId), track.file);
      measurements.track = { startOffsetMs: track.startOffsetMs, durationMs: track.durationMs, framesWritten: track.framesWritten, framesDropped: track.framesDropped, bytes: statSync(file).size, encoder: track.encoder, width: track.width, height: track.height, endReason: track.endReason ?? null };
      measurements.recordingDurationMs = manifest.durationMs;
      const expectedMs = stopObsAfter !== null && obsPid > 0 ? stopObsAfter * 1000 : seconds * 1000;
      run.check('project.json has the camera track from the OBS Virtual Camera', track.sourceKind === 'camera' && track.sourceId === obs.id && manifest.hasVideo === true, `${track.name}, ${track.width} × ${track.height} @ ${track.frameRate}`);
      run.check('the camera track lasts as long as the camera sent pictures', Math.abs(track.durationMs - expectedMs) < 3500, `starts at ${track.startOffsetMs} ms, ${track.durationMs} ms long; recording ${manifest.durationMs} ms`);
      run.check('its end reason says why it ended', stopObsAfter !== null && obsPid > 0 ? track.endReason === 'sourceLost' : !track.endReason, String(track.endReason ?? 'none'));
      run.check('frames written and dropped are counted', track.framesWritten > 0 && track.framesDropped <= Math.max(45, track.framesWritten / 10), `${track.framesWritten} written, ${track.framesDropped} dropped`);
      const project = await run.bridge('project.get', { recordingId });
      const url = project.video.tracks.find((t) => t.id === 'camera')?.parts?.[0]?.url;
      const media = await playable(url);
      measurements.playback = media;
      run.check('WebView2 plays the camera track with its length and size and seeks into it', Math.abs(media.duration * 1000 - track.durationMs) < 500 && media.width === track.width && media.readyState >= 2, `${media.duration.toFixed(3)} s, ${media.width} × ${media.height}, seek to ${media.landed.toFixed(2)} s`);
    }
  }
} catch (error) {
  if (!(error instanceof SkipRest)) run.check('the run finished', false, error.stack ?? String(error));
} finally {
  try {
    if (run.app) await run.app.close();
  } catch (error) {
    run.log('close failed', String(error));
    await run.app?.kill();
  }

  const summary = { ...run.summary(), measurements };
  writeFileSync(join(run.out, 'summary.json'), JSON.stringify(summary, null, 2));
  if (!keep && existsSync(run.dataRoot)) {
    try {
      rmSync(run.dataRoot, { recursive: true, force: true });
    } catch {
      // Stored tracks are read-only: PowerShell clears the attribute itself.
    }
    if (existsSync(run.dataRoot)) {
      execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', `Remove-Item -LiteralPath '${run.dataRoot.replace(/'/g, "''")}' -Recurse -Force`], { windowsHide: true });
    }
    run.log(existsSync(run.dataRoot) ? 'NOT DELETED' : 'deleted', run.dataRoot);
  }

  run.log('done', `${summary.passed} passed, ${summary.failed} failed · ${resolve(run.out, 'summary.json')}`);
  process.exitCode = summary.failed === 0 ? 0 : 1;
}
