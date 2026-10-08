import { describe, expect, it } from 'vitest';
import type { GenerationProgress, ProviderInfo, Template } from '../../bridge/types';
import { builtInTemplates } from '../../bridge/mockTemplates';
import { failureLead, failureWords, NOTHING_TWICE } from './generation';

const claude: ProviderInfo = { id: 'anthropic', name: 'Claude', vendor: 'Anthropic', kind: 'cloud', ready: true, reason: null, modelLabel: 'claude-opus-5-5', code: null, detail: null, modelId: null, gpuMemory: null, gpuNote: null };
const local: ProviderInfo = { id: 'local', name: 'Local model', vendor: 'This PC', kind: 'local', ready: true, reason: null, modelLabel: 'Qwen3.5 4B', code: null, detail: null, modelId: 'qwen3.5-4b-q4', gpuMemory: null, gpuNote: null };
function minutes(): Template {
  const found = builtInTemplates().find((t) => t.id === 'meeting-minutes');
  if (found === undefined) throw new Error('The mock has no Meeting minutes template.');
  return found;
}
const template = minutes();

const failed = (code: string | null): GenerationProgress => ({ jobId: 'g1', recordingId: 'r1', documentId: null, stage: 'failed', moduleId: null, percent: 0, message: 'x', code });

describe('the provider failure card (DESIGN.md §17)', () => {
  it('names the provider and what happened', () => {
    expect(failureLead({ template, provider: claude, failureCode: null, progress: failed('ai.invalidKey') })).toBe('Claude did not accept the key');
    expect(failureLead({ template, provider: claude, failureCode: null, progress: failed('ai.network') })).toBe('Claude could not be reached');
    expect(failureLead({ template, provider: local, failureCode: null, progress: failed('ai.notEnoughVram') })).toBe('The local model ran out of video memory');
    expect(failureLead({ template, provider: claude, failureCode: null, progress: failed(null) })).toBe("Claude didn't respond");
    expect(failureLead({ template, provider: claude, failureCode: 'ai.disabled', progress: null })).toBe('The meeting minutes could not be started');
  });

  it('says what is safe once', () => {
    expect(failureWords('Claude did not accept the saved API key.')).toBe(`Claude did not accept the saved API key. ${NOTHING_TWICE}`);
    const local = 'The graphics card ran out of its own memory. Nothing left this PC and no document was changed. Close apps that use the graphics card.';
    expect(failureWords(local)).toBe(local);
  });
});
