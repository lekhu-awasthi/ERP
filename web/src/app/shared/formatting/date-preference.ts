import { Injectable, inject, signal } from '@angular/core';

import { UserPreferenceKeys } from '../../core/platform/platform.models';
import { PlatformService } from '../../core/platform/platform.service';

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
 *   - <s>The preference does not follow the user to another device or browser.</s> <b>Closed in
 *     Phase 33.</b> Phase 23 named this service as "the single seam to move behind an endpoint if
 *     that changes", and phase 33 built the endpoint — a per-user store it had to build anyway for
 *     the Quick Links tray, which the reference product keeps server-side. So the seam moved, and
 *     the second consumer cost one `activate()` call.
 *
 *     <b>`localStorage` did not go away, and must not.</b> It is now the synchronous cache: the
 *     first paint of every page reads it with no await, so nothing flashes the wrong calendar while
 *     a request is in flight, and the preference still works before an organization is chosen (the
 *     login and organization-list screens have no tenant to scope a preference to). The server row
 *     is the durable copy and wins on {@link DatePreferenceService.activate}.
 *   - <s>Server-rendered output cannot read it.</s> <b>Closed in Phase 27b.</b> The preference is
 *     still stored here, but `calendarInterceptor` now sends it to the API as an `X-Calendar`
 *     header, so print/PDF and every `.xlsx` export render business dates in the chosen calendar.
 *     Audit timestamps and download file names deliberately stay AD -- see phase-27b Decision B.
 *
 * The value is a signal, so every date on screen re-renders the moment it flips, with no reload and
 * no per-page subscription -- and since Phase 27b the next API request carries it too.
 */
@Injectable({ providedIn: 'root' })
export class DatePreferenceService {
  private readonly platform = inject(PlatformService);
  private readonly current = signal<CalendarFormat>(read());

  /** The organization whose stored preference this session is writing through to, once known. */
  private organizationId: string | null = null;

  /** The active calendar. Read this in templates and computed()s. */
  readonly format = this.current.asReadonly();

  /**
   * Phase 33 — binds this service to an organization and adopts that organization's stored choice.
   * Called by the shell once an organization is in scope; safe to call repeatedly.
   *
   * The server row wins over the local cache, because it is the copy that followed the user here.
   * A failed read changes nothing: the cached value stays, which is exactly the phase-23 behaviour.
   */
  activate(organizationId: string): void {
    this.organizationId = organizationId;

    this.platform.getPreferences(organizationId).subscribe({
      next: (preferences) => {
        const stored = preferences.find((x) => x.key === UserPreferenceKeys.calendar);

        if (!stored) {
          return;
        }

        try {
          const value: unknown = JSON.parse(stored.value);

          if (value === 'AD' || value === 'BS') {
            this.applyLocally(value);
          }
        } catch {
          // A value written in some other shape is not worth a broken shell.
        }
      },
      error: () => {
        // Offline, or no permission: the cached choice still applies.
      },
    });
  }

  set(format: CalendarFormat): void {
    this.applyLocally(format);

    if (this.organizationId) {
      this.platform
        .setPreference(this.organizationId, UserPreferenceKeys.calendar, JSON.stringify(format))
        // Fire and forget: the choice has already applied locally and been cached, so a failed write
        // costs the user cross-device sync of this one flip, not the flip itself.
        .subscribe({ error: () => undefined });
    }
  }

  private applyLocally(format: CalendarFormat): void {
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
