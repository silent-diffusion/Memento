// The browser-preview host's Ask (2.9.0): a conversation per recording kept in memory, answers streamed as
// ask.progress from a canned reply that cites the recording's first lines, and the host's rules: the provider must be
// ready, external AI on for one that leaves the PC, "Ask before every send" holds such a question until ask.confirm.
// A transcript of more than 30 lines is answered from "sections" so the disclosure has something to show. Nothing here
// makes a network request.
import { MockHostError } from './mockSession';
import type { AskContextInfo, AskConversationResult, AskSendResult, AskTurn, EmptyResult, EventName, EventPayload, MethodParams, ProviderId, ProviderInfo, SettingsSnapshot } from './types';

export interface MockAskSegment {
  id: string;
  start: number;
  end: number;
  text: string;
}

export interface MockAskEnvironment {
  emit: <E extends EventName>(event: E, payload: EventPayload<E>) => void;
  now: () => number;
  /** Throws project.notFound for an unknown recording. */
  find: (recordingId: string) => { title: string };
  segments: (recordingId: string) => MockAskSegment[];
  providers: () => ProviderInfo[];
  settings: () => SettingsSnapshot;
  stepMs: number;
}

export interface MockAskHandlers {
  'ask.get': (params: MethodParams<'ask.get'>) => AskConversationResult;
  'ask.send': (params: MethodParams<'ask.send'>) => AskSendResult;
  'ask.confirm': (params: MethodParams<'ask.confirm'>) => EmptyResult;
  'ask.cancel': (params: MethodParams<'ask.cancel'>) => EmptyResult;
  'ask.clear': (params: MethodParams<'ask.clear'>) => AskConversationResult;
}

interface Job {
  jobId: string;
  recordingId: string;
  provider: ProviderInfo;
  question: AskTurn;
  context: AskContextInfo;
  pending: boolean;
  finished: boolean;
  received: string;
  timers: ReturnType<typeof setTimeout>[];
}

export const MOCK_ASK_MAX_QUESTION = 4000;

export function createMockAsk(env: MockAskEnvironment): { handlers: MockAskHandlers } {
  const conversations = new Map<string, AskTurn[]>();
  let job: Job | null = null;
  let counter = 0;
  const turnsOf = (recordingId: string): AskTurn[] => {
    let turns = conversations.get(recordingId);
    if (turns === undefined) {
      turns = [];
      conversations.set(recordingId, turns);
    }
    return turns;
  };
  const nextId = (turns: AskTurn[]): string => `t${turns.length + 1}`;
  const busy = (): boolean => job !== null && !job.finished;
  const iso = (): string => new Date(env.now()).toISOString();

  const contextFor = (recordingId: string, question: string): AskContextInfo => {
    const segments = env.segments(recordingId);
    if (segments.length <= 30) {
      return { mode: 'whole', sections: [], sectionCount: 0, totalLines: segments.length, inputs: ['Recording details', 'Participants'], anyMatched: true, tokens: segments.length * 30 };
    }
    const words = question.toLowerCase().split(/[^a-z0-9]+/).filter((w) => w.length > 3);
    const size = 10;
    const count = Math.ceil(segments.length / size);
    const hit = segments.findIndex((s) => words.some((w) => s.text.toLowerCase().includes(w)));
    const best = hit < 0 ? 0 : Math.floor(hit / size);
    const picked = [best - 1, best, best + 1].filter((i) => i >= 0 && i < count);
    return {
      mode: 'sections',
      sections: picked.map((i) => {
        const first = segments[i * size];
        const last = segments[Math.min(segments.length, (i + 1) * size) - 1];
        return { number: i + 1, firstLine: i * size + 1, lastLine: Math.min(segments.length, (i + 1) * size), start: first?.start ?? 0, end: last?.end ?? 0, title: null, matched: i === best && hit >= 0 };
      }),
      sectionCount: count,
      totalLines: segments.length,
      inputs: ['Recording details'],
      anyMatched: hit >= 0,
      tokens: picked.length * size * 30,
    };
  };

  const finish = (current: Job, kind: 'done' | 'cancelled' | 'failed', text: string, turn: AskTurn | null): void => {
    current.finished = true;
    current.timers.forEach((t) => {
      clearTimeout(t);
    });
    env.emit('ask.progress', { jobId: current.jobId, recordingId: current.recordingId, kind, text, context: null, turn, code: null });
  };

  const answerTurn = (current: Job, text: string, stopped: boolean): AskTurn => {
    const segments = env.segments(current.recordingId);
    const citations = [...text.matchAll(/\[(\d+)\]/g)]
      .map((m) => Number(m[1]))
      .filter((line, i, all) => all.indexOf(line) === i)
      .flatMap((line) => {
        const seg = segments[line - 1];
        return seg === undefined ? [] : [{ line, segmentId: seg.id, start: seg.start }];
      });
    return {
      id: nextId(turnsOf(current.recordingId)),
      role: 'assistant',
      text,
      at: iso(),
      providerId: current.provider.id,
      providerName: current.provider.name,
      modelLabel: current.provider.modelLabel,
      stayedOnPc: !current.provider.leavesPc,
      citations,
      context: current.context,
      stopped: stopped ? true : null,
    };
  };

  const run = (current: Job): void => {
    turnsOf(current.recordingId).push(current.question);
    env.emit('ask.progress', { jobId: current.jobId, recordingId: current.recordingId, kind: 'context', text: '', context: current.context, turn: null, code: null });
    const first = current.context.mode === 'whole' ? 1 : (current.context.sections[0]?.firstLine ?? 1);
    const pieces = [
      'From what was said, ',
      `the team agreed to move the launch by a week [${first}]`,
      ` and to check the numbers again before Friday [${first + 1}].`,
      ' Nothing in the transcript says who will tell the customers.',
    ];
    pieces.forEach((piece, index) => {
      current.timers.push(
        setTimeout(
          () => {
            if (current.finished) {
              return;
            }
            current.received += piece;
            env.emit('ask.progress', { jobId: current.jobId, recordingId: current.recordingId, kind: 'token', text: piece, context: null, turn: null, code: null });
            if (index === pieces.length - 1) {
              const turn = answerTurn(current, current.received.trim(), false);
              turnsOf(current.recordingId).push(turn);
              finish(current, 'done', '', turn);
            }
          },
          env.stepMs * (index + 1),
        ),
      );
    });
  };

  const handlers: MockAskHandlers = {
    'ask.get': (params) => {
      env.find(params.recordingId);
      return { recordingId: params.recordingId, turns: [...turnsOf(params.recordingId)], jobId: job !== null && busy() && job.recordingId === params.recordingId ? job.jobId : null };
    },
    'ask.send': (params) => {
      const project = env.find(params.recordingId);
      const question = params.question.trim();
      if (question === '') {
        throw new MockHostError('bridge.invalidParams', 'Type a question first. Nothing was sent.', 'question');
      }
      if (question.length > MOCK_ASK_MAX_QUESTION) {
        throw new MockHostError('bridge.invalidParams', `The question is ${question.length.toLocaleString('en-US')} characters long; Ask takes up to 4,000. Nothing was sent. Shorten it, or ask in two parts.`, 'question');
      }
      const settings = env.settings();
      const id: ProviderId = params.providerId ?? settings.ai.defaultProviderId ?? 'local';
      const provider = env.providers().find((p) => p.id === id);
      if (provider === undefined) {
        throw new MockHostError('bridge.invalidParams', `There is no AI provider called "${id}". Nothing was sent. Choose one from the list.`, id);
      }
      if (provider.leavesPc && !settings.ai.enabled) {
        throw new MockHostError('ai.disabled', `${provider.name} is an external service, and external AI is off, so nothing was sent. Turn on "Allow external AI services" in Settings › AI and privacy, or ask the local model.`, provider.name);
      }
      if (busy()) {
        throw new MockHostError('ask.busy', 'Another question is still being answered. Nothing was sent. Wait for the answer, or stop it, then ask again.');
      }
      if (env.segments(params.recordingId).length === 0) {
        throw new MockHostError('ask.noTranscript', `"${project.title}" has no transcript yet, so there is nothing to answer from. Nothing was sent. Wait until transcription finishes, then ask again.`);
      }
      if (!provider.ready) {
        throw new MockHostError('ai.providerNotReady', provider.detail ?? `${provider.name} is not ready, so nothing was sent.`, provider.code);
      }
      const turns = turnsOf(params.recordingId);
      counter += 1;
      const current: Job = {
        jobId: `a${counter}`,
        recordingId: params.recordingId,
        provider,
        question: { id: nextId(turns), role: 'user', text: question, at: iso() },
        context: contextFor(params.recordingId, question),
        pending: provider.leavesPc && settings.ai.askBeforeSend,
        finished: false,
        received: '',
        timers: [],
      };
      job = current;
      if (current.pending) {
        return {
          jobId: current.jobId,
          question: current.question,
          confirmationRequired: true,
          summary: { providerId: provider.id, providerName: provider.name, modelLabel: provider.modelLabel, context: current.context, bytes: 2000 + current.context.tokens * 4 },
        };
      }
      run(current);
      return { jobId: current.jobId, question: current.question, confirmationRequired: false, summary: null };
    },
    'ask.confirm': (params) => {
      const current = job;
      if (current?.jobId !== params.jobId || !current.pending || current.finished) {
        throw new MockHostError('ask.notFound', 'That question is no longer being answered; it may have finished or been stopped. Nothing was changed.', params.jobId);
      }
      current.pending = false;
      if (params.approved) {
        run(current);
      } else {
        finish(current, 'cancelled', 'Nothing was sent.', null);
      }
      return {};
    },
    'ask.cancel': (params) => {
      const current = job;
      if (current?.jobId !== params.jobId || current.finished) {
        throw new MockHostError('ask.notFound', 'That question is no longer being answered; it may have finished or been stopped. Nothing was changed.', params.jobId);
      }
      if (current.pending || current.received.trim() === '') {
        finish(current, 'cancelled', current.pending ? 'Nothing was sent.' : 'Stopped before any answer arrived. Nothing was saved.', null);
        return {};
      }
      const turn = answerTurn(current, current.received.trim(), true);
      turnsOf(current.recordingId).push(turn);
      finish(current, 'cancelled', 'Stopped. The part of the answer that had arrived was saved.', turn);
      return {};
    },
    'ask.clear': (params) => {
      env.find(params.recordingId);
      if (job !== null && busy() && job.recordingId === params.recordingId) {
        throw new MockHostError('ask.busy', 'A question about this recording is still being answered, so the conversation was not cleared. Stop the answer first, then clear.');
      }
      conversations.set(params.recordingId, []);
      return { recordingId: params.recordingId, turns: [], jobId: null };
    },
  };
  return { handlers };
}
