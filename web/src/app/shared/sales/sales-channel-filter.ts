import { Component, input, output } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { SalesChannel } from '../../core/sales/sales.models';

/**
 * Phase 66 -- the Sales Channel filter on the ERP sales reports (Sales by Item, by Customer, Sales
 * Summary, Sales Master, Sales Register) and on System Audit, where Point of Sale is POS activity.
 *
 * The vendor's POS reports are its ERP reports read with channel=POS (read live 2026-10-02); ours are
 * the same reports with this filter, so a till's figures and the ERP's are one report, two views. A POS
 * screen links here with ?channel=Pos, which {@link initialSalesChannel} reads.
 */
@Component({
  selector: 'app-sales-channel-filter',
  template: `
    <label class="form-label small fw-semibold" [for]="controlId()">Sales Channel</label>
    <select class="form-select" [id]="controlId()" (change)="onChange($event)">
      <option value="" [selected]="!value()">All channels</option>
      <option value="Erp" [selected]="value() === 'Erp'">ERP</option>
      <option value="Pos" [selected]="value() === 'Pos'">Point of Sale</option>
    </select>
  `,
})
export class SalesChannelFilter {
  readonly controlId = input<string>('sales-channel');

  /** The channel the page holds; per-option [selected] rather than [value] on the select (phase 5). */
  readonly value = input<SalesChannel | null>(null);

  readonly channelChange = output<SalesChannel | null>();

  protected onChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.channelChange.emit(value === 'Erp' || value === 'Pos' ? value : null);
  }
}

/** The channel a page was opened with (?channel=Pos from a POS screen), or every channel. */
export function initialSalesChannel(route: ActivatedRoute): SalesChannel | null {
  const value = route.snapshot.queryParamMap?.get('channel') ?? null;
  return value === 'Erp' || value === 'Pos' ? value : null;
}
