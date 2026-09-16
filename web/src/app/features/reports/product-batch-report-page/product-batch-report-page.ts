import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CatalogueReportsService } from '../../../core/reports/catalogue-reports.service';
import { ProductBatchRowDto } from '../../../core/reports/catalogue-reports.models';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { ReportLocationFilter } from '../../../shared/locations/report-location-filter';
import { StatusBanner } from '../../../shared/a11y/status-banner';

/**
 * Phase 51 -- **Product Batch Report**.
 *
 * <p><b>Its columns were never read.</b> The vendor gates this report behind a permission key its
 * demo Admin does not hold, so the 2026-09-16 pass got `permission denied`. Phase 51 chose to design
 * from the product detail Batch tab rather than obtain an account -- the phase-8f rule, invoked
 * explicitly and recorded in `docs/phase-51-status.md` Decision A. Every column below traces to a
 * column on that tab (BATCH NO. / MANUFACTURE DATE / EXPIRY DATE / QUANTITY) or to a filter the
 * catalogue showed (Period / Group By / Warehouse), plus the Product identification any
 * tenant-wide view obviously needs.</p>
 *
 * <p>Quantity is a GROUP BY over the FIFO layers carrying each batch's id; the batch row stores no
 * quantity of its own, which is what makes this report and Stock Position incapable of
 * disagreeing.</p>
 */
@Component({
  selector: 'app-product-batch-report-page',
  imports: [RouterLink, AmountPipe, BsDateInput, NepaliDatePipe, ReportLocationFilter, StatusBanner],
  templateUrl: './product-batch-report-page.html',
})
export class ProductBatchReportPage {
  private readonly route = inject(ActivatedRoute);
  private readonly reports = inject(CatalogueReportsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<ProductBatchRowDto[]>([]);

  protected readonly fromDate = signal(firstOfMonth());
  protected readonly toDate = signal(today());

  /** The catalogue's own Group By control: None (default) / Product / Warehouse. */
  protected readonly groupBy = signal('None');
  protected readonly locationId = signal('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(50);
  protected readonly totalCount = signal(0);

  /** Server-computed over the full filtered set -- never a reduce over one page (phase-16c #1). */
  protected readonly totalQuantity = signal(0);

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

  /** Every filter handler resets the page, or a narrowing filter on page 3 reads as "no data"
   *  (phase 35b). */
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

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.reports
      .getProductBatchReport(
        this.organizationId, this.fromDate(), this.toDate(), null, null,
        this.groupBy(), this.page(), this.pageSize(), this.locationId() || null,
      )
      .subscribe({
        next: (report) => {
          this.rows.set(report.items);
          this.totalCount.set(report.totalCount);
          this.totalQuantity.set(report.totalQuantity);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Product Batch Report.');
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
