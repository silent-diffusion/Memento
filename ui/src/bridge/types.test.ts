import { describe, expect, expectTypeOf, it } from 'vitest';
import { createBridgeClient, type BridgeLogger } from './client';
import { CLIENT_ERROR_CODES, ERROR_CODES, EVENT_NAMES, METHOD_NAMES, type BridgeEvents, type BridgeMethods, type EventName, type MethodName } from './types';

const quiet: BridgeLogger = { info: () => undefined, warn: () => undefined };

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
