import { FIRST_BS_YEAR, LAST_BS_YEAR, bsToAd } from './bs-date';
import {
  FISCAL_YEAR_START_MONTH,
  currentFiscalYear,
  fiscalYearLabel,
  fiscalYearOf,
  supportedFiscalYears,
} from './bs-fiscal-year';

/**
 * The client half of phase 26b's fiscal-year handling. The month-length table itself is covered by
 * `bs-date.spec.ts`; what matters here is the *boundary* -- a Nepali fiscal year opens on Shrawan 1,
 * and getting that one day wrong would file a sale under the wrong year in five reports.
 *
 * The server's `BsCalendarTests.FiscalYearOf_splits_on_Shrawan_one` asserts the same boundary from
 * the other side.
 */
describe('bs-fiscal-year', () => {
  it('opens the fiscal year on Shrawan, the fourth BS month', () => {
    expect(FISCAL_YEAR_START_MONTH).toBe(4);
  });

  it('splits on Shrawan 1: the last day of Asar belongs to the previous fiscal year', () => {
    // Asar 2083 has 30 days in this table; its last day closes fiscal year 2082-2083.
    const lastDayOfAsar = bsToAd({ year: 2083, month: 3, day: 30 })!;
    const firstDayOfShrawan = bsToAd({ year: 2083, month: 4, day: 1 })!;

    expect(lastDayOfAsar).not.toBeNull();
    expect(fiscalYearOf(lastDayOfAsar)).toBe(2082);
    expect(fiscalYearOf(firstDayOfShrawan)).toBe(2083);
  });

  it('keeps a date late in the fiscal year on the opening BS year', () => {
    // Baisakh 2084 is nine months into fiscal year 2083-2084, on the far side of the BS new year.
    expect(fiscalYearOf(bsToAd({ year: 2084, month: 1, day: 1 })!)).toBe(2083);
    expect(fiscalYearOf(bsToAd({ year: 2084, month: 3, day: 1 })!)).toBe(2083);
    expect(fiscalYearOf(bsToAd({ year: 2084, month: 4, day: 1 })!)).toBe(2084);
  });

  it('returns null for a date outside the supported BS range rather than guessing', () => {
    expect(fiscalYearOf('1900-01-01')).toBeNull();
    expect(fiscalYearOf('2100-01-01')).toBeNull();
    expect(fiscalYearOf('not-a-date')).toBeNull();
  });

  it('lists every expressible fiscal year newest first, ending one short of the table', () => {
    const years = supportedFiscalYears();

    expect(years[0]).toBe(LAST_BS_YEAR - 1);
    expect(years[years.length - 1]).toBe(FIRST_BS_YEAR);
    expect(years.length).toBe(LAST_BS_YEAR - FIRST_BS_YEAR);

    // A fiscal year needs its *following* BS year in the table too, so LAST_BS_YEAR is not one.
    expect(years).not.toContain(LAST_BS_YEAR);
  });

  it('labels a fiscal year the way the reference product does', () => {
    expect(fiscalYearLabel(2083)).toBe('2083 - 2084');
  });

  it('derives the current fiscal year from a real date', () => {
    expect(currentFiscalYear(new Date('2026-09-03T00:00:00Z'))).toBe(2083);
    expect(currentFiscalYear(new Date('2026-07-01T00:00:00Z'))).toBe(2082);
  });
});
