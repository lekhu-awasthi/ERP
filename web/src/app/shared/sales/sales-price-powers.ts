import {
  ChangeDetectionStrategy,
  Component,
  Injector,
  Signal,
  WritableSignal,
  effect,
  inject,
  input,
  untracked,
} from '@angular/core';

import { Product } from '../../core/catalog/catalog.models';
import { BASE_CURRENCY_CODE } from '../../core/organizations/organizations.models';
import { PricedSalesDocumentType, SalesPricePowers } from '../../core/sales/sales.models';
import { SalesService } from '../../core/sales/sales.service';

export type { SalesPricePowers };

/**
 * Phase 81 -- keeps a Quotation, Sales Order or Invoice form's idea of what its user may price in step
 * with the document's billing location, so the form disables what Save would refuse
 * (`SalesPriceGate` on the server, which decides regardless).
 *
 * <p>The same shape as `locationAwareProducts`, and an `effect` for the same reason: nothing else
 * responds to the location signal, and re-reading the answer is idempotent. Null until the answer
 * arrives, which the form reads as "allowed" -- a hint that flickers to disabled for an Admin on every
 * open would be worse than one that arrives a moment late for a clerk.</p>
 */
export function salesPricePowers(
  organizationId: string,
  documentType: PricedSalesDocumentType,
  locationId: Signal<string>,
  target: WritableSignal<SalesPricePowers | null>,
  options?: { injector?: Injector },
): void {
  const sales = inject(SalesService);
  const injector = options?.injector;

  effect(
    () => {
      const location = locationId();

      untracked(() =>
        sales.getPricePowers(organizationId, documentType, location || undefined).subscribe({
          next: (powers) => untracked(() => target.set(powers)),
          error: () => untracked(() => target.set(null)),
        }));
    },
    injector ? { injector } : undefined,
  );
}

/**
 * Whether a line's Rate input is the catalogue's to set, not the user's: they may not change a rate,
 * the document is in the base currency, and the product has a price in the line's unit. The same three
 * exemptions the server's gate makes -- a foreign-currency rate and a product with no price have
 * nothing to keep.
 */
export function rateIsLocked(
  powers: SalesPricePowers | null,
  currencyCode: string,
  product: Product | undefined,
  unitId: string,
): boolean {
  if (!powers || powers.canChangeRate || currencyCode !== BASE_CURRENCY_CODE || !product) {
    return false;
  }

  const price = !unitId || unitId === product.primaryUnitId
    ? product.sellingPrice
    : (product.secondaryUnits ?? []).find((u) => u.unitId === unitId)?.sellingPrice ?? 0;

  return price > 0;
}

/**
 * Phase 81 -- says why a discount or a rate cannot be changed, naming the key, as the till does
 * (phase 76). Renders nothing while the user may do both; it is guidance, not a status message, so it
 * is not a live region (phase 40's rule about furniture).
 */
@Component({
  selector: 'app-sales-price-note',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (powers(); as p) {
      @if (!p.canDiscount) {
        <p class="small text-body-secondary mb-1">
          Giving a discount needs the Sales.Invoice.Discount permission at this location.
        </p>
      }
      @if (!p.canChangeRate && currencyCode() === baseCurrency) {
        <p class="small text-body-secondary mb-1">
          A rate other than the product's price needs the Sales.Invoice.ChangeRate permission at this location.
        </p>
      }
    }
  `,
})
export class SalesPriceNote {
  readonly powers = input<SalesPricePowers | null>(null);
  readonly currencyCode = input<string>(BASE_CURRENCY_CODE);
  protected readonly baseCurrency = BASE_CURRENCY_CODE;
}
