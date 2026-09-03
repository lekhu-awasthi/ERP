import {
  BsDate,
  FIRST_BS_YEAR,
  LAST_BS_YEAR,
  adToBs,
  bsDaysInMonth,
  bsToAd,
  formatBs,
  formatBsLong,
  parseBs,
} from './bs-date';

/**
 * Phase 23's testing bar says it in as many words: <b>the conversion table is the thing under test,
 * not the widget.</b> A BS conversion that is one day off for one month of one year is silent,
 * permanent, and lands in a filed tax return -- and no amount of component testing would catch it.
 *
 * Two independent kinds of evidence are asserted here:
 *   1. <b>Anchors verified against the live reference product</b> (Tigg UAT, Phase 23 Step 2). The
 *      profile menu's AD/BS toggle was flipped and the same Journal Voucher grid read in both
 *      calendars, giving 16 same-row AD/BS pairs. Those are reproduced below.
 *   2. <b>Published Nepali New Year dates</b> for BS 2070..2083, which pin the cumulative day count
 *      once per year across a 14-year span -- including the four years whose Baisakh 1 falls on
 *      April 13 rather than 14, which is exactly where an off-by-one would surface.
 */
describe('bs-date conversion', () => {
  /** AD ISO date -> the BS date the live reference product rendered for that same row. */
  const liveConfirmedPairs: ReadonlyArray<readonly [string, BsDate]> = [
    ['2026-09-01', { year: 2083, month: 5, day: 16 }],
    ['2026-09-02', { year: 2083, month: 5, day: 17 }],
    ['2026-08-30', { year: 2083, month: 5, day: 14 }],
    ['2026-08-26', { year: 2083, month: 5, day: 10 }],
    ['2026-08-19', { year: 2083, month: 5, day: 3 }],
    ['2026-08-14', { year: 2083, month: 4, day: 29 }],
    ['2026-08-12', { year: 2083, month: 4, day: 27 }],
    ['2026-08-09', { year: 2083, month: 4, day: 24 }],
    ['2026-08-08', { year: 2083, month: 4, day: 23 }],
    ['2026-08-04', { year: 2083, month: 4, day: 19 }],
    ['2026-08-03', { year: 2083, month: 4, day: 18 }],
    ['2026-08-01', { year: 2083, month: 4, day: 16 }],
  ];

  /** Baisakh 1 (Nepali New Year) as published, BS 2070..2083. Note 2073/2077/2081 fall on Apr 13. */
  const newYearPairs: ReadonlyArray<readonly [string, BsDate]> = [
    ['2013-04-14', { year: 2070, month: 1, day: 1 }],
    ['2014-04-14', { year: 2071, month: 1, day: 1 }],
    ['2015-04-14', { year: 2072, month: 1, day: 1 }],
    ['2016-04-13', { year: 2073, month: 1, day: 1 }],
    ['2017-04-14', { year: 2074, month: 1, day: 1 }],
    ['2018-04-14', { year: 2075, month: 1, day: 1 }],
    ['2019-04-14', { year: 2076, month: 1, day: 1 }],
    ['2020-04-13', { year: 2077, month: 1, day: 1 }],
    ['2021-04-14', { year: 2078, month: 1, day: 1 }],
    ['2022-04-14', { year: 2079, month: 1, day: 1 }],
    ['2023-04-14', { year: 2080, month: 1, day: 1 }],
    ['2024-04-13', { year: 2081, month: 1, day: 1 }],
    ['2025-04-14', { year: 2082, month: 1, day: 1 }],
    ['2026-04-14', { year: 2083, month: 1, day: 1 }],
  ];

  /**
   * Dates chosen because their BS month has a length its neighbouring years do not share -- the
   * exact shape of error a computed (rather than tabulated) conversion would produce. BS 2062's
   * Kartik is 29 days where 2061 and 2063 both have 30; 2029's Bhadra is 32; 2035's Aswin is 31.
   */
  const irregularMonthPairs: ReadonlyArray<readonly [string, BsDate]> = [
    ['2006-01-13', { year: 2062, month: 9, day: 29 }],
    ['1978-10-17', { year: 2035, month: 6, day: 31 }],
    ['1951-11-15', { year: 2008, month: 7, day: 29 }],
    ['2010-03-13', { year: 2066, month: 11, day: 29 }],
    ['1972-09-16', { year: 2029, month: 5, day: 32 }],
  ];

  const allPairs = [...liveConfirmedPairs, ...newYearPairs, ...irregularMonthPairs];

  it('converts AD to BS for every anchor confirmed against the live reference product', () => {
    for (const [ad, bs] of liveConfirmedPairs) {
      expect(adToBs(ad), ad).toEqual(bs);
    }
  });

  it('converts AD to BS for every published Nepali New Year, BS 2070..2083', () => {
    for (const [ad, bs] of newYearPairs) {
      expect(adToBs(ad), ad).toEqual(bs);
    }
  });

  it('converts AD to BS for months whose length differs from their neighbouring years', () => {
    for (const [ad, bs] of irregularMonthPairs) {
      expect(adToBs(ad), ad).toEqual(bs);
    }
  });

  it('converts BS back to AD for every anchor (the other direction)', () => {
    for (const [ad, bs] of allPairs) {
      expect(bsToAd(bs), formatBs(bs)).toBe(ad);
    }
  });

  // 30s, not the 5s default. This one test makes ~136,000 assertions and was already close enough
  // to the default that it started timing out on a loaded machine (found in Phase 26a). Sampling it
  // down would defeat its purpose -- see the comment below -- so it gets the time it needs instead.
  it('round-trips every single day of the supported range', { timeout: 30_000 }, () => {
    // 33,969 days. Exhaustive rather than sampled: a table typo affects one month of one year, and
    // sampling is exactly how such a typo survives a test suite.
    let checked = 0;
    for (let year = FIRST_BS_YEAR; year <= LAST_BS_YEAR; year++) {
      for (let month = 1; month <= 12; month++) {
        const days = bsDaysInMonth(year, month)!;
        expect(days, `${year}-${month}`).toBeGreaterThanOrEqual(29);
        expect(days, `${year}-${month}`).toBeLessThanOrEqual(32);
        for (let day = 1; day <= days; day++) {
          const ad = bsToAd({ year, month, day });
          expect(ad, `bs ${year}-${month}-${day}`).not.toBeNull();
          expect(adToBs(ad!), ad!).toEqual({ year, month, day });
          checked++;
        }
      }
    }
    expect(checked).toBe(33969);
  });

  it('advances exactly one BS day for each AD day across a BS year boundary', () => {
    // Chaitra 30, 2082 -> Baisakh 1, 2083 -> Baisakh 2. A year rollover is where a cumulative-sum
    // bug lands, and it is invisible to any test that stays inside one year.
    expect(adToBs('2026-04-13')).toEqual({ year: 2082, month: 12, day: 30 });
    expect(adToBs('2026-04-14')).toEqual({ year: 2083, month: 1, day: 1 });
    expect(adToBs('2026-04-15')).toEqual({ year: 2083, month: 1, day: 2 });
  });

  describe('the supported range', () => {
    it('accepts both boundary dates', () => {
      expect(adToBs('1943-04-14')).toEqual({ year: FIRST_BS_YEAR, month: 1, day: 1 });
      expect(bsToAd({ year: FIRST_BS_YEAR, month: 1, day: 1 })).toBe('1943-04-14');

      expect(adToBs('2036-04-13')).toEqual({ year: LAST_BS_YEAR, month: 12, day: 31 });
      expect(bsToAd({ year: LAST_BS_YEAR, month: 12, day: 31 })).toBe('2036-04-13');
    });

    it('fails loudly one day outside each end rather than returning a plausible date', () => {
      // The whole point of the module: never guess, never extrapolate, never clamp.
      expect(adToBs('1943-04-13')).toBeNull();
      expect(adToBs('2036-04-14')).toBeNull();
      expect(bsToAd({ year: FIRST_BS_YEAR - 1, month: 12, day: 30 })).toBeNull();
      expect(bsToAd({ year: LAST_BS_YEAR + 1, month: 1, day: 1 })).toBeNull();
    });

    it('fails loudly far outside the range too', () => {
      expect(adToBs('1900-01-01')).toBeNull();
      expect(adToBs('2100-01-01')).toBeNull();
      expect(bsToAd({ year: 1970, month: 1, day: 1 })).toBeNull();
      expect(bsToAd({ year: 2200, month: 1, day: 1 })).toBeNull();
    });

    it('pins the range constants, so widening it stays a deliberate decision', () => {
      expect(FIRST_BS_YEAR).toBe(2000);
      expect(LAST_BS_YEAR).toBe(2092);
    });
  });

  describe('rejecting dates that do not exist', () => {
    it('rejects a BS day past the end of its own month', () => {
      // Poush 2083 has 30 days; Poush 2084 has 29. Same month number, consecutive years, different
      // valid maximum -- which is the reason this module is a table and not an algorithm.
      expect(bsDaysInMonth(2083, 9)).toBe(30);
      expect(bsDaysInMonth(2084, 9)).toBe(29);
      expect(bsToAd({ year: 2083, month: 9, day: 30 })).not.toBeNull();
      expect(bsToAd({ year: 2084, month: 9, day: 30 })).toBeNull();
      expect(bsToAd({ year: 2083, month: 9, day: 31 })).toBeNull();
    });

    it('rejects an out-of-range BS month or a zero day', () => {
      expect(bsToAd({ year: 2083, month: 0, day: 1 })).toBeNull();
      expect(bsToAd({ year: 2083, month: 13, day: 1 })).toBeNull();
      expect(bsToAd({ year: 2083, month: 5, day: 0 })).toBeNull();
      expect(bsDaysInMonth(2083, 13)).toBeNull();
    });

    it('rejects an AD date that does not exist rather than rolling it forward', () => {
      // Date.UTC would silently turn 2025-02-30 into March 2. That is precisely the class of
      // plausible-but-wrong answer this module must never produce.
      expect(adToBs('2025-02-30')).toBeNull();
      expect(adToBs('2025-13-01')).toBeNull();
      expect(adToBs('2024-02-29')).not.toBeNull(); // a real leap day still converts
    });

    it('rejects malformed input', () => {
      expect(adToBs('')).toBeNull();
      expect(adToBs('01-09-2026')).toBeNull();
      expect(adToBs('2026-9-1')).toBeNull();
      expect(adToBs('not a date')).toBeNull();
    });
  });

  describe('formatting and parsing', () => {
    it('zero-pads so BS strings sort like the ISO AD ones', () => {
      expect(formatBs({ year: 2083, month: 5, day: 6 })).toBe('2083-05-06');
      expect(formatBs({ year: 2083, month: 12, day: 30 })).toBe('2083-12-30');
    });

    it('renders the long form with the Nepali month name', () => {
      expect(formatBsLong({ year: 2083, month: 5, day: 16 })).toBe('16 Bhadra 2083');
      expect(formatBsLong({ year: 2083, month: 1, day: 1 })).toBe('1 Baisakh 2083');
    });

    it('parses padded and unpadded BS strings, and round-trips formatBs', () => {
      expect(parseBs('2083-05-16')).toEqual({ year: 2083, month: 5, day: 16 });
      expect(parseBs('2083-5-6')).toEqual({ year: 2083, month: 5, day: 6 });
      expect(parseBs('  2083-05-16  ')).toEqual({ year: 2083, month: 5, day: 16 });
      const date = { year: 2071, month: 11, day: 29 };
      expect(parseBs(formatBs(date))).toEqual(date);
    });

    it('refuses to parse a string that is not a real BS date', () => {
      expect(parseBs('2084-09-30')).toBeNull(); // Poush 2084 has only 29 days
      expect(parseBs('2093-01-01')).toBeNull(); // past the supported range
      expect(parseBs('2083-05')).toBeNull();
      expect(parseBs('rubbish')).toBeNull();
    });
  });
});
