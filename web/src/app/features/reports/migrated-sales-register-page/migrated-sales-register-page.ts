import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { SalesService } from '../../../core/sales/sales.service';
import { SalesRegisterRowDto } from '../../../core/sales/sales.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';

/**
 * Phase 21c / FR-2.10 / FR-9.4 -- the Migrated Sales Register.
 *
 * <p><b>A separate page rather than a mode on the live register, and that is Decision B.</b> The
 * reference product lists Sales Register and Migrated Sales Register as two menu entries, and the
 * risk a shared screen carries is specific and serious: these rows are pre-cutover history that was
 * typed into a spreadsheet, never posted to the General Ledger, and never validated against a
 * document -- reading them as this year's real books is the one mistake this data makes possible.
 * A toggle would also have collided with Angular's default route-reuse strategy, which keeps one
 * component instance alive across a same-component navigation (phase-3's bug #1).</p>
 *
 * <p>The banner is part of the feature, not decoration. So is the filter: the live register filters
 * by a Customer dropdown because its rows always have a ContactId; a migrated row's party is free
 * text carried over from the prior system, so this one searches name and PAN instead.</p>
 */
@Component({
  selector: 'app-migrated-sales-register-page',
  imports: [RouterLink, PaginationControl],
  templateUrl: './migrated-sales-register-page.html',
})
export class MigratedSalesRegisterPage {
  private readonly route = inject(ActivatedRoute);
  private readonly salesService = inject(SalesService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<SalesRegisterRowDto[]>([]);

  protected readonly fromDate = signal(this.startOfPreviousYear());
  protected readonly toDate = signal(this.today());
  protected readonly partySearch = signal('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);
  protected readonly totalValue = signal(0);
  protected readonly totalTaxExemptValue = signal(0);
  protected readonly totalTaxableValue = signal(0);
  protected readonly totalVatAmount = signal(0);

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
    this.salesService
      .exportMigratedSalesRegister(
        this.organizationId, this.fromDate(), this.toDate(), this.partySearch() || null, full, page, pageSize,
      )
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `MigratedSalesRegister_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(
            extractErrorMessage(err) ?? 'Could not export the Migrated Sales Register.',
          );
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.salesService
      .getMigratedSalesRegister(
        this.organizationId, this.fromDate(), this.toDate(), this.partySearch() || null,
        this.page(), this.pageSize(),
      )
      .subscribe({
        next: (report) => {
          this.rows.set(report.items);
          this.totalCount.set(report.totalCount);
          // Every total comes from the server, computed over the full filtered set -- never a
          // reduce over the current page (phase-16c bug #1).
          this.totalValue.set(report.totalValue);
          this.totalTaxExemptValue.set(report.totalTaxExemptValue);
          this.totalTaxableValue.set(report.totalTaxableValue);
          this.totalVatAmount.set(report.totalVatAmount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(
            extractErrorMessage(err) ?? 'Could not load the Migrated Sales Register.',
          );
        },
      });
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  /** Migrated rows are pre-cutover by definition, so the live register's this-month default would
   * show an empty screen on almost every tenant. Two years back is a window that actually contains
   * them. */
  private startOfPreviousYear(): string {
    const now = new Date();
    return new Date(now.getFullYear() - 2, 0, 1).toISOString().slice(0, 10);
  }
}
