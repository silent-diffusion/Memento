import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { createBridgeClient, type BridgeClient } from './client';
import type { MockOptions } from './mock';
import { extractArticle, restyle } from './mockPaper';
import { builtInStyleSettings } from './mockTemplates';
import { M4_METHODS } from './mockGeneration';
import { ERROR_CODES, EVENT_NAMES, METHOD_NAMES, type GenerationProgress } from './types';

const quiet = { info: (): void => undefined, warn: (): void => undefined };
const DESIGN = '20261005-160000-dsrev';
const FIXTURES = resolve(__dirname, '../../../tests/Memento.Documents.Tests/fixtures/expected');

function client(mock: MockOptions = {}): BridgeClient {
  return createBridgeClient({ logger: quiet, mock: { live: false, recovery: false, stepMs: 5, ...mock } });
}

const fixture = (name: string): string | null => {
  const path = resolve(FIXTURES, name);
  return existsSync(path) ? readFileSync(path, 'utf8') : null;
};

/** The rows of a paper: everything after the title block. */
const rowsOf = (html: string): string => html.slice(html.indexOf('</header>'));

async function progressUntilEnd(bridge: BridgeClient, jobId: string): Promise<GenerationProgress[]> {
  const seen: GenerationProgress[] = [];
  await new Promise<void>((done) => {
    const off = bridge.on('generation.progress', (p) => {
      if (p.jobId !== jobId) {
        return;
      }
      seen.push(p);
      if (p.stage === 'done' || p.stage === 'failed' || p.stage === 'cancelled') {
        off();
        done();
      }
    });
  });
  return seen;
}

describe('M4 contract names (BRIDGE-M4.md)', () => {
  it('lists every M4 method, event and error code in types.ts, in the contract’s order', () => {
    expect(METHOD_NAMES.slice(METHOD_NAMES.indexOf('modules.list'))).toEqual([...M4_METHODS]);
    expect(EVENT_NAMES.slice(-4)).toEqual(['generation.progress', 'documents.changed', 'templates.changed', 'styles.changed']);
    expect(ERROR_CODES.slice(ERROR_CODES.indexOf('ai.disabled'))).toEqual([
      'ai.disabled',
      'ai.providerNotReady',
      'ai.noKey',
      'ai.invalidKey',
      'ai.rateLimited',
      'ai.network',
      'ai.providerError',
      'ai.contentTooLong',
      'ai.modelNotInstalled',
      'ai.notEnoughVram',
      'ai.workerCrashed',
      'generation.noTranscript',
      'generation.busy',
      'generation.notFound',
      'templates.notFound',
      'templates.builtIn',
      'styles.notFound',
      'styles.builtIn',
      'styles.inUse',
      'documents.notFound',
      'documents.unsupportedEdit',
      'documents.versionNotFound',
    ]);
  });

  it('carries the M4 settings: default provider, local model, default template and style', async () => {
    const bridge = client();
    const settings = await bridge.call('settings.get');
    expect(settings.documents).toEqual({ defaultTemplateId: 'meeting-minutes', defaultStyleId: 'corporate' });
    expect(settings.ai).toMatchObject({ defaultProviderId: null, localModelId: 'qwen3.5-4b-instruct-q4' });
    const changed = await bridge.call('settings.set', { ai: { defaultProviderId: 'local' }, documents: { defaultStyleId: 'minimal' } });
    expect(changed.ai.defaultProviderId).toBe('local');
    expect(changed.documents.defaultStyleId).toBe('minimal');
    expect((await bridge.call('settings.set', { ai: { defaultProviderId: null } })).ai.defaultProviderId).toBeNull();
    await expect(bridge.call('settings.set', { documents: { defaultTemplateId: 'gone' } })).rejects.toMatchObject({ code: 'settings.invalidValue' });
    await expect(bridge.call('settings.set', { ai: { localModelId: 'whisper-small' } })).rejects.toMatchObject({ code: 'settings.invalidValue' });
  });
});

describe('M4 browser-preview host', () => {
  it('lists the 23 modules in palette order with their groups and shapes', async () => {
    const { modules } = await client().call('modules.list');
    expect(modules).toHaveLength(23);
    expect(modules.filter((m) => m.group === 'structure').map((m) => m.id)).toEqual([
      'title', 'summary', 'executiveSummary', 'meetingPurpose', 'participants', 'agenda', 'discussion', 'decisions', 'actionItems', 'openQuestions', 'nextMeeting',
    ]);
    expect(modules.filter((m) => m.group === 'custom').map((m) => m.name)).toEqual(['Custom text', 'Custom AI section']);
    expect(modules.find((m) => m.id === 'actionItems')).toMatchObject({ shape: 'table', generated: true });
    expect(modules.find((m) => m.id === 'fullTranscript')).toMatchObject({ shape: 'transcript', generated: false });
  });

  it('has four built-in templates and three styles, and protects the built-ins', async () => {
    const bridge = client();
    const { templates } = await bridge.call('templates.list');
    expect(templates.map((t) => t.name)).toEqual(['Meeting minutes', 'Interview notes', 'Lecture summary', 'Dictation clean-up']);
    const { styles } = await bridge.call('styles.list');
    expect(styles.map((s) => [s.name, s.usedByTemplates])).toEqual([
      ['Corporate', 1],
      ['Minimal', 2],
      ['Academic', 1],
    ]);
    await expect(bridge.call('templates.delete', { templateId: 'meeting-minutes' })).rejects.toMatchObject({ code: 'templates.builtIn' });
    const minutes = templates[0];
    if (minutes === undefined) {
      throw new Error('no templates');
    }
    const copy = await bridge.call('templates.save', { template: minutes });
    expect(copy).toMatchObject({ name: 'Meeting minutes (copy)', builtIn: false });
    expect(copy.id).not.toBe('meeting-minutes');
    await expect(bridge.call('styles.delete', { styleId: 'minimal' })).rejects.toMatchObject({ code: 'styles.builtIn' });
    const style = await bridge.call('styles.duplicate', { styleId: 'minimal' });
    await bridge.call('templates.save', { template: { ...copy, styleId: style.id } });
    await expect(bridge.call('styles.delete', { styleId: style.id })).rejects.toMatchObject({ code: 'styles.inUse' });
  });

  it('answers styles.sampleHtml exactly as the engine renders the three built-in samples', async () => {
    const bridge = client();
    for (const id of ['corporate', 'minimal', 'academic'] as const) {
      const expected = fixture(`style-sample.${id}.html`);
      if (expected === null) {
        continue;
      }
      const { html } = await bridge.call('styles.sampleHtml', { settings: builtInStyleSettings()[id] });
      expect(html).toBe(extractArticle(expected));
    }
  });

  it('draws the Builder skeleton with the engine’s markup for every shape', async () => {
    const bridge = client();
    const template = await bridge.call('templates.get', { templateId: 'meeting-minutes' });
    const expected = fixture('meeting-minutes.corporate.skeleton.html');
    const { html } = await bridge.call('generation.previewHtml', { recordingId: DESIGN, template, styleId: 'corporate' });
    expect(html).toMatch(/^<article class="paper paper-skeleton caps title-rule th-fill" data-style="corporate"/);
    if (expected !== null) {
      expect(rowsOf(html)).toBe(rowsOf(extractArticle(expected)));
    }
    const empty = await bridge.call('generation.previewHtml', { recordingId: null, template: { ...template, rows: [] }, styleId: 'minimal' });
    expect(empty.html).toContain('<p class="paper-empty">Add modules to see the layout.</p>');
    expect(empty.html).toContain('Sample recording');
  });

  it('restyles a viewer paper by its open tag only, as the engine does', () => {
    const corporate = fixture('meeting-minutes.corporate.viewer.html');
    const academic = fixture('meeting-minutes.academic.viewer.html');
    if (corporate === null || academic === null) {
      return;
    }
    expect(restyle(extractArticle(corporate), builtInStyleSettings().academic, 'academic', 'viewer')).toBe(extractArticle(academic));
  });

  it('reports providers from the ?ai= flag and refuses to generate while external AI is off', async () => {
    const off = client({ m4: { ai: 'off' } });
    const listed = await off.call('providers.list');
    expect(listed.externalAiEnabled).toBe(false);
    expect(listed.providers.map((p) => [p.id, p.ready, p.reason])).toEqual([
      ['anthropic', false, 'External AI is off'],
      ['openai', false, 'External AI is off'],
      ['local', false, 'Model not installed'],
    ]);
    const template = await off.call('templates.get', { templateId: 'meeting-minutes' });
    await expect(off.call('generation.start', { recordingId: DESIGN, template })).rejects.toMatchObject({ code: 'ai.disabled' });
    const nokey = await client({ m4: { ai: 'nokey' } }).call('providers.list');
    expect(nokey.providers.map((p) => p.reason)).toEqual(['No key saved', 'No key saved', 'Model not installed']);
    const local = await client({ m4: { ai: 'local' } }).call('providers.list');
    expect(local.providers.find((p) => p.id === 'local')).toMatchObject({ ready: true, modelLabel: 'Qwen3.5 4B' });
  });

  it('asks before sending, then writes the document through every stage', async () => {
    const bridge = client({ m4: { ai: 'ready' } });
    const template = await bridge.call('templates.get', { templateId: 'meeting-minutes' });
    const preview = await bridge.call('generation.preview', { recordingId: DESIGN, template });
    expect(preview.payloadText).toContain('[Transcript]');
    expect(preview.payloadText).toContain('Audio and video are not sent.');
    expect(preview.inputsUsed).toMatchObject({ transcript: true, agenda: true, attachments: false });
    const started = await bridge.call('generation.start', { recordingId: DESIGN, template });
    expect(started.confirmationRequired).toBe(true);
    expect(started.summary).toMatchObject({ providerId: 'anthropic', bytes: preview.bytes, chunks: preview.chunks });
    const ended = progressUntilEnd(bridge, started.jobId);
    await bridge.call('generation.confirm', { jobId: started.jobId, approved: true });
    const seen = await ended;
    expect([...new Set(seen.map((p) => p.stage))]).toEqual(['composing', 'generating', 'verifying', 'rendering', 'done']);
    const done = seen[seen.length - 1];
    expect(done?.documentId).toEqual(expect.any(String));
    const { documents } = await bridge.call('documents.list', { recordingId: DESIGN });
    expect(documents.map((d) => d.name)).toContain('Meeting minutes 2');
    const { html } = await bridge.call('documents.renderHtml', { recordingId: DESIGN, documentId: done?.documentId ?? '', mode: 'view' });
    expect(html).toMatch(/^<article class="paper paper-viewer/);
    expect(html).toContain('<span class="chip">Aiko Tanaka</span>');
  });

  it('fails the first generation with ?gen=fail and succeeds on the next try', async () => {
    const bridge = client({ m4: { ai: 'ready', gen: 'fail' } });
    await bridge.call('settings.set', { ai: { askBeforeSend: false } });
    const template = await bridge.call('templates.get', { templateId: 'meeting-minutes' });
    const first = await bridge.call('generation.start', { recordingId: DESIGN, template });
    const failed = await progressUntilEnd(bridge, first.jobId);
    expect(failed[failed.length - 1]?.stage).toBe('failed');
    expect(failed[failed.length - 1]?.message).toContain('Claude could not be reached');
    const second = await bridge.call('generation.start', { recordingId: DESIGN, template });
    const ok = await progressUntilEnd(bridge, second.jobId);
    expect(ok[ok.length - 1]?.stage).toBe('done');
  });

  it('keeps edits as versions and refuses markup a document cannot hold', async () => {
    const bridge = client();
    const id = 'doc-20261005-minutes';
    const { versions: before } = await bridge.call('documents.versions', { recordingId: DESIGN, documentId: id });
    expect(before.map((v) => v.reason)).toEqual(['edited', 'generated']);
    const { html } = await bridge.call('documents.renderHtml', { recordingId: DESIGN, documentId: id, mode: 'view' });
    await expect(bridge.call('documents.saveEdit', { recordingId: DESIGN, documentId: id, html: html.replace('</article>', '<script>x</script></article>') })).rejects.toMatchObject({
      code: 'documents.unsupportedEdit',
    });
    const saved = await bridge.call('documents.saveEdit', { recordingId: DESIGN, documentId: id, html: html.replace('Agree the library layout', 'Agree the library layout and colours') });
    expect(saved.version).toBe(3);
    const restored = await bridge.call('documents.restoreVersion', { recordingId: DESIGN, documentId: id, versionId: 'v1' });
    expect(restored.document.title).toBe('Design review: library screen');
    const { versions: after } = await bridge.call('documents.versions', { recordingId: DESIGN, documentId: id });
    expect(after[0]?.reason).toBe('restored');
  });

  it('exports the chosen documents from the Export dialog’s Documents row', async () => {
    const bridge = client();
    const settings = await bridge.call('settings.get');
    const estimate = await bridge.call('export.estimate', {
      recordingId: DESIGN,
      selection: { ...settings.export.defaults, documents: { on: true, documentIds: ['doc-20261005-notes'], format: 'markdown' } },
    });
    const docs = estimate.items.filter((i) => i.component === 'documents');
    expect(docs).toHaveLength(1);
    expect(docs[0]?.documentId).toBe('doc-20261005-notes');
    expect(docs[0]?.name).toMatch(/^Design review - library screen \d{4}-\d{2}-\d{2} - My notes\.md$/);
  });
});
