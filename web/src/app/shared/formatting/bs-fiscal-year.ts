import { FIRST_BS_YEAR, LAST_BS_YEAR, adToBs } from './bs-date';

/**
 * The Bikram Sambat fiscal year, client side (phase 26b).
 *
 * <p>A Nepali fiscal year runs <b>Shrawan 1 to the last day of Asar</b>, so it spans two BS years
 * and is named by the first: the reference product's picker labels it "2083 - 2084" and its report
 * subtitle "For fiscal year 2083 / 2084". Five of this phase's reports are keyed by one of these
 * rather than by a date range, so the picker needs to know what a fiscal year is.</p>
 *
 * <p>This mirrors <c>BsCalendar.FiscalYearStartMonth</c>/<c>FiscalYearOf</c> on the server, which is
 * where the actual month-length table lives for report grouping. Nothing here converts a date for
 * storage -- dates are stored in AD, always (phase-23).</p>
 */

/** Shrawan. Baisakh is month 1, so the fiscal year opens on the fourth BS month. */
export const FISCAL_YEAR_START_MONTH = 4;

/**
 * The fiscal year an ISO AD date falls in, named by its starting BS year, or null when the date is
 * outside the supported BS range.
 */
export function fiscalYearOf(isoDate: string): number | null {
  const bs = adToBs(isoDate);
  if (!bs) {
    return null;
  }
  return bs.month >= FISCAL_YEAR_START_MONTH ? bs.year : bs.year - 1;
}

/** Today's fiscal year, falling back to the last expressible one outside the supported range. */
export function currentFiscalYear(today: Date = new Date()): number {
  const iso = today.toISOString().slice(0, 10);
  return fiscalYearOf(iso) ?? LAST_BS_YEAR - 1;
}

/**
 * Every fiscal year the BS table can express, **newest first** so a picker opens on recent years.
 * The last is `LAST_BS_YEAR - 1`, because a fiscal year needs its following BS year in the table
 * too.
 */
export function supportedFiscalYears(): number[] {
  const years: number[] = [];
  for (let year = LAST_BS_YEAR - 1; year >= FIRST_BS_YEAR; year--) {
    years.push(year);
  }
  return years;
}

/** `2083 - 2084`, the label the live picker shows. */
export function fiscalYearLabel(fiscalYear: number): string {
  return `${fiscalYear} - ${fiscalYear + 1}`;
}
