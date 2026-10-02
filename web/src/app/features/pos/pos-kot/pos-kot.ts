import { ChangeDetectionStrategy, Component, ViewEncapsulation, input } from '@angular/core';

import { POS_TAB_LABELS } from '../../../core/pos/pos.models';
import { KitchenTicket, PosOrder } from '../../../core/pos/pos-restaurant.models';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { POS_RECEIPT_STYLES } from '../pos-receipt/pos-receipt';

/** One ticket to print and which printing of it this is. */
export interface PrintedTicket {
  ticket: KitchenTicket;
  printNumber: number;
}

/**
 * Phase 64 -- the 80 mm kitchen order ticket: what one send told one station (phase 59 Decision F).
 *
 * <p>Headed by the station, then the ticket's number (<code>ORD0007-2</code>: the order and the send,
 * because the vendor's ticket has none of its own), the table or the order type, guests, time and who
 * sent it. The lines are what the kitchen cooks: quantity, item, unit and the line's note (the vendor's
 * defect 9 kept the note in the product's description). A cancellation says so in capitals with its
 * reason, its quantities negative.</p>
 *
 * <p><b>A reprint says REPRINT</b>, from the server's count, so a kitchen handed the paper twice does
 * not cook the order twice. Several tickets print as several pages, one per station.</p>
 *
 * <p>Printed the receipt's way: rendered again inside the page's <code>.pos-print-root</code>, with the
 * shared unencapsulated 80 mm stylesheet.</p>
 */
@Component({
  selector: 'app-pos-kot',
  imports: [NepaliDatePipe],
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [
    POS_RECEIPT_STYLES,
    `
    .pos-receipt.pos-kot { font-size: 14px; }
    .pos-kot .k-line { font-size: 16px; font-weight: 700; }
    .pos-kot + .pos-kot { break-before: page; page-break-before: always; }
  `,
  ],
  template: `
    @for (printed of tickets(); track printed.ticket.id) {
      @let ticket = printed.ticket;
      <article class="pos-receipt pos-kot" [attr.aria-label]="'Kitchen ticket ' + ticket.number">
        <p class="r-center r-title">{{ ticket.isCancellation ? 'Cancellation' : 'Kitchen Order' }}</p>
        <p class="r-center r-strong">{{ ticket.kitchenStationName }}</p>
        @if (printed.printNumber > 1) {
          <p class="r-copy">REPRINT ({{ printed.printNumber }})</p>
        }
        <div class="r-rule"></div>
        <p class="r-row"><span>KOT</span><span class="r-strong">{{ ticket.number }}</span></p>
        @if (order().tableName) {
          <p class="r-row"><span>Table</span><span class="r-strong">{{ order().tableName }}{{ order().areaName ? ' · ' + order().areaName : '' }}</span></p>
          <p class="r-row"><span>Guests</span><span>{{ order().covers }}</span></p>
        } @else {
          <p class="r-row"><span>Type</span><span class="r-strong">{{ typeLabels[order().orderType] }}</span></p>
          <p class="r-row"><span>Customer</span><span>{{ order().contactName }}</span></p>
        }
        <p class="r-row"><span>Sent</span><span>{{ ticket.createdAt | nepaliDate: 'datetime' }}</span></p>
        <p class="r-row"><span>By</span><span>{{ ticket.createdByName }}</span></p>
        <div class="r-rule"></div>
        @for (line of ticket.lines; track line.orderLineId) {
          <p class="k-line">{{ line.quantity }} × {{ line.productName }}{{ line.unitName ? ' (' + line.unitName + ')' : '' }}</p>
          @if (line.note) {
            <p class="r-indent">» {{ line.note }}</p>
          }
        }
        @if (ticket.reason) {
          <div class="r-rule"></div>
          <p>Reason: {{ ticket.reason }}</p>
        }
        <div class="r-rule"></div>
        <p class="r-center">{{ order().code }} &middot; {{ order().locationName }}</p>
      </article>
    }
  `,
})
export class PosKotView {
  readonly order = input.required<PosOrder>();
  readonly tickets = input.required<PrintedTicket[]>();

  protected readonly typeLabels = POS_TAB_LABELS;
}
