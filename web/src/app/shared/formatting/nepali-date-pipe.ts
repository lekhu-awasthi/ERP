import { Pipe, PipeTransform, inject } from '@angular/core';

import { adToBs, formatBs } from './bs-date';
import { DatePreferenceService } from './date-preference';

/**
 * NFR-1.1's display half: renders an ISO `yyyy-MM-dd` date in whichever calendar the user has
 * selected, as `DD-MM-YYYY` in both -- the format the live reference product uses for AD and BS
 * alike (Phase 23 Step 2: the same grid was read in both calendars and only the numbers changed).
 *
 * <b>Why this pipe is impure.</b> A pure pipe caches on its argument, and the argument here (the
 * ISO string) does not change when the user flips the calendar toggle -- so a pure pipe would keep
 * serving the AD rendering forever while the rest of the app switched to BS. That is the same shape
 * as CLAUDE.md's zoneless-`computed()`-over-`FormControl` gotcha: a value with no tracked
 * dependency, silently stale, with nothing in `tsc` or `ng build` to catch it. Impure means
 * `transform` runs on every change-detection pass, so the module-level memo below is what keeps a
 * 200-row grid cheap -- the conversion is deterministic, so caching it is free.
 *
 * <b>Out-of-range dates fall back to the AD rendering</b> rather than throwing or printing
 * something wrong. `bs-date.ts` only covers BS 2000..2092 (AD 1943-04-14..2036-04-13); a date
 * outside it renders as a visibly-AD date, which is honest and non-destructive, where a guessed BS
 * date would not be.
 */
@Pipe({ name: 'nepaliDate', pure: false })
export class NepaliDatePipe implements PipeTransform {
  private readonly preference = inject(DatePreferenceService);

  transform(value: string | null | undefined): string {
    if (!value) {
      return '';
    }
    // Tolerate a full ISO timestamp by taking just the date part -- some DTO fields are instants.
    const iso = value.length > 10 ? value.slice(0, 10) : value;
    return this.preference.isBs() ? toBsDisplay(iso) : toAdDisplay(iso);
  }
}

const bsCache = new Map<string, string>();
const adCache = new Map<string, string>();

/** `2026-09-01` -> `16-05-2083`, or the AD rendering when the date is outside the BS table. */
export function toBsDisplay(iso: string): string {
  const hit = bsCache.get(iso);
  if (hit !== undefined) {
    return hit;
  }
  const bs = adToBs(iso);
  const text = bs === null ? toAdDisplay(iso) : reorder(formatBs(bs));
  bsCache.set(iso, text);
  return text;
}

/** `2026-09-01` -> `01-09-2026`. */
export function toAdDisplay(iso: string): string {
  const hit = adCache.get(iso);
  if (hit !== undefined) {
    return hit;
  }
  const text = /^\d{4}-\d{2}-\d{2}$/.test(iso) ? reorder(iso) : iso;
  adCache.set(iso, text);
  return text;
}

/** `yyyy-MM-dd` -> `dd-MM-yyyy`, the display order both calendars use here. */
function reorder(ymd: string): string {
  const [y, m, d] = ymd.split('-');
  return `${d}-${m}-${y}`;
}
