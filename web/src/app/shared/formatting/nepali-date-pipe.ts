import { Pipe, PipeTransform, inject } from '@angular/core';

import { adToBs, formatBs } from './bs-date';
import { DatePreferenceService } from './date-preference';
import { instantToNepal, isInstant } from './nepal-time';

/**
 * How much of an instant to show. `'date'` (the default) is the calendar date alone; `'datetime'`
 * appends the Nepal wall-clock time as `HH:mm`; `'datetime-seconds'` appends `HH:mm:ss`.
 *
 * <p>The third exists because two audit screens need to the second — and that is a reason to render
 * seconds, never a reason to render the wrong calendar. `user-log-page` had carried the sentence
 * "the seconds matter more here than the calendar does" since phase 26c, which reads as a decision
 * and is really a false choice: nothing about a BS date prevents it carrying seconds. An Admin
 * reading the sign-in log with the calendar set to BS saw Gregorian dates, in the one report where
 * being sure which day something happened on matters most.</p>
 */
export type NepaliDateMode = 'date' | 'datetime' | 'datetime-seconds';

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
 *
 * <h4>Phase 48 — an <i>instant</i> is shifted to the Nepal wall clock before its date is taken</h4>
 *
 * <p>Most values reaching this pipe are `DateOnly` — a business date with no time zone, which needs
 * no shifting. Some are `DateTimeOffset`s serialised as UTC (`2026-09-14T20:00:00+00:00`), and the
 * previous implementation took `slice(0, 10)` of those, which is the <b>UTC</b> day. Between 18:15
 * and 24:00 UTC that is the day before the one the event happened on in Kathmandu, so the
 * Transaction List has been dating late-evening approvals to the previous day since phase 12. The
 * shift happens here rather than at each call site because a caller cannot tell from a string
 * whether it is looking at a date or an instant — but this pipe can.</p>
 *
 * <p><b>`| nepaliDate: 'datetime'`</b> adds the wall-clock time. It is a mode on this pipe and not a
 * second pipe, because two pipes for one calendar is the divergence phase-26b's shared table exists
 * to prevent: the time is rendered from the same shifted instant that produced the date, so the two
 * halves can never disagree about which day it is. Asking for a time on a date-only value returns
 * the date alone rather than inventing `00:00`.</p>
 */
@Pipe({ name: 'nepaliDate', pure: false })
export class NepaliDatePipe implements PipeTransform {
  private readonly preference = inject(DatePreferenceService);

  transform(value: string | null | undefined, mode: NepaliDateMode = 'date'): string {
    if (!value) {
      return '';
    }

    // A calendar date carries no time zone and is rendered as it stands; an instant is an event,
    // and the day it happened on is its day in Kathmandu.
    if (!isInstant(value)) {
      return this.render(value);
    }

    const nepal = instantToNepal(value);
    if (nepal === null) {
      return value; // Unparseable: show it rather than render NaN.
    }

    const date = this.render(nepal.date);
    switch (mode) {
      case 'datetime':
        return `${date} ${nepal.time}`;
      case 'datetime-seconds':
        return `${date} ${nepal.seconds}`;
      default:
        return date;
    }
  }

  private render(iso: string): string {
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
