import { render } from 'preact';
import { afterEach, describe, expect, it } from 'vitest';
import type { DocumentSummary, GenerationRecord } from '../../bridge/types';
import { HowMade } from './SideColumn';

const summary: DocumentSummary = {
  id: 'd1',
  name: 'Minutes',
  kind: 'generated',
  templateName: 'Meeting minutes',
  styleId: 'corporate',
  providerId: 'local',
  generatedAt: '2026-10-21T10:00:00+01:00',
  version: 1,
  versions: 1,
  modifiedAt: '2026-10-21T10:00:00+01:00',
  sizeBytes: 4096,
};

const record = (placement: string | null, providerId: GenerationRecord['providerId'] = 'local', modelLabel = 'Qwen3.5 35B-A3B · graphics card and RAM'): GenerationRecord => ({
  templateId: 'meeting-minutes',
  templateName: 'Meeting minutes',
  styleId: 'corporate',
  providerId,
  modelLabel,
  startedAt: '2026-10-21T10:00:00+01:00',
  durationMs: 1_200_000,
  inputs: { transcript: true, details: true, participants: true, agenda: true, highlights: false, attachments: false, previousDocuments: false },
  payloadHash: 'h',
  payloadKept: false,
  chunks: 2,
  modules: [],
  sent: [],
  bytes: 1000,
  stayedOnPc: providerId === 'local',
  claims: [],
  payloadText: null,
  placement,
});

/** 2.2: How this was made names where a larger local model ran, how fast and how much RAM it took (DESIGN.md §11, §12). */
describe('How this was made: the larger model placement', () => {
  const host = document.createElement('div');

  afterEach(() => {
    render(null, host);
  });

  const facts = (r: GenerationRecord): string[][] => {
    render(<HowMade summary={summary} record={r} styleName="Corporate" now={new Date('2026-10-21T12:00:00+01:00')} onRegenerate={null} />, host);
    const dts = [...host.querySelectorAll('.how-facts dt')].map((d) => d.textContent);
    const dds = [...host.querySelectorAll('.how-facts dd')].map((d) => d.textContent);
    return dts.map((d, i) => [d, dds[i] ?? '']);
  };

  it('adds a Placement row after the provider for a larger local model', () => {
    const rows = facts(record('Ran on the graphics card with its experts in RAM · 12.3 tokens/s · 19 GB of RAM'));
    expect(rows.map((r) => r[0])).toEqual(['Template', 'Style', 'Provider', 'Placement', 'Generated']);
    expect(rows[2]?.[1]).toBe('Qwen3.5 35B-A3B · graphics card and RAM (this PC)');
    expect(rows[3]?.[1]).toBe('Ran on the graphics card with its experts in RAM · 12.3 tokens/s · 19 GB of RAM');
  });

  it('has no Placement row for the small models or a cloud provider', () => {
    expect(facts(record(null, 'local', 'Qwen3.5 4B · graphics card')).map((r) => r[0])).not.toContain('Placement');
    expect(facts(record(null, 'anthropic', 'claude-opus-5-5')).map((r) => r[0])).not.toContain('Placement');
  });
});
