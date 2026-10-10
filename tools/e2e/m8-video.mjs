// The 2.0 video end-to-end check on the published app, through the bridge (the Recording screen's VIDEO section is
// built separately; this drives the host contract directly, as BRIDGE.md "Video (2.0)" defines it). Nothing is played
// through the speakers: the audio is the simulated engine's.
//
//   node tools/e2e/m8-video.mjs [--seconds 20] [--data <dir>] [--out <dir>] [--port 9371] [--keep]
//
// Records Display 1 for --seconds beside simulated audio, stops, and checks: videoTracks[] in project.json and in
// recording.state.json while recording, the Encoder card's events, the video stage (SHA-256), the Library and Review
// payloads, playback and seeking of the track through https://library.memento/ (range requests) in WebView2, a composed
// "One file" MP4 export with the mix, and "Remove video, keep audio". The capture shows this PC's display, so the
// project folder and the export are deleted at the end (pass --keep to look at them; delete them yourself after).
// Results and measurements go to <out>/summary.json; no picture of the display is saved.

import { existsSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { join, resolve } from 'node:path';
import { Run, option, repoRoot, sleep } from './h1-lib.mjs';

const args = process.argv.slice(2);
const seconds = Number(option(args, '--seconds', '20'));
const run = new Run({
  name: 'm8-video',
  dataRoot: option(args, '--data', join(repoRoot, 'artifacts', 'e2e-data-m8')),
  out: option(args, '--out', join(repoRoot, 'artifacts', 'e2e', 'm8')),
  port: Number(option(args, '--port', '9371')),
});
const keep = args.includes('--keep');
const exportFolder = join(run.out, 'export');
const measurements = {};

/** Processor time (ms) of this run's Memento, from Windows. */
function cpuMs(pid) {
  const text = execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', `[int64](Get-Process -Id ${pid}).TotalProcessorTime.TotalMilliseconds`], { encoding: 'utf8', windowsHide: true });
  return Number(text.trim());
}

async function seekCheck(url, durationMs) {
  // A muted, never-played <video> on the page: only metadata and seeks, so nothing is heard.
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
    const result = { duration: video.duration, width: video.videoWidth, height: video.videoHeight, seeks: [] };
    for (const fraction of [0.9, 0.1, 0.5, 0.75, 0.25, 0.95, 0.05]) {
      const target = (${durationMs} / 1000) * fraction;
      const started = performance.now();
      const seeked = once('seeked');
      video.currentTime = target;
      await seeked;
      result.seeks.push({ target, landed: video.currentTime, ms: Math.round(performance.now() - started), readyState: video.readyState });
    }
    result.seekable = video.seekable.length ? video.seekable.end(0) : 0;
    video.removeAttribute('src'); video.load(); video.remove();
    return result;
  })()`);
}

let recordingId = null;
try {
  await run.start(['--simulate-audio', '--update-feed=off']);
  run.log('started', run.dataRoot);

  // Sources: video is available, the displays are listed, a thumbnail is a JPEG data URL (never saved).
  const sources = await run.bridge('sources.list');
  run.check('sources.list says video is available', sources.videoAvailable === true);
  const video = await run.bridge('video.listSources');
  const display = video.displays.find((d) => d.primary) ?? video.displays[0];
  run.check('video.listSources lists the displays, primary first', video.displays.length > 0 && video.displays[0].primary, `${video.displays.length} displays, ${video.windows.length} windows, ${video.cameras.length} cameras`);
  run.check('Memento\'s own window is listed but excluded', video.windows.some((w) => w.excluded && w.detail === 'This app is excluded from capture'));
  const thumbStarted = Date.now();
  const thumb = await run.bridge('video.thumbnail', { sourceId: display.id, width: 320 });
  measurements.thumbnailMs = Date.now() - thumbStarted;
  run.check('video.thumbnail answers a JPEG data URL of the asked width', thumb.dataUrl.startsWith('data:image/jpeg;base64,') && thumb.width === 320, `${thumb.width} × ${thumb.height}, ${thumb.dataUrl.length} characters, ${measurements.thumbnailMs} ms`);

  // Record: the simulated microphone and Display 1.
  const mic = sources.audio.find((s) => s.kind === 'microphone') ?? sources.audio[0];
  const started = await run.bridge('recording.start', { title: 'M8 video check', type: 'meeting', sourceIds: [mic.id, display.id] });
  recordingId = started.recordingId;
  const pid = run.app.process.pid;
  const cpuBefore = cpuMs(pid);
  const wallBefore = Date.now();
  run.log('recording', `${recordingId} · ${display.name} ${display.width} × ${display.height}`);
  await sleep(Math.min(8, seconds / 2) * 1000);
  const state = JSON.parse(readFileSync(join(run.folder(recordingId), 'recording.state.json'), 'utf8'));
  run.check('recording.state.json names the video track while recording', state.videoTracks?.[0]?.parts?.[0]?.file === 'tracks/screen.mp4', JSON.stringify(state.videoTracks?.[0]?.parts ?? null));
  await sleep(Math.max(0, seconds * 1000 - (Date.now() - wallBefore)));
  const current = await run.bridge('recording.current');
  const cpuAfter = cpuMs(pid);
  measurements.recordingCpuPercentOfPc = Number(((100 * (cpuAfter - cpuBefore)) / (Date.now() - wallBefore) / (await run.page.eval('navigator.hardwareConcurrency'))).toFixed(2));
  const statuses = (await run.events('recording.videoStatus')).filter((e) => e.payload.sessionId === started.sessionId);
  const last = statuses.at(-1)?.payload.videoTracks[0];
  run.check('recording.videoStatus arrives about once a second', statuses.length >= seconds - 3, `${statuses.length} events in ${seconds} s`);
  run.check('the Encoder card has its figures', last && last.encoder.length > 0 && last.framesWritten > 0 && last.bytesWritten > 0, last ? `${last.encoder} (hardware ${last.hardware}), ${last.width} × ${last.height} @ ${last.frameRate}, ${last.framesWritten} frames, ${last.framesDropped} dropped, ${(last.actualBitsPerSecond / 1e6).toFixed(2)} Mb/s` : 'none');
  run.check('recording.current carries the video tracks', current.session?.videoTracks?.[0]?.trackId === 'screen');
  measurements.encoder = last && { name: last.encoder, hardware: last.hardware, width: last.width, height: last.height, frameRate: last.frameRate, actualMbps: Number((last.actualBitsPerSecond / 1e6).toFixed(2)) };

  await run.bridge('recording.stop', { sessionId: started.sessionId });
  await run.until(async () => (await run.events('recording.state')).some((e) => e.payload.recordingId === recordingId && e.payload.state === 'ready'), 'the recording to be ready', 120_000);
  const project = await run.until(async () => {
    const p = await run.bridge('project.get', { recordingId });
    return p.summary.stages.find((s) => s.stage === 'video')?.state === 'done' ? p : null;
  }, 'the video stage', 180_000, 500);
  run.check('the video stage is done', true, project.summary.stages.map((s) => `${s.stage} ${s.state}`).join(', '));

  // project.json
  const manifest = run.manifest(recordingId);
  const track = manifest.videoTracks?.[0];
  const audio = manifest.tracks?.[0];
  const file = join(run.folder(recordingId), track.file);
  measurements.track = { startOffsetMs: track.startOffsetMs, durationMs: track.durationMs, framesWritten: track.framesWritten, framesDropped: track.framesDropped, bytes: statSync(file).size, encoder: track.encoder, hardware: track.hardwareEncoder, width: track.width, height: track.height };
  measurements.recordingDurationMs = manifest.durationMs;
  measurements.audioDurationMs = audio.durationMs;
  run.check('project.json is schema 4 with one typed video track', manifest.schemaVersion === 4 && manifest.videoTracks.length === 1 && track.kind === 'video' && track.role === 'screen' && manifest.hasVideo === true);
  run.check('the video track lasts as long as the recording, from its start offset', Math.abs(track.startOffsetMs + track.durationMs - manifest.durationMs) < 1500 && track.startOffsetMs < 3000, `starts at ${track.startOffsetMs} ms, ${track.durationMs} ms long; recording ${manifest.durationMs} ms; audio ${audio.durationMs} ms`);
  run.check('frames written and dropped are counted', track.framesWritten > (seconds - 3) * track.frameRate && track.framesDropped <= track.framesWritten / 10, `${track.framesWritten} written, ${track.framesDropped} dropped`);
  run.check('the video file is hashed in integrity', typeof track.sha256 === 'string' && manifest.integrity.files[track.file] === track.sha256);
  run.check('History says the video was checked', run.history(recordingId).some((h) => h.stage === 'video' && h.summary === 'Video checked'));

  // Library and Review payloads.
  const row = (await run.bridge('library.list', {})).recordings.find((r) => r.id === recordingId);
  run.check('the Library row has the video summary', row?.hasVideo === true && row.video?.state === 'present' && row.video.layout === 'screen' && row.video.sizeBytes > 0, JSON.stringify(row?.video ?? null));
  const info = project.videoTracks[0];
  run.check('Review gets the track URL and its start offset', info.url === `https://library.memento/${recordingId}/tracks/screen.mp4` && info.startOffsetMs === track.startOffsetMs, info.url);

  // Playback and seeking through the library.memento virtual host: the responses are watched over the DevTools
  // protocol to see that the media element's range requests are answered with 206 Partial Content.
  const responses = [];
  run.page.on((message) => {
    if (message.method === 'Network.responseReceived' && message.params.response.url.startsWith('https://library.memento/')) {
      responses.push({ status: message.params.response.status, range: message.params.response.headers['Content-Range'] ?? message.params.response.headers['content-range'] ?? null });
    }
  });
  await run.page.send('Network.enable');
  const seek = await seekCheck(info.url, track.durationMs);
  await run.page.send('Network.disable');
  measurements.playback = { ...seek, responses };
  const worst = Math.max(...seek.seeks.map((s) => Math.abs(s.landed - s.target)));
  run.check('WebView2 opens the track with its length and size', Math.abs(seek.duration * 1000 - track.durationMs) < 500 && seek.width === track.width, `${seek.duration.toFixed(3)} s, ${seek.width} × ${seek.height}`);
  run.check('every seek completes where it was asked, with the picture ready', worst <= 1 / track.frameRate + 0.001 && seek.seeks.every((s) => s.readyState >= 2), `seek times ${seek.seeks.map((s) => s.ms).join(', ')} ms`);
  run.check('the virtual host answers range requests (206)', responses.some((r) => r.status === 206 && r.range), responses.map((r) => `${r.status} ${r.range ?? ''}`).join('; '));

  // Export: One file, MP4, with the mix.
  const selection = {
    audioMixed: { on: false, format: 'flac', bitrateKbps: null },
    tracks: { on: false, format: 'flac', bitrateKbps: null },
    transcript: { on: false, formats: [] },
    documents: { on: false, documentIds: [], format: 'docx' },
    details: { on: false },
    attachments: { on: false },
    video: { on: true, format: 'mp4', layout: 'oneFile' },
  };
  const estimate = await run.bridge('export.estimate', { recordingId, selection });
  const item = estimate.items.find((i) => i.component === 'video');
  run.check('export.estimate lists the one video file', !!item && item.name.endsWith('.mp4') && item.bytes > 0, item ? `${item.name}, about ${(item.bytes / 1e6).toFixed(1)} MB` : JSON.stringify(estimate.unavailable));
  const exportStarted = Date.now();
  const job = await run.bridge('export.run', { recordingId, selection, destination: { folder: exportFolder, createSubfolder: false }, remember: false });
  const done = await run.until(async () => (await run.events('export.progress')).find((e) => e.payload.jobId === job.jobId && e.payload.state !== 'running')?.payload, 'the export', 600_000, 500);
  measurements.exportMs = Date.now() - exportStarted;
  const progressPercents = (await run.events('export.progress')).filter((e) => e.payload.jobId === job.jobId).map((e) => e.payload.percent);
  const exported = join(exportFolder, item.name);
  run.check('the export composes the MP4 with the mix', done.state === 'done' && existsSync(exported) && statSync(exported).size > 0, `${done.state}: ${done.message ?? ''} ${existsSync(exported) ? statSync(exported).size : 0} bytes in ${measurements.exportMs} ms; progress ${[...new Set(progressPercents)].join(',')}`);
  const exportManifest = JSON.parse(readFileSync(join(exportFolder, 'manifest.json'), 'utf8'));
  run.check('manifest.json lists the video file with its hash', exportManifest.files.some((f) => f.name === item.name && f.sha256?.length === 64));
  measurements.exportBytes = existsSync(exported) ? statSync(exported).size : 0;

  // Remove video, keep audio.
  const removed = await run.bridge('video.remove', { recordingId });
  const after = run.manifest(recordingId);
  run.check('video.remove frees the video and keeps the audio', removed.freedBytes > 0 && !existsSync(file) && after.videoRemoved?.reason === 'user' && after.tracks.length === 1, `${removed.freedBytes} bytes freed`);
  const reviewAfter = await run.bridge('project.get', { recordingId });
  run.check('Review says the video was removed', reviewAfter.video?.state === 'removed' && reviewAfter.videoTracks[0].url === null);
} catch (error) {
  run.check('the run finished', false, error.stack ?? String(error));
} finally {
  try {
    if (run.app) await run.app.close();
  } catch (error) {
    run.log('close failed', String(error));
    await run.app?.kill();
  }

  const summary = { ...run.summary(), measurements };
  writeFileSync(join(run.out, 'summary.json'), JSON.stringify(summary, null, 2));
  if (!keep) {
    // The capture and the export show this PC's display: never leave them behind (stored tracks are read-only).
    for (const folder of [exportFolder, recordingId && run.folder(recordingId)].filter(Boolean)) {
      try {
        rmSync(folder, { recursive: true, force: true });
      } catch {
        // Fall through to PowerShell, which clears the read-only attribute itself.
      }
      if (existsSync(folder)) {
        execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', `Remove-Item -LiteralPath '${folder.replace(/'/g, "''")}' -Recurse -Force`], { windowsHide: true });
      }
      run.log(existsSync(folder) ? 'NOT DELETED' : 'deleted', folder);
    }
  }

  run.log('done', `${summary.passed} passed, ${summary.failed} failed · ${resolve(run.out, 'summary.json')}`);
  process.exitCode = summary.failed === 0 ? 0 : 1;
}
