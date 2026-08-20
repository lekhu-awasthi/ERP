import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { PurchasingService } from '../../../core/purchasing/purchasing.service';
import { PurchaseMasterReportRowDto } from '../../../core/purchasing/purchasing.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { Product } from '../../../core/catalog/catalog.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { Warehouse } from '../../../core/organizations/organizations.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';

/**
 * Mirror of SalesMasterReportPage over PurchaseBill/DebitNote lines instead of Invoice/
 * CreditNote -- same date-range/optional-filter/paginated shape (Phase 16c), see that
 * component's doc comment and phase-8b-status.md.
 */
@Component({
  selector: 'app-purchase-master-report-page',
  imports: [RouterLink, PaginationControl],
  templateUrl: './purchase-master-report-page.html',
})
export class PurchaseMasterReportPage {
  private readonly route = inject(ActivatedRoute);
  private readonly purchasingService = inject(PurchasingService);
  private readonly contactsService = inject(ContactsService);
  private readonly catalogService = inject(CatalogService);
  private readonly organizationsService = inject(OrganizationsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<PurchaseMasterReportRowDto[]>([]);
  protected readonly suppliers = signal<Contact[]>([]);
  protected readonly products = signal<Product[]>([]);
  protected readonly warehouses = signal<Warehouse[]>([]);

  protected readonly fromDate = signal(this.firstOfMonth());
  protected readonly toDate = signal(this.today());
  protected readonly contactId = signal('');
  protected readonly productId = signal('');
  protected readonly warehouseId = signal('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);
  protected readonly totalAmount = signal(0);

  protected readonly exporting = signal(false);

  constructor() {
    this.contactsService.listAllContacts(this.organizationId, 'Supplier').subscribe({ next: (c) => this.suppliers.set(c) });
    this.catalogService.listAllProducts(this.organizationId).subscribe({ next: (p) => this.products.set(p) });
    this.organizationsService.listWarehouses(this.organizationId).subscribe({ next: (w) => this.warehouses.set(w) });
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

  protected onContactChange(event: Event): void {
    this.contactId.set((event.target as HTMLSelectElement).value);
    this.page.set(1);
    this.load();
  }

  protected onProductChange(event: Event): void {
    this.productId.set((event.target as HTMLSelectElement).value);
    this.page.set(1);
    this.load();
  }

  protected onWarehouseChange(event: Event): void {
    this.warehouseId.set((event.target as HTMLSelectElement).value);
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
      .exportPurchaseMasterReport(
        this.organizationId, this.fromDate(), this.toDate(),
        this.contactId() || null, this.productId() || null, this.warehouseId() || null,
        full, page, pageSize,
      )
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `PurchaseMasterReport_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Purchase Master Report.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.purchasingService
      .getPurchaseMasterReport(
        this.organizationId,
        this.fromDate(),
        this.toDate(),
        this.contactId() || null,
        this.productId() || null,
        this.warehouseId() || null,
        this.page(),
        this.pageSize(),
      )
      .subscribe({
        next: (report) => {
          this.rows.set(report.rows);
          this.totalCount.set(report.totalCount);
          this.totalAmount.set(report.totalAmount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Purchase Master Report.');
        },
      });
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private firstOfMonth(): string {
    const now = new Date();
    return new Date(now.getFullYear(), now.getMonth(), 1).toISOString().slice(0, 10);
  }
}
