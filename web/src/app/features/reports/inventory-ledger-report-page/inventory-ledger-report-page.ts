import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CatalogueReportsService } from '../../../core/reports/catalogue-reports.service';
import {
  InventoryLedgerReportDto,
  InventoryLedgerReportRowDto,
} from '../../../core/reports/catalogue-reports.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product } from '../../../core/catalog/catalog.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { Warehouse } from '../../../core/organizations/organizations.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';

/**
 * Phase 26c -- the kardex as a report: every movement of one product over a period, bracketed by an
 * Opening Balance row and a Closing Balance row.
 *
 * A product must be chosen before anything is loaded, matching the live screen (which refuses to
 * generate and says "Please select a product"). The bracket rows come from the same
 * `StockFactReader` Inventory Position reads, so this report's Closing Balance and that report's
 * Amount are one figure.
 */
@Component({
  selector: 'app-inventory-ledger-report-page',
  imports: [RouterLink, PaginationControl, AmountPipe, BsDateInput, NepaliDatePipe],
  templateUrl: './inventory-ledger-report-page.html',
})
export class InventoryLedgerReportPage {
  private readonly route = inject(ActivatedRoute);
  private readonly reports = inject(CatalogueReportsService);
  private readonly catalogService = inject(CatalogService);
  private readonly organizationsService = inject(OrganizationsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<InventoryLedgerReportDto | null>(null);
  protected readonly rows = signal<InventoryLedgerReportRowDto[]>([]);
  protected readonly products = signal<Product[]>([]);
  protected readonly warehouses = signal<Warehouse[]>([]);

  protected readonly fromDate = signal(firstOfMonth());
  protected readonly toDate = signal(today());
  protected readonly productId = signal('');
  protected readonly warehouseId = signal('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  protected readonly exporting = signal(false);

  constructor() {
    this.catalogService.listAllProducts(this.organizationId).subscribe({
      next: (products) => this.products.set(products),
    });
    this.organizationsService.listWarehouses(this.organizationId).subscribe({
      next: (warehouses) => this.warehouses.set(warehouses),
    });
  }

  protected onFromDateChange(value: string): void {
    this.fromDate.set(value);
    this.reload();
  }

  protected onToDateChange(value: string): void {
    this.toDate.set(value);
    this.reload();
  }

  protected onProductChange(event: Event): void {
    this.productId.set((event.target as HTMLSelectElement).value);
    this.reload();
  }

  protected onWarehouseChange(event: Event): void {
    this.warehouseId.set((event.target as HTMLSelectElement).value);
    this.reload();
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

  private reload(): void {
    this.page.set(1);
    this.load();
  }

  private runExport(full: boolean, page: number, pageSize: number): void {
    if (!this.productId()) {
      return;
    }

    this.exporting.set(true);
    this.reports
      .exportInventoryLedger(
        this.organizationId, this.fromDate(), this.toDate(), this.productId(),
        this.warehouseId() || null, full, page, pageSize,
      )
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `InventoryLedger_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Inventory Ledger.');
        },
      });
  }

  private load(): void {
    // A kardex is a per-product document; without one there is nothing to ask for.
    if (!this.productId()) {
      this.report.set(null);
      this.rows.set([]);
      this.totalCount.set(0);
      return;
    }

    this.loading.set(true);
    this.errorMessage.set(null);

    this.reports
      .getInventoryLedger(
        this.organizationId, this.fromDate(), this.toDate(), this.productId(),
        this.warehouseId() || null, this.page(), this.pageSize(),
      )
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.rows.set(report.items);
          this.totalCount.set(report.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Inventory Ledger.');
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
