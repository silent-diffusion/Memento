// The 2.2 larger-model check, driven through the real UI of the published app over the DevTools protocol: Settings ›
// AI and privacy lists the larger models under "Larger models" with each one's memory plan, "Use this model" makes the
// installed one the model in effect, an imported recording's Meeting minutes are written with it, the Builder's Local
// card names it and where it runs, and How this was made records where it ran. Each step saves a screenshot; the run
// ends with a JSON summary.
//
//   node tools/e2e/m12-large-model.mjs --models <dir> --audio <speech file> [--model qwen3.5-35b-a3b-q4] [--data <dir>]
//                                      [--out <dir>] [--port 9421] [--exe <Memento.exe>] [--settings-only]
//
// --models is a folder with whisper/ and llama/ holding at least one larger model (Qwen3.5 27B or Qwen3.5 35B-A3B); its
// files are hard-linked into the private data root (copied when a link is not possible), never moved. Without a larger
// model there the run is skipped and says so (exit 0). The app runs with LOCALAPPDATA pointed at --data (default
// artifacts/e2e-data-m12), so the real library is never touched. --audio is imported through "Import audio or video"
// (nothing is played). --settings-only stops after Settings and the Builder card (no generation, which takes 15-60 min).

import { spawn } from 'node:child_process';
import { copyFileSync, existsSync, linkSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { App, repoRoot } from './app.mjs';
import { sleep } from './cdp.mjs';

const args = process.argv.slice(2);
const option = (name, fallback) => (args.includes(name) ? args[args.indexOf(name) + 1] : fallback);
const flag = (name) => args.includes(name);
const dataRoot = resolve(option('--data', join(repoRoot, 'artifacts', 'e2e-data-m12')));
const out = resolve(option('--out', join(repoRoot, 'artifacts', 'e2e', 'm12')));
const models = option('--models', null) && resolve(option('--models', null));
const audio = option('--audio', null) && resolve(option('--audio', null));
const exe = option('--exe', null);
const LARGER = { 'qwen3.5-27b-q4': ['Qwen3.5-27B-Q4_K_M.gguf', 'Qwen3.5 27B'], 'qwen3.5-35b-a3b-q4': ['Qwen3.5-35B-A3B-Q4_K_M.gguf', 'Qwen3.5 35B-A3B'] };
const summary = { steps: [], checks: {}, timings: {} };
const t0 = Date.now();
mkdirSync(out, { recursive: true });
let page;
let shots = 0;

function log(step, detail = '') {
  const line = `[${((Date.now() - t0) / 1000).toFixed(0).padStart(5)} s] ${step}${detail ? ' · ' + detail : ''}`;
  console.log(line);
  summary.steps.push(line);
}

function check(condition, message) {
  if (!condition) throw new Error(`Check failed: ${message}`);
  summary.checks[message] = true;
}

async function shot(name) {
  await sleep(500);
  shots += 1;
  await page.screenshot(join(out, `${String(shots).padStart(2, '0')}-${name}.png`));
}

/** The larger models whose files are in --models/llama, by catalog id. */
function largerModelsIn(folder) {
  const llama = join(folder, 'llama');
  if (!existsSync(llama)) return [];
  const files = readdirSync(llama);
  return Object.entries(LARGER).filter(([, [file]]) => files.includes(file)).map(([id]) => id);
}

/** Hard-links (or copies) every model file into the data root's models folder. */
function installModels(from, memento) {
  const to = join(memento, 'models');
  for (const kind of readdirSync(from)) {
    mkdirSync(join(to, kind), { recursive: true });
    for (const file of readdirSync(join(from, kind)).filter((f) => !f.endsWith('.part'))) {
      const target = join(to, kind, file);
      if (existsSync(target)) continue;
      try {
        linkSync(join(from, kind, file), target);
      } catch {
        copyFileSync(join(from, kind, file), target);
      }
    }
  }
}

/** Answers Windows' file picker titled `title` with `path` (UI Automation). */
function answerDialog(title, path) {
  return new Promise((done, fail) => {
    const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', join(repoRoot, 'tools', 'e2e', 'answer-dialog.ps1'), '-Title', title, '-Path', path], { stdio: ['ignore', 'ignore', 'pipe'], windowsHide: true });
    let errors = '';
    child.stderr.on('data', (d) => (errors += d));
    child.once('exit', (code) => (code === 0 ? done() : fail(new Error(`answer-dialog "${title}" exited ${code}: ${errors.trim()}`))));
  });
}

async function home() {
  for (let i = 0; i < 8; i++) {
    if (await page.eval(`/Newest first|Your library is empty/.test(document.body.innerText) && !document.querySelector('[data-spoke-back]')`)) return;
    if (await page.locate({ name: 'Library' })) await page.click({ name: 'Library' });
    else if (await page.locate('[data-spoke-back]')) await page.click({ selector: '[data-spoke-back]' });
    await sleep(700);
  }
}

const text = (selector) => page.eval(`(document.querySelector(${JSON.stringify(selector)})?.innerText ?? '').replace(/\\s+/g, ' ').trim()`);

async function main() {
  const available = models ? largerModelsIn(models) : [];
  if (available.length === 0) {
    console.log(JSON.stringify({ result: 'skipped', reason: `no larger model (${Object.values(LARGER).map(([f]) => f).join(' or ')}) under ${models ?? '--models'}/llama` }, null, 2));
    return;
  }

  const modelId = option('--model', available.includes('qwen3.5-35b-a3b-q4') ? 'qwen3.5-35b-a3b-q4' : available[0]);
  const [, name] = LARGER[modelId];

  const app = new App({ dataRoot, port: Number(option('--port', '9421')), ...(exe ? { exe: resolve(exe) } : {}) });
  installModels(models, app.memento);
  page = await app.start();
  try {
    // 1. Settings › AI and privacy: the group, the plan lines, Use this model.
    await home();
    await page.click({ name: 'Settings' });
    await page.click({ name: 'AI and privacy' });
    await page.waitFor(`document.querySelectorAll('.model-group .model-card').length === 2 && document.querySelectorAll('.model-group .model-plan').length === 2`, 'the Larger models group with both plans', 60_000);
    check((await page.eval(`document.querySelector('.model-group .lbl')?.textContent`)) === 'Larger models', 'Settings lists a "Larger models" group');
    const plans = await page.eval(`[...document.querySelectorAll('.model-group .model-card')].map((c) => ({ id: c.dataset.modelId, facts: c.querySelector('.model-facts')?.innerText ?? '', plan: c.querySelector('.model-plan')?.innerText ?? '', short: !!c.querySelector('.model-plan--short') }))`);
    summary.plans = plans;
    const mine = plans.find((p) => p.id === modelId);
    check(mine && /Installed/.test(mine.facts), `${name} reads as installed`);
    check(mine && /^Runs .* · about \d+(\.\d)? tokens\/s · \d+(\.\d)? GB of RAM while writing$|needs \d+ GB of memory; this PC has \d+ GB free\./.test(mine.plan), `${name} has a plan line (${mine?.plan})`);
    check(plans.every((p) => /Graphics card and RAM, \d+(\.\d)? GB of memory/.test(p.facts)), 'each larger card names the memory it needs');
    await page.eval(`document.querySelector('.model-group').scrollIntoView({ block: 'center' })`);
    await shot('settings-larger-models');
    if (mine.short) {
      log('does not fit now', mine.plan);
      summary.result = 'passed (the model does not fit this PC now; the §17 sentence was shown)';
      return;
    }

    await page.click({ selector: `.model-group [data-model-id="${modelId}"] input[type="radio"]` });
    await page.waitFor(`(document.querySelector('.model-in-use')?.innerText ?? '').includes(${JSON.stringify(name)})`, `${name} in use`, 20_000);
    const inUse = await text('.model-in-use');
    summary.inUse = inUse;
    check(inUse.includes(`${name} · `), `the line under the cards names ${name} and where it runs (${inUse})`);
    await shot('settings-larger-in-use');
    log('chosen', inUse);
    if (flag('--settings-only') || !audio) {
      summary.result = 'passed (settings only)';
      return;
    }

    // 2. Import a recording (nothing is played) and wait for its transcript.
    check(existsSync(audio), '--audio names the speech file to import');
    await home();
    const answered = answerDialog('Import audio or video', audio);
    if (await page.locate({ name: 'Import audio or video' })) await page.click({ name: 'Import audio or video' });
    else {
      await page.click({ name: 'More library actions' });
      await page.click({ name: 'Import audio or video…' });
    }
    await answered;
    const library = join(app.memento, 'Library', 'projects');
    await page.waitFor(`document.querySelectorAll('.lib-item').length >= 1`, 'the imported recording', 60_000);
    const [id] = readdirSync(library).sort();
    const manifest = () => JSON.parse(readFileSync(join(library, id, 'project.json'), 'utf8'));
    const deadline = Date.now() + 20 * 60_000;
    while (Date.now() < deadline && (manifest().stages.length < 2 || manifest().stages.some((s) => s.state === 'active' || s.state === 'queued'))) await sleep(1000);
    check(existsSync(join(library, id, 'transcript.json')), 'the import was transcribed');
    log('imported', id);

    // 3. Review › Create document: the Local card names the model and its placement.
    await home();
    await page.eval(`document.querySelector('.lib-item .row-title')?.click()`);
    await page.waitFor(`!!document.querySelector('.review-panes')`, 'Review', 30_000);
    await page.click({ name: 'Create document' });
    await page.waitFor(`!!document.querySelector('[data-card="m05"]')`, 'the Builder', 30_000);
    await page.click({ name: 'Inputs and output' });
    await page.waitFor(`!!document.querySelector('.providers .provider')`, 'the provider cards', 20_000);
    const card = await page.eval(`(() => { const l = [...document.querySelectorAll('.providers label.provider')].find((x) => x.querySelector('.provider-name')?.innerText === 'Local model'); return l ? { note: l.querySelector('.provider-note')?.innerText ?? '', plan: l.querySelector('.provider-detail--plan')?.innerText ?? '' } : null; })()`);
    summary.builderCard = card;
    check(card && card.note.startsWith(`${name} · `), `the Builder's Local card names ${name} and where it runs (${card?.note})`);
    check(card && card.plan.startsWith('Runs '), `the Builder's Local card shows the plan (${card?.plan})`);
    await shot('builder-local-card');

    // 4. Generate Meeting minutes with the larger model, then How this was made.
    const started = Date.now();
    await page.click({ selector: '.spoke-primary' }, { timeoutMs: 120_000 });
    const ended = await page.waitFor(`document.querySelector('.ai-failure') ? 'failed' : document.querySelector('.doc-paper article') ? 'done' : ''`, 'the minutes', 90 * 60_000);
    summary.timings.generateSeconds = Math.round((Date.now() - started) / 1000);
    if (ended === 'failed') summary.failure = await text('.ai-failure');
    check(ended === 'done', `the minutes were written with ${name} (${summary.failure ?? 'done'})`);
    await page.waitFor(`!!document.querySelector('.how-made')`, 'How this was made', 30_000);
    const how = await text('.how-made');
    summary.howMade = how;
    check(/Placement Ran /.test(how), `How this was made has a Placement row (${how})`);
    await shot('viewer-how-made');
    const history = readFileSync(join(library, id, 'history.jsonl'), 'utf8').trim().split('\n').map((l) => JSON.parse(l));
    const line = history.findLast((h) => h.stage === 'minutes');
    summary.historyLine = line;
    check(line?.event === 'completed' && /Ran /.test(line.detail ?? ''), 'History names where the model ran');
    summary.result = 'passed';
  } finally {
    writeFileSync(join(out, 'summary.json'), JSON.stringify(summary, null, 2));
    console.log(JSON.stringify(summary, null, 2));
    await app.close().catch(() => app.kill());
  }
}

main().catch((error) => {
  console.error(error);
  summary.error = error.message;
  writeFileSync(join(out, 'summary.json'), JSON.stringify(summary, null, 2));
  process.exitCode = 1;
});
