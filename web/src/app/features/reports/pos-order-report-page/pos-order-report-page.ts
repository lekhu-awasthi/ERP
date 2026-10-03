import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PosTab } from '../../../core/pos/pos.models';
import { PosOrderReport } from '../../../core/pos/pos-reports.models';
import { PosReportsService } from '../../../core/pos/pos-reports.service';
import { PosOrderStatus } from '../../../core/pos/pos-restaurant.models';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { ReportLocationFilter } from '../../../shared/locations/report-location-filter';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { PosPeriod, PosPeriodFilter, lastDays } from '../../../shared/pos/pos-period-filter';

const ORDER_TYPE_LABELS: Readonly<Record<PosTab, string>> = {
  Retail: 'Retail', DineIn: 'Dine In', TakeAway: 'Take Away', Delivery: 'Delivery',
};

/**
 * Phase 66 -- the Order Report: a period's restaurant orders, open, settled or voided, with every bill
 * each has had (a split order has several) and what is still to bill. Order value is the estimate at the
 * lines' frozen rates before rounding; Billed is what the bills came to, each rounded to the rupee; so a
 * settled order's Billed differs from its value by exactly its bills' round-off. A refund changes nothing
 * here (the food was billed and returned); it is on the Day Report and the Payment Summary.
 */
@Component({
  selector: 'app-pos-order-report-page',
  imports: [
    RouterLink, AmountPipe, NepaliDatePipe, PaginationControl, ReportLocationFilter, PosPeriodFilter, StatusBanner,
  ],
  templateUrl: './pos-order-report-page.html',
})
export class PosOrderReportPage {
  private readonly route = inject(ActivatedRoute);
  private readonly reports = inject(PosReportsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly orderTypeLabels = ORDER_TYPE_LABELS;
  protected readonly statuses: readonly PosOrderStatus[] = ['Open', 'Settled', 'Voided'];
  protected readonly orderTypes: readonly PosTab[] = ['DineIn', 'TakeAway', 'Delivery'];

  protected readonly period = signal<PosPeriod>(lastDays(1));
  protected readonly locationId = signal('');
  protected readonly status = signal<PosOrderStatus | null>(null);
  protected readonly orderType = signal<PosTab | null>(null);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly loading = signal(true);
  protected readonly exporting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<PosOrderReport | null>(null);

  constructor() {
    this.load();
  }

  protected onPeriodChange(period: PosPeriod): void {
    this.period.set(period);
    this.reload();
  }

  protected onLocationChange(value: string): void {
    this.locationId.set(value);
    this.reload();
  }

  protected onStatusChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.status.set(value ? (value as PosOrderStatus) : null);
    this.reload();
  }

  protected onOrderTypeChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.orderType.set(value ? (value as PosTab) : null);
    this.reload();
  }

  protected onPageChange(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected onPageSizeChange(pageSize: number): void {
    this.pageSize.set(pageSize);
    this.reload();
  }

  protected exportCurrentView(): void {
    this.runExport(false, this.page());
  }

  protected exportFullDataset(): void {
    this.runExport(true, 1);
  }

  private runExport(full: boolean, page: number): void {
    this.exporting.set(true);
    const period = this.period();
    this.reports
      .exportOrderReport(
        this.organizationId, { ...period, locationId: this.locationId() }, this.status(), this.orderType(),
        full, page, this.pageSize())
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `PosOrderReport_${period.fromDate}_${period.toDate}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Order Report.');
        },
      });
  }

  private reload(): void {
    this.page.set(1);
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.reports
      .getOrderReport(
        this.organizationId, { ...this.period(), locationId: this.locationId() }, this.status(), this.orderType(),
        this.page(), this.pageSize())
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.report.set(null);
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Order Report.');
        },
      });
  }
}
