import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { ContactGroup } from '../../../core/contacts/contacts.models';
import { TradeByContactDto } from '../../../core/trade/trade-reports.models';
import { TradeReportsService } from '../../../core/trade/trade-reports.service';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';

/**
 * Purchase By Supplier -- confirmed live 2026-09-03: filters Period and Contact Group; columns
 * Supplier, Contact Group, Amount, Discount, Net Purchase, Vat Amount, Total Amount, with a
 * footer Total over all five money columns. Amount is gross, so Amount less Discount equals
 * Net Purchase on every row -- an identity the live figures satisfy.
 *
 * Returns are folded in as negative values rather than listed as their own rows, which is why a
 * heavily-credited supplier can read negative here.
 */
@Component({
  selector: 'app-purchase-by-supplier-page',
  imports: [PaginationControl, AmountPipe, BsDateInput],
  templateUrl: './purchase-by-supplier-page.html',
})
export class PurchaseBySupplierPage {
  private readonly route = inject(ActivatedRoute);
  private readonly contactsService = inject(ContactsService);
  private readonly reports = inject(TradeReportsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<TradeByContactDto | null>(null);
  protected readonly contactGroups = signal<ContactGroup[]>([]);

  protected readonly fromDate = signal(startOfYear());
  protected readonly toDate = signal(today());
  protected readonly contactGroupId = signal('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly exporting = signal(false);

  constructor() {
    this.contactsService.listContactGroups(this.organizationId).subscribe({ next: (g) => this.contactGroups.set(g) });
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

  protected onContactGroupChange(event: Event): void {
    this.contactGroupId.set((event.target as HTMLSelectElement).value);
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
      .exportTradeByContact(
        this.organizationId, 'purchase-by-supplier', this.fromDate(), this.toDate(),
        this.contactGroupId() || null, full, page, pageSize)
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `PurchaseBySupplier_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the Purchase By Supplier.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.reports
      .getTradeByContact(
        this.organizationId, 'purchase-by-supplier', this.fromDate(), this.toDate(),
        this.contactGroupId() || null, this.page(), this.pageSize())
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Purchase By Supplier.');
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
