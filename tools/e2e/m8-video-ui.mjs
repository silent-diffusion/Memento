// The 2.0 video screens end to end on the published app, driven like a person through the page (DESIGN.md §18):
// the Recording session's VIDEO section with its live preview, the display and window picker, the Encoder card while
// recording, then Review's video pane playing in step with the mix and following seeks. m8-video.mjs checks the host
// contract underneath; this one checks the screens on top of it. Nothing is heard: every media element on the page is
// muted before anything plays, and the audio is the simulated engine's.
//
//   node tools/e2e/m8-video-ui.mjs [--seconds 20] [--data <dir>] [--out <dir>] [--port 9373] [--keep]
//
// The capture shows this PC's display, so the data root (recording, thumbnails in memory only) is deleted at the end
// (--keep leaves it; delete it yourself after). No screenshot is taken: it would show the display. Results go to
// <out>/summary.json.

import { existsSync, rmSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { join, resolve } from 'node:path';
import { Run, option, repoRoot, sleep } from './h1-lib.mjs';

const args = process.argv.slice(2);
const seconds = Number(option(args, '--seconds', '20'));
const run = new Run({
  name: 'm8-video-ui',
  dataRoot: option(args, '--data', join(repoRoot, 'artifacts', 'e2e-data-m8-ui')),
  out: option(args, '--out', join(repoRoot, 'artifacts', 'e2e', 'm8-ui')),
  port: Number(option(args, '--port', '9373')),
});
const keep = args.includes('--keep');
const measurements = {};

const text = (selector) => run.page.eval(`document.querySelector(${JSON.stringify(selector)})?.textContent ?? ''`);
const exists = (selector) => run.page.eval(`!!document.querySelector(${JSON.stringify(selector)})`);

/** Mutes every <audio> and <video> now and later, so playing the mix in Review is never heard. */
async function muteEverything() {
  await run.page.eval(`(() => {
    const mute = (el) => { el.muted = true; el.volume = 0; };
    document.querySelectorAll('audio, video').forEach(mute);
    if (!window.__muted) {
      window.__muted = new MutationObserver(() => document.querySelectorAll('audio, video').forEach(mute));
      window.__muted.observe(document.body, { childList: true, subtree: true });
      // A media element that is not attached yet (new Audio()) is muted as soon as it plays.
      document.addEventListener('play', (e) => mute(e.target), true);
    }
    return true;
  })()`);
}

/** The mix and the screen video, as Review has them. */
const mediaState = () =>
  run.page.eval(`(() => {
    const audio = document.querySelector('audio');
    const video = document.querySelector('video.vid-main');
    return {
      audio: audio ? { t: audio.currentTime, paused: audio.paused, muted: audio.muted, src: audio.currentSrc } : null,
      video: video ? { t: video.currentTime, paused: video.paused, muted: video.muted, readyState: video.readyState, seeking: video.seeking, src: video.currentSrc, width: video.videoWidth } : null,
    };
  })()`);

let recordingId = null;
try {
  await run.start(['--simulate-audio', '--update-feed=off']);
  await muteEverything();
  run.log('started', run.dataRoot);

  // --- Recording session: the VIDEO section -------------------------------------------------------------------
  await run.page.click({ name: 'New recording' });
  await run.page.waitFor(`document.querySelectorAll('.rec-video-src').length === 3`, 'the VIDEO section', 30_000);
  run.check('the VIDEO section lists Screen, Window and Camera', (await text('.rec-video-list')).includes('Capture one window instead of a whole display'));
  // Count the page's video.thumbnail answers: a still display gives the same picture every time, so the number of
  // different pictures says how busy the display is, not how often the preview refreshes (this PC's primary display
  // is often idle while Memento's window sits on another one).
  await run.page.eval(`(() => {
    window.__thumbs = { asked: new Map(), answered: [] };
    const webview = window.chrome.webview;
    const post = webview.postMessage.bind(webview);
    webview.postMessage = (message) => {
      try {
        const m = typeof message === 'string' ? JSON.parse(message) : message;
        if (m && m.method === 'video.thumbnail') window.__thumbs.asked.set(m.id, performance.now());
      } catch { /* not a bridge call */ }
      return post(message);
    };
    webview.addEventListener('message', (event) => {
      const m = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
      const at = m && window.__thumbs.asked.get(m.id);
      if (at !== undefined && !m.error) window.__thumbs.answered.push({ at: performance.now(), ms: performance.now() - at });
    });
    return true;
  })()`);
  await run.page.click({ role: 'switch', name: 'Screen' });
  const firstPreview = await run.page.waitFor(`document.querySelector('.rec-preview-main')?.src`, 'the live preview', 20_000);
  run.check('the live preview shows the host\'s JPEG thumbnail of the display', firstPreview.startsWith('data:image/jpeg'), `${firstPreview.length} characters`);
  const seen = new Set([firstPreview]);
  const answeredBefore = await run.page.eval('window.__thumbs.answered.length');
  const watchStarted = Date.now();
  while (Date.now() - watchStarted < 3000) {
    seen.add(await run.page.eval(`document.querySelector('.rec-preview-main')?.src ?? ''`));
    await sleep(100);
  }
  const answered = (await run.page.eval('window.__thumbs.answered')).slice(answeredBefore);
  measurements.previewPicturesIn3s = seen.size;
  measurements.previewAnswersIn3s = answered.length;
  measurements.previewThumbnailMs = answered.map((a) => Math.round(a.ms));
  run.check('the preview refreshes about twice a second', answered.length >= 4, `${answered.length} stills in 3 s (${measurements.previewThumbnailMs.join(', ')} ms each), ${seen.size} different`);
  run.check('the Screen row names the display and its size', /^Display \d+ · \d+ × \d+ · \d+ fps$/.test(await text('[data-row="screen"] .src-sub')), await text('[data-row="screen"] .src-sub'));
  run.check('the sub-line counts the screen with the audio tracks', /^screen \+ \d+ audio tracks?$/.test(await text('.rec-subline')), await text('.rec-subline'));

  // --- The picker -------------------------------------------------------------------------------------------------
  await run.page.click({ name: 'Choose the display to capture', exact: false });
  await run.page.waitFor(`document.querySelectorAll('.sheet--wide .rec-tile').length > 0`, 'the picker', 15_000);
  const displays = await run.page.eval(`[...document.querySelectorAll('.rec-tile')].map((t) => ({ name: t.querySelector('.rec-tile-name')?.textContent, chosen: t.getAttribute('aria-pressed') === 'true' }))`);
  run.check('the picker shows the displays with the one in use chosen', displays.length > 0 && displays.filter((d) => d.chosen).length === 1, displays.map((d) => d.name + (d.chosen ? ' (chosen)' : '')).join(', '));
  // The tiles ask for their stills together and the host answers them one after another: wait for all of them.
  const tilesStarted = Date.now();
  const allStills = `[...document.querySelectorAll('.rec-tile')].every((t) => t.querySelector('.rec-shot-img')?.src?.startsWith('data:image/jpeg'))`;
  await run.page.waitFor(allStills, 'the tiles\' stills', 15_000).catch(() => false);
  measurements.tileStillsMs = Date.now() - tilesStarted;
  run.check('every display tile has a still from the host', await run.page.eval(allStills), `${displays.length} tiles in ${measurements.tileStillsMs} ms`);
  await run.page.click({ role: 'tab', name: 'This window' });
  await run.page.waitFor(`document.querySelector('.rec-pick-search-input') !== null`, 'the window tab', 10_000);
  const own = await run.page.eval(`(() => { const t = [...document.querySelectorAll('.rec-tile')].find((x) => x.textContent.includes('This app is excluded from capture')); return t ? { disabled: t.getAttribute('aria-disabled'), name: t.querySelector('.rec-tile-name')?.textContent } : null; })()`);
  run.check('Memento\'s own window is listed and explained but not selectable', own?.disabled === 'true', JSON.stringify(own));
  measurements.windowsListed = await run.page.eval(`document.querySelectorAll('.rec-tile').length`);
  await run.page.click({ name: 'Cancel' });
  await run.page.waitFor(`document.querySelector('.sheet--wide') === null`, 'the picker to close', 10_000);

  // --- Recording with the screen ----------------------------------------------------------------------------------
  await run.page.click({ name: 'Start recording' });
  await run.page.waitFor(`document.querySelector('.rec-encoder') !== null`, 'the Encoder card', 30_000);
  const session = await run.bridge('recording.current');
  recordingId = session.session?.recordingId ?? null;
  run.log('recording', recordingId ?? 'unknown');
  await sleep(seconds * 1000);
  const encoder = await text('.rec-encoder');
  measurements.encoderCard = encoder;
  run.check('the Encoder card shows the encoder, resolution, frame rate with drops, bitrate and size', /(Hardware · GPU|Software · CPU)/.test(encoder) && /\d+ × \d+/.test(encoder) && /\d+ fps · \d+ dropped/.test(encoder) && /b\/s/.test(encoder) && /(KB|MB|GB)/.test(encoder), encoder);
  run.check('the VIDEO section says video is recording', (await text('.rec-sources .pill.active')) === 'Recording video');
  run.check('the Tracks card has the video lane', (await text('.rec-lane-name--video')) === 'Screen');
  run.check('the preview is live', (await text('.rec-preview-tag')) === 'LIVE');
  run.check('the footer says video and audio are in sync and estimates at video rates', (await text('.app-footer')).includes('video and audio in sync') && /at this quality \(screen \d+ fps\)/.test(await text('.app-footer')), await text('.app-footer'));

  // --- Review: the video pane -------------------------------------------------------------------------------------
  await run.page.click({ name: 'Stop and open review' });
  await run.page.waitFor(`document.querySelector('.vid') !== null`, 'Review\'s video pane', 180_000);
  await muteEverything();
  const project = await run.bridge('project.get', { recordingId });
  const track = project.video?.tracks?.find((t) => t.id === 'screen');
  run.check('Review gets the screen track with its URL', !!track?.parts?.[0]?.url, track?.parts?.[0]?.url ?? JSON.stringify(project.video));
  await run.page.waitFor(`document.querySelector('video.vid-main')?.readyState >= 2`, 'the video to load', 60_000);
  let state = await mediaState();
  run.check('the video plays from the library host, muted (the sound is the mix)', state.video.src.startsWith('https://library.memento/') && state.video.muted && state.audio.muted, `${state.video.width} px wide`);
  run.check('the header says how much video there is', /screen · [\d.]+ (KB|MB|GB) video/.test(await text('.spoke-meta, .review-meta, header')), await text('header'));

  await run.page.click({ name: 'Play' });
  await sleep(4000);
  state = await mediaState();
  const offset = (track?.startOffsetMs ?? 0) / 1000;
  const playingDrift = Math.abs(state.video.t + offset - state.audio.t);
  measurements.playingDriftMs = Math.round(playingDrift * 1000);
  run.check('the video plays with the mix, within a few frames', !state.audio.paused && !state.video.paused && playingDrift < 0.25, `mix ${state.audio.t.toFixed(3)} s, video ${state.video.t.toFixed(3)} s + ${offset.toFixed(3)} s offset, drift ${measurements.playingDriftMs} ms`);
  await run.page.click({ name: 'Pause' });
  await sleep(500);
  state = await mediaState();
  run.check('pausing the mix pauses the video', state.audio.paused && state.video.paused);

  // Seek: click the waveform at two places, then the position slider's keys, paused each time.
  const seeks = [];
  for (const fraction of [0.75, 0.2]) {
    await run.page.clickAt('.wave', fraction);
    await run.page.waitFor(`(() => { const v = document.querySelector('video.vid-main'); return v && !v.seeking && v.readyState >= 2; })()`, 'the video to seek', 15_000);
    await sleep(300);
    const s = await mediaState();
    seeks.push({ fraction, mix: s.audio.t, video: s.video.t + offset, error: Math.abs(s.video.t + offset - s.audio.t) });
  }
  await run.page.click({ role: 'slider', name: 'Playback position' });
  await run.page.key('ArrowRight', ['shift']);
  await run.page.waitFor(`(() => { const v = document.querySelector('video.vid-main'); return v && !v.seeking; })()`, 'the slider seek', 15_000);
  await sleep(300);
  const s = await mediaState();
  seeks.push({ fraction: 'slider +30 s', mix: s.audio.t, video: s.video.t + offset, error: Math.abs(s.video.t + offset - s.audio.t) });
  measurements.seeks = seeks;
  run.check('the video follows every seek of the mix to within one frame', seeks.every((x) => x.error <= 1 / (track?.frameRate ?? 30) + 0.02), seeks.map((x) => `${x.fraction}: mix ${x.mix.toFixed(2)} s, video ${x.video.toFixed(2)} s`).join('; '));

  // Play from the seeked position: still in step.
  await run.page.click({ name: 'Play' });
  await sleep(2500);
  state = await mediaState();
  const afterSeekDrift = Math.abs(state.video.t + offset - state.audio.t);
  measurements.afterSeekDriftMs = Math.round(afterSeekDrift * 1000);
  run.check('after a seek the video keeps playing in step', afterSeekDrift < 0.25, `${measurements.afterSeekDriftMs} ms`);
  await run.page.click({ name: 'Pause' });

  // The overlay row: Audio only hides the pane; Show video brings it back; Fullscreen and Esc.
  await run.page.click({ name: 'Audio only' });
  await run.page.waitFor(`document.querySelector('.vid') === null && document.body.innerText.includes('Video hidden. Playback and the transcript are unchanged.')`, 'Audio only', 10_000);
  await run.page.click({ name: 'Show video' });
  await run.page.waitFor(`document.querySelector('.vid') !== null`, 'Show video', 10_000);
  await run.page.click({ name: 'Fullscreen' });
  const full = await run.page.waitFor(`!!document.querySelector('.review-panes--video-full')`, 'Fullscreen', 10_000);
  await run.page.key('Escape');
  await run.page.waitFor(`!document.querySelector('.review-panes--video-full')`, 'Esc to leave Fullscreen', 10_000);
  run.check('Audio only, Show video, Fullscreen and Esc work', full);
  run.check('the outline lists the video and offers Remove video, keep audio', (await text('.outline-video')).includes('Remove video, keep audio'), await text('.outline-video'));
} catch (error) {
  // What the video pane showed, to tell a loading problem from a sync one.
  const pane = await run.page
    ?.eval(`(() => { const v = document.querySelector('video'); return { pane: document.querySelector('.vid-wrap, .vid-line')?.innerText ?? null, video: v ? { src: v.currentSrc || v.getAttribute('src'), readyState: v.readyState, networkState: v.networkState, error: v.error && { code: v.error.code, message: v.error.message } } : null }; })()`)
    .catch(() => null);
  run.check('the run finished', false, `${error.stack ?? String(error)}\n${JSON.stringify(pane)}`);
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
    // The recording shows this PC's display: never leave it behind (stored tracks are read-only).
    const folder = run.dataRoot;
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
  run.log('done', `${summary.passed} passed, ${summary.failed} failed · ${resolve(run.out, 'summary.json')}`);
  process.exitCode = summary.failed === 0 ? 0 : 1;
}
