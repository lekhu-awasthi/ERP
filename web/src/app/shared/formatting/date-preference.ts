import { Injectable, signal } from '@angular/core';

export type CalendarFormat = 'AD' | 'BS';

const STORAGE_KEY = 'erp.calendarFormat';

/**
 * NFR-1.1's "switchable per user preference" (Phase 23, <b>Decision C</b>).
 *
 * <b>Where this lives, and what that costs.</b> Nothing in this codebase stored a per-user
 * preference before Phase 23 -- `Domain/Identity/User` has no settings at all, and `TenantSettings`
 * is per-<i>organization</i>, so it is the wrong home for a display choice one user makes for
 * themselves. Three options were on the table: a column on `User` (a migration, an endpoint, and a
 * pure display concern pushed into the Identity aggregate), a general `UserPreference` entity (a
 * table, a command, a query and permission plumbing for one boolean), or browser storage. This is
 * browser storage, chosen deliberately.
 *
 * <b>What that explicitly does not support</b>, so no future reader has to discover it:
 *   - The preference does <b>not</b> follow the user to another device or browser. NFR-1.1 asks for
 *     "per user preference" and does not ask for it to be synchronised; if that is wanted later,
 *     this service is the single seam to move behind an endpoint.
 *   - Server-rendered output cannot read it. Phase 20d's print/PDF pipeline and Phase 16c/21b's
 *     .xlsx exports both format dates on the server, so <b>they stay AD regardless of this
 *     setting</b>. That is a real gap, stated rather than hidden -- see Decision A.
 *
 * The value is a signal, so every date on screen re-renders the moment it flips, with no reload and
 * no per-page subscription.
 */
@Injectable({ providedIn: 'root' })
export class DatePreferenceService {
  private readonly current = signal<CalendarFormat>(read());

  /** The active calendar. Read this in templates and computed()s. */
  readonly format = this.current.asReadonly();

  set(format: CalendarFormat): void {
    this.current.set(format);
    try {
      localStorage.setItem(STORAGE_KEY, format);
    } catch {
      // Private mode / storage disabled: the choice still applies for this session, it just will
      // not survive a reload. Never let a preference write break the page it was set from.
    }
  }

  toggle(): void {
    this.set(this.current() === 'AD' ? 'BS' : 'AD');
  }

  isBs(): boolean {
    return this.current() === 'BS';
  }
}

/** AD is the default: it is what every screen rendered before Phase 23, and what the live reference
 * product ships as its own default. */
function read(): CalendarFormat {
  try {
    return localStorage.getItem(STORAGE_KEY) === 'BS' ? 'BS' : 'AD';
  } catch {
    return 'AD';
  }
}
