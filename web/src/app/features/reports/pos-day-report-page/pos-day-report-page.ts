import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { PosDayReport } from '../../../core/pos/pos-reports.models';
import { PosReportsService } from '../../../core/pos/pos-reports.service';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { ReportLocationFilter } from '../../../shared/locations/report-location-filter';
import { PosPeriod, PosPeriodFilter, lastDays } from '../../../shared/pos/pos-period-filter';

/**
 * Phase 66 -- the POS Day Report: one period's till takings, every figure read from PosSalesReader,
 * the reader the session's X/Z report reads (phase 61 K). The vendor's showed 610.20 for a day whose
 * invoices came to 611 and printed no round-off; this one prints the round-off as its own line, and
 * the line that takes the till's figure to the Sales Register's.
 *
 * Refunds are shown as their own block and taken off once, into Net sales; voided sales, refunds and
 * orders are counted and never summed (phase-66-status.md Decision C). The payments block is where net
 * sales went, mode by mode, and adds up to it.
 */
@Component({
  selector: 'app-pos-day-report-page',
  imports: [RouterLink, AmountPipe, NepaliDatePipe, ReportLocationFilter, PosPeriodFilter, StatusBanner],
  templateUrl: './pos-day-report-page.html',
})
export class PosDayReportPage {
  private readonly route = inject(ActivatedRoute);
  private readonly reports = inject(PosReportsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly period = signal<PosPeriod>(lastDays(1));
  protected readonly locationId = signal('');

  protected readonly loading = signal(true);
  protected readonly exporting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<PosDayReport | null>(null);

  constructor() {
    this.load();
  }

  protected onPeriodChange(period: PosPeriod): void {
    this.period.set(period);
    this.load();
  }

  protected onLocationChange(value: string): void {
    this.locationId.set(value);
    this.load();
  }

  protected export(): void {
    this.exporting.set(true);
    const period = this.period();
    this.reports.exportDayReport(this.organizationId, { ...period, locationId: this.locationId() }).subscribe({
      next: (blob) => {
        this.exporting.set(false);
        triggerBlobDownload(blob, `PosDayReport_${period.fromDate}_${period.toDate}.xlsx`);
      },
      error: (err: unknown) => {
        this.exporting.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Day Report.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.reports.getDayReport(this.organizationId, { ...this.period(), locationId: this.locationId() }).subscribe({
      next: (report) => {
        this.report.set(report);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.report.set(null);
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Day Report.');
      },
    });
  }
}
