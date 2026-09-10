import { Component, ElementRef, HostListener, computed, inject, signal } from '@angular/core';

import { BsDateInput } from '../formatting/bs-date-input';
import { DATE_RANGE_PRESETS, DateRangePreset, DateRangeService, rangeFor } from './date-range.service';

/**
 * Phase 34b — the top bar's global date-range control.
 *
 * The preset list, its wording and its order are the reference product's, read off the live popover
 * on 2026-09-10: Today / Last 7 / 15 / 30 / 45 / 60 days / This Fiscal Year to Date / Fiscal Year /
 * Custom Range. Each row shows the bounds it resolves to, as that popover does, so the choice is
 * legible before it is made rather than after.
 *
 * See {@link DateRangeService} for the finding that made this a shell control rather than a
 * dashboard one — it scopes list queries, not dashboard figures.
 *
 * The two custom-range fields are `<app-bs-date-input>`, so the control obeys the tenant's AD/BS
 * choice like every other date field in the app (phase 23), and inherits the `inputId` contract
 * 34a's guard asserts every caller passes.
 */
@Component({
  selector: 'app-date-range-picker',
  imports: [BsDateInput],
  templateUrl: './date-range-picker.html',
  styleUrl: './date-range-picker.scss',
})
export class DateRangePicker {
  private readonly host = inject(ElementRef<HTMLElement>);
  protected readonly dateRange = inject(DateRangeService);

  protected readonly open = signal(false);
  protected readonly presets = DATE_RANGE_PRESETS;

  /** Draft values for the custom range, so neither field applies until both are set. */
  protected readonly customFrom = signal('');
  protected readonly customTo = signal('');

  protected readonly customValid = computed(() => {
    const from = this.customFrom();
    const to = this.customTo();
    return from.length > 0 && to.length > 0 && from <= to;
  });

  protected toggle(): void {
    if (!this.open()) {
      this.customFrom.set(this.dateRange.from());
      this.customTo.set(this.dateRange.to());
    }

    this.open.update((x) => !x);
  }

  /** The bounds a preset resolves to today, for the row's subtitle. */
  protected boundsOf(preset: DateRangePreset): string {
    const range = rangeFor(preset);
    return `${range.from} – ${range.to}`;
  }

  protected choose(preset: DateRangePreset): void {
    this.dateRange.setPreset(preset);
    this.open.set(false);
  }

  protected applyCustom(): void {
    if (!this.customValid()) {
      return;
    }

    this.dateRange.setCustom(this.customFrom(), this.customTo());
    this.open.set(false);
  }

  @HostListener('document:click', ['$event'])
  protected onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }

  @HostListener('keydown.escape')
  protected onEscape(): void {
    this.open.set(false);
  }
}
