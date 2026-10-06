import { describe, expect, it } from 'vitest';
import { calendarDaysBetween, dateBucket, formatClock, formatRecordedAt, formatWhen, parseIso } from './when';

// All dates are built in local time so the tests read the same in every time zone.
const at = (y: number, m: number, d: number, h = 12, min = 0): Date => new Date(y, m - 1, d, h, min);

describe('formatClock', () => {
  it.each([
    [at(2026, 10, 6, 10, 0), '10:00 AM'],
    [at(2026, 10, 6, 14, 15), '2:15 PM'],
    [at(2026, 10, 6, 0, 5), '12:05 AM'],
    [at(2026, 10, 6, 12, 0), '12:00 PM'],
    [at(2026, 10, 6, 23, 59), '11:59 PM'],
  ])('%s reads %s', (date, expected) => {
    expect(formatClock(date)).toBe(expected);
  });
});

describe('date buckets', () => {
  // Tuesday 6 October 2026, mid-afternoon.
  const now = at(2026, 10, 6, 15, 30);

  it.each([
    [at(2026, 10, 6, 0, 1), 'Today'],
    [at(2026, 10, 6, 15, 0), 'Today'],
    [at(2026, 10, 5, 23, 59), 'Yesterday'],
    [at(2026, 10, 5, 0, 0), 'Yesterday'],
    [at(2026, 10, 4, 9, 0), 'Earlier this week'],
    [at(2026, 9, 30, 9, 0), 'Earlier this week'],
    [at(2026, 9, 29, 9, 0), 'September'],
    [at(2026, 8, 14, 9, 0), 'August'],
  ])('%s is %s', (date, label) => {
    expect(dateBucket(date, now).label).toBe(label);
  });

  it('treats a time slightly in the future as today rather than inventing a bucket', () => {
    expect(dateBucket(at(2026, 10, 6, 16, 0), now).label).toBe('Today');
  });

  it('keys month buckets by year and month so two Septembers never merge', () => {
    expect(dateBucket(at(2026, 9, 1), now).key).toBe('month:2026-09');
    expect(dateBucket(at(2025, 9, 1), now).key).toBe('month:2025-09');
  });

  it('adds the year to months of another year', () => {
    expect(dateBucket(at(2025, 12, 20), now).label).toBe('December 2025');
  });

  it('counts calendar days across a daylight-saving change', () => {
    // 29 March 2026 is the EU spring-forward day; whole-day arithmetic must still give 1.
    expect(calendarDaysBetween(at(2026, 3, 29, 0, 30), at(2026, 3, 30, 0, 30))).toBe(1);
    expect(calendarDaysBetween(at(2026, 10, 24, 23, 0), at(2026, 10, 25, 1, 0))).toBe(1);
  });
});

describe('{when} wording', () => {
  const now = at(2026, 10, 6, 15, 30);

  it('reads a time for today and yesterday', () => {
    expect(formatWhen(at(2026, 10, 6, 10, 0), now)).toBe('10:00 AM');
    expect(formatWhen(at(2026, 10, 5, 16, 0), now)).toBe('4:00 PM');
  });

  it('reads weekday and time within the week, up to six days back', () => {
    expect(formatWhen(at(2026, 10, 4, 11, 0), now)).toBe('Sun, 11:00 AM');
    expect(formatWhen(at(2026, 9, 30, 15, 30), now)).toBe('Wed, 3:30 PM');
  });

  it('switches to a date at the week boundary', () => {
    // Seven days back is the same weekday as today, so it must not read as a weekday.
    expect(formatWhen(at(2026, 9, 29, 9, 0), now)).toBe('Sep 29');
  });

  it('crosses the year change', () => {
    const newYear = at(2027, 1, 2, 9, 0);
    expect(dateBucket(at(2027, 1, 1, 22, 0), newYear).label).toBe('Yesterday');
    expect(formatWhen(at(2026, 12, 30, 16, 45), newYear)).toBe('Wed, 4:45 PM');
    expect(dateBucket(at(2026, 12, 30, 16, 45), newYear).label).toBe('Earlier this week');
    expect(formatWhen(at(2026, 12, 20, 9, 0), newYear)).toBe('Dec 20, 2026');
    expect(dateBucket(at(2026, 12, 20, 9, 0), newYear).label).toBe('December 2026');
  });

  it('words the processing card time', () => {
    expect(formatRecordedAt(at(2026, 10, 6, 10, 0), now)).toBe('recorded today at 10:00 AM');
    expect(formatRecordedAt(at(2026, 10, 5, 10, 0), now)).toBe('recorded yesterday at 10:00 AM');
    expect(formatRecordedAt(at(2026, 10, 1, 11, 0), now)).toBe('recorded Thu at 11:00 AM');
    expect(formatRecordedAt(at(2026, 9, 18, 11, 0), now)).toBe('recorded Sep 18');
  });

  it('reads ISO strings with an offset and never throws on a bad one', () => {
    expect(parseIso('2026-10-06T10:00:00+00:00').toISOString()).toBe('2026-10-06T10:00:00.000Z');
    expect(parseIso('not a date').getTime()).toBe(0);
  });
});
