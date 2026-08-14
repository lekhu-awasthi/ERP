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

/**
 * Mirror of SalesMasterReportPage over PurchaseBill/DebitNote lines instead of Invoice/
 * CreditNote -- same date-range/optional-filter/unpaginated shape, see that component's doc
 * comment and phase-8b-status.md.
 */
@Component({
  selector: 'app-purchase-master-report-page',
  imports: [RouterLink],
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

  constructor() {
    this.contactsService.listContacts(this.organizationId, 'Supplier').subscribe({ next: (c) => this.suppliers.set(c) });
    this.catalogService.listProducts(this.organizationId).subscribe({ next: (p) => this.products.set(p) });
    this.organizationsService.listWarehouses(this.organizationId).subscribe({ next: (w) => this.warehouses.set(w) });
    this.load();
  }

  protected onFromDateChange(event: Event): void {
    this.fromDate.set((event.target as HTMLInputElement).value);
    this.load();
  }

  protected onToDateChange(event: Event): void {
    this.toDate.set((event.target as HTMLInputElement).value);
    this.load();
  }

  protected onContactChange(event: Event): void {
    this.contactId.set((event.target as HTMLSelectElement).value);
    this.load();
  }

  protected onProductChange(event: Event): void {
    this.productId.set((event.target as HTMLSelectElement).value);
    this.load();
  }

  protected onWarehouseChange(event: Event): void {
    this.warehouseId.set((event.target as HTMLSelectElement).value);
    this.load();
  }

  protected totalAmount(): number {
    return this.rows().reduce((sum, r) => sum + r.totalAmount, 0);
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
      )
      .subscribe({
        next: (report) => {
          this.rows.set(report.rows);
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
