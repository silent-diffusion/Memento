import { describe, expect, it } from 'vitest';
import { createBridgeClient, type BridgeClient } from './client';
import type { MockOptions } from './mock';
import type { GenerationOutput, GenerationProgress } from './types';

const quiet = { info: (): void => undefined, warn: (): void => undefined };
const DESIGN = '20261005-160000-dsrev';

function client(mock: MockOptions = {}): BridgeClient {
  return createBridgeClient({ logger: quiet, mock: { live: false, recovery: false, stepMs: 5, ...mock } });
}

async function runToEnd(bridge: BridgeClient, jobId: string): Promise<{ progress: GenerationProgress[]; output: GenerationOutput[] }> {
  const progress: GenerationProgress[] = [];
  const output: GenerationOutput[] = [];
  const offOutput = bridge.on('generation.output', (o) => {
    if (o.jobId === jobId) {
      output.push(o);
    }
  });
  await new Promise<void>((done) => {
    const off = bridge.on('generation.progress', (p) => {
      if (p.jobId !== jobId) {
        return;
      }
      progress.push(p);
      if (p.stage === 'done' || p.stage === 'failed' || p.stage === 'cancelled') {
        off();
        done();
      }
    });
  });
  offOutput();
  return { progress, output };
}

describe('browser-preview host: Your own server (2.5)', () => {
  it('answers Test connection for the pretend Ollama, nothing at another port, and refuses a public http address', async () => {
    const bridge = client();
    expect(await bridge.call('ai.serverProbe', { address: 'http://localhost:11434' })).toEqual({
      reachable: true,
      serverName: 'Ollama 0.12.3',
      models: ['qwen3:8b', 'llama3.1:8b', 'gemma3:4b'],
      code: null,
      detail: null,
      loopback: true,
      leavesPc: false,
      insecure: false,
    });
    const nothing = await bridge.call('ai.serverProbe', { address: 'localhost:1234' });
    expect(nothing).toMatchObject({ reachable: false, code: 'ai.serverUnreachable', models: [], loopback: true });
    expect(nothing.detail).toContain('Nothing answered at localhost:1234.');
    const insecure = await bridge.call('ai.serverProbe', { address: 'http://203.0.113.7:11434' });
    expect(insecure).toMatchObject({ reachable: false, code: 'ai.serverInsecure', leavesPc: true, insecure: true });
    expect(insecure.detail).toContain('public address over plain http');
    expect(await bridge.call('ai.serverProbe', { address: 'http://192.168.1.20:11434' })).toMatchObject({ reachable: true, leavesPc: true, loopback: false });
    await expect(bridge.call('ai.serverProbe', { address: 'ftp://nope' })).rejects.toMatchObject({ code: 'bridge.invalidParams' });
  });

  it('reports the server as not ready until it has an address and a model, and keeps external AI for addresses that leave the PC', async () => {
    const bridge = client();
    const server = async (): Promise<unknown> => (await bridge.call('providers.list')).providers.find((p) => p.id === 'server');
    expect(await server()).toMatchObject({ kind: 'server', name: 'Your own server', ready: false, code: 'ai.noServer', reason: 'No server address', leavesPc: false, address: null });
    await bridge.call('settings.set', { ai: { providers: { server: { address: 'http://localhost:11434' } } } });
    expect(await server()).toMatchObject({ ready: false, code: 'ai.noModel', reason: 'No model chosen' });
    await bridge.call('settings.set', { ai: { providers: { server: { model: 'qwen3:8b' } } } });
    // On this PC it needs no external AI (it is off by default).
    expect(await server()).toMatchObject({ ready: true, code: null, modelLabel: 'qwen3:8b at localhost:11434', leavesPc: false, address: 'http://localhost:11434', modelListed: null });
    await bridge.call('settings.set', { ai: { providers: { server: { address: 'http://192.168.1.20:11434' } } } });
    expect(await server()).toMatchObject({ ready: false, code: 'ai.disabled', leavesPc: true });
    await bridge.call('settings.set', { ai: { enabled: true } });
    expect(await server()).toMatchObject({ ready: true, modelLabel: 'qwen3:8b at 192.168.1.20:11434' });
    await bridge.call('settings.set', { ai: { providers: { server: { address: 'http://203.0.113.7:11434' } } } });
    expect(await server()).toMatchObject({ ready: false, code: 'ai.serverInsecure' });
    const cloud = (await bridge.call('providers.list')).providers.filter((p) => p.kind === 'cloud');
    expect(cloud.every((p) => p.leavesPc && p.address === null)).toBe(true);
    expect((await bridge.call('providers.list')).providers.map((p) => p.id)).toEqual(['anthropic', 'openai', 'server', 'local']);
  });

  it('checks the server settings as the host does, and stores the optional key without returning it', async () => {
    const bridge = client();
    await expect(bridge.call('settings.set', { ai: { providers: { server: { address: 'not an address' } } } })).rejects.toMatchObject({ code: 'settings.invalidValue' });
    await expect(bridge.call('settings.set', { ai: { providers: { server: { model: 'qwen 3' } } } })).rejects.toMatchObject({ code: 'settings.invalidValue' });
    await expect(bridge.call('settings.set', { ai: { providers: { server: { contextTokens: 100 } } } })).rejects.toMatchObject({ code: 'settings.invalidValue' });
    const saved = await bridge.call('settings.set', { ai: { providers: { server: { contextTokens: 32768 } } } });
    expect(saved.ai.providers.server.contextTokens).toBe(32768);
    expect(await bridge.call('ai.setKey', { provider: 'server', key: 'lm-studio-0123456789' })).toEqual({ hasKey: true });
    const settings = await bridge.call('settings.get');
    expect(settings.ai.providers.server.hasKey).toBe(true);
    expect(JSON.stringify(settings)).not.toContain('lm-studio-0123456789');
    expect(await bridge.call('ai.clearKey', { provider: 'server' })).toEqual({ hasKey: false });
    expect((await bridge.call('settings.set', { ai: { defaultProviderId: 'server' } })).ai.defaultProviderId).toBe('server');
  });

  it('streams a generation on a server on this PC without asking, and asks first for one on the network (keyed on leavesPc)', async () => {
    const bridge = client({ m4: { server: 'local' } });
    const minutes = await bridge.call('templates.get', { templateId: 'meeting-minutes' });
    const template = { ...minutes, providerId: 'server' as const };
    const preview = await bridge.call('generation.preview', { recordingId: DESIGN, template });
    expect(preview).toMatchObject({ providerId: 'server', staysOnPc: true, provider: { id: 'server', kind: 'server', modelLabel: 'qwen3:8b at localhost:11434' } });
    const started = await bridge.call('generation.start', { recordingId: DESIGN, template });
    // Ask before every send is on by default; nothing leaves this PC, so nothing is asked.
    expect(started.confirmationRequired).toBeUndefined();
    const { progress, output } = await runToEnd(bridge, started.jobId);
    expect(progress.at(-1)?.stage).toBe('done');
    expect(output.some((o) => o.kind === 'request' && o.streamed === true)).toBe(true);
    expect(output.some((o) => o.kind === 'token')).toBe(true);
    const documentId = progress.at(-1)?.documentId ?? '';
    const { record } = (await bridge.call('documents.get', { recordingId: DESIGN, documentId })).document;
    expect(record).toMatchObject({ providerId: 'server', modelLabel: 'qwen3:8b at localhost:11434', stayedOnPc: true });
    const project = await bridge.call('project.get', { recordingId: DESIGN });
    expect(project.history.at(-1)?.summary).toBe('Meeting minutes generated with Your own server · qwen3:8b at localhost:11434 (this PC)');
    expect(project.history.at(-1)?.detail).toMatch(/^Read on this PC: /);

    const network = client({ m4: { server: 'network', ai: 'ready' } });
    const asked = await network.call('generation.start', { recordingId: DESIGN, template });
    expect(asked.confirmationRequired).toBe(true);
    expect(asked.summary).toMatchObject({ providerId: 'server', providerName: 'Your own server', modelLabel: 'qwen3:8b at 192.168.1.20:11434' });
    await network.call('generation.confirm', { jobId: asked.jobId, approved: false });
  });

  it('fails with the server codes when nothing answers or the model is not served', async () => {
    const bridge = client({ m4: { server: 'local' } });
    const minutes = await bridge.call('templates.get', { templateId: 'meeting-minutes' });
    const template = { ...minutes, providerId: 'server' as const };
    await bridge.call('settings.set', { ai: { providers: { server: { model: 'mistral:7b' } } } });
    const unknown = await runToEnd(bridge, (await bridge.call('generation.start', { recordingId: DESIGN, template })).jobId);
    expect(unknown.progress.at(-1)).toMatchObject({ stage: 'failed', code: 'ai.serverModelUnknown' });
    await bridge.call('settings.set', { ai: { providers: { server: { address: 'http://localhost:1234', model: 'qwen3:8b' } } } });
    const unreachable = await runToEnd(bridge, (await bridge.call('generation.start', { recordingId: DESIGN, template })).jobId);
    expect(unreachable.progress.at(-1)).toMatchObject({ stage: 'failed', code: 'ai.serverUnreachable' });
    expect(unreachable.progress.at(-1)?.message).toContain('Nothing answered at localhost:1234');
  });
});
