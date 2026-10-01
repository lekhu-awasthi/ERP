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
  styles: `
    .pos-receipt { width: 72mm; padding: 2mm; background: #fff; color: #000; font: 12px/1.35 'Courier New', monospace; }
    .pos-receipt p { margin: 0; }
    .pos-receipt .r-center { text-align: center; }
    .pos-receipt .r-title { font-weight: 700; font-size: 14px; text-transform: uppercase; }
    .pos-receipt .r-copy { border: 1px solid #000; text-align: center; font-weight: 700; margin: 1mm 0; }
    .pos-receipt .r-rule { border-top: 1px dashed #000; margin: 1.5mm 0; }
    .pos-receipt .r-row { display: flex; justify-content: space-between; gap: 2mm; }
    .pos-receipt .r-strong { font-weight: 700; }
    .pos-receipt .r-indent { padding-left: 3mm; }
    .pos-print-root { display: none; }
    @media print {
      .pos-screen-root, .skip-link { display: none !important; }
      .pos-print-root { display: block !important; }
      @page { margin: 2mm; }
    }
  `,
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
      <div class="r-rule"></div>
      <div class="r-row"><span>Bill No</span><span>{{ r.code }}</span></div>
      <div class="r-row"><span>Date</span><span>{{ r.date | nepaliDate }}</span></div>
      <div class="r-row"><span>Time</span><span>{{ r.soldAt | nepaliDate: 'datetime' }}</span></div>
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
      <p>Printed {{ r.printedAt | nepaliDate: 'datetime' }} by {{ r.printedByName }}</p>
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
