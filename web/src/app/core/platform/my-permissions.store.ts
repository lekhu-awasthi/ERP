import { Injectable, Signal, WritableSignal, inject, signal, untracked } from '@angular/core';
import { Observable, catchError, map, of, shareReplay, tap } from 'rxjs';

import { AuthService } from '../auth/auth.service';
import { PlatformService } from './platform.service';

/**
 * What the shell knows about the signed-in user's grants in one organization.
 *
 * - `'loading'` -- not answered yet. Screens that need a key stay hidden, so a restricted user never
 *   sees a link appear and then vanish.
 * - `'unknown'` -- the read failed (a custom role without `Tenancy.Subscription.View`, a network
 *   error). The shell then **fails open** and shows everything: hiding a link is usability, not
 *   security, and the server still refuses whatever was not granted. Failing closed here would leave
 *   a working role with an empty nav.
 * - a set -- every key held organization-wide or at some billing location.
 */
export type GrantState = ReadonlySet<string> | 'loading' | 'unknown';

interface Entry {
  readonly state: WritableSignal<GrantState>;
  request: Observable<ReadonlySet<string> | null>;
}

/**
 * The signed-in user's permission keys per organization, read once and shared by the nav (as a
 * signal) and by `permissionGuard` (as an observable), so a navigation never waits on a second copy.
 *
 * Keyed by organization, and dropped whole when the signed-in user changes, so a second user on the
 * same tab never sees the first one's nav. A role edit calls {@link refresh}, because the editor may
 * have just changed their own role.
 */
@Injectable({ providedIn: 'root' })
export class MyPermissionsStore {
  private readonly platform = inject(PlatformService);
  private readonly auth = inject(AuthService);

  private readonly cache = new Map<string, Entry>();
  private owner: string | null = null;

  /** For the nav. Reading it inside a `computed()` is safe: every write is `untracked` (phase 35a). */
  granted(organizationId: string): Signal<GrantState> {
    return this.entry(organizationId).state.asReadonly();
  }

  /** For the route guard: emits once, `null` when the read failed. */
  load(organizationId: string): Observable<ReadonlySet<string> | null> {
    return this.entry(organizationId).request;
  }

  /** Re-reads every organization already asked about, keeping the signals the nav holds. */
  refresh(): void {
    for (const [organizationId, entry] of this.cache) {
      entry.request = this.fetch(organizationId, entry.state);
    }
  }

  private entry(organizationId: string): Entry {
    // Read tracked on purpose: a nav computed over `granted()` re-runs when the user changes.
    const user = this.auth.currentUser()?.userId ?? null;

    // From nobody to someone is a hard reload restoring the session: whatever was read meanwhile was
    // read with the same cookie, so it is kept. From one user to another drops everything.
    if (user !== null && user !== this.owner) {
      if (this.owner !== null) {
        this.cache.clear();
      }

      this.owner = user;
    }

    const hit = this.cache.get(organizationId);

    if (hit) {
      return hit;
    }

    const state = signal<GrantState>('loading');
    const created: Entry = { state, request: of(null) };
    this.cache.set(organizationId, created);
    created.request = this.fetch(organizationId, state);

    return created;
  }

  private fetch(organizationId: string, state: WritableSignal<GrantState>): Observable<ReadonlySet<string> | null> {
    const request = this.platform.getMyPermissions(organizationId).pipe(
      map((dto): ReadonlySet<string> | null => new Set([...dto.organizationWide, ...dto.atSomeLocation])),
      catchError(() => of(null)),
      tap((result) => untracked(() => state.set(result ?? 'unknown'))),
      shareReplay({ bufferSize: 1, refCount: false }),
    );

    // Started now rather than on first subscribe, so the nav's signal fills without a subscriber.
    // `untracked`, per phase 35a: the first caller is a `computed()`, and a source that answers
    // synchronously (a test double) would otherwise write a signal during it and throw NG0600.
    untracked(() => request.subscribe());

    return request;
  }
}
