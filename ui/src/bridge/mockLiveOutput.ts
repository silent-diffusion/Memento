// The browser-preview host's generation.output (BRIDGE.md, M4 Live output): the simulated job's
// exchange, so the Live output sheet can be tried without a model. The local model streams each reply
// in a few token events over a step; a cloud provider's reply arrives whole. Kept in memory only.
import type { EventName, EventPayload, GenerationOutput, GenerationOutputStep } from './types';

export interface MockLiveOutput {
  /** A step done in code (segment, reduce, grounding). */
  step(step: GenerationOutputStep, title: string, summary: string): void;
  /** A pass with its request and reply, played out over `withinMs` (tokens for the local model). */
  pass(step: 'map' | 'verify', title: string, request: string, reply: string, withinMs: number): void;
  /** The end of the output (before the job's final progress). */
  end(): void;
}

interface Environment {
  emit: <E extends EventName>(event: E, payload: EventPayload<E>) => void;
  jobId: string;
  streamed: boolean;
  /** The job is over (cancelled, failed or done): nothing more is sent. */
  finished: () => boolean;
}

const BLANK: Omit<GenerationOutput, 'jobId' | 'passId' | 'kind' | 'text'> = {
  step: null,
  title: null,
  streamed: null,
  outputTokens: null,
  promptTokens: null,
  tokensPerSecond: null,
  elapsedMs: null,
  stopReason: null,
};

/** Rough tokens of a text (four characters each), as the preview counts them. */
const tokensOf = (text: string): number => Math.max(1, Math.round(text.length / 4));

export function createMockLiveOutput(env: Environment): MockLiveOutput {
  let passes = 0;
  let ended = false;
  const timers: ReturnType<typeof setTimeout>[] = [];
  const send = (event: Omit<GenerationOutput, 'jobId'>): void => {
    if (!ended) {
      env.emit('generation.output', { jobId: env.jobId, ...event });
    }
  };
  return {
    step(step, title, summary) {
      send({ ...BLANK, passId: `p${String(++passes)}`, kind: 'step', step, title, text: summary, elapsedMs: 2 });
    },
    pass(step, title, request, reply, withinMs) {
      const passId = `p${String(++passes)}`;
      send({ ...BLANK, passId, kind: 'request', step, title, text: request, streamed: env.streamed });
      const finish = (): void => {
        if (!env.finished()) {
          send({ ...BLANK, passId, kind: 'reply', text: reply, outputTokens: tokensOf(reply), promptTokens: tokensOf(request), tokensPerSecond: env.streamed ? 48.5 : null, elapsedMs: withinMs, stopReason: env.streamed ? 'eog' : 'end_turn' });
        }
      };
      if (!env.streamed) {
        timers.push(setTimeout(finish, Math.round(withinMs * 0.8)));
        return;
      }
      // Six token events, then the reply.
      const pieces = 6;
      const size = Math.ceil(reply.length / pieces);
      for (let i = 0; i < pieces; i++) {
        const text = reply.slice(i * size, (i + 1) * size);
        timers.push(
          setTimeout(
            () => {
              if (!env.finished() && text !== '') {
                const sofar = Math.min(reply.length, (i + 1) * size);
                send({ ...BLANK, passId, kind: 'token', text, outputTokens: tokensOf(reply.slice(0, sofar)), tokensPerSecond: 48.5, elapsedMs: Math.round(((i + 1) * withinMs * 0.8) / (pieces + 1)) });
              }
            },
            Math.round(((i + 1) * withinMs * 0.8) / (pieces + 1)),
          ),
        );
      }
      timers.push(setTimeout(finish, Math.round(withinMs * 0.8)));
    },
    end() {
      if (!ended) {
        send({ ...BLANK, passId: '', kind: 'done', text: '' });
        ended = true;
        timers.forEach(clearTimeout);
      }
    },
  };
}
