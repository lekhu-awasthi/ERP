import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CatalogueReportsService } from '../../../core/reports/catalogue-reports.service';
import { ProductSerialRowDto } from '../../../core/reports/catalogue-reports.models';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { ReportLocationFilter } from '../../../shared/locations/report-location-filter';
import { StatusBanner } from '../../../shared/a11y/status-banner';

/**
 * Phase 51 -- **Product Serial No Report**.
 *
 * <p><b>Its columns were never read</b>, for the same reason as its batch sibling; see that page's
 * doc comment and `docs/phase-51-status.md` Decision A. Derived from the product detail Serial
 * Number tab (SERIAL NO. / WAREHOUSE / CREATED AT) and the catalogue's filters (Period / Group By /
 * Status, with Group By offering None / Product / Warehouse).</p>
 *
 * <p><b>The Status filter was not invented.</b> The kickoff flagged it as a trap: the catalogue
 * shows a Status control that the tab's three columns do not explain. The model answered it --
 * because a serial is a FIFO layer of quantity one, In Stock and Issued are just
 * `QuantityRemaining` being 1 or 0. The lifecycle was already in the ledger.</p>
 */
@Component({
  selector: 'app-product-serial-report-page',
  imports: [RouterLink, AmountPipe, BsDateInput, NepaliDatePipe, ReportLocationFilter, StatusBanner],
  templateUrl: './product-serial-report-page.html',
})
export class ProductSerialReportPage {
  private readonly route = inject(ActivatedRoute);
  private readonly reports = inject(CatalogueReportsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<ProductSerialRowDto[]>([]);

  protected readonly fromDate = signal(firstOfMonth());
  protected readonly toDate = signal(today());

  protected readonly status = signal('All');
  protected readonly groupBy = signal('None');
  protected readonly locationId = signal('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(50);
  protected readonly totalCount = signal(0);

  /** Counts over the full filtered set, computed server-side (phase-16c #1). */
  protected readonly inStockCount = signal(0);
  protected readonly issuedCount = signal(0);

  protected readonly statusOptions = ['All', 'InStock', 'Issued'];
  protected readonly groupByOptions = ['None', 'Product', 'Warehouse'];

  constructor() {
    this.load();
  }

  protected onFromDateChange(value: string): void {
    this.fromDate.set(value);
    this.page.set(1);
    this.load();
  }

  protected onToDateChange(value: string): void {
    this.toDate.set(value);
    this.page.set(1);
    this.load();
  }

  protected onStatusChange(event: Event): void {
    this.status.set((event.target as HTMLSelectElement).value);
    this.page.set(1);
    this.load();
  }

  protected onGroupByChange(event: Event): void {
    this.groupBy.set((event.target as HTMLSelectElement).value);
    this.page.set(1);
    this.load();
  }

  protected onLocationChange(value: string): void {
    this.locationId.set(value);
    this.page.set(1);
    this.load();
  }

  protected goToPage(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount() / this.pageSize()));
  }

  /** The three Status values read back for display, so the column reads as words and not as an enum. */
  protected statusLabel(status: string): string {
    return status === 'InStock' ? 'In Stock' : 'Issued';
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.reports
      .getProductSerialReport(
        this.organizationId, this.fromDate(), this.toDate(), null, null,
        this.status(), this.groupBy(), this.page(), this.pageSize(), this.locationId() || null,
      )
      .subscribe({
        next: (report) => {
          this.rows.set(report.items);
          this.totalCount.set(report.totalCount);
          this.inStockCount.set(report.inStockCount);
          this.issuedCount.set(report.issuedCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Product Serial No Report.');
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
