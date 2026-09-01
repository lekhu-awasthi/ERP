import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { PurchasingService } from '../../../core/purchasing/purchasing.service';
import { PurchaseRegisterRowDto } from '../../../core/purchasing/purchasing.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';

/**
 * Phase 21c / FR-2.10 / FR-9.4 -- the Migrated Purchase Register. The Purchase-side twin of
 * MigratedSalesRegisterPage; read that component for Decision B (why this is a separate page rather
 * than a mode on the live register).
 */
@Component({
  selector: 'app-migrated-purchase-register-page',
  imports: [RouterLink, PaginationControl],
  templateUrl: './migrated-purchase-register-page.html',
})
export class MigratedPurchaseRegisterPage {
  private readonly route = inject(ActivatedRoute);
  private readonly purchasingService = inject(PurchasingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<PurchaseRegisterRowDto[]>([]);

  protected readonly fromDate = signal(this.startOfPreviousYear());
  protected readonly toDate = signal(this.today());
  protected readonly partySearch = signal('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);
  protected readonly totalTaxExemptValue = signal(0);
  protected readonly totalTaxableNonCapitalLocalValue = signal(0);
  protected readonly totalTaxableNonCapitalLocalVat = signal(0);
  protected readonly totalTaxableNonCapitalImportValue = signal(0);
  protected readonly totalTaxableNonCapitalImportVat = signal(0);
  protected readonly totalTaxableCapitalValue = signal(0);
  protected readonly totalTaxableCapitalVat = signal(0);

  protected readonly exporting = signal(false);

  constructor() {
    this.load();
  }

  protected onFromDateChange(event: Event): void {
    this.fromDate.set((event.target as HTMLInputElement).value);
    this.page.set(1);
    this.load();
  }

  protected onToDateChange(event: Event): void {
    this.toDate.set((event.target as HTMLInputElement).value);
    this.page.set(1);
    this.load();
  }

  protected onPartySearchChange(event: Event): void {
    this.partySearch.set((event.target as HTMLInputElement).value);
    this.page.set(1);
    this.load();
  }

  protected onPageChange(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected onPageSizeChange(pageSize: number): void {
    this.pageSize.set(pageSize);
    this.page.set(1);
    this.load();
  }

  protected exportCurrentView(): void {
    this.runExport(false, this.page(), this.pageSize());
  }

  protected exportFullDataset(): void {
    this.runExport(true, 1, this.pageSize());
  }

  private runExport(full: boolean, page: number, pageSize: number): void {
    this.exporting.set(true);
    this.purchasingService
      .exportMigratedPurchaseRegister(
        this.organizationId, this.fromDate(), this.toDate(), this.partySearch() || null, full, page, pageSize,
      )
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `MigratedPurchaseRegister_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(
            extractErrorMessage(err) ?? 'Could not export the Migrated Purchase Register.',
          );
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.purchasingService
      .getMigratedPurchaseRegister(
        this.organizationId, this.fromDate(), this.toDate(), this.partySearch() || null,
        this.page(), this.pageSize(),
      )
      .subscribe({
        next: (report) => {
          this.rows.set(report.items);
          this.totalCount.set(report.totalCount);
          // Server-computed over the full filtered set, never a page reduce (phase-16c bug #1).
          this.totalTaxExemptValue.set(report.totalTaxExemptValue);
          this.totalTaxableNonCapitalLocalValue.set(report.totalTaxableNonCapitalLocalValue);
          this.totalTaxableNonCapitalLocalVat.set(report.totalTaxableNonCapitalLocalVat);
          this.totalTaxableNonCapitalImportValue.set(report.totalTaxableNonCapitalImportValue);
          this.totalTaxableNonCapitalImportVat.set(report.totalTaxableNonCapitalImportVat);
          this.totalTaxableCapitalValue.set(report.totalTaxableCapitalValue);
          this.totalTaxableCapitalVat.set(report.totalTaxableCapitalVat);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(
            extractErrorMessage(err) ?? 'Could not load the Migrated Purchase Register.',
          );
        },
      });
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  /** Migrated rows are pre-cutover by definition -- see the Sales-side page. */
  private startOfPreviousYear(): string {
    const now = new Date();
    return new Date(now.getFullYear() - 2, 0, 1).toISOString().slice(0, 10);
  }
}
