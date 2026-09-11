import { Component, computed, inject, input, output, signal } from '@angular/core';

import { BillingLocationStore } from './billing-location-store';

/**
 * Phase 35b -- the Billing Location filter on a report's filter bar.
 *
 * <p>Confirm-live 2026-09-10 read all 49 report screens in the reference product's catalogue: 43
 * carry a `Billing Location (All)` control and 6 do not. This is that control, rendered as one more
 * column in the filter row every report page in this app already has, so a report opts in with one
 * tag rather than fifteen lines — the same call phase 35a made for `LocationListFilter` inside
 * `ListChrome`.</p>
 *
 * <p><b>Not the same component as `LocationListFilter`, on purpose.</b> That one asks
 * `store.applies(orgId, documentType)`, because a document list has exactly one type and a tenant
 * whose `LocationScopeMode` excludes it has no dimension to filter. A report spans many types at
 * once — the Trial Balance reads eleven — so the only honest question here is whether the tenant has
 * the entitlement and more than one location at all. Reusing the list filter would have meant
 * passing it a document type no report actually has.</p>
 *
 * <p><b>Hidden below two locations</b>, like its sibling: a tenant with one location has no choice
 * to make, and an "All locations" select over a column every row shares is chrome over nothing.</p>
 */
@Component({
  selector: 'app-report-location-filter',
  imports: [],
  template: `
    @if (visible()) {
      <label class="form-label small fw-semibold" [attr.for]="controlId()">Billing Location</label>
      <select
        class="form-select"
        [id]="controlId()"
        (change)="onChange($any($event.target).value)"
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
export class ReportLocationFilter {
  private readonly store = inject(BillingLocationStore);

  readonly organizationId = input.required<string>();

  /** Distinct per page so the `<label for>` association WCAG 1.3.1 needs stays unique (phase 34a). */
  readonly controlId = input<string>('report-billing-location');

  /**
   * Emitted on every change, empty string meaning "All locations". An output rather than a
   * two-way `model()`: every consuming page reloads itself in response, and an `effect()` over a
   * signal the handler already acts on is the race phase 34b named.
   */
  readonly locationChange = output<string>();

  protected readonly locationId = signal('');

  protected readonly locations = computed(() =>
    this.store.locations(this.organizationId())().filter((x) => x.isActive));

  protected readonly visible = computed(() =>
    this.store.settings(this.organizationId())().multipleLocationsEnabled && this.locations().length > 1);

  protected onChange(value: string): void {
    this.locationId.set(value);
    this.locationChange.emit(value);
  }
}
