import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CatalogueReportsService } from '../../../core/reports/catalogue-reports.service';
import { SalesReturnRegisterRowDto } from '../../../core/reports/catalogue-reports.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';

/**
 * Phase 26c -- the Nepal IRD statutory Sales Return Book, one row per approved Credit Note with
 * values shown positive.
 *
 * The same credit notes also appear in the main Sales Register, negatively; that is not a bug and
 * was confirmed live over one period on 2026-09-03. The two screens link to each other so a reader
 * comparing them can see both readings of the same documents.
 */
@Component({
  selector: 'app-sales-return-register-page',
  imports: [RouterLink, PaginationControl, AmountPipe, BsDateInput, NepaliDatePipe],
  templateUrl: './sales-return-register-page.html',
})
export class SalesReturnRegisterPage {
  private readonly route = inject(ActivatedRoute);
  private readonly reports = inject(CatalogueReportsService);
  private readonly contactsService = inject(ContactsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<SalesReturnRegisterRowDto[]>([]);
  protected readonly customers = signal<Contact[]>([]);

  protected readonly fromDate = signal(firstOfMonth());
  protected readonly toDate = signal(today());
  protected readonly contactId = signal('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);
  protected readonly totalReturnValue = signal(0);
  protected readonly totalTaxExemptReturnValue = signal(0);
  protected readonly totalTaxableReturnValue = signal(0);
  protected readonly totalVatAmount = signal(0);

  protected readonly exporting = signal(false);

  constructor() {
    this.contactsService.listAllContacts(this.organizationId, 'Customer').subscribe({
      next: (customers) => this.customers.set(customers),
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
    this.reports
      .exportSalesReturnRegister(
        this.organizationId, this.fromDate(), this.toDate(), this.contactId() || null, full, page, pageSize,
      )
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `SalesReturnRegister_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Sales Return Register.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.reports
      .getSalesReturnRegister(
        this.organizationId, this.fromDate(), this.toDate(), this.contactId() || null,
        this.page(), this.pageSize(),
      )
      .subscribe({
        next: (report) => {
          this.rows.set(report.items);
          this.totalCount.set(report.totalCount);
          this.totalReturnValue.set(report.totalReturnValue);
          this.totalTaxExemptReturnValue.set(report.totalTaxExemptReturnValue);
          this.totalTaxableReturnValue.set(report.totalTaxableReturnValue);
          this.totalVatAmount.set(report.totalVatAmount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Sales Return Register.');
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
