import { Injectable, Signal, inject, signal, untracked } from '@angular/core';

import { UnitOfMeasurement } from '../../core/catalog/catalog.models';
import { CatalogService } from '../../core/catalog/catalog.service';

/**
 * Phase 52 -- one shared, cached read of a tenant's units of measurement.
 *
 * <p><b>Why it exists.</b> A Product carries unit <i>ids</i> only -- `primaryUnitId` and a
 * `secondaryUnits[]` of `(unitId, conversionRate, prices)` -- so the unit control in the Qty cell
 * has nothing to render without the lookup. Eight document forms need it, and a per-form request
 * would be eight copies of one call; a per-row resolve would be one per cell. This is phase 35a's
 * `BillingLocationStore` answering the same shape of question, and it is deliberately the same
 * shape of answer.</p>
 *
 * <p><b>The `untracked` is load-bearing and is not defensive noise.</b> The first caller is a
 * `computed()` inside a line grid, so a source that resolves <i>synchronously</i> -- a test double,
 * a warmed cache -- would write this signal during that computed and throw NG0600. Real HTTP
 * resolves later and has no active consumer, which is exactly why the bug hides in production and
 * surfaces only under test (phase 35a).</p>
 *
 * <p>A failed read resolves to an empty list rather than staying pending for ever: a tenant whose
 * units cannot be read must still get a working document form, and an empty list degrades to the
 * pre-phase-52 behaviour -- every line in the product's primary unit, with no name beside the
 * quantity.</p>
 */
@Injectable({ providedIn: 'root' })
export class UnitOfMeasurementStore {
  private readonly catalog = inject(CatalogService);

  private readonly cache = new Map<string, ReturnType<typeof signal<UnitOfMeasurement[]>>>();

  /** The tenant's units. Empty until the first read resolves. */
  units(organizationId: string): Signal<UnitOfMeasurement[]> {
    const existing = this.cache.get(organizationId);
    if (existing) {
      return existing.asReadonly();
    }

    const store = signal<UnitOfMeasurement[]>([]);
    this.cache.set(organizationId, store);

    untracked(() =>
      this.catalog.listUnitsOfMeasurement(organizationId).subscribe({
        next: (units) => untracked(() => store.set(units)),
        error: () => untracked(() => store.set([])),
      }));

    return store.asReadonly();
  }

  /**
   * The write half. The Units Of Measurement list page is the only screen that creates or renames a
   * unit, and it calls this after a successful save so a document form opened later in the same
   * session sees the new row.
   */
  invalidate(organizationId: string): void {
    this.cache.delete(organizationId);
  }
}
