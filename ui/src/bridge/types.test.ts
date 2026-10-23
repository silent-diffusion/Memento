import { describe, expect, expectTypeOf, it } from 'vitest';
import { createBridgeClient, type BridgeLogger } from './client';
import {
  CLIENT_ERROR_CODES,
  ERROR_CODES,
  EVENT_NAMES,
  METHOD_NAMES,
  type AiKeyProvider,
  type AiProvider,
  type AiProvidersPatch,
  type AiProvidersStatus,
  type AiServerPatch,
  type AiServerProbeParams,
  type AiServerProbeResult,
  type AiServerStatus,
  type AiSettings,
  type BridgeEvents,
  type BridgeMethods,
  type EventName,
  type GenerationSendSummary,
  type MethodName,
  type MethodParams,
  type MethodResult,
  type PreviewProviderInfo,
  type ProviderInfo,
  type Template,
} from './types';

const quiet: BridgeLogger = { info: () => undefined, warn: () => undefined };

describe('2.5 Your own server in the contract (checked by tsc)', () => {
  it('every provider id and kind union includes the server', () => {
    expectTypeOf<ProviderInfo['id']>().toEqualTypeOf<'anthropic' | 'openai' | 'local' | 'server'>();
    expectTypeOf<ProviderInfo['kind']>().toEqualTypeOf<'cloud' | 'local' | 'server'>();
    expectTypeOf<PreviewProviderInfo['id']>().toEqualTypeOf<'anthropic' | 'openai' | 'local' | 'server'>();
    expectTypeOf<PreviewProviderInfo['kind']>().toEqualTypeOf<'cloud' | 'local' | 'server'>();
    expectTypeOf<NonNullable<Template['providerId']>>().toEqualTypeOf<ProviderInfo['id']>();
    expectTypeOf<NonNullable<AiSettings['defaultProviderId']>>().toEqualTypeOf<ProviderInfo['id']>();
    expectTypeOf<GenerationSendSummary['providerId']>().toEqualTypeOf<ProviderInfo['id']>();
    expectTypeOf<ProviderInfo['leavesPc']>().toEqualTypeOf<boolean>();
    expectTypeOf<ProviderInfo['address']>().toEqualTypeOf<string | null>();
  });

  it('keeps the cloud map for the two services and adds the server beside it', () => {
    expectTypeOf<AiProvider>().toEqualTypeOf<'anthropic' | 'openai'>();
    expectTypeOf<AiKeyProvider>().toEqualTypeOf<'anthropic' | 'openai' | 'server'>();
    expectTypeOf<MethodParams<'ai.setKey'>['provider']>().toEqualTypeOf<AiKeyProvider>();
    expectTypeOf<MethodParams<'ai.clearKey'>['provider']>().toEqualTypeOf<AiKeyProvider>();
    expectTypeOf<AiSettings['providers']>().toEqualTypeOf<AiProvidersStatus>();
    expectTypeOf<AiProvidersStatus['server']>().toEqualTypeOf<AiServerStatus>();
    expectTypeOf<AiServerStatus>().toEqualTypeOf<{ hasKey: boolean; address: string | null; model: string | null; contextTokens: number; loopback: boolean; leavesPc: boolean; insecure: boolean }>();
    expectTypeOf<NonNullable<AiProvidersPatch['server']>>().toEqualTypeOf<AiServerPatch>();
    expectTypeOf<AiServerPatch>().toEqualTypeOf<{ address?: string | null; model?: string | null; contextTokens?: number }>();
  });

  it('ai.serverProbe takes the typed address and answers what the server is', () => {
    expectTypeOf<MethodParams<'ai.serverProbe'>>().toEqualTypeOf<AiServerProbeParams>();
    expectTypeOf<AiServerProbeParams>().toEqualTypeOf<{ address: string }>();
    expectTypeOf<MethodResult<'ai.serverProbe'>>().toEqualTypeOf<AiServerProbeResult>();
    expectTypeOf<AiServerProbeResult>().toEqualTypeOf<{
      reachable: boolean;
      serverName: string | null;
      models: string[];
      code: string | null;
      detail: string | null;
      loopback: boolean;
      leavesPc: boolean;
      insecure: boolean;
    }>();
    expect(METHOD_NAMES).toContain('ai.serverProbe');
    expect(ERROR_CODES).toEqual(expect.arrayContaining(['ai.noServer', 'ai.noModel', 'ai.serverUnreachable', 'ai.serverNotCompatible', 'ai.serverNeedsKey', 'ai.serverModelUnknown', 'ai.serverInsecure']));
  });
});

describe('contract name lists', () => {
  it('METHOD_NAMES lists every key of BridgeMethods and nothing else', () => {
    // Checked by tsc (npm run build / typecheck): both differences must be empty.
    expectTypeOf<Exclude<keyof BridgeMethods, (typeof METHOD_NAMES)[number]>>().toEqualTypeOf<never>();
    expectTypeOf<Exclude<(typeof METHOD_NAMES)[number], keyof BridgeMethods>>().toEqualTypeOf<never>();
    expect(new Set(METHOD_NAMES).size).toBe(METHOD_NAMES.length);
  });

  it('EVENT_NAMES lists every key of BridgeEvents and nothing else', () => {
    expectTypeOf<Exclude<keyof BridgeEvents, (typeof EVENT_NAMES)[number]>>().toEqualTypeOf<never>();
    expectTypeOf<Exclude<(typeof EVENT_NAMES)[number], keyof BridgeEvents>>().toEqualTypeOf<never>();
    expect(new Set(EVENT_NAMES).size).toBe(EVENT_NAMES.length);
  });

  it('error codes are area.reason, router codes carry the bridge. prefix, and client codes are separate', () => {
    for (const code of [...ERROR_CODES, ...CLIENT_ERROR_CODES]) {
      expect(code).toMatch(/^[a-z]+(\.[a-zA-Z]+)+$/);
    }
    expect(ERROR_CODES.filter((code) => code.startsWith('bridge.'))).toEqual([
      'bridge.invalidJson',
      'bridge.invalidRequest',
      'bridge.unknownMethod',
      'bridge.invalidParams',
      'bridge.cancelled',
      'bridge.internal',
    ]);
    expect(ERROR_CODES).toEqual(expect.arrayContaining(['annotations.notFound', 'recording.alreadyActive', 'settings.invalidValue']));
    expect(new Set(ERROR_CODES).size).toBe(ERROR_CODES.length);
    expect(CLIENT_ERROR_CODES.filter((code) => (ERROR_CODES as readonly string[]).includes(code))).toEqual([]);
  });

  it('names follow area.verb', () => {
    const names: readonly (MethodName | EventName)[] = [...METHOD_NAMES, ...EVENT_NAMES];
    for (const name of names) {
      expect(name).toMatch(/^[a-z]+\.[a-zA-Z]+$/);
    }
  });

  it('the browser-preview host answers every method without bridge.unknownMethod', async () => {
    const client = createBridgeClient({ logger: quiet, mock: { live: false, recovery: false } });
    for (const method of METHOD_NAMES) {
      // Parameters do not matter here; a validation error is still an answer from a known method.
      const outcome = await (client.call as (m: string, p: unknown) => Promise<unknown>)(method, {}).catch((e: unknown) => e);
      expect(outcome, method).not.toMatchObject({ code: 'bridge.unknownMethod' });
    }
  });
});
