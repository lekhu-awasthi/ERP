import { Component, computed, inject, input, model } from '@angular/core';

import { BillingLocationStore } from './billing-location-store';

/**
 * Phase 35a -- the Billing Location filter on a document list.
 *
 * <p>The reference product renders this as a funnel on the LOCATION column its grids carry in
 * second position (confirm-live 2026-09-10: Quotation, Invoice, Journal Voucher, Purchase Bill and
 * Warehouse Transfer all show it). This app's list chrome is a toolbar of selects, so the control
 * takes that shape here -- the same call phase 32 made for the Invoice list, extracted.</p>
 *
 * <p><b>Hidden below two locations.</b> A tenant with one location has no choice to make, and a
 * tenant whose scope excludes this document type has no dimension at all; in both cases showing an
 * "All locations" select would be a filter over a column every row shares.</p>
 */
@Component({
  selector: 'app-location-list-filter',
  imports: [],
  template: `
    @if (visible()) {
      <!--
        Phase 40: the caption lives here, inside the @if, not in the host chrome.
        ListChrome used to render "Billing location" above this tag unconditionally while this
        control renders nothing on a tenant with one location — which is the default tenant. So 22
        list screens showed a label naming no control, and no source-level guard could see it,
        because the control disappears at *runtime*. Same shape as ReportLocationFilter, which
        already owned its own label.
      -->
      <label class="form-label small text-muted mb-1 d-block" [attr.for]="controlId">Billing location</label>
      <select
        class="form-select form-select-sm w-auto"
        [id]="controlId"
        (change)="locationId.set($any($event.target).value)"
      >
        <option value="" [selected]="locationId() === ''">All locations</option>
        @for (location of locations(); track location.id) {
          <option [value]="location.id" [selected]="locationId() === location.id">
            {{ location.name }} ({{ location.code }})
          </option>
        }
      </select>
    }
  `,
})
export class LocationListFilter {
  private readonly store = inject(BillingLocationStore);

  readonly organizationId = input.required<string>();
  readonly documentType = input.required<string>();

  /** Unique per screen, so the label can point at the select. One chrome per page, so one id. */
  protected readonly controlId = 'list-chrome-location';

  /** Empty means "All locations", which is the default and what a pre-phase-35 caller sent. */
  readonly locationId = model<string>('');

  protected readonly locations = computed(() =>
    this.store.locations(this.organizationId())().filter((x) => x.isActive));

  protected readonly visible = computed(() =>
    this.store.applies(this.organizationId(), this.documentType()) && this.locations().length > 1);
}
