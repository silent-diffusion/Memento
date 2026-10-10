import { describe, expect, it } from 'vitest';
import { chapterTitleFromLine } from './reviewActions20';
import { firstName, matchWording } from './ReviewSuggestions';

describe('Review 2.0 wording', () => {
  it('words a match as the design does', () => {
    expect(matchWording({ speakerId: 'sp2', voiceId: 'v1', name: 'Priya Natarajan', similarity: 0.912, recordings: 4 })).toBe('91% match · 4 past recordings');
    expect(matchWording({ speakerId: 'sp2', voiceId: 'v1', name: 'Priya', similarity: 0.6, recordings: 1 })).toBe('60% match · 1 past recording');
    expect(firstName('Priya Natarajan')).toBe('Priya');
  });

  it('titles a chapter started at a line with its first words', () => {
    expect(chapterTitleFromLine('So the budget for next year is the first thing, I think.')).toBe('So the budget for next year');
    expect(chapterTitleFromLine('Agreed.')).toBe('Agreed');
  });
});
