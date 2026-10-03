import { Component, input, output } from '@angular/core';

import { BsDateInput } from '../formatting/bs-date-input';
import { nepalToday } from '../formatting/nepal-time';

/** A period of Nepal business days, `yyyy-MM-dd` both ends inclusive. */
export interface PosPeriod {
  fromDate: string;
  toDate: string;
}

/** `yyyy-MM-dd` moved by whole days, on the calendar alone (no clock, so no time zone to get wrong). */
export function shiftDate(date: string, days: number): string {
  const ms = Date.parse(`${date}T00:00:00Z`) + days * 86_400_000;
  return new Date(ms).toISOString().slice(0, 10);
}

/** The last `days` Nepal business days, ending today. */
export function lastDays(days: number): PosPeriod {
  const today = nepalToday();
  return { fromDate: shiftDate(today, -(days - 1)), toDate: today };
}

/**
 * Phase 66 -- the period every POS report and the dashboard is read over: two dates and the three
 * presets the vendor's dashboard offers (Today, Last 7 days, Last 30 days -- read live 2026-10-02). The
 * days are Nepal business days, the till's own (a sale's date is the Nepal date it was rung up on).
 */
@Component({
  selector: 'app-pos-period-filter',
  imports: [BsDateInput],
  template: `
    <div class="row g-3 align-items-end">
      <div class="col-12 col-sm-6 col-lg-3">
        <label class="form-label small fw-semibold" [for]="idPrefix() + '-from'">From</label>
        <app-bs-date-input [inputId]="idPrefix() + '-from'" [inputClass]="'form-control'"
          [value]="fromDate()" (valueChange)="emit($event, toDate())" />
      </div>
      <div class="col-12 col-sm-6 col-lg-3">
        <label class="form-label small fw-semibold" [for]="idPrefix() + '-to'">To</label>
        <app-bs-date-input [inputId]="idPrefix() + '-to'" [inputClass]="'form-control'"
          [value]="toDate()" (valueChange)="emit(fromDate(), $event)" />
      </div>
      <div class="col-12 col-lg-6">
        <div class="d-flex flex-wrap gap-2" role="group" [attr.aria-label]="'Quick periods'">
          <button type="button" class="btn btn-outline-secondary btn-sm" (click)="preset(1)">Today</button>
          <button type="button" class="btn btn-outline-secondary btn-sm" (click)="preset(7)">Last 7 days</button>
          <button type="button" class="btn btn-outline-secondary btn-sm" (click)="preset(30)">Last 30 days</button>
        </div>
      </div>
    </div>
  `,
})
export class PosPeriodFilter {
  readonly fromDate = input.required<string>();
  readonly toDate = input.required<string>();

  /** Distinct per page, so each date's label names its own input (phase 34a). */
  readonly idPrefix = input<string>('pos-period');

  readonly periodChange = output<PosPeriod>();

  protected emit(fromDate: string, toDate: string): void {
    if (fromDate && toDate) {
      this.periodChange.emit({ fromDate, toDate });
    }
  }

  protected preset(days: number): void {
    this.periodChange.emit(lastDays(days));
  }
}
