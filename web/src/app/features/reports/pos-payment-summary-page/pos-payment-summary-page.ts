import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaymentMode } from '../../../core/configuration/configuration.models';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import {
  POS_PAYMENT_ENTRY_LABELS,
  POS_PAYMENT_TYPES,
  POS_PAYMENT_TYPE_LABELS,
  PosPaymentRow,
  PosPaymentSummary,
  PosPaymentType,
} from '../../../core/pos/pos-reports.models';
import { PosReportsService } from '../../../core/pos/pos-reports.service';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { ReportLocationFilter } from '../../../shared/locations/report-location-filter';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { PosPeriod, PosPeriodFilter, lastDays } from '../../../shared/pos/pos-period-filter';

/**
 * Phase 66 -- the Payment Summary: every way money moved at the tills in a period, one row each -- a
 * tender, the change handed back, the part left on a customer's credit, a refund's payout, the part of a
 * refund taken off an account. The vendor nets change into its cash row (317 for 500 handed over); the
 * drawer saw both, so both are rows. Unfiltered, the rows add up to net sales, and each type's total to
 * the Day Report's figure for it.
 *
 * Admin-only (Reports.PosPaymentSummary.View): a flat register that names the customer.
 */
@Component({
  selector: 'app-pos-payment-summary-page',
  imports: [
    RouterLink, AmountPipe, NepaliDatePipe, PaginationControl, ReportLocationFilter, PosPeriodFilter, StatusBanner,
  ],
  templateUrl: './pos-payment-summary-page.html',
})
export class PosPaymentSummaryPage {
  private readonly route = inject(ActivatedRoute);
  private readonly reports = inject(PosReportsService);
  private readonly configuration = inject(ConfigurationService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly types = POS_PAYMENT_TYPES;
  protected readonly typeLabels = POS_PAYMENT_TYPE_LABELS;
  protected readonly entryLabels = POS_PAYMENT_ENTRY_LABELS;

  protected readonly period = signal<PosPeriod>(lastDays(1));
  protected readonly locationId = signal('');
  protected readonly type = signal<PosPaymentType | null>(null);
  protected readonly paymentModeId = signal('');
  protected readonly modes = signal<PaymentMode[]>([]);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly loading = signal(true);
  protected readonly exporting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<PosPaymentSummary | null>(null);

  constructor() {
    this.configuration.listPaymentModes(this.organizationId).subscribe({
      next: (modes) => this.modes.set(modes.filter((x) => x.kind !== null)),
    });
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

  protected onTypeChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.type.set(value ? (value as PosPaymentType) : null);
    this.reload();
  }

  protected onModeChange(event: Event): void {
    this.paymentModeId.set((event.target as HTMLSelectElement).value);
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

  protected documentLink(row: PosPaymentRow): unknown[] {
    return ['/organizations', this.organizationId, 'sales', row.documentType === 'Invoice' ? 'invoices' : 'credit-notes', row.documentId];
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
      .exportPaymentSummary(
        this.organizationId, { ...period, locationId: this.locationId() }, this.type(), this.paymentModeId() || null,
        full, page, this.pageSize())
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `PosPaymentSummary_${period.fromDate}_${period.toDate}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Payment Summary.');
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
      .getPaymentSummary(
        this.organizationId, { ...this.period(), locationId: this.locationId() }, this.type(),
        this.paymentModeId() || null, this.page(), this.pageSize())
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.report.set(null);
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Payment Summary.');
        },
      });
  }
}
