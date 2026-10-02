import { ChangeDetectionStrategy, Component, ViewEncapsulation, input } from '@angular/core';

import { POS_TAB_LABELS } from '../../../core/pos/pos.models';
import { PosOrder, PosOrderBillPreview } from '../../../core/pos/pos-restaurant.models';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { POS_RECEIPT_STYLES } from '../pos-receipt/pos-receipt';

/**
 * Phase 65 -- the estimate bill: what the table's order comes to, printed for the guests before they
 * pay (the location's <i>Estimate Bill</i> print toggle, phase 60).
 *
 * <p><b>It is not a tax invoice and says so</b>, at the top and the bottom. The vendor headed its
 * settled tax invoice "ESTIMATE BILL" (phase 59 defect 3); the reverse error -- an estimate a guest
 * could take for a tax invoice -- is the one this paper must not make. It carries no PAN block, no
 * invoice number and no print count: nothing is issued, so the 2072 procedure's copy rule, which is
 * about invoices, does not reach it, and printing it twice is two estimates, not a reprint.</p>
 *
 * <p><b>Its figures are the server's</b>: the preview of everything still to be billed, by the same
 * planner the bill posts through, so the estimate a guest reads is what the cashier will charge.
 * When part of the order is already paid it says how much, and prints what is left.</p>
 */
@Component({
  selector: 'app-pos-estimate',
  imports: [AmountPipe, NepaliDatePipe],
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: POS_RECEIPT_STYLES,
  template: `
    @let o = order();
    @let p = preview();
    <article class="pos-receipt" aria-label="Estimate bill">
      <p class="r-center r-title">Estimate Bill</p>
      <p class="r-center">अनुमानित बिल</p>
      <p class="r-copy">NOT A TAX INVOICE</p>
      <p class="r-center">{{ o.locationName }}</p>
      <div class="r-rule"></div>
      <p class="r-row"><span>Order</span><span class="r-strong">{{ o.code }}</span></p>
      @if (o.tableName) {
        <p class="r-row"><span>Table</span><span>{{ o.tableName }}{{ o.areaName ? ' · ' + o.areaName : '' }}</span></p>
        <p class="r-row"><span>Guests</span><span>{{ o.covers }}</span></p>
      } @else {
        <p class="r-row"><span>Type</span><span>{{ typeLabels[o.orderType] }}</span></p>
      }
      <p class="r-row"><span>Printed</span><span>{{ printedAt() | nepaliDate: 'datetime' }}</span></p>
      <div class="r-rule"></div>
      @for (line of p.lines; track line.orderLineId) {
        <p>{{ line.productName }}{{ line.unitName ? ' (' + line.unitName + ')' : '' }}</p>
        <p class="r-row r-indent"><span>{{ line.quantity }} × {{ line.rate | amount }}</span><span>{{ line.amount | amount }}</span></p>
      }
      <div class="r-rule"></div>
      <p class="r-row"><span>Sub Total</span><span>{{ p.amount | amount }}</span></p>
      @if (p.serviceCharge > 0) {
        <p class="r-row"><span>Service Charge</span><span>{{ p.serviceCharge | amount }}</span></p>
      }
      <p class="r-row"><span>VAT</span><span>{{ p.vat | amount }}</span></p>
      @if (p.roundOff !== 0) {
        <p class="r-row"><span>Round Off</span><span>{{ p.roundOff | amount }}</span></p>
      }
      <p class="r-row r-strong"><span>Estimated Total</span><span>{{ p.total | amount }}</span></p>
      @if (p.billedBefore > 0) {
        <p class="r-row"><span>Already paid</span><span>{{ p.billedBefore | amount }}</span></p>
      }
      <div class="r-rule"></div>
      <p class="r-center">This is an estimate, not a tax invoice.</p>
      <p class="r-center">The tax invoice is issued when the bill is paid.</p>
    </article>
  `,
})
export class PosEstimateView {
  readonly order = input.required<PosOrder>();
  readonly preview = input.required<PosOrderBillPreview>();
  readonly printedAt = input.required<string>();

  protected readonly typeLabels = POS_TAB_LABELS;
}
