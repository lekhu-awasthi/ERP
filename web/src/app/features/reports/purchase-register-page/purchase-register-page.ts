import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { PurchasingService } from '../../../core/purchasing/purchasing.service';
import { PurchaseRegisterRowDto } from '../../../core/purchasing/purchasing.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';

/**
 * Read-only report screen -- Phase 19's PurchaseRegisterQuery, the Nepal IRD statutory Purchase
 * Book (one row per Approved PurchaseBill/DebitNote), reusing PurchaseBill's existing
 * IsImport/ExpenditureClassification split for the 4-bucket columns.
 */
@Component({
  selector: 'app-purchase-register-page',
  imports: [RouterLink, PaginationControl, AmountPipe, BsDateInput, NepaliDatePipe],
  templateUrl: './purchase-register-page.html',
})
export class PurchaseRegisterPage {
  private readonly route = inject(ActivatedRoute);
  private readonly purchasingService = inject(PurchasingService);
  private readonly contactsService = inject(ContactsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<PurchaseRegisterRowDto[]>([]);
  protected readonly suppliers = signal<Contact[]>([]);

  protected readonly fromDate = signal(this.firstOfMonth());
  protected readonly toDate = signal(this.today());
  protected readonly contactId = signal('');

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
    this.contactsService.listAllContacts(this.organizationId, 'Supplier').subscribe({
      next: (suppliers) => this.suppliers.set(suppliers),
    });
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

  protected onContactChange(event: Event): void {
    this.contactId.set((event.target as HTMLSelectElement).value);
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
      .exportPurchaseRegister(this.organizationId, this.fromDate(), this.toDate(), this.contactId() || null, full, page, pageSize)
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `PurchaseRegister_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Purchase Register.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.purchasingService
      .getPurchaseRegister(this.organizationId, this.fromDate(), this.toDate(), this.contactId() || null, this.page(), this.pageSize())
      .subscribe({
        next: (report) => {
          this.rows.set(report.items);
          this.totalCount.set(report.totalCount);
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
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Purchase Register.');
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
