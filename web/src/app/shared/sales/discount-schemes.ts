import { Injector, Signal, effect, inject, untracked } from '@angular/core';

import { ApplicableDiscounts, SlabStep } from '../../core/sales/sales.models';
import { SalesService } from '../../core/sales/sales.service';

/**
 * Phase 97 -- discount schemes on a Quotation, Sales Order or Invoice form. A scheme's percent is where a line starts and
 * the slab its amount reaches is where the document's discount starts (the user, 2026-10-21: "the scheme is the start").
 * The server's price gate keeps exactly these figures without the discount key (`DiscountSchemeRates`, one reader for
 * both), so a clerk whose discount inputs are disabled still gets them.
 *
 * <p>A line follows its product's scheme while `schemeFollows` is true: from the moment its product is picked until the
 * user changes or removes its discount. A line read back from a saved draft or a conversion does not follow: what was saved
 * is never moved by opening it. The document's discount follows the slab on the same terms.</p>
 */
export interface SchemeFollowingLine {
  productId: string;
  discountPct: number;
  schemeFollows?: boolean;
}

/** The slab percent for a bill of `amount` before VAT, in rupees: the highest step it reaches (the server's
 * `DiscountSchemeRates.ForBillAmount`). */
export function slabDiscountPct(slabs: readonly SlabStep[], amount: number): number {
  let pct = 0;
  for (const step of slabs) {
    if (amount >= step.minimumAmount) {
      pct = step.discountPct;
    }
  }
  return pct;
}

/** The line as its product's scheme leaves it: its discount moved to the scheme's when it is still following one. */
export function withSchemeRate<T extends SchemeFollowingLine>(line: T, discounts: ApplicableDiscounts): T {
  if (!line.schemeFollows || !line.productId) {
    return line;
  }
  const pct = discounts.products.find((p) => p.productId === line.productId)?.discountPct ?? 0;
  return pct === line.discountPct ? line : { ...line, discountPct: pct };
}

/**
 * Re-reads the schemes whenever the document's location or date changes, for the products already on it, and hands the
 * answer to `apply` (which sets the slab ladder and moves each following line). An `effect` for the reason
 * `salesPricePowers` gives: nothing else responds to those two signals, and re-reading is idempotent. A user who may not
 * read schemes (a 403) simply gets none.
 */
export function followDiscountSchemes(options: {
  organizationId: string;
  locationId: Signal<string>;
  date: Signal<string>;
  productIds: () => string[];
  apply: (discounts: ApplicableDiscounts) => void;
  injector?: Injector;
}): void {
  const sales = inject(SalesService);

  effect(
    () => {
      const location = options.locationId();
      const date = options.date();
      if (!date) {
        return;
      }

      untracked(() =>
        sales
          .getApplicableDiscounts(options.organizationId, date, location || undefined, options.productIds())
          .subscribe({
            next: (discounts) => untracked(() => options.apply(discounts)),
            error: () => untracked(() => options.apply({ products: [], slabs: [] })),
          }));
    },
    options.injector ? { injector: options.injector } : undefined,
  );
}
