/**
 * The client's single copy of the Nepal wall clock, mirroring `Domain/Common/NepalTime`.
 *
 * <p>Nepal is UTC+05:45 and has observed no DST since 1986, so a fixed offset is the whole rule —
 * the server says the same thing in the same way, and for the same reason.</p>
 *
 * <h4>Why this file exists (phase 48)</h4>
 *
 * <p>It was already here twice: `home-dashboard-page.ts`'s `nepalToday()` and `bs-date-input.ts`'s
 * `isoToday()` each carried their own `(5 * 60 + 45) * 60_000`, each with its own paragraph
 * explaining the 18:15 boundary. Phase 48 needed a third for comment timestamps, and CLAUDE.md's
 * rule is to retire copies 1..N before writing copy N+1. Both callers now route through here.</p>
 *
 * <h4>The boundary this protects</h4>
 *
 * <p>Between 18:15 and 24:00 UTC the Nepal calendar date is already tomorrow. Anything that takes
 * an instant and wants the day it happened on must shift first: `'2026-09-14T20:00:00+00:00'.slice(0, 10)`
 * is `2026-09-14`, and the comment it describes was written on the 15th in Kathmandu.</p>
 */
export const NEPAL_OFFSET_MINUTES = 5 * 60 + 45;

const NEPAL_OFFSET_MS = NEPAL_OFFSET_MINUTES * 60_000;

/** True when the string carries a time of day, i.e. it is an instant rather than a calendar date. */
export function isInstant(value: string): boolean {
  return value.length > 10;
}

/**
 * An ISO instant expressed on the Nepal wall clock, as `{ date: 'yyyy-MM-dd', time: 'HH:mm' }`.
 *
 * <p>Returns null for anything `Date.parse` cannot read, so a caller can fall back to showing the
 * raw value rather than rendering `NaN-NaN-NaN` — the same honesty rule `NepaliDatePipe` already
 * applies to a date outside the BS table.</p>
 */
export function instantToNepal(value: string): { date: string; time: string; seconds: string } | null {
  const ms = Date.parse(value);
  if (Number.isNaN(ms)) {
    return null;
  }
  const shifted = new Date(ms + NEPAL_OFFSET_MS).toISOString();
  return { date: shifted.slice(0, 10), time: shifted.slice(11, 16), seconds: shifted.slice(11, 19) };
}

/** Today's calendar date on the Nepal wall clock, as `yyyy-MM-dd`. Never the UTC date. */
export function nepalToday(): string {
  return new Date(Date.now() + NEPAL_OFFSET_MS).toISOString().slice(0, 10);
}
