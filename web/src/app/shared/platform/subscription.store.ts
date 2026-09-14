import { Injectable, Signal, inject, signal, untracked } from '@angular/core';
import { Observable, tap } from 'rxjs';

import {
  SetTenantSubscriptionRequest,
  TenantSubscription,
} from '../../core/organizations/organizations.models';
import { OrganizationsService } from '../../core/organizations/organizations.service';

/**
 * Phase 41 -- one shared, cached read of a tenant's subscription, on `BillingLocationStore`'s shape
 * (phase 35a) and for a sharper reason.
 *
 * <p><b>Two screens show the same state and one of them changes it.</b> The shell banner warns about
 * expiry and spent allowances on every page; the Subscription screen records a new term. Without a
 * shared source they are two reads of one fact, and the moment a term is saved the banner is stale --
 * it kept saying "366 days remaining in your trial" on the very page that had just recorded a paid
 * Standard plan. That is phase 34b's rule in another costume: <i>anything global a screen both shows
 * and sends must reload when it changes, from the first version.</i> Found in this phase's own
 * browser pass, before it shipped.</p>
 *
 * <p><b>Why a store rather than an event.</b> The banner would have to subscribe to something, and
 * the something would have to carry the new value anyway -- at which point it is this. A store also
 * removes the second HTTP call: the save response is the same DTO the read returns, by construction
 * (phase 31 extracted `ToDto` for exactly that), so {@link save} folds the response straight into
 * the cache and nothing re-fetches.</p>
 *
 * <p><b>Keyed by organization</b>, so switching company never shows the previous tenant's plan. A
 * failed read resolves to null rather than staying pending: a banner that cannot read its subject
 * says nothing, and every screen behind it still works.</p>
 */
@Injectable({ providedIn: 'root' })
export class SubscriptionStore {
  private readonly organizationsService = inject(OrganizationsService);

  private readonly cache = new Map<string, ReturnType<typeof signal<TenantSubscription | null>>>();

  /** The tenant's subscription, or null until the first read resolves (or if it failed). */
  subscription(organizationId: string): Signal<TenantSubscription | null> {
    return this.store(organizationId).asReadonly();
  }

  /**
   * Records a term and folds the response into the cache, so every consumer -- the shell banner
   * above all -- re-renders from the same object the screen just received.
   */
  save(organizationId: string, request: SetTenantSubscriptionRequest): Observable<TenantSubscription> {
    return this.organizationsService
      .setSubscription(organizationId, request)
      .pipe(tap((result) => untracked(() => this.store(organizationId).set(result))));
  }

  /** Re-reads from the server. For a screen that has reason to believe usage has moved. */
  reload(organizationId: string): void {
    this.fetch(organizationId, this.store(organizationId));
  }

  private store(organizationId: string): ReturnType<typeof signal<TenantSubscription | null>> {
    const existing = this.cache.get(organizationId);

    if (existing) {
      return existing;
    }

    const created = signal<TenantSubscription | null>(null);
    this.cache.set(organizationId, created);
    this.fetch(organizationId, created);

    return created;
  }

  private fetch(
    organizationId: string,
    store: ReturnType<typeof signal<TenantSubscription | null>>,
  ): void {
    // untracked, per phase 35a: the first caller is a `computed()` in the banner, and a source that
    // resolves synchronously -- a test double, a warmed cache -- would otherwise write this signal
    // during that computed and throw NG0600. Real HTTP resolves later and has no active consumer,
    // so this costs nothing and removes the difference between the two.
    untracked(() =>
      this.organizationsService.getSubscription(organizationId).subscribe({
        next: (result) => untracked(() => store.set(result)),
        error: () => untracked(() => store.set(null)),
      }));
  }
}
