// 2.5, Your own server in the settings snapshot (`ai.providers.server`): the defaults and limits Settings offers, how a
// settings.set patch merges into it (the page's optimistic copy and the browser-preview host both use this, so they
// agree with the host), and a provider's key flag set after ai.setKey / ai.clearKey.
import { readServerAddress } from '../format/serverAddress';
import type { AiKeyProvider, AiProvidersStatus, AiServerPatch, AiServerStatus } from './types';

/** What an Ollama server with num_ctx raised once, LM Studio and llama.cpp's server commonly run. */
export const SERVER_CONTEXT_DEFAULT = 8192;
export const SERVER_CONTEXT_MIN = 2048;
export const SERVER_CONTEXT_MAX = 1_048_576;

/** A typed context size as Settings saves it: a whole number of tokens inside the limits; null when it is not a number. */
export function clampContext(typed: string | number): number | null {
  const value = typeof typed === 'number' ? typed : Number(typed.replace(/[\s,_]/g, ''));
  if (!Number.isFinite(value)) {
    return null;
  }
  return Math.min(SERVER_CONTEXT_MAX, Math.max(SERVER_CONTEXT_MIN, Math.round(value)));
}

/** The snapshot entry for an address, a model and a context; the flags read from the address. */
export function serverStatus(hasKey: boolean, address: string | null, model: string | null, contextTokens: number = SERVER_CONTEXT_DEFAULT): AiServerStatus {
  const info = address === null ? null : readServerAddress(address);
  return {
    hasKey,
    address,
    model,
    contextTokens,
    loopback: info?.place === 'loopback',
    leavesPc: info !== null && info.place !== 'loopback',
    insecure: info?.insecure ?? false,
  };
}

/** No address, no model, no key: as a new install starts. */
export const NO_SERVER: AiServerStatus = serverStatus(false, null, null);

/** `before` with a settings.set patch applied: a field left out keeps its value, null clears it; the key never changes here. */
export function mergeServer(before: AiServerStatus, patch: AiServerPatch | null | undefined): AiServerStatus {
  if (patch == null) {
    return before;
  }
  const address = 'address' in patch ? (patch.address == null ? null : patch.address.trim() === '' ? null : patch.address.trim()) : before.address;
  const model = 'model' in patch ? (patch.model == null ? null : patch.model.trim() === '' ? null : patch.model.trim()) : before.model;
  const context = patch.contextTokens === undefined ? before.contextTokens : (clampContext(patch.contextTokens) ?? before.contextTokens);
  return serverStatus(before.hasKey, address, model, context);
}

/** The providers with one key flag changed (ai.setKey / ai.clearKey answered `hasKey`). */
export function withKey(providers: AiProvidersStatus, provider: AiKeyProvider, hasKey: boolean): AiProvidersStatus {
  switch (provider) {
    case 'server':
      return { ...providers, server: { ...providers.server, hasKey } };
    case 'anthropic':
      return { ...providers, anthropic: { ...providers.anthropic, hasKey } };
    case 'openai':
      return { ...providers, openai: { ...providers.openai, hasKey } };
  }
}
