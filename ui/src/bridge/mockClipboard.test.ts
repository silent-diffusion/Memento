import { describe, expect, it } from 'vitest';
import { createBridgeClient } from './client';
import { mockTranscriptText } from './mockClipboard';
import type { Transcript } from './types';

const quiet = { info: () => undefined, warn: () => undefined };
const DESIGN_REVIEW = '20261005-160000-dsrev';

const transcript = {
  speakers: [
    { id: 'spk1', name: 'Speaker 1', renamed: false, color: 1, talkTimeMs: 0 },
    { id: 'spk2', name: 'Speaker 2', renamed: false, color: 2, talkTimeMs: 0 },
  ],
  segments: [
    { id: 's1', start: 0.4, end: 2, speaker: 'spk1', text: 'Hello  there.' },
    { id: 's2', start: 3, end: 4, speaker: 'spk1', text: 'Second line.' },
    { id: 's3', start: 65, end: 66, speaker: 'spk2', text: 'Reply [x].' },
    { id: 's5', start: 70.9, end: 71, speaker: null, text: 'No speaker.' },
  ],
} as unknown as Transcript;
const summary = { title: 'Weekly sync', createdAt: '2026-10-06T10:00:00+01:00', durationMs: 3_733_000 };

describe('browser-preview clipboard (mirrors TranscriptText in the host)', () => {
  it('writes the host’s defaults: Markdown in speaker turns, text a line per segment', () => {
    expect(mockTranscriptText('markdown', transcript, summary).text).toBe(
      '# Weekly sync\r\n\r\n2026-10-06 10:00 · 1:02:13 · Speakers: Speaker 1, Speaker 2\r\n\r\n**Speaker 1:** [0:00:00] Hello there. [0:00:03] Second line.\r\n\r\n**Speaker 2:** [0:01:05] Reply \\[x\\].\r\n\r\n[0:01:10] No speaker.\r\n',
    );
    expect(mockTranscriptText('text', transcript, summary).text).toBe(
      'Weekly sync\r\n2026-10-06 10:00 · 1:02:13 · Speakers: Speaker 1, Speaker 2\r\n[0:00:00] Speaker 1: Hello there.\r\n[0:00:03] Speaker 1: Second line.\r\n[0:01:05] Speaker 2: Reply [x].\r\n[0:01:10] No speaker.\r\n',
    );
  });

  it('leaves out timestamps and speakers, in turns', () => {
    expect(mockTranscriptText('text', transcript, summary, { timestamps: false, speakers: false, layout: 'turns' }).text).toBe(
      'Weekly sync\r\n2026-10-06 10:00 · 1:02:13\r\n\r\nHello there. Second line.\r\n\r\nReply [x].\r\n\r\nNo speaker.\r\n',
    );
  });

  it('answers transcript.copy for a filtered view and refuses while the clipboard is busy', async () => {
    const client = createBridgeClient({ logger: quiet, mock: { live: false, recovery: false } });
    const { transcript: sample } = await client.call('transcript.get', { recordingId: DESIGN_REVIEW });
    const ids = (sample?.segments ?? []).slice(0, 2).map((s) => s.id);

    const result = await client.call('transcript.copy', { recordingId: DESIGN_REVIEW, format: 'text', segmentIds: ids });

    expect(result.lines).toBe(2);
    expect(result.totalLines).toBe(sample?.segments.length);
    const busy = createBridgeClient({ logger: quiet, mock: { live: false, recovery: false, clipboardBusy: true } });
    await expect(busy.call('transcript.copy', { recordingId: DESIGN_REVIEW, format: 'text' })).rejects.toMatchObject({ code: 'clipboard.unavailable' });
    await expect(client.call('transcript.copy', { recordingId: DESIGN_REVIEW, format: 'text', segmentIds: [] })).rejects.toMatchObject({ code: 'bridge.invalidParams' });
  });
});
