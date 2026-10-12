import { currentFiscalYear } from './bs-fiscal-year';
import { nepalDayOf, nepalFirstOfMonth, nepalToday, shiftDay } from './nepal-time';

/**
 * Phase 89 -- the Nepal day at the boundary that used to be wrong: 18:15 to 24:00 UTC is already tomorrow in Kathmandu.
 * Asserted with a faked clock, so the result does not depend on the machine's time zone.
 */
describe('nepal-time', () => {
  afterEach(() => vi.useRealTimers());

  it("is already tomorrow's date after 18:15 UTC, and still today's before", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-09-30T18:20:00Z'));
    expect(nepalToday()).toBe('2026-10-01');

    vi.setSystemTime(new Date('2026-09-30T18:10:00Z'));
    expect(nepalToday()).toBe('2026-09-30');
  });

  it('opens a report on the first of the Nepal month, not the last day of the one before', () => {
    vi.useFakeTimers();
    // 00:05 on 1 October in Kathmandu.
    vi.setSystemTime(new Date('2026-09-30T18:20:00Z'));
    expect(nepalFirstOfMonth()).toBe('2026-10-01');

    // Midday on the 12th.
    vi.setSystemTime(new Date('2026-10-12T06:00:00Z'));
    expect(nepalFirstOfMonth()).toBe('2026-10-01');
  });

  it('names the Nepal day of any instant', () => {
    expect(nepalDayOf(new Date('2026-07-16T18:15:00Z'))).toBe('2026-07-17');
    expect(nepalDayOf(new Date('2026-07-16T18:14:59Z'))).toBe('2026-07-16');
  });

  it('moves a day by whole days on the calendar alone', () => {
    expect(shiftDay('2026-10-12', 30)).toBe('2026-11-11');
    expect(shiftDay('2026-03-01', -1)).toBe('2026-02-28');
  });

  it('starts the fiscal year at midnight in Kathmandu on Shrawan 1', () => {
    // Shrawan 1, 2083 is 2026-07-17; 00:05 there is 18:20 UTC on the 16th.
    expect(currentFiscalYear(new Date('2026-07-16T18:20:00Z'))).toBe(2083);
    expect(currentFiscalYear(new Date('2026-07-16T18:10:00Z'))).toBe(2082);
  });
});
