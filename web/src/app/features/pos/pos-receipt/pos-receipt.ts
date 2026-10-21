import { ChangeDetectionStrategy, Component, ViewEncapsulation, computed, input } from '@angular/core';

import { POS_RECEIPT_TITLES, PosReceipt } from '../../../core/pos/pos.models';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';

/** The heading in Nepali beside the English, as the forms in the VAT Rules' schedules carry it. */
const NEPALI_TITLES: Readonly<Record<PosReceipt['title'], string>> = {
  Invoice: 'बीजक',
  TaxInvoice: 'कर बीजक',
  AbbreviatedTaxInvoice: 'संक्षिप्त कर बीजक',
};

/**
 * The 80 mm paper's stylesheet, shared by the sale's receipt and the refund's credit note (phase 63)
 * so the two print alike, and so a page rendering only one of them still prints. Unencapsulated, and
 * it hides `.pos-screen-root` when printing -- see the component comment below.
 */
export const POS_RECEIPT_STYLES = `
    .pos-receipt { width: 72mm; padding: 2mm; background: #fff; color: #000; font: 12px/1.35 'Courier New', monospace; }
    .pos-receipt p { margin: 0; }
    .pos-receipt .r-center { text-align: center; }
    .pos-receipt .r-title { font-weight: 700; font-size: 14px; text-transform: uppercase; }
    .pos-receipt .r-copy { border: 1px solid #000; text-align: center; font-weight: 700; margin: 1mm 0; }
    .pos-receipt .r-rule { border-top: 1px dashed #000; margin: 1.5mm 0; }
    .pos-receipt .r-row { display: flex; justify-content: space-between; gap: 2mm; }
    .pos-receipt .r-row > span:first-child { white-space: nowrap; }
    .pos-receipt .r-row > span:last-child { text-align: right; white-space: pre-line; }
    .pos-receipt .r-strong { font-weight: 700; }
    .pos-receipt .r-indent { padding-left: 3mm; }
    .pos-print-root { display: none; }
    @media print {
      .pos-screen-root, .skip-link { display: none !important; }
      .pos-print-root { display: block !important; }
      @page { margin: 2mm; }
    }
  `;

/**
 * Phase 62 -- the 80 mm receipt: a till sale exactly as it was issued, plus which printing this is.
 *
 * <p><b>The heading follows the bill</b> (Decision A; the vendor's defect 3 printed "ESTIMATE BILL"
 * on a settled tax invoice): Tax Invoice, Abbreviated Tax Invoice, or Invoice for a seller not
 * registered for VAT -- decided by the server and stored on the sale, so a reprint carries the
 * heading the original did.</p>
 *
 * <p><b>Service charge and round-off are lines of their own</b> (the vendor folded the service charge
 * into Taxable and printed neither).</p>
 *
 * <p><b>A reprint says so, twice</b> (Decision B): the Procedure Related to Computerized Invoicing
 * 2072, §6, allows one original and requires every later print to carry a visible "Copy of
 * Original" with how many times the invoice has been printed. The number is the server's count, not
 * this browser's.</p>
 *
 * <p><b>Every date is a bill's date</b> (phase 71): BS with AD beside it, whatever the calendar toggle says. A
 * full tax invoice (and a non-VAT seller's invoice) carries both dates its form asks for, the transaction date and
 * the issue date (Schedule 5; the e-billing procedure's Annex 6); an abbreviated one carries Schedule 6's single
 * date. A till sale is created approved, so its issue date is the Nepal day it was sold.</p>
 *
 * <p><b>Printing.</b> The page renders its screen inside `.pos-screen-root` and this component a
 * second time inside `.pos-print-root`; the stylesheet below shows only the second when printing.
 * It is unencapsulated for that reason, and it leaves the document with the till, because Angular
 * removes a component's styles when the component is destroyed.</p>
 */
@Component({
  selector: 'app-pos-receipt',
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
      <p class="r-center r-title">{{ title() }}</p>
      <p class="r-center">{{ nepaliTitle() }}</p>
      <p class="r-center r-strong">{{ r.sellerName }}</p>
      @if (r.sellerAddress) {
        <p class="r-center">{{ r.sellerAddress }}</p>
      }
      @if (r.sellerPan) {
        <p class="r-center">PAN: {{ r.sellerPan }}</p>
      }
      <p class="r-center">{{ r.locationName }}</p>
      @if (r.locationAddress) {
        <p class="r-center">{{ r.locationAddress }}</p>
      }
      <div class="r-rule"></div>
      <div class="r-row"><span>Bill No</span><span>{{ r.code }}</span></div>
      <div class="r-row"><span>Date</span><span>{{ r.date | nepaliDate: 'date' : 'bill' }}</span></div>
      @if (r.title !== 'AbbreviatedTaxInvoice' && r.soldAt) {
        <div class="r-row"><span>Issue Date</span><span>{{ r.soldAt | nepaliDate: 'date' : 'bill' }}</span></div>
      }
      <div class="r-row"><span>Time</span><span>{{ r.soldAt | nepaliDate: 'time' }}</span></div>
      @if (r.title !== 'AbbreviatedTaxInvoice') {
        <div class="r-row"><span>Customer</span><span>{{ r.customerName }}</span></div>
        @if (r.customerAddress) {
          <div class="r-row"><span>Address</span><span>{{ r.customerAddress }}</span></div>
        }
        @if (r.customerPan) {
          <div class="r-row"><span>Buyer PAN</span><span>{{ r.customerPan }}</span></div>
        }
      }
      <div class="r-rule"></div>
      @for (line of r.lines; track $index) {
        <p>{{ line.productName }}</p>
        @if (line.serialNumbers?.length) {
          <!-- Phase 95 -- when the location's bill template prints them. -->
          <p>SN: {{ line.serialNumbers!.join(', ') }}</p>
        }
        <div class="r-row r-indent">
          <span>{{ line.quantity }} {{ line.unitShortName }} &times; {{ line.rate | amount }}</span>
          <span>{{ line.amount | amount }}</span>
        </div>
        @if (line.discountPct > 0) {
          <p class="r-indent">less {{ line.discountPct }}% discount</p>
        }
        @if (line.discountPerUnit > 0) {
          <p class="r-indent">less {{ line.discountPerUnit | amount }} each discount</p>
        }
      }
      <div class="r-rule"></div>
      @if (r.discountAmount > 0) {
        <div class="r-row"><span>Gross</span><span>{{ r.grossAmount | amount }}</span></div>
        <!-- Phase 91 -- a guest's own discount (phase 83) apart from the order's and the lines', both above Sub Total. -->
        @if (r.discountAmount - (r.partDiscountAmount ?? 0) > 0) {
          <div class="r-row"><span>Discount</span><span>{{ r.discountAmount - (r.partDiscountAmount ?? 0) | amount }}</span></div>
        }
        @if ((r.partDiscountAmount ?? 0) > 0) {
          <div class="r-row"><span>Bill Discount</span><span>{{ r.partDiscountAmount | amount }}</span></div>
        }
      }
      <div class="r-row"><span>Sub Total</span><span>{{ r.subTotal | amount }}</span></div>
      @if (r.serviceCharge !== 0) {
        <div class="r-row"><span>Service Charge</span><span>{{ r.serviceCharge | amount }}</span></div>
      } @else if ((r.serviceChargeWaived ?? 0) > 0) {
        <!-- Phase 91 -- declined on this bill: said, not priced, so it cannot read as charged. -->
        <div class="r-row"><span>Service Charge</span><span>Waived</span></div>
      }
      @if (r.packagingFee !== 0) {
        <div class="r-row"><span>Packaging</span><span>{{ r.packagingFee | amount }}</span></div>
      }
      <div class="r-row"><span>Taxable</span><span>{{ r.taxableAmount | amount }}</span></div>
      @if (r.nonTaxableAmount !== 0) {
        <div class="r-row"><span>Non-taxable</span><span>{{ r.nonTaxableAmount | amount }}</span></div>
      }
      <div class="r-row"><span>VAT 13%</span><span>{{ r.vat | amount }}</span></div>
      @if (r.roundOff !== 0) {
        <div class="r-row"><span>Round Off</span><span>{{ r.roundOff | amount }}</span></div>
      }
      <div class="r-row r-strong"><span>Total</span><span>{{ r.grandTotal | amount }}</span></div>
      <p>{{ r.amountInWords }}</p>
      <div class="r-rule"></div>
      @for (tender of r.tenders; track $index) {
        <div class="r-row"><span>{{ tender.paymentModeName }}</span><span>{{ tender.amount | amount }}</span></div>
      }
      @if (r.changeAmount !== 0) {
        <div class="r-row"><span>Change</span><span>{{ r.changeAmount | amount }}</span></div>
      }
      @if (r.creditAmount !== 0) {
        <div class="r-row r-strong"><span>On credit</span><span>{{ r.creditAmount | amount }}</span></div>
      }
      <div class="r-rule"></div>
      <p>Session {{ r.sessionCode }} &middot; Cashier {{ r.cashierName }}</p>
      <p>Printed {{ r.printedAt | nepaliDate: 'datetime' : 'bill' }} by {{ r.printedByName }}</p>
      @if (isCopy()) {
        <p class="r-copy">COPY OF ORIGINAL &middot; printed {{ r.printNumber }} times</p>
      }
    </div>
  `,
})
export class PosReceiptView {
  readonly receipt = input.required<PosReceipt>();

  protected readonly isCopy = computed(() => this.receipt().printNumber > 1);
  protected readonly title = computed(() => POS_RECEIPT_TITLES[this.receipt().title]);
  protected readonly nepaliTitle = computed(() => NEPALI_TITLES[this.receipt().title]);
}
