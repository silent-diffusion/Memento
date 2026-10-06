import { describe, expect, it } from 'vitest';
import { formatDuration, formatTotalDuration } from './duration';

const s = (seconds: number): number => seconds * 1000;

describe('formatDuration', () => {
  it.each([
    [0, '0:00'],
    [s(5), '0:05'],
    [s(59), '0:59'],
    [s(60), '1:00'],
    [s(401), '6:41'],
    [s(3599), '59:59'],
    [s(3600), '1:00:00'],
    [s(3734), '1:02:14'],
    [s(36_000 + 61), '10:01:01'],
  ])('%i ms reads %s', (ms, expected) => {
    expect(formatDuration(ms)).toBe(expected);
  });

  it('drops partial seconds instead of rounding up', () => {
    expect(formatDuration(59_999)).toBe('0:59');
  });

  it.each([-1, Number.NaN, Number.POSITIVE_INFINITY])('treats %s as zero', (ms) => {
    expect(formatDuration(ms)).toBe('0:00');
  });
});

describe('formatTotalDuration', () => {
  it.each([
    [0, '0 min'],
    [s(29), '0 min'],
    [s(30), '1 min'],
    [s(45 * 60), '45 min'],
    [s(2 * 3600), '2 h'],
    [s(8 * 3600 + 38 * 60), '8 h 38 min'],
    [s(8 * 3600 + 38 * 60 + 20), '8 h 38 min'],
    [s(3600 + 59 * 60 + 40), '2 h'],
  ])('%i ms reads %s', (ms, expected) => {
    expect(formatTotalDuration(ms)).toBe(expected);
  });

  it('sums the Main render sample to 8 h 38 min', () => {
    const sample = [3734, 2910, 4202, 401, 5325, 1748, 862, 3130, 2465, 6330];
    const total = sample.reduce((sum, seconds) => sum + s(seconds), 0);
    expect(formatTotalDuration(total)).toBe('8 h 38 min');
  });
});
