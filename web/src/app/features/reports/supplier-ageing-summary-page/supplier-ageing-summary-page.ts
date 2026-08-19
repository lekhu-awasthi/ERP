import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { ContactAgeingSummaryDto, ContactGroup } from '../../../core/contacts/contacts.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';

/**
 * Read-only report screen -- roadmap Phase 9's ContactAgeingSummaryQuery (ContactType=Supplier).
 * Confirmed live directly against the reference product's own Supplier Ageing Summary screen during
 * this phase's design pass -- identical columns to Customer's confirmed shape (Account Name, Contact
 * Group, 1-30/31-60/61-90/91+ Days, Total), the mirror bet the user chose over a Customer-only phase.
 * Paginated (Phase 16c) -- the footer totals come from the backend now (Total*Days fields), not a
 * client-side reduce over the displayed page.
 */
@Component({
  selector: 'app-supplier-ageing-summary-page',
  imports: [RouterLink, PaginationControl],
  templateUrl: './supplier-ageing-summary-page.html',
})
export class SupplierAgeingSummaryPage {
  private readonly route = inject(ActivatedRoute);
  private readonly contactsService = inject(ContactsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<ContactAgeingSummaryDto | null>(null);
  protected readonly contactGroups = signal<ContactGroup[]>([]);

  protected readonly asOfDate = signal(this.today());
  protected readonly contactGroupId = signal('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly exporting = signal(false);

  constructor() {
    this.contactsService.listContactGroups(this.organizationId).subscribe({ next: (g) => this.contactGroups.set(g) });
    this.load();
  }

  protected onAsOfDateChange(event: Event): void {
    this.asOfDate.set((event.target as HTMLInputElement).value);
    this.page.set(1);
    this.load();
  }

  protected onContactGroupChange(event: Event): void {
    this.contactGroupId.set((event.target as HTMLSelectElement).value);
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
    this.contactsService
      .exportSupplierAgeingSummary(this.organizationId, this.asOfDate(), this.contactGroupId() || null, full, page, pageSize)
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `SupplierAgeingSummary_${this.asOfDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Supplier Ageing Summary.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.contactsService
      .getSupplierAgeingSummary(
        this.organizationId, this.asOfDate(), this.contactGroupId() || null, this.page(), this.pageSize())
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Supplier Ageing Summary.');
        },
      });
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }
}
