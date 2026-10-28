import { describe, expect, it } from 'vitest';
import type { AskContextInfo, ProviderInfo } from '../../bridge/types';
import { answerParts, contextExplanation, contextSummary, destinationNote, initialProvider, sectionLabel } from './askText';

const cite = (line: number, start: number): { line: number; segmentId: string; start: number } => ({ line, segmentId: `s${line}`, start });

describe('Ask answer text', () => {
  it('turns [n], lists and short ranges into one chip per line and keeps the words between', () => {
    const parts = answerParts('Ana agreed [12]. Ben too [13, 15-16].', [cite(12, 62), cite(13, 70), cite(15, 80), cite(16, 90)]);
    expect(parts.map((p) => (p.kind === 'text' ? p.text : `#${p.line}`))).toEqual(['Ana agreed ', '#12', '. Ben too ', '#13', '#15', '#16', '.']);
  });

  it('keeps a cited line the host did not confirm as a chip without a target', () => {
    const [, chip] = answerParts('Said [99].', [cite(12, 1)]);
    expect(chip).toEqual({ kind: 'cite', line: 99, citation: null });
  });

  it('leaves text without citations whole', () => {
    expect(answerParts('No citations here.', null)).toEqual([{ kind: 'text', text: 'No citations here.' }]);
  });
});

describe('what an answer was given', () => {
  const sections: AskContextInfo = {
    mode: 'sections',
    sections: [{ number: 4, firstLine: 31, lastLine: 40, start: 302, end: 401, title: 'Pricing', matched: true }],
    sectionCount: 40,
    totalLines: 400,
    inputs: ['Recording details', 'Agenda'],
    anyMatched: true,
    tokens: 900,
  };

  it('names the whole recording or the share of sections', () => {
    expect(contextSummary({ ...sections, mode: 'whole', sections: [], sectionCount: 0 })).toBe('Answered from the whole recording (400 lines)');
    expect(contextSummary(sections)).toBe('Answered from 1 of 40 sections of the transcript');
    expect(sections.sections.map(sectionLabel)).toEqual(['Lines 31–40 · 05:02–06:41']);
  });

  it('explains how sections were picked, and what to do when nothing matched', () => {
    expect(contextExplanation(sections)).toBe(
      'The recording is too long to send whole to this model. Memento searched the transcript on this PC for the words of the question and sent the best matching sections with the lines around them. Recording details and agenda went along too.',
    );
    expect(contextExplanation({ ...sections, anyMatched: false })).toContain('Try naming a person, a topic or a word that was said.');
    expect(contextExplanation({ ...sections, mode: 'whole', inputs: [] })).toBe('The whole transcript was sent.');
  });
});

describe('providers', () => {
  const provider = (id: ProviderInfo['id'], ready: boolean, leavesPc: boolean): ProviderInfo =>
    ({ id, name: id, kind: id === 'local' ? 'local' : id === 'server' ? 'server' : 'cloud', ready, leavesPc }) as ProviderInfo;

  it('starts with the ready default, else the ready local model, else the first ready one', () => {
    const list = [provider('anthropic', true, true), provider('local', true, false), provider('server', false, false)];
    expect(initialProvider(list, 'anthropic')?.id).toBe('anthropic');
    expect(initialProvider(list, 'server')?.id).toBe('local');
    expect(initialProvider([provider('openai', true, true)], null)?.id).toBe('openai');
    expect(initialProvider([provider('local', false, false)], null)).toBeNull();
  });

  it('says where a question goes before it is sent', () => {
    expect(destinationNote(provider('local', true, false))).toBe('The local model answers on this PC: nothing leaves it.');
    expect(destinationNote({ ...provider('server', true, false), name: 'Your own server' })).toBe('Your own server runs on this PC: nothing leaves it.');
    expect(destinationNote({ ...provider('anthropic', true, true), name: 'Claude' })).toBe('Questions and the transcript go to Claude when you press Ask. Audio and video are never sent.');
  });
});
