// The browser-preview host's Your own server (2.5): a pretend Ollama on http://localhost:11434 (and on port 11434 of a
// private address), a pretend vLLM behind any public https address, nothing at any other address, and a public http
// address refused as insecure; then the provider entry providers.list gives for the saved address, and settings.set's
// checks of `ai.providers.server`. Nothing here makes a network request. `?server=local|network|public|unreachable`
// starts the preview with an address (and qwen3:8b chosen, except for `unreachable`).
import { readServerAddress, serverLine, serverPlace } from '../format/serverAddress';
import { modelIdProblem } from './cloudModels';
import { MockHostError } from './mockSession';
import { NO_SERVER, SERVER_CONTEXT_MAX, SERVER_CONTEXT_MIN, serverStatus } from './serverSettings';
import type { AiServerPatch, AiServerProbeResult, AiServerStatus, AiSettings, MethodName, MethodParams, MethodResult, ProviderInfo } from './types';

export type ServerFlag = 'none' | 'local' | 'network' | 'public' | 'unreachable';

/** The pretend Ollama: its name and the models it serves. */
export const MOCK_OLLAMA = { name: 'Ollama 0.12.3', models: ['qwen3:8b', 'llama3.1:8b', 'gemma3:4b'] } as const;
/** The pretend server behind a public https address. */
export const MOCK_VLLM = { name: 'vLLM 0.11.0', models: ['Qwen/Qwen3-8B'] } as const;

export const SERVER_NAME = 'Your own server';

export function serverFlagFromQuery(value: string | null): ServerFlag | undefined {
  return value === 'none' || value === 'local' || value === 'network' || value === 'public' || value === 'unreachable' ? value : undefined;
}

/** The server settings the preview starts with for a `?server=` flag. */
export function mockServerSettings(flag: ServerFlag): AiServerStatus {
  switch (flag) {
    case 'local':
      return serverStatus(false, 'http://localhost:11434', 'qwen3:8b');
    case 'network':
      return serverStatus(false, 'http://192.168.1.20:11434', 'qwen3:8b');
    case 'public':
      return serverStatus(false, 'http://203.0.113.7:11434', 'qwen3:8b');
    case 'unreachable':
      return serverStatus(false, 'http://localhost:1234', null);
    case 'none':
      return NO_SERVER;
  }
}

/** What answers at an address in the preview: the pretend Ollama, the pretend vLLM, or nothing. */
function serverAt(address: string): { name: string; models: readonly string[] } | null {
  const info = readServerAddress(address);
  if (info === null || info.insecure) {
    return null;
  }
  if (info.place === 'remote') {
    return MOCK_VLLM;
  }
  return info.hostPort.endsWith(':11434') ? MOCK_OLLAMA : null;
}

export function insecureSentence(hostPort: string): string {
  return `${hostPort} is a public address over plain http, so a key or a transcript sent to it could be read on the way. Memento does not send to it. Use the server's https address, or a server on this PC or your network.`;
}

export function unreachableSentence(hostPort: string): string {
  return `Nothing answered at ${hostPort}. Check that the server is running and that the address and port are right (Ollama listens on 11434, LM Studio on 1234), then test again. Nothing was sent.`;
}

/** ai.serverProbe: GET /v1/models on the typed address, as the host asks it. */
export function mockServerProbe(address: string): AiServerProbeResult {
  const info = readServerAddress(address);
  if (info === null) {
    throw new MockHostError('bridge.invalidParams', `'${address.trim()}' is not a server address. Type it like http://localhost:11434. Nothing was sent.`, 'address');
  }
  const flags = { loopback: info.place === 'loopback', leavesPc: info.place !== 'loopback', insecure: info.insecure };
  if (info.insecure) {
    return { reachable: false, serverName: null, models: [], code: 'ai.serverInsecure', detail: insecureSentence(info.hostPort), ...flags };
  }
  const server = serverAt(address);
  if (server === null) {
    return { reachable: false, serverName: null, models: [], code: 'ai.serverUnreachable', detail: unreachableSentence(info.hostPort), ...flags };
  }
  return { reachable: true, serverName: server.name, models: [...server.models], code: null, detail: null, ...flags };
}

export const SERVER_METHODS = ['ai.serverProbe'] as const satisfies readonly MethodName[];

export type ServerMethod = (typeof SERVER_METHODS)[number];

/** The browser-preview host's 2.5 methods, spread into mock.ts's handlers. */
export const SERVER_HANDLERS: { [M in ServerMethod]: (params: MethodParams<M>) => MethodResult<M> } = {
  'ai.serverProbe': (params) => mockServerProbe(params.address),
};

/** Whether the server at the saved address serves `model` (generation's ai.serverModelUnknown otherwise). */
export function mockServerServes(address: string, model: string): boolean {
  return serverAt(address)?.models.includes(model) ?? false;
}

/** Whether anything answers at the saved address (generation's ai.serverUnreachable otherwise). */
export function mockServerAnswers(address: string): boolean {
  return serverAt(address) !== null;
}

const count = (n: number): string => n.toLocaleString('en-US');

/** providers.list's entry for Your own server, as the host's ProviderRegistry builds it. */
export function serverProviderInfo(ai: AiSettings): ProviderInfo {
  const s = ai.providers.server;
  const hostPort = s.address === null ? null : (readServerAddress(s.address)?.hostPort ?? s.address);
  const place = serverPlace(s, s.address);
  const where = place === 'network' ? 'on your network' : 'a remote address';
  let code: string | null = null;
  let reason: string | null = null;
  let detail: string;
  if (s.address === null || hostPort === null) {
    code = 'ai.noServer';
    reason = 'No server address';
    detail = 'Your own server has no address yet. Set it in Settings › AI and privacy › Your own server. Nothing was sent.';
  } else if (s.insecure) {
    code = 'ai.serverInsecure';
    reason = 'Plain http to a public address';
    detail = insecureSentence(hostPort);
  } else if (s.leavesPc && !ai.enabled) {
    code = 'ai.disabled';
    reason = 'External AI is off';
    detail = `The server at ${hostPort} is ${where}, so sending to it needs Allow external AI services. Turn it on in Settings › AI and privacy, or use a server on this PC. Nothing was sent.`;
  } else if (s.model === null) {
    code = 'ai.noModel';
    reason = 'No model chosen';
    detail = `No model is chosen for the server at ${hostPort}. Test the connection in Settings › AI and privacy › Your own server and choose one. Nothing was sent.`;
  } else {
    detail = `Writes with ${s.model} on the server at ${hostPort}, which runs a ${count(s.contextTokens)}-token context; a transcript is split to fit a quarter of it. ${s.leavesPc ? 'Ask before every send applies.' : 'Nothing leaves this PC.'}`;
  }
  return {
    id: 'server',
    name: SERVER_NAME,
    vendor: SERVER_NAME,
    kind: 'server',
    ready: code === null,
    reason,
    modelLabel: s.model === null || hostPort === null ? null : `${s.model} at ${hostPort}`,
    code,
    detail,
    leavesPc: s.leavesPc,
    address: s.address,
    modelId: null,
    modelListed: null,
    gpuMemory: null,
    gpuNote: null,
    memoryPlan: null,
    largerModels: null,
  };
}

/** "Your own server · qwen3:8b at localhost:11434 (this PC)" for a History line. */
export function serverHistoryName(provider: ProviderInfo): string {
  return serverLine(provider.name, provider.modelLabel, serverPlace(provider, provider.address));
}

/** settings.set's checks of `ai.providers.server`, as the host makes them; throws settings.invalidValue. */
export function checkServerPatch(patch: AiServerPatch | undefined): void {
  if (patch === undefined) {
    return;
  }
  const invalid = (message: string, field: string): MockHostError => new MockHostError('settings.invalidValue', message, `ai.providers.server.${field}`);
  if (typeof patch.address === 'string' && patch.address.trim() !== '' && readServerAddress(patch.address) === null) {
    throw invalid(`'${patch.address.trim()}' is not a server address. Type it like http://localhost:11434. Nothing was changed.`, 'address');
  }
  if (typeof patch.model === 'string' && patch.model.trim() !== '') {
    const problem = modelIdProblem(patch.model, 'qwen3:8b');
    if (problem !== null) {
      throw invalid(problem, 'model');
    }
  }
  if (patch.contextTokens !== undefined && !(Number.isInteger(patch.contextTokens) && patch.contextTokens >= SERVER_CONTEXT_MIN && patch.contextTokens <= SERVER_CONTEXT_MAX)) {
    throw invalid(`The context size is a whole number of tokens from ${count(SERVER_CONTEXT_MIN)} to ${count(SERVER_CONTEXT_MAX)}. Nothing was changed.`, 'contextTokens');
  }
}
