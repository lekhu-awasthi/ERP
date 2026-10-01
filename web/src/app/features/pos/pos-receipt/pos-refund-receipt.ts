import { ChangeDetectionStrategy, Component, ViewEncapsulation, computed, input } from '@angular/core';

import { PosRefundReceipt } from '../../../core/pos/pos.models';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { POS_RECEIPT_STYLES } from './pos-receipt';

/**
 * Phase 63 -- a till refund's credit note on 80 mm paper, laid out to what VAT Rules 2053, Rule 20(1)
 * asks a credit note to carry (phase-63-status.md Decision A): its number and date; the supplier's
 * name, address and registration number; the recipient's name and address (and registration number
 * if registered) -- printed even when the sale was an abbreviated tax invoice without a buyer block,
 * because the rule asks it of the note; the number and date of the tax invoice it relates to; the
 * goods and the reason; and the amount of the credit and of its tax.
 *
 * <p>Like the sale's receipt, a reprint says "Copy of Original" with the server's print count, top
 * and bottom (the 2072 procedure, §6, read to cover credit notes), and service charge and round-off
 * are lines of their own.</p>
 */
@Component({
  selector: 'app-pos-refund-receipt',
  imports: [AmountPipe, NepaliDatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  styles: POS_RECEIPT_STYLES,
  template: `
    @let r = receipt();
    <div class="pos-receipt">
      @if (isCopy()) {
        <p class="r-copy">COPY OF ORIGINAL &middot; printed {{ r.printNumber }} times</p>
      }
      <p class="r-center r-title">Credit Note</p>
      <p class="r-center">क्रेडिट नोट</p>
      <p class="r-center r-strong">{{ r.sellerName }}</p>
      @if (r.sellerAddress) {
        <p class="r-center">{{ r.sellerAddress }}</p>
      }
      @if (r.sellerPan) {
        <p class="r-center">PAN: {{ r.sellerPan }}</p>
      }
      <p class="r-center">{{ r.locationName }}</p>
      <div class="r-rule"></div>
      <div class="r-row"><span>Credit Note No</span><span>{{ r.code }}</span></div>
      <div class="r-row"><span>Date</span><span>{{ r.date | nepaliDate }}</span></div>
      <div class="r-row"><span>Time</span><span>{{ r.refundedAt | nepaliDate: 'datetime' }}</span></div>
      @if (r.invoiceCode) {
        <div class="r-row"><span>Against Bill No</span><span>{{ r.invoiceCode }}</span></div>
      }
      @if (r.invoiceDate) {
        <div class="r-row"><span>Bill Date</span><span>{{ r.invoiceDate | nepaliDate }}</span></div>
      }
      <div class="r-row"><span>Customer</span><span>{{ r.customerName }}</span></div>
      @if (r.customerAddress) {
        <div class="r-row"><span>Address</span><span>{{ r.customerAddress }}</span></div>
      }
      @if (r.customerPan) {
        <div class="r-row"><span>Buyer PAN</span><span>{{ r.customerPan }}</span></div>
      }
      @if (r.reason) {
        <p>Reason: {{ r.reason }}</p>
      }
      <div class="r-rule"></div>
      @for (line of r.lines; track $index) {
        <p>{{ line.productName }}</p>
        <div class="r-row r-indent">
          <span>{{ line.quantity }} {{ line.unitShortName }} &times; {{ line.rate | amount }}</span>
          <span>{{ line.amount | amount }}</span>
        </div>
        @if (line.discountPct > 0) {
          <p class="r-indent">less {{ line.discountPct }}% discount</p>
        }
      }
      <div class="r-rule"></div>
      @if (r.discountAmount > 0) {
        <div class="r-row"><span>Gross</span><span>{{ r.grossAmount | amount }}</span></div>
        <div class="r-row"><span>Discount</span><span>{{ r.discountAmount | amount }}</span></div>
      }
      <div class="r-row"><span>Sub Total</span><span>{{ r.subTotal | amount }}</span></div>
      @if (r.serviceCharge !== 0) {
        <div class="r-row"><span>Service Charge</span><span>{{ r.serviceCharge | amount }}</span></div>
      }
      <div class="r-row"><span>Taxable</span><span>{{ r.taxableAmount | amount }}</span></div>
      @if (r.nonTaxableAmount !== 0) {
        <div class="r-row"><span>Non-taxable</span><span>{{ r.nonTaxableAmount | amount }}</span></div>
      }
      <div class="r-row"><span>VAT 13%</span><span>{{ r.vat | amount }}</span></div>
      @if (r.roundOff !== 0) {
        <div class="r-row"><span>Round Off</span><span>{{ r.roundOff | amount }}</span></div>
      }
      <div class="r-row r-strong"><span>Total Credit</span><span>{{ r.grandTotal | amount }}</span></div>
      <p>{{ r.amountInWords }}</p>
      <div class="r-rule"></div>
      @for (payout of r.payouts; track $index) {
        <div class="r-row"><span>Paid back: {{ payout.paymentModeName }}</span><span>{{ payout.amount | amount }}</span></div>
      }
      @if (r.toAccount !== 0) {
        <div class="r-row r-strong"><span>Off your account</span><span>{{ r.toAccount | amount }}</span></div>
      }
      <div class="r-rule"></div>
      <p>Session {{ r.sessionCode }} &middot; Cashier {{ r.cashierName }}</p>
      <p>Printed {{ r.printedAt | nepaliDate: 'datetime' }} by {{ r.printedByName }}</p>
      @if (isCopy()) {
        <p class="r-copy">COPY OF ORIGINAL &middot; printed {{ r.printNumber }} times</p>
      }
    </div>
  `,
})
export class PosRefundReceiptView {
  readonly receipt = input.required<PosRefundReceipt>();

  protected readonly isCopy = computed(() => this.receipt().printNumber > 1);
}
