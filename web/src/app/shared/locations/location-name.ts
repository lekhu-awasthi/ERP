import { Component, computed, inject, input } from '@angular/core';

import { BillingLocationStore } from './billing-location-store';

/**
 * Phase 35a -- one LOCATION cell in a document grid.
 *
 * <p>A component rather than a pipe because the lookup depends on a signal the store owns: a pure
 * pipe would cache against its arguments and never see the list arrive, and an impure one would be
 * phase-23's `NepaliDatePipe` problem for a value that is not global. Reading the store here costs
 * nothing per cell -- {@link BillingLocationStore} issues one request per organization however many
 * rows render.</p>
 *
 * <p>Renders nothing when the row carries no location, so a grid on a tenant without the
 * entitlement shows an empty column rather than a column of dashes.</p>
 */
@Component({
  selector: 'app-location-name',
  imports: [],
  template: `
    @if (name(); as label) {
      <span class="badge bg-light text-muted border">
        <i aria-hidden="true" class="bi bi-geo-alt"></i> {{ label }}
      </span>
    }
  `,
})
export class LocationName {
  private readonly store = inject(BillingLocationStore);

  readonly organizationId = input.required<string>();
  readonly locationId = input.required<string | null>();

  protected readonly name = computed(() => this.store.name(this.organizationId(), this.locationId()));
}
