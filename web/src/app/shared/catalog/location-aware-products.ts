import { Injector, Signal, WritableSignal, effect, inject, untracked } from '@angular/core';

import { CatalogService } from '../../core/catalog/catalog.service';
import { Product, ProductType, ProductVariantFilter } from '../../core/catalog/catalog.models';

/**
 * Phase 36 -- keeps a document form's product picker in step with the document's own billing
 * location.
 *
 * <p><b>What it is for.</b> A Product carries a set of billing locations it is available at (empty
 * = everywhere), and the reference product filters the line picker by the document's header
 * location: with a product scoped to POS Retail alone, the Invoice form's picker returned nothing
 * at HeadOffice and returned that product the moment the header was switched -- one
 * `products-minimized?…&location_id=…` call per switch, so the filtering is the server's
 * (confirmed live 2026-09-11). This is that behaviour, in the one place all eleven
 * location-bearing document forms can share.</p>
 *
 * <p><b>It reloads when the location changes, from the first version</b> -- phase 34b's rule that a
 * filter a screen displays but does not apply is worse than no filter. An `effect` is the right
 * mechanism here and not the race phase 34b warned about: nothing else responds to the location
 * signal, and re-reading a picker list is idempotent.</p>
 *
 * <p><b>Never removes what is already on the document.</b> A saved line naming a product that is no
 * longer offered at this location still has to render its own name, so the caller's list is only
 * ever the picker's source -- the form reads a line's product from the line.</p>
 */
export function locationAwareProducts(
  organizationId: string,
  locationId: Signal<string>,
  target: WritableSignal<Product[]>,
  options?: { type?: ProductType; variantFilter?: ProductVariantFilter; injector?: Injector },
): void {
  const catalog = inject(CatalogService);
  const injector = options?.injector;

  effect(
    () => {
      const location = locationId();

      untracked(() =>
        catalog
          .listAllProducts(organizationId, options?.type, options?.variantFilter ?? 'Transactable', location || undefined)
          .subscribe({
            // untracked on the write too: a synchronous source (a test double) would otherwise
            // write a signal inside a running effect's read phase -- phase 35a's NG0600.
            next: (products) => untracked(() => target.set(products)),
            error: () => untracked(() => target.set([])),
          }));
    },
    injector ? { injector } : undefined,
  );
}
