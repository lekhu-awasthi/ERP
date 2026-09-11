import { Injectable, Signal, inject, signal, untracked } from '@angular/core';

import { BillingLocation, BillingLocationSettings } from '../../core/organizations/organizations.models';
import { OrganizationsService } from '../../core/organizations/organizations.service';

const NO_SETTINGS: BillingLocationSettings = {
  locationScopeMode: 'SalesTransactionsOnly',
  locationWiseReportPermission: false,
  multipleLocationsEnabled: false,
  locationBearingDocumentTypes: [],
};

/**
 * Phase 35a -- one shared, cached read of a tenant's billing locations.
 *
 * <p>Phase 32 wired the location picker into the Invoice form and the LOCATION column into the
 * Invoice list by giving each page its own `listBillingLocations` call and its own
 * `locationName()` helper. Sweeping that to the other fourteen document types would have meant
 * thirty copies of the same request, and a grid resolving a name per row would have meant one per
 * cell. This store answers both questions from a single request per organization.</p>
 *
 * <p><b>Root-provided but keyed by organization</b>, so switching company does not read the
 * previous tenant's locations: each organization gets its own signal and its own in-flight request.
 * A failed read resolves to an empty list rather than staying pending forever -- a tenant whose
 * locations cannot be read (no entitlement, no permission, a network fault) must still get a
 * working document form, which is phase-20f's failure mode stated as a rule.</p>
 *
 * <p><b>{@link invalidate} is the write half.</b> The Billing Location list page is the only screen
 * that creates or edits a location, and it calls this after a successful save so that a form opened
 * afterwards in the same session sees the new row. Nothing else may need it: locations are
 * configuration, not transactional data.</p>
 */
@Injectable({ providedIn: 'root' })
export class BillingLocationStore {
  private readonly organizationsService = inject(OrganizationsService);

  private readonly cache = new Map<string, ReturnType<typeof signal<BillingLocation[]>>>();
  private readonly settingsCache = new Map<string, ReturnType<typeof signal<BillingLocationSettings>>>();

  /** The tenant's active billing locations. Empty until the first read resolves. */
  locations(organizationId: string): Signal<BillingLocation[]> {
    const existing = this.cache.get(organizationId);
    if (existing) {
      return existing.asReadonly();
    }

    const store = signal<BillingLocation[]>([]);
    this.cache.set(organizationId, store);

    // untracked: the first caller is almost always a `computed()` in a picker or a cell, and a
    // source that resolves synchronously (a test double, a warmed cache) would then write this
    // signal *during* that computed -- NG0600. Real HTTP resolves later and has no active consumer,
    // so this costs nothing and removes the difference between the two.
    untracked(() =>
      this.organizationsService.listBillingLocations(organizationId).subscribe({
        next: (locations) => untracked(() => store.set(locations)),
        error: () => untracked(() => store.set([])),
      }));

    return store.asReadonly();
  }

  /** The display name of one location, or null when it is unknown or the id is null. Used by the
   * LOCATION column every document grid carries. */
  name(organizationId: string, locationId: string | null | undefined): string | null {
    if (!locationId) {
      return null;
    }

    return this.locations(organizationId)().find((x) => x.id === locationId)?.name ?? null;
  }

  /**
   * The tenant's Advanced-panel settings, whose `locationBearingDocumentTypes` is the server's own
   * answer to "which document types carry a location here?".
   *
   * <p><b>Asked, never re-derived.</b> `Domain.Tenancy.DocumentLocationScope` states the rule once,
   * on the server, and resolves it into that list; a client-side copy of "sales-only means Invoice,
   * Sales Order and Credit Note" would be a second source of truth that drifts the first time an
   * Admin flips the radio (phase-30's find-the-rule lesson, and phase 32's reason for shipping the
   * resolved list on this DTO at all).</p>
   */
  settings(organizationId: string): Signal<BillingLocationSettings> {
    const existing = this.settingsCache.get(organizationId);
    if (existing) {
      return existing.asReadonly();
    }

    const store = signal<BillingLocationSettings>(NO_SETTINGS);
    this.settingsCache.set(organizationId, store);

    // untracked -- see `locations` above.
    untracked(() =>
      this.organizationsService.getBillingLocationSettings(organizationId).subscribe({
        next: (settings) => untracked(() => store.set(settings)),
        error: () => untracked(() => store.set(NO_SETTINGS)),
      }));

    return store.asReadonly();
  }

  /** Does a document of this type carry a billing location for this tenant? */
  applies(organizationId: string, documentType: string): boolean {
    return this.settings(organizationId)().locationBearingDocumentTypes.includes(documentType);
  }

  /** Drop the cached list and settings so the next reader re-fetches them. */
  invalidate(organizationId: string): void {
    this.cache.delete(organizationId);
    this.settingsCache.delete(organizationId);
  }
}
