/**
 * Bikram Sambat <-> Gregorian conversion (NFR-1.1). <b>This module is the phase's risk, not the
 * widget on top of it.</b> BS month lengths are not computable -- they vary per year and come from
 * the published Nepali Panchanga -- so this is a data table with an explicit supported range, and a
 * conversion one day off is silent, permanent, and ends up in a filed tax return.
 *
 * <b>Provenance of BS_MONTH_LENGTHS (Phase 23, Decision B).</b> Cross-checked across four
 * independent open-source implementations rather than transcribed from any one of them:
 *   - `bikram-sambat` (medic)              -- 2-bit-packed lengths, real data through BS 2083
 *   - `nepali-date-converter` (subeshb1)   -- named-month map,      real data through BS 2086
 *   - `nepali-datetime` (opensource-nepal) -- [lengths, yearTotal] pairs
 *   - `nepali_utils` (sarbagyastha, Dart)  -- [yearTotal, ...lengths] rows
 * All four agree on every year of BS 2000..2083. The first two then emit filler rows whose last
 * three months are always 30/30/30 -- the giveaway that their real data has run out -- while the
 * two that carry genuine data past that point agree with each other through BS 2092 and first
 * diverge at BS 2093. The supported range is therefore the unanimous one.
 *
 * <b>Supported range: BS 2000-01-01 .. 2092-12-31, i.e. AD 1943-04-14 .. 2036-04-13.</b> Outside
 * it every function here returns null. They never guess, never extrapolate and never clamp -- a
 * plausible-looking wrong date is the single outcome this module exists to prevent. Callers decide
 * what null means: `NepaliDatePipe` falls back to rendering the AD date unchanged (visibly not a BS
 * date), and `BsDateInput` refuses the entry with a message rather than storing something.
 *
 * <b>Extending it:</b> append BS 2093+ once two independent sources agree, and bump LAST_BS_YEAR.
 * `bs-date.spec.ts` pins the current boundary in both directions, so widening the range is a
 * decision a future reader has to take deliberately rather than drift into.
 *
 * This is a *calendar* concern and is deliberately unrelated to `Domain/Common/NepalTime`, which is
 * a *time zone* (UTC+05:45). Nothing here converts an instant; every function takes and returns a
 * calendar date.
 */

/** BS 2000-01-01 in the Gregorian calendar -- the table's anchor. */
const EPOCH_AD = { year: 1943, month: 4, day: 14 };

export const FIRST_BS_YEAR = 2000;
export const LAST_BS_YEAR = 2092;

/** 12 month lengths per year, BS 2000..2092 laid out flat. See the provenance note above. */
const BS_MONTH_LENGTHS: readonly number[] = [
  30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2000
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2001
  31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2002
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2003
  30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2004
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2005
  31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2006
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2007
  31, 31, 31, 32, 31, 31, 29, 30, 30, 29, 29, 31, // 2008
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2009
  31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2010
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2011
  31, 31, 31, 32, 31, 31, 29, 30, 30, 29, 30, 30, // 2012
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2013
  31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2014
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2015
  31, 31, 31, 32, 31, 31, 29, 30, 30, 29, 30, 30, // 2016
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2017
  31, 32, 31, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2018
  31, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2019
  31, 31, 31, 32, 31, 31, 30, 29, 30, 29, 30, 30, // 2020
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2021
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 30, // 2022
  31, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2023
  31, 31, 31, 32, 31, 31, 30, 29, 30, 29, 30, 30, // 2024
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2025
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2026
  30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2027
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2028
  31, 31, 32, 31, 32, 30, 30, 29, 30, 29, 30, 30, // 2029
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2030
  30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2031
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2032
  31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2033
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2034
  30, 32, 31, 32, 31, 31, 29, 30, 30, 29, 29, 31, // 2035
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2036
  31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2037
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2038
  31, 31, 31, 32, 31, 31, 29, 30, 30, 29, 30, 30, // 2039
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2040
  31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2041
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2042
  31, 31, 31, 32, 31, 31, 29, 30, 30, 29, 30, 30, // 2043
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2044
  31, 32, 31, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2045
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2046
  31, 31, 31, 32, 31, 31, 30, 29, 30, 29, 30, 30, // 2047
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2048
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 30, // 2049
  31, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2050
  31, 31, 31, 32, 31, 31, 30, 29, 30, 29, 30, 30, // 2051
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2052
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 30, // 2053
  31, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2054
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2055
  31, 31, 32, 31, 32, 30, 30, 29, 30, 29, 30, 30, // 2056
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2057
  30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2058
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2059
  31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2060
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2061
  30, 32, 31, 32, 31, 31, 29, 30, 29, 30, 29, 31, // 2062
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2063
  31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2064
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2065
  31, 31, 31, 32, 31, 31, 29, 30, 30, 29, 29, 31, // 2066
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2067
  31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2068
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2069
  31, 31, 31, 32, 31, 31, 29, 30, 30, 29, 30, 30, // 2070
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2071
  31, 32, 31, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2072
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2073
  31, 31, 31, 32, 31, 31, 30, 29, 30, 29, 30, 30, // 2074
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2075
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 30, // 2076
  31, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2077
  31, 31, 31, 32, 31, 31, 30, 29, 30, 29, 30, 30, // 2078
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2079
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 30, // 2080
  31, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2081
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2082
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2083
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2084
  30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2085
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2086
  31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2087
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2088
  30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 29, 31, // 2089
  31, 31, 32, 31, 31, 31, 30, 29, 30, 29, 30, 30, // 2090
  31, 31, 32, 32, 31, 30, 30, 29, 30, 29, 30, 30, // 2091
  31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 31, // 2092
];

/** Baisakh is month 1. Spelled as the reference product spells them (erp-module-scan.md). */
export const BS_MONTH_NAMES: readonly string[] = [
  'Baisakh', 'Jestha', 'Asar', 'Shrawan', 'Bhadra', 'Aswin',
  'Kartik', 'Mangsir', 'Poush', 'Magh', 'Falgun', 'Chaitra',
];

/** A Bikram Sambat calendar date. Month is 1-based, matching how a user reads it. */
export interface BsDate {
  readonly year: number;
  readonly month: number;
  readonly day: number;
}

function daysInBsMonth(year: number, month: number): number {
  return BS_MONTH_LENGTHS[(year - FIRST_BS_YEAR) * 12 + (month - 1)];
}

/** Days the table covers in total -- one past the last representable day index. */
const TOTAL_DAYS = BS_MONTH_LENGTHS.reduce((sum, n) => sum + n, 0);

/** Days from BS 2000-01-01 to the given BS date, or null if it is outside the table. */
function bsToDayIndex(date: BsDate): number | null {
  const { year, month, day } = date;
  if (!Number.isInteger(year) || !Number.isInteger(month) || !Number.isInteger(day)) {
    return null;
  }
  if (year < FIRST_BS_YEAR || year > LAST_BS_YEAR || month < 1 || month > 12) {
    return null;
  }
  if (day < 1 || day > daysInBsMonth(year, month)) {
    return null;
  }

  let days = 0;
  for (let y = FIRST_BS_YEAR; y < year; y++) {
    for (let m = 1; m <= 12; m++) {
      days += daysInBsMonth(y, m);
    }
  }
  for (let m = 1; m < month; m++) {
    days += daysInBsMonth(year, m);
  }
  return days + (day - 1);
}

/** Whole days between the epoch and a Gregorian date, computed in UTC so no local DST shift can
 * round it to the wrong day. */
function adDayIndex(year: number, month: number, day: number): number {
  const msPerDay = 86_400_000;
  const epoch = Date.UTC(EPOCH_AD.year, EPOCH_AD.month - 1, EPOCH_AD.day);
  return Math.round((Date.UTC(year, month - 1, day) - epoch) / msPerDay);
}

/**
 * Gregorian -> Bikram Sambat. `ad` is an ISO `yyyy-MM-dd` string -- the shape every date-bearing
 * DTO in this app already uses on the wire, and the shape `<input type="date">` produced before
 * Phase 23. Returns null for a malformed string, for a date that does not exist (2025-02-30), or
 * for a real date outside the supported range.
 */
export function adToBs(ad: string): BsDate | null {
  const parts = /^(\d{4})-(\d{2})-(\d{2})$/.exec(ad);
  if (!parts) {
    return null;
  }
  const year = Number(parts[1]);
  const month = Number(parts[2]);
  const day = Number(parts[3]);

  // Reject a syntactically valid but non-existent AD date rather than letting Date.UTC roll it
  // forward into a plausible neighbouring day.
  const probe = new Date(Date.UTC(year, month - 1, day));
  if (probe.getUTCFullYear() !== year || probe.getUTCMonth() !== month - 1 || probe.getUTCDate() !== day) {
    return null;
  }

  let remaining = adDayIndex(year, month, day);
  if (remaining < 0 || remaining >= TOTAL_DAYS) {
    return null;
  }

  let bsYear = FIRST_BS_YEAR;
  for (;;) {
    let yearDays = 0;
    for (let m = 1; m <= 12; m++) {
      yearDays += daysInBsMonth(bsYear, m);
    }
    if (remaining < yearDays) {
      break;
    }
    remaining -= yearDays;
    bsYear++;
  }

  let bsMonth = 1;
  while (remaining >= daysInBsMonth(bsYear, bsMonth)) {
    remaining -= daysInBsMonth(bsYear, bsMonth);
    bsMonth++;
  }

  return { year: bsYear, month: bsMonth, day: remaining + 1 };
}

/**
 * Bikram Sambat -> Gregorian, returned as an ISO `yyyy-MM-dd` string so it drops straight into the
 * same DTO field an AD entry would have filled. Returns null outside the table, and for a day
 * number that does not exist in that BS month (Poush 30 in a 29-day Poush, say).
 */
export function bsToAd(date: BsDate): string | null {
  const dayIndex = bsToDayIndex(date);
  if (dayIndex === null) {
    return null;
  }
  const result = new Date(Date.UTC(EPOCH_AD.year, EPOCH_AD.month - 1, EPOCH_AD.day + dayIndex));
  const yyyy = String(result.getUTCFullYear()).padStart(4, '0');
  const mm = String(result.getUTCMonth() + 1).padStart(2, '0');
  const dd = String(result.getUTCDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
}

/** Days in a BS month, or null outside the table -- what a day-picker grid needs. */
export function bsDaysInMonth(year: number, month: number): number | null {
  if (year < FIRST_BS_YEAR || year > LAST_BS_YEAR || month < 1 || month > 12) {
    return null;
  }
  return daysInBsMonth(year, month);
}

/** `2083-05-16`, zero-padded so BS strings sort and compare the way the ISO AD ones do. */
export function formatBs(date: BsDate): string {
  return `${date.year}-${String(date.month).padStart(2, '0')}-${String(date.day).padStart(2, '0')}`;
}

/** `16 Bhadra 2083` -- the long form, for display where a bare numeric string reads ambiguously. */
export function formatBsLong(date: BsDate): string {
  return `${date.day} ${BS_MONTH_NAMES[date.month - 1]} ${date.year}`;
}

/** Parses `2083-05-16`. Returns null unless it is a real day of a real month inside the table. */
export function parseBs(value: string): BsDate | null {
  const parts = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(value.trim());
  if (!parts) {
    return null;
  }
  const candidate = { year: Number(parts[1]), month: Number(parts[2]), day: Number(parts[3]) };
  return bsToDayIndex(candidate) === null ? null : candidate;
}
