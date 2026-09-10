import { DATE_RANGE_PRESETS, fiscalYearBounds, rangeFor } from './date-range.service';
import { FISCAL_YEAR_START_MONTH, currentFiscalYear } from '../formatting/bs-fiscal-year';
import { adToBs, bsToAd } from '../formatting/bs-date';

/**
 * Phase 34b — the global date range's arithmetic.
 *
 * The presets are the reference product's own, read off its live popover on 2026-09-10 along with
 * the bounds each one resolved to. Two of those observations are pinned here as fixtures, because
 * they are the only external evidence of what "This Fiscal Year to Date" is supposed to mean.
 */
describe('date range presets', () => {
  // Today, on the tenant that was read: it showed "This Fiscal Year to Date" as 17-07-2026 to
  // 10-09-2026, and "Last 7 days" as 03-09-2026 to 10-09-2026.
  const observedToday = new Date('2026-09-10T00:00:00Z');

  it('offers the eight named presets, in the order the reference product lists them', () => {
    expect(DATE_RANGE_PRESETS.map((p) => p.label)).toEqual([
      'Today',
      'Last 7 days',
      'Last 15 days',
      'Last 30 days',
      'Last 45 days',
      'Last 60 days',
      'This Fiscal Year to Date',
      'Fiscal Year',
    ]);
  });

  it('resolves Last 7 days exactly as the live control did', () => {
    const range = rangeFor('last-7', observedToday);

    expect(range.from).toBe('2026-09-03');
    expect(range.to).toBe('2026-09-10');
  });

  it('resolves This Fiscal Year to Date exactly as the live control did', () => {
    const range = rangeFor('fiscal-year-to-date', observedToday);

    // Shrawan 1 of BS 2083 is 17 July 2026 — which is what the live tenant's TOP_DATE_FROM held.
    expect(range.from).toBe('2026-07-17');
    expect(range.to).toBe('2026-09-10');
  });

  it('makes Today a single day rather than an empty range', () => {
    const range = rangeFor('today', observedToday);

    expect(range.from).toBe('2026-09-10');
    expect(range.to).toBe('2026-09-10');
  });

  it('never produces a range whose end precedes its start', () => {
    for (const { preset } of DATE_RANGE_PRESETS) {
      const range = rangeFor(preset, observedToday);

      // The server's validator refuses an inverted range, and a range that silently returned
      // nothing on every list in the app would read as data loss rather than as a filter.
      expect(range.from <= range.to).toBe(true);
    }
  });

  it('labels every preset', () => {
    for (const { preset, label } of DATE_RANGE_PRESETS) {
      expect(rangeFor(preset, observedToday).label).toBe(label);
    }
  });
});

/**
 * A Nepali fiscal year runs Shrawan 1 to the last day of Asar, so it spans two BS years and its
 * length in days is not constant — phase-26b's rule that both boundaries must stay pinned, and that
 * the BS table is ported rather than retyped.
 */
describe('fiscal year bounds', () => {
  it('starts on Shrawan 1 and ends the day before the next Shrawan 1', () => {
    const year = currentFiscalYear(new Date('2026-09-10T00:00:00Z'));
    const bounds = fiscalYearBounds(year);

    expect(bounds.from).toBe(bsToAd({ year, month: FISCAL_YEAR_START_MONTH, day: 1 }));

    const dayAfter = new Date(`${bounds.to}T00:00:00Z`);
    dayAfter.setUTCDate(dayAfter.getUTCDate() + 1);

    expect(dayAfter.toISOString().slice(0, 10)).toBe(
      bsToAd({ year: year + 1, month: FISCAL_YEAR_START_MONTH, day: 1 }),
    );
  });

  it('starts in Shrawan and ends in Asar, read back through the BS calendar', () => {
    const bounds = fiscalYearBounds(2083);

    expect(adToBs(bounds.from)).toEqual({ year: 2083, month: 4, day: 1 });
    expect(adToBs(bounds.to)?.month).toBe(3); // Asar
    expect(adToBs(bounds.to)?.year).toBe(2084);
  });

  it('is not simply 365 days, because BS month lengths vary', () => {
    const lengths = [2081, 2082, 2083].map((year) => {
      const bounds = fiscalYearBounds(year);
      const from = new Date(`${bounds.from}T00:00:00Z`).getTime();
      const to = new Date(`${bounds.to}T00:00:00Z`).getTime();
      return Math.round((to - from) / 86_400_000) + 1;
    });

    for (const length of lengths) {
      expect(length).toBeGreaterThanOrEqual(364);
      expect(length).toBeLessThanOrEqual(366);
    }
  });
});
