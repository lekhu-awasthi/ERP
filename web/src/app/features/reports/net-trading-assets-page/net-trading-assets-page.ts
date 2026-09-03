import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CatalogueReportsService } from '../../../core/reports/catalogue-reports.service';
import { NetTradingAssetsRowDto } from '../../../core/reports/catalogue-reports.models';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';

/**
 * Phase 26c -- Net Trading Assets: Receivables less Payables plus Inventory, each grouped row
 * expanded into its own two leaves.
 *
 * Every figure here is a closing balance shared with a report that already exists -- Customer
 * Receivable Summary, Supplier Payable Summary and Inventory Position all read the same server-side
 * readers -- so this screen is a rollup of theirs, not a second opinion.
 */
@Component({
  selector: 'app-net-trading-assets-page',
  imports: [RouterLink, AmountPipe, BsDateInput, NepaliDatePipe],
  templateUrl: './net-trading-assets-page.html',
})
export class NetTradingAssetsPage {
  private readonly route = inject(ActivatedRoute);
  private readonly reports = inject(CatalogueReportsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<NetTradingAssetsRowDto[]>([]);
  protected readonly compareAsOfDate = signal<string | null>(null);

  protected readonly fromDate = signal(firstOfMonth());
  protected readonly toDate = signal(today());
  protected readonly compare = signal(false);
  protected readonly excludeAdvance = signal(false);

  protected readonly exporting = signal(false);

  constructor() {
    this.load();
  }

  protected onFromDateChange(value: string): void {
    this.fromDate.set(value);
    this.load();
  }

  protected onToDateChange(value: string): void {
    this.toDate.set(value);
    this.load();
  }

  /**
   * The checkbox's own event handler writes the signal. The app is zoneless, so a `computed()` over
   * a plain control value would cache forever (phase-17).
   */
  protected onCompareChange(event: Event): void {
    this.compare.set((event.target as HTMLInputElement).checked);
    this.load();
  }

  protected onExcludeAdvanceChange(event: Event): void {
    this.excludeAdvance.set((event.target as HTMLInputElement).checked);
    this.load();
  }

  protected exportReport(): void {
    this.exporting.set(true);
    this.reports
      .exportNetTradingAssets(
        this.organizationId, this.fromDate(), this.toDate(), this.compare(), this.excludeAdvance(),
      )
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `NetTradingAssets_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export Net Trading Assets.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.reports
      .getNetTradingAssets(
        this.organizationId, this.fromDate(), this.toDate(), this.compare(), this.excludeAdvance(),
      )
      .subscribe({
        next: (report) => {
          this.rows.set(report.rows);
          this.compareAsOfDate.set(report.compareAsOfDate);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load Net Trading Assets.');
        },
      });
  }
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

function firstOfMonth(): string {
  const now = new Date();
  return new Date(Date.UTC(now.getFullYear(), now.getMonth(), 1)).toISOString().slice(0, 10);
}
