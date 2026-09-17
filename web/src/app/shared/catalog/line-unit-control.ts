import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import { Product, UnitOfMeasurement } from '../../core/catalog/catalog.models';

/** One selectable unit on a line: the product's primary, or one of its secondary units. */
export interface LineUnitOption {
  unitId: string;
  shortName: string;
  conversionRate: number;
  sellingPrice: number;
  purchasePrice: number;
}

/**
 * Phase 52 -- the unit control that sits **inside** the Qty cell on all eight line grids.
 *
 * <p><b>Why a component and not eight copies.</b> CLAUDE.md's rule is to retire copies 1..N before
 * writing copy N+1, and this control has three behaviours that have to agree everywhere: which
 * units it offers, what it does when the product has only one, and what it names itself to a
 * screen reader. Eight hand-written copies is eight chances for one of them to drift, and it is the
 * shape phase 33 found in `GrantedPermissionReader` -- two inlined copies of one join, both missing
 * the same filter.</p>
 *
 * <p><b>The shape is the live product's, read 2026-09-17 on the reference tenant.</b> It is not a
 * column of its own: the grid is
 * `Product / service | Qty | Item Batch | Rate | Discount | Tax | Amount` and the unit renders
 * right of the quantity input inside the same cell. For a product carrying only its primary unit
 * the vendor renders a greyed, `cursor: not-allowed` label rather than a one-option dropdown --
 * that is a control that is <i>disabled</i>, not absent, which is phase 44's parent-and-modifier
 * shape (Display Warehouse in Column is disabled until Group by Warehouse is ticked). For a product
 * with secondary units it is a real select listing the <b>whole</b> matrix, primary included,
 * because the vendor's own dropdown offered both `BTL` and `PIS` on `Steel rods`.</p>
 *
 * <p><b>The conversion factor is never sent.</b> This control emits a unit id; the server resolves
 * the factor from the catalogue and freezes it on the line. A client able to state its own factor
 * could claim a conversion the product does not have, which is the guarantee the whole phase rests
 * on.</p>
 */
@Component({
  selector: 'app-line-unit',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (options().length > 1) {
      <!-- Bind [selected] per option, never [value] on the select: a signal-fed native select
           bound by [value] persisted the wrong id for three phases (phases 5/6/7). -->
      <select
        class="form-select form-select-sm w-auto"
        [attr.aria-label]="label()"
        [disabled]="disabled()"
        (change)="onChange($event)"
      >
        @for (option of options(); track option.unitId) {
          <option [value]="option.unitId" [selected]="option.unitId === selectedId()">
            {{ option.shortName }}
          </option>
        }
      </select>
    } @else {
      <span class="text-body-secondary small text-nowrap">{{ primaryName() }}</span>
    }
  `,
})
export class LineUnitControl {
  /** The picker's product list -- the same array the line's product was chosen from. */
  readonly products = input.required<readonly Product[]>();

  /** The tenant's units, from `UnitOfMeasurementStore`. A Product carries unit *ids* only, so
   *  without this the control has nothing to render. Empty degrades to the pre-phase-52 behaviour:
   *  every line in the primary unit, with no name beside the quantity. */
  readonly units = input.required<readonly UnitOfMeasurement[]>();

  readonly productId = input<string>('');

  /** The unit currently on the line; empty or null means the product's own primary unit. */
  readonly unitId = input<string | null>(null);

  readonly disabled = input(false);

  /** Named per line so a screen reader hears which row it is on. */
  readonly label = input('Unit');

  readonly unitChange = output<LineUnitOption>();

  /** The chosen product, or undefined while the line is still blank. */
  private readonly product = computed(() =>
    this.products().find((p) => p.id === this.productId()),
  );

  /**
   * The product's whole unit matrix: its primary first, then its secondary units. The primary is
   * synthesised here rather than read from `secondaryUnits`, because this codebase keeps it on
   * `Product.primaryUnitId` and leaves the collection genuinely empty -- the reference product
   * materialises it as row 0 of `secondary_units[]` instead, which is a presentation difference and
   * not a model one (phase 45).
   */
  readonly options = computed<LineUnitOption[]>(() => {
    const product = this.product();

    if (!product) {
      return [];
    }

    const primary: LineUnitOption = {
      unitId: product.primaryUnitId,
      shortName: this.primaryName(),
      conversionRate: 1,
      sellingPrice: product.sellingPrice,
      purchasePrice: product.purchasePrice,
    };

    const secondaries = (product.secondaryUnits ?? []).map((u) => ({
      unitId: u.unitId,
      shortName: this.nameOf(u.unitId),
      conversionRate: u.conversionRate,
      sellingPrice: u.sellingPrice,
      purchasePrice: u.purchasePrice,
    }));

    // A secondary unit whose lookup row has not arrived (or was deactivated) would render as a
    // blank option the user cannot tell apart from the next one, so it is dropped rather than
    // shown nameless -- phase 40's rule that a control which hides itself owns its own label,
    // applied to an option.
    return [primary, ...secondaries.filter((u) => u.shortName !== '')];
  });

  /** What the disabled label shows: the product's primary unit, or nothing before one is chosen. */
  readonly primaryName = computed(() => {
    const product = this.product();
    return product ? this.nameOf(product.primaryUnitId) : '';
  });

  private nameOf(unitId: string): string {
    return this.units().find((u) => u.id === unitId)?.shortName ?? '';
  }

  /** Null on the line means the primary, so the select still shows the right row. */
  readonly selectedId = computed(() => this.unitId() || this.product()?.primaryUnitId || '');

  protected onChange(event: Event): void {
    const unitId = (event.target as HTMLSelectElement).value;
    const option = this.options().find((o) => o.unitId === unitId);

    if (option) {
      this.unitChange.emit(option);
    }
  }
}
