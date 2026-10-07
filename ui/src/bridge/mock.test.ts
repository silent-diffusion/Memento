import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { BridgeLogger } from './client';
import { createMockTransport, type MockOptions } from './mock';
import { ERROR_CODES, type BridgeError, type MethodName, type MethodParams, type MethodResult } from './types';

const quiet: BridgeLogger = { info: () => undefined, warn: () => undefined };
const SAMPLE = '20261006-100000-q3plan';

interface Envelope {
  id?: number | null;
  result?: unknown;
  error?: BridgeError;
  event?: string;
  payload?: unknown;
}

/** Drives the preview host directly, settling its zero-delay deliveries with fake timers. */
function previewHost(options: MockOptions = {}) {
  const messages: Envelope[] = [];
  const transport = createMockTransport(quiet, { live: false, recovery: false, ...options });
  transport.subscribe((message) => messages.push(message as Envelope));
  let nextId = 0;
  const send = (method: string, params: unknown): Envelope => {
    const id = ++nextId;
    transport.send({ id, method: method as MethodName, params: params as never });
    vi.advanceTimersByTime(0);
    const response = messages.find((m) => m.id === id);
    if (response === undefined) {
      throw new Error(`no answer to ${method}`);
    }
    return response;
  };
  return {
    messages,
    call<M extends MethodName>(method: M, params: MethodParams<M>): MethodResult<M> {
      const response = send(method, params);
      if (response.error !== undefined) {
        throw new Error(`${method} failed: ${response.error.code}`);
      }
      return response.result as MethodResult<M>;
    },
    fail(method: MethodName, params: unknown): BridgeError {
      const response = send(method, params);
      if (response.error === undefined) {
        throw new Error(`${method} succeeded`);
      }
      return response.error;
    },
  };
}

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

describe('preview host annotations', () => {
  it('assigns ids on add and ignores any id the page sends', () => {
    const host = previewHost();
    const { chapters } = host.call('annotations.addChapter', { recordingId: SAMPLE, chapter: { id: 'mine', atMs: 5, title: ' Intro ' } });
    const added = chapters.find((c) => c.title === 'Intro');
    expect(added).toMatchObject({ atMs: 5, origin: 'user' });
    expect(added?.id).toMatch(/^c[0-9a-f]+$/);

    const { highlights } = host.call('annotations.addHighlight', { recordingId: SAMPLE, highlight: { id: 'mine', atMs: 7 } });
    expect(highlights.find((h) => h.atMs === 7)).toMatchObject({ note: '', origin: 'user', segmentId: null });
    expect(highlights.some((h) => h.id === 'mine')).toBe(false);

    const { topics } = host.call('annotations.addTopic', { recordingId: SAMPLE, topic: { id: 'mine', label: 'Hiring' } });
    expect(topics.find((t) => t.label === 'Hiring')?.id).toMatch(/^t[0-9a-f]+$/);
  });

  it('updates only the fields sent and keeps lists in time order', () => {
    const host = previewHost();
    const id = host.call('annotations.addChapter', { recordingId: SAMPLE, chapter: { atMs: 1_000, title: 'Late' } }).chapters.find((c) => c.title === 'Late')?.id ?? '';
    const { chapters } = host.call('annotations.updateChapter', { recordingId: SAMPLE, chapter: { id, atMs: 0 } });
    expect(chapters.find((c) => c.id === id)).toMatchObject({ atMs: 0, title: 'Late' });
    expect(chapters.map((c) => c.atMs)).toEqual([...chapters.map((c) => c.atMs)].sort((a, b) => a - b));
  });

  it('answers annotations.notFound for ids the recording does not have', () => {
    const host = previewHost();
    const cases: [MethodName, unknown, string][] = [
      ['annotations.updateChapter', { recordingId: SAMPLE, chapter: { id: 'c-gone', title: 'x' } }, 'c-gone'],
      ['annotations.removeChapter', { recordingId: SAMPLE, chapterId: 'c-gone' }, 'c-gone'],
      ['annotations.updateHighlight', { recordingId: SAMPLE, highlight: { id: 'h-gone', note: 'x' } }, 'h-gone'],
      ['annotations.removeHighlight', { recordingId: SAMPLE, highlightId: 'h-gone' }, 'h-gone'],
      ['annotations.removeTopic', { recordingId: SAMPLE, topicId: 't-gone' }, 't-gone'],
    ];
    for (const [method, params, id] of cases) {
      const error = host.fail(method, params);
      expect(error.code, method).toBe('annotations.notFound');
      expect(error.detail, method).toBe(id);
      expect(error.message, method).toContain('Nothing was changed.');
    }
  });

  it('answers bridge.invalidParams like the host', () => {
    const host = previewHost();
    expect(host.fail('annotations.addChapter', { recordingId: SAMPLE, chapter: { title: 'no time' } }).code).toBe('bridge.invalidParams');
    expect(host.fail('annotations.addHighlight', { recordingId: SAMPLE, highlight: { note: 'no time' } }).code).toBe('bridge.invalidParams');
    expect(host.fail('annotations.updateHighlight', { recordingId: SAMPLE, highlight: { note: 'no id' } }).code).toBe('bridge.invalidParams');
    expect(host.fail('annotations.addTopic', { recordingId: SAMPLE, topic: { label: '  ' } }).code).toBe('bridge.invalidParams');
    expect(host.fail('annotations.addChapter', { recordingId: SAMPLE, chapter: { atMs: 1, origin: 'robot' } }).code).toBe('bridge.invalidParams');
    expect(host.fail('annotations.addTopic', { recordingId: 'gone', topic: { label: 'x' } }).code).toBe('project.notFound');
  });

  it('adds a topic label once, ignoring case', () => {
    const host = previewHost();
    const before = host.call('annotations.addTopic', { recordingId: SAMPLE, topic: { label: 'Hiring' } }).topics.length;
    expect(host.call('annotations.addTopic', { recordingId: SAMPLE, topic: { label: 'hiring' } }).topics).toHaveLength(before);
  });
});

describe('preview host errors', () => {
  it('uses the bridge. prefix for router codes and only documented codes', () => {
    const host = previewHost();
    expect(host.fail('nothing.here' as MethodName, {}).code).toBe('bridge.unknownMethod');
    expect(host.fail('project.rename', { recordingId: SAMPLE, title: ' ' }).code).toBe('bridge.invalidParams');
  });

  it('refuses a second recording with recording.alreadyActive', () => {
    const host = previewHost();
    const sourceIds = [host.call('sources.list', {}).audio[0]?.id ?? ''];
    const { sessionId } = host.call('recording.start', { title: 'One', type: 'meeting', sourceIds });
    const error = host.fail('recording.start', { title: 'Two', type: 'meeting', sourceIds });
    expect(error.code).toBe('recording.alreadyActive');
    expect(error.detail).toBe(sessionId);
    expect(ERROR_CODES).toContain(error.code);
  });

  it('reports the host footer reason while space is low', () => {
    expect(previewHost({ lowSpace: true }).call('status.get', {}).processingPaused).toBe('Low disk space');
    expect(previewHost().call('status.get', {}).processingPaused).toBeNull();
  });
});

