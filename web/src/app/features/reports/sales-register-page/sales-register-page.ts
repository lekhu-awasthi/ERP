import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { SalesService } from '../../../core/sales/sales.service';
import { SalesRegisterRowDto } from '../../../core/sales/sales.models';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { ReportingTagOption } from '../../../core/configuration/configuration.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';

/**
 * Read-only report screen -- Phase 19's SalesRegisterQuery, the Nepal IRD statutory Sales Book
 * (one row per Approved Invoice/CreditNote). TagOptionIds narrows to Invoice rows carrying any of
 * the selected Reporting Tags (decision #1's OR semantics) -- CreditNote rows never carry tags, so
 * an active tag filter excludes every CreditNote row too.
 */
@Component({
  selector: 'app-sales-register-page',
  imports: [RouterLink, PaginationControl],
  templateUrl: './sales-register-page.html',
})
export class SalesRegisterPage {
  private readonly route = inject(ActivatedRoute);
  private readonly salesService = inject(SalesService);
  private readonly contactsService = inject(ContactsService);
  private readonly configurationService = inject(ConfigurationService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<SalesRegisterRowDto[]>([]);
  protected readonly customers = signal<Contact[]>([]);
  protected readonly tagOptions = signal<ReportingTagOption[]>([]);

  protected readonly fromDate = signal(this.firstOfMonth());
  protected readonly toDate = signal(this.today());
  protected readonly contactId = signal('');
  protected readonly selectedTagOptionIds = signal<string[]>([]);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);
  protected readonly totalValue = signal(0);
  protected readonly totalTaxExemptValue = signal(0);
  protected readonly totalTaxableValue = signal(0);
  protected readonly totalVatAmount = signal(0);

  protected readonly exporting = signal(false);

  constructor() {
    this.contactsService.listAllContacts(this.organizationId, 'Customer').subscribe({
      next: (customers) => this.customers.set(customers),
    });
    this.configurationService.listReportingTagOptions(this.organizationId).subscribe({
      next: (options) => this.tagOptions.set(options),
    });
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

  protected onTagOptionsChange(event: Event): void {
    const select = event.target as HTMLSelectElement;
    this.selectedTagOptionIds.set(Array.from(select.selectedOptions).map((o) => o.value));
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
      .exportSalesRegister(
        this.organizationId, this.fromDate(), this.toDate(), this.contactId() || null,
        this.selectedTagOptionIds(), full, page, pageSize,
      )
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `SalesRegister_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Sales Register.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.salesService
      .getSalesRegister(
        this.organizationId, this.fromDate(), this.toDate(), this.contactId() || null,
        this.selectedTagOptionIds(), this.page(), this.pageSize(),
      )
      .subscribe({
        next: (report) => {
          this.rows.set(report.items);
          this.totalCount.set(report.totalCount);
          this.totalValue.set(report.totalValue);
          this.totalTaxExemptValue.set(report.totalTaxExemptValue);
          this.totalTaxableValue.set(report.totalTaxableValue);
          this.totalVatAmount.set(report.totalVatAmount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Sales Register.');
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
