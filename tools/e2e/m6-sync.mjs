// Transcript times against the audio, and History's versions, end to end through the published app over the
// DevTools protocol (ENGINE-NOTES.md §K, BRIDGE.md "History links"):
//   1. "Import audio or video" imports a synthetic recording whose every line is at a known time
//      (tools/e2e/sync-fixture.ps1 writes it and its truth file) and waits for the transcript;
//   2. every line in transcript.json starts within 100 ms of its voice from 2:00 on (0.5 s before; one line may
//      lose its first word);
//   3. the player in Review (WebView2, https://library.memento/) is heard at the time currentTime says: each line's
//      voice is detected in the played audio within 50 ms of its known start, after seeking to it;
//   4. a line is corrected in place, then History opens the transcript as it was before (a kept version) under its
//      banner, read-only; Restore this version brings it back in transcript.json and Undo takes the restore back.
//
//   node tools/e2e/m6-sync.mjs --models <dir> [--audio <file>] [--truth <json>] [--data <dir>] [--out <dir>] [--port 9353]
//
// --audio defaults to artifacts/e2e-fixtures/sync-44k.wav (made with `powershell -File tools/e2e/sync-fixture.ps1
// -Path artifacts/e2e-fixtures/sync-44k.wav` when missing); any file Memento imports works with its truth beside it
// (--truth, default <audio>.json; an MP3 made from the WAV keeps the WAV's truth). --models is a models folder
// (whisper and sherpa-onnx are copied; the run downloads nothing). The app runs with LOCALAPPDATA pointed at --data
// (default artifacts/e2e-data-m6), so the real library is never touched.

import { execFileSync, spawn } from 'node:child_process';
import { cpSync, existsSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { Run, option, repoRoot, sleep } from './h1-lib.mjs';

const args = process.argv.slice(2);
const dataRoot = resolve(option(args, '--data', join(repoRoot, 'artifacts', 'e2e-data-m6')));
const out = resolve(option(args, '--out', join(repoRoot, 'artifacts', 'e2e', 'm6-sync')));
const audio = resolve(option(args, '--audio', join(repoRoot, 'artifacts', 'e2e-fixtures', 'sync-44k.wav')));
const truthFile = resolve(option(args, '--truth', `${audio}.json`));
const models = option(args, '--models', null);
const run = new Run({ name: 'M6 transcript sync and History versions', dataRoot, out, port: Number(option(args, '--port', '9353')) });

function answerDialog(title, path) {
  return new Promise((done, fail) => {
    const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', join(repoRoot, 'tools', 'e2e', 'answer-dialog.ps1'), '-Title', title, '-Path', path], { stdio: ['ignore', 'ignore', 'pipe'], windowsHide: true });
    let errors = '';
    child.stderr.on('data', (d) => (errors += d));
    child.once('exit', (code) => (code === 0 ? done() : fail(new Error(`answer-dialog "${title}" exited ${code}: ${errors.trim()}`))));
  });
}

const readJson = (file) => JSON.parse(readFileSync(file, 'utf8').replace(/^﻿/, ''));
const transcriptOf = (id) => readJson(join(run.folder(id), 'transcript.json'));
const page = () => run.page;
const words = (s) => new Set(s.toLowerCase().split(/[^a-z0-9]+/).filter(Boolean));

try {
  if (!existsSync(audio) && audio.endsWith('sync-44k.wav')) {
    execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', join(repoRoot, 'tools', 'e2e', 'sync-fixture.ps1'), '-Path', audio], { stdio: 'inherit' });
  }
  const truth = readJson(truthFile).lines;
  rmSync(dataRoot, { recursive: true, force: true });
  if (models) {
    for (const engine of ['whisper', 'sherpa-onnx']) {
      if (existsSync(join(models, engine))) cpSync(join(models, engine), join(dataRoot, 'Memento', 'models', engine), { recursive: true });
    }
  }
  await run.start(['--simulate-audio', '--update-feed=off']);
  await run.hasText('Your library is empty');

  // 1. Import and wait for the transcript (speakers and topics too, so History has their lines).
  const answered = answerDialog('Import audio or video', audio);
  await page().click({ name: 'Import audio or video' });
  await answered;
  await run.until(() => run.projectIds().length === 1, 'the imported recording', 60_000);
  const [id] = run.projectIds();
  await run.until(() => {
    const stages = run.manifest(id).stages;
    return stages.length > 2 && stages.every((s) => s.state === 'done' || s.state === 'failed');
  }, 'the import to be transcribed', 30 * 60_000, 1000);
  run.check('the import was transcribed', run.manifest(id).stages.every((s) => s.state === 'done'), run.manifest(id).stages.map((s) => `${s.stage}:${s.state}`).join(', '));

  // 2. Line times against the truth.
  const segments = transcriptOf(id).segments;
  const early = [];
  const late = [];
  const errors = [];
  for (const line of truth) {
    const first = segments.filter((s) => s.start < line.end).sort((a, b) => Math.abs(a.start - line.start) - Math.abs(b.start - line.start))[0];
    const error = first === undefined ? NaN : first.start - line.start;
    const said = words(line.text);
    const shares = first !== undefined && [...words(first.text)].filter((w) => said.has(w)).length >= 2;
    errors.push(error);
    if (first === undefined || !shares || Math.abs(error) > (line.start >= 120 ? 0.1 : 0.5)) {
      (line.start >= 120 ? late : early).push(`"${line.text}" at ${line.start.toFixed(2)} s: ${first === undefined ? 'no line' : `"${first.text}" at ${first.start.toFixed(2)} s`}`);
    }
  }
  const finite = errors.filter(Number.isFinite).map(Math.abs).sort((a, b) => a - b);
  run.log('line starts', `${truth.length} lines, ${segments.length} segments; |error| median ${finite[finite.length >> 1]?.toFixed(3)} s, max ${finite.at(-1)?.toFixed(3)} s`);
  run.check('every line from 2:00 on starts within 100 ms of its voice (minute three included)', late.length === 0 && truth.some((l) => l.start > 180), late.join('; '));
  run.check('every earlier line starts within 0.5 s (one may lose its first word)', early.length <= 1, early.join('; '));

  // 3. Review: the player is heard at the time it says.
  await page().click({ selector: '.lib-item .row-title' });
  await page().waitFor(`!!document.querySelector('.review-panes') && document.querySelectorAll('.segm').length > 2 && !!document.querySelector('audio')?.getAttribute('src')`, 'Review with the transcript', 60_000);
  await run.shot('review');
  const probes = truth.filter((l, i) => i % 3 === 0 || l.start > 180).map((l) => l.start);
  const heard = await page().eval(`(async () => {
    const ctx = new AudioContext();
    const a = new Audio();
    a.crossOrigin = 'anonymous';
    a.src = document.querySelector('audio').src;
    const node = ctx.createScriptProcessor(512, 1, 1);
    const mute = ctx.createGain(); mute.gain.value = 0;
    ctx.createMediaElementSource(a).connect(node); node.connect(mute); mute.connect(ctx.destination);
    const onsets = [];
    let quiet = 1e9;
    node.onaudioprocess = (e) => {
      const ch = e.inputBuffer.getChannelData(0);
      for (let i = 0; i < ch.length; i += 128) {
        let sum = 0; for (let j = i; j < i + 128; j++) sum += ch[j] * ch[j];
        if (Math.sqrt(sum / 128) > 0.01) { if (quiet > 0.5 * ctx.sampleRate && !a.paused) onsets.push(a.currentTime); quiet = 0; } else quiet += 128;
      }
    };
    await new Promise((r) => a.addEventListener('loadedmetadata', r, { once: true }));
    await ctx.resume();
    const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
    const result = [];
    for (const t of ${JSON.stringify(probes)}) {
      onsets.length = 0;
      a.currentTime = t - 1;
      await new Promise((r) => a.addEventListener('seeked', r, { once: true }));
      await a.play();
      await sleep(1600);
      a.pause();
      result.push({ line: t, heardAt: onsets[0] ?? null });
    }
    await ctx.close();
    return result;
  })()`);
  const off = heard.filter((h) => h.heardAt === null || Math.abs(h.heardAt - h.line) > 0.05);
  run.log('player', heard.map((h) => `${h.line.toFixed(2)}→${h.heardAt === null ? '-' : (h.heardAt - h.line).toFixed(3)}`).join(' '));
  run.check('each line is heard within 50 ms of the time the player shows', off.length === 0, off.map((h) => `${h.line} heard at ${h.heardAt}`).join(', '));

  // 4. Correct a line, then open the transcript from before it in History and restore it.
  const second = transcriptOf(id).segments[1];
  await page().click({ selector: '[data-index="1"] .segm-text' });
  await page().waitFor(`document.activeElement?.classList.contains('segm-editor')`, 'the line editor');
  await page().eval(`(() => { const t = document.activeElement; t.setSelectionRange(t.value.length, t.value.length); })()`);
  await page().type(' Checked.');
  await page().key('Enter');
  await run.until(() => transcriptOf(id).segments[1].text.endsWith('Checked.'), 'the corrected line in transcript.json');
  await page().eval(`document.activeElement?.blur()`);
  await page().click({ selector: '#review-tab-history' });
  await page().waitFor(`document.querySelectorAll('.history-open').length >= 2`, 'History lines that open versions', 20_000);
  await run.shot('history');
  const lines = await page().eval(`[...document.querySelectorAll('.history-item')].map((li) => ({ summary: li.querySelector('.history-title')?.textContent ?? '', opens: !!li.querySelector('.history-open'), title: li.querySelector('span.history-title')?.getAttribute('title') ?? null }))`);
  run.log('history', lines.map((l) => `${l.opens ? '[open] ' : ''}${l.summary}`).join(' | '));
  run.check('lines that made no version say so on hover', lines.filter((l) => !l.opens).every((l) => l.title !== null && l.title.length > 0), JSON.stringify(lines.filter((l) => !l.opens).map((l) => l.title)));
  const before = lines.findIndex((l) => l.opens && !l.summary.startsWith('Transcript edited'));
  run.check('the line before the correction opens a version', before >= 0, lines[before]?.summary);
  await page().eval(`document.querySelectorAll('.history-item')[${before}].querySelector('.history-open').click()`);
  await page().waitFor(`!!document.querySelector('.tx-version-list .segm')`, 'the version, read-only', 20_000);
  const banner = await page().text('.tx-version-banner .banner-lead');
  run.check('the banner says when and after what', /^Transcript as of .+, after .+\.$/.test(banner), banner);
  const versionLine = await page().eval(`document.querySelectorAll('.tx-version-list .segm-text')[1]?.textContent ?? ''`);
  run.check('the version shows the line as it was', versionLine === second.text, versionLine);
  run.check('the version is read-only', (await page().eval(`!!document.querySelector('.tx-version-list .segm-text--editable, .tx-version-list button.segm-speaker')`)) === false);
  await run.shot('version-open');
  await page().click({ name: 'Restore this version' });
  await run.until(() => transcriptOf(id).segments[1].text === second.text, 'the restored line in transcript.json');
  run.check('Restore this version brought the earlier transcript back', true);
  await page().waitFor(`!document.querySelector('.tx-version-banner')`, 'the live transcript again');
  await run.shot('restored');
  const undoName = await page().eval(`document.querySelector('.undo-btn')?.getAttribute('aria-label') ?? ''`);
  run.check('Undo names the restore', undoName.startsWith('Undo restore the transcript as of'), undoName);
  await page().click({ selector: '.undo-btn' });
  await run.until(() => transcriptOf(id).segments[1].text.endsWith('Checked.'), 'the correction back after Undo');
  run.check('Undo took the restore back', true);
  await run.shot('restore-undone');
  await run.app.close();
} catch (error) {
  run.check('the run reached the end', false, error.stack);
  await run.shot('failure').catch(() => null);
  await run.app?.kill();
} finally {
  await sleep(500);
  const summary = run.summary();
  writeFileSync(join(out, 'summary.json'), JSON.stringify(summary, null, 2));
  console.log(JSON.stringify({ passed: summary.passed, failed: summary.failed, failures: summary.results.filter((r) => !r.passed) }, null, 2));
}
