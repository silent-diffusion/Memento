// Ask (2.9.0): turning an answer's text into words and citation chips, and the sentences that say what an answer was
// given. Pure functions, no DOM.
import { formatRuler } from '../../format/duration';
import type { AskCitation, AskContextInfo, ProviderInfo } from '../../bridge/types';

export type AnswerPart = { kind: 'text'; text: string } | { kind: 'cite'; line: number; citation: AskCitation | null };

/**
 * The answer as text and citations. `[12]`, `[12, 15]` and `[12-14]` become one chip per line; a line the host did not
 * keep as a citation (it was not in what was sent) stays a chip without a target, so it reads as unverified.
 */
export function answerParts(text: string, citations: readonly AskCitation[] | null | undefined): AnswerPart[] {
  const byLine = new Map((citations ?? []).map((c) => [c.line, c]));
  const parts: AnswerPart[] = [];
  const pattern = /\[(\d{1,6}(?:\s*[-–]\s*\d{1,6})?(?:\s*,\s*\d{1,6}(?:\s*[-–]\s*\d{1,6})?)*)\]/g;
  let last = 0;
  for (const match of text.matchAll(pattern)) {
    const index = match.index;
    if (index > last) {
      parts.push({ kind: 'text', text: text.slice(last, index) });
    }
    for (const piece of (match[1] ?? '').split(',')) {
      const [fromText, toText] = piece.split(/[-–]/).map((s) => s.trim());
      const from = Number(fromText);
      const to = toText === undefined ? from : Number(toText);
      const end = Number.isFinite(to) && to >= from && to - from <= 20 ? to : from;
      for (let line = from; line <= end; line++) {
        parts.push({ kind: 'cite', line, citation: byLine.get(line) ?? null });
      }
    }
    last = index + match[0].length;
  }
  if (last < text.length) {
    parts.push({ kind: 'text', text: text.slice(last) });
  }
  return parts;
}

/** The chip's label: the line's time in the recording. */
export function citationLabel(citation: AskCitation): string {
  return formatRuler(citation.start * 1000);
}

/** The disclosure's summary: "Answered from the whole recording" or "Answered from 3 of 40 sections". */
export function contextSummary(context: AskContextInfo): string {
  if (context.mode === 'whole') {
    return `Answered from the whole recording (${context.totalLines.toLocaleString('en-US')} lines)`;
  }
  const n = context.sections.length;
  return `Answered from ${n} of ${context.sectionCount} ${context.sectionCount === 1 ? 'section' : 'sections'} of the transcript`;
}

/** Why only some sections were used, and how they were picked. */
export function contextExplanation(context: AskContextInfo): string {
  if (context.mode === 'whole') {
    return context.inputs.length === 0 ? 'The whole transcript was sent.' : `The whole transcript was sent with ${joinWords(context.inputs)}.`;
  }
  const picked = context.anyMatched
    ? 'Memento searched the transcript on this PC for the words of the question and sent the best matching sections with the lines around them.'
    : 'No part of the transcript shared words with the question, so the opening sections were sent. Try naming a person, a topic or a word that was said.';
  const extra = context.inputs.length === 0 ? '' : ` ${joinWords(context.inputs)} went along too.`;
  return `The recording is too long to send whole to this model. ${picked}${extra}`;
}

/** "Lines 12–34 · 01:02–03:40". */
export function sectionLabel(section: AskContextInfo['sections'][number]): string {
  return `Lines ${section.firstLine}–${section.lastLine} · ${formatRuler(section.start * 1000)}–${formatRuler(section.end * 1000)}`;
}

/** Where a question goes with this provider, before it is sent. */
export function destinationNote(provider: Pick<ProviderInfo, 'name' | 'leavesPc' | 'kind'>): string {
  if (!provider.leavesPc) {
    return provider.kind === 'server' ? `${provider.name} runs on this PC: nothing leaves it.` : 'The local model answers on this PC: nothing leaves it.';
  }
  return `Questions and the transcript go to ${provider.name} when you press Ask. Audio and video are never sent.`;
}

/** The provider the page starts with: the Settings default when it is ready, else the first ready one (local first). */
export function initialProvider(providers: readonly ProviderInfo[], defaultId: ProviderInfo['id'] | null): ProviderInfo | null {
  const ready = providers.filter((p) => p.ready);
  const preferred = ready.find((p) => p.id === defaultId);
  return preferred ?? ready.find((p) => p.id === 'local') ?? ready[0] ?? null;
}

function joinWords(words: readonly string[]): string {
  const lower = words.map((w, i) => (i === 0 ? w : w.charAt(0).toLowerCase() + w.slice(1)));
  return lower.length <= 1 ? (lower[0] ?? '') : `${lower.slice(0, -1).join(', ')} and ${lower[lower.length - 1] ?? ''}`;
}
