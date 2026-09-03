import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import {
  AGEABLE_TYPE_LABELS,
  AgeableDocumentType,
  DocumentAgeDto,
  SUPPLIER_AGEABLE_TYPES,
} from '../../../core/trade/trade-reports.models';
import { TradeReportsService } from '../../../core/trade/trade-reports.service';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';

/**
 * Purchase Bill Age -- confirmed live 2026-09-03. Every outstanding document with its own age, where
 * **age is measured from the Due Date** and only the As Of date bounds the document set (the live
 * period picker's From end does not filter, which is right for an ageing report).
 *
 * Invoice and PurchaseBill store no due date in this codebase, so Due Date equals the document date
 * for them -- the same gap phase-9 recorded when it dropped the live Ageing Summary's Credit Term
 * column. See docs/phase-26b-status.md.
 *
 * Checkbox state lives in its own signal rather than being read off the DOM: the app is zoneless, so
 * a computed over a control's `.value` would cache forever (phase-17).
 */
@Component({
  selector: 'app-purchase-bill-age-page',
  imports: [PaginationControl, AmountPipe, BsDateInput, NepaliDatePipe],
  templateUrl: './purchase-bill-age-page.html',
})
export class PurchaseBillAgePage {
  private readonly route = inject(ActivatedRoute);
  private readonly contactsService = inject(ContactsService);
  private readonly reports = inject(TradeReportsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly ageableTypes = SUPPLIER_AGEABLE_TYPES;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<DocumentAgeDto | null>(null);
  protected readonly contacts = signal<Contact[]>([]);

  protected readonly fromDate = signal(startOfYear());
  protected readonly asOfDate = signal(today());
  protected readonly contactId = signal('');
  protected readonly selectedTypes = signal<AgeableDocumentType[]>([]);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly exporting = signal(false);

  constructor() {
    this.contactsService
      .listAllContacts(this.organizationId, 'Supplier')
      .subscribe({ next: (rows) => this.contacts.set(rows) });
    this.load();
  }

  protected typeLabel(type: AgeableDocumentType): string {
    return AGEABLE_TYPE_LABELS[type];
  }

  protected onFromDateChange(value: string): void {
    this.fromDate.set(value);
    this.reload();
  }

  protected onAsOfDateChange(value: string): void {
    this.asOfDate.set(value);
    this.reload();
  }

  protected onContactChange(event: Event): void {
    this.contactId.set((event.target as HTMLSelectElement).value);
    this.reload();
  }

  protected onTypeToggle(type: AgeableDocumentType): void {
    const current = this.selectedTypes();
    this.selectedTypes.set(
      current.includes(type) ? current.filter((x) => x !== type) : [...current, type]);
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
      .exportDocumentAge(
        this.organizationId, 'purchase-bill-age', this.fromDate(), this.asOfDate(),
        this.contactId() || null, this.selectedTypes(), full, page, pageSize)
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `PurchaseBillAge_${this.fromDate()}_${this.asOfDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Purchase Bill Age.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.reports
      .getDocumentAge(
        this.organizationId, 'purchase-bill-age', this.fromDate(), this.asOfDate(),
        this.contactId() || null, this.selectedTypes(), this.page(), this.pageSize())
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Purchase Bill Age.');
        },
      });
  }
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

function startOfYear(): string {
  return `${new Date().getFullYear()}-01-01`;
}
