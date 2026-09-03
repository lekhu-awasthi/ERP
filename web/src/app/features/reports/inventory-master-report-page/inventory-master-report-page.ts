import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CatalogueReportsService } from '../../../core/reports/catalogue-reports.service';
import { InventoryMasterRowDto } from '../../../core/reports/catalogue-reports.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product } from '../../../core/catalog/catalog.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';

/**
 * Phase 26c -- the denormalised line-level fact table: one row per document line across every
 * stock-affecting document type. Quantity is signed by **stock direction**, so an Invoice line is
 * negative and a Credit Note line positive, whatever either does to revenue.
 */
@Component({
  selector: 'app-inventory-master-report-page',
  imports: [RouterLink, PaginationControl, AmountPipe, BsDateInput, NepaliDatePipe],
  templateUrl: './inventory-master-report-page.html',
})
export class InventoryMasterReportPage {
  private readonly route = inject(ActivatedRoute);
  private readonly reports = inject(CatalogueReportsService);
  private readonly catalogService = inject(CatalogService);
  private readonly contactsService = inject(ContactsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  /** The six types this report covers -- see InventoryMasterReportQuery for why not eight. */
  protected readonly documentTypes = [
    'Invoice',
    'CreditNote',
    'PurchaseBill',
    'DebitNote',
    'InventoryAdjustment',
    'ProductionJournal',
  ];

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<InventoryMasterRowDto[]>([]);
  protected readonly contacts = signal<Contact[]>([]);
  protected readonly products = signal<Product[]>([]);

  protected readonly fromDate = signal(firstOfMonth());
  protected readonly toDate = signal(today());
  protected readonly contactId = signal('');
  protected readonly productId = signal('');
  protected readonly documentType = signal('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);
  protected readonly totalNetAmount = signal(0);
  protected readonly totalVatAmount = signal(0);
  protected readonly totalAmount = signal(0);

  protected readonly exporting = signal(false);

  constructor() {
    this.contactsService.listAllContacts(this.organizationId).subscribe({
      next: (contacts) => this.contacts.set(contacts),
    });
    this.catalogService.listAllProducts(this.organizationId).subscribe({
      next: (products) => this.products.set(products),
    });
    this.load();
  }

  protected onFromDateChange(value: string): void {
    this.fromDate.set(value);
    this.reload();
  }

  protected onToDateChange(value: string): void {
    this.toDate.set(value);
    this.reload();
  }

  protected onContactChange(event: Event): void {
    this.contactId.set((event.target as HTMLSelectElement).value);
    this.reload();
  }

  protected onProductChange(event: Event): void {
    this.productId.set((event.target as HTMLSelectElement).value);
    this.reload();
  }

  protected onDocumentTypeChange(event: Event): void {
    this.documentType.set((event.target as HTMLSelectElement).value);
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
    this.exporting.set(true);
    this.reports
      .exportInventoryMaster(
        this.organizationId, this.fromDate(), this.toDate(), this.contactId() || null,
        this.productId() || null, this.documentType() || null, full, page, pageSize,
      )
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `InventoryMaster_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Inventory Master Report.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.reports
      .getInventoryMaster(
        this.organizationId, this.fromDate(), this.toDate(), this.contactId() || null,
        this.productId() || null, this.documentType() || null, this.page(), this.pageSize(),
      )
      .subscribe({
        next: (report) => {
          this.rows.set(report.items);
          this.totalCount.set(report.totalCount);
          this.totalNetAmount.set(report.totalNetAmount);
          this.totalVatAmount.set(report.totalVatAmount);
          this.totalAmount.set(report.totalAmount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Inventory Master Report.');
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
