import { describe, expect, it } from 'vitest';
import { providerState } from './cloudModels';
import { clampContext, mergeServer, NO_SERVER, SERVER_CONTEXT_DEFAULT, serverStatus, withKey } from './serverSettings';
import { mergeProviders } from './settingsMerge';
import type { AiProvidersStatus } from './types';

describe('Your own server in the settings snapshot (2.5)', () => {
  it('starts with no address, no model, no key and an 8192-token context', () => {
    expect(NO_SERVER).toEqual({ hasKey: false, address: null, model: null, contextTokens: SERVER_CONTEXT_DEFAULT, loopback: false, leavesPc: false, insecure: false });
    expect(SERVER_CONTEXT_DEFAULT).toBe(8192);
  });

  it('clamps a typed context size to whole tokens inside the limits', () => {
    expect(clampContext('8192')).toBe(8192);
    expect(clampContext('32,768')).toBe(32768);
    expect(clampContext(100)).toBe(2048);
    expect(clampContext('5000000')).toBe(1_048_576);
    expect(clampContext('4096.6')).toBe(4097);
    expect(clampContext('lots')).toBeNull();
  });

  it('merges a patch field by field: null clears, left out keeps, the flags follow the address', () => {
    const local = mergeServer(NO_SERVER, { address: ' http://localhost:11434 ', model: 'qwen3:8b' });
    expect(local).toMatchObject({ address: 'http://localhost:11434', model: 'qwen3:8b', loopback: true, leavesPc: false, insecure: false });
    const lan = mergeServer(local, { address: 'http://192.168.1.20:11434' });
    expect(lan).toMatchObject({ model: 'qwen3:8b', loopback: false, leavesPc: true, insecure: false });
    expect(mergeServer(lan, { address: 'http://203.0.113.7:11434' })).toMatchObject({ leavesPc: true, insecure: true });
    expect(mergeServer(lan, { model: null })).toMatchObject({ address: 'http://192.168.1.20:11434', model: null });
    expect(mergeServer(lan, { address: null })).toMatchObject({ address: null, leavesPc: false, loopback: false });
    expect(mergeServer(lan, { contextTokens: 16384 }).contextTokens).toBe(16384);
    expect(mergeServer({ ...lan, hasKey: true }, { address: 'http://localhost:1234' }).hasKey).toBe(true);
  });

  it('keeps the cloud models and the server apart in mergeProviders, and sets one key flag at a time', () => {
    const before: AiProvidersStatus = { anthropic: providerState('anthropic', true, null), openai: providerState('openai', false, null), server: NO_SERVER };
    const next = mergeProviders(before, { server: { address: 'http://localhost:11434' } });
    expect(next.anthropic).toEqual(before.anthropic);
    expect(next.server.loopback).toBe(true);
    expect(mergeProviders(before, { anthropic: { model: 'claude-haiku-5-5' } }).server).toEqual(NO_SERVER);
    const keyed = withKey(before, 'server', true);
    expect(keyed.server.hasKey).toBe(true);
    expect(keyed.anthropic.hasKey).toBe(true);
    expect(withKey(keyed, 'anthropic', false)).toMatchObject({ anthropic: { hasKey: false }, server: { hasKey: true } });
    expect(serverStatus(false, 'not an address', null)).toMatchObject({ loopback: false, leavesPc: false, insecure: false });
  });
});
