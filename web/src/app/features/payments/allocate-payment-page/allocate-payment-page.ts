import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import { PaymentsService } from '../../../core/payments/payments.service';
import { AllocatablePaymentDto, PaymentDirection } from '../../../core/payments/payments.models';
import { BASE_CURRENCY_CODE } from '../../../core/organizations/organizations.models';
import { SalesService } from '../../../core/sales/sales.service';
import { PurchasingService } from '../../../core/purchasing/purchasing.service';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';

interface TargetOption {
  id: string;
  code: string;
  contactId: string;
  currencyCode: string;
}

/**
 * Phase 17 -- Allocate Customer/Supplier Payment (FR-5.12/FR-6.12, docs/phase-17-status.md
 * decision #8). One component parameterized by route data `direction` serves both the Customer and
 * Supplier screens. Lists two credit sources (decision #2): Approved Payments and Approved
 * JournalVouchers' own Contact-tagged lines -- rows are keyed by (sourceType, id), not id alone,
 * since a Payment's Id and a JournalVoucherLine's Id are independent Guid spaces.
 */
@Component({
  selector: 'app-allocate-payment-page',
  imports: [RouterLink, PaginationControl, DatePipe, DecimalPipe, NepaliDatePipe],
  templateUrl: './allocate-payment-page.html',
})
export class AllocatePaymentPage {
  private readonly route = inject(ActivatedRoute);
  private readonly paymentsService = inject(PaymentsService);
  private readonly contactsService = inject(ContactsService);
  private readonly salesService = inject(SalesService);
  private readonly purchasingService = inject(PurchasingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly direction = (this.route.snapshot.data['direction'] as PaymentDirection) ?? 'Received';
  protected readonly isCustomer = this.direction === 'Received';

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);
  protected readonly contacts = signal<Contact[]>([]);
  protected readonly contactId = signal('');
  protected readonly showAllocated = signal(false);
  protected readonly items = signal<AllocatablePaymentDto[]>([]);
  protected readonly targets = signal<TargetOption[]>([]);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  protected readonly applyRowKey = signal<string | null>(null);
  protected readonly applyTargetId = signal('');
  protected readonly applyAmount = signal(0);
  protected readonly applying = signal(false);

  constructor() {
    this.contactsService.listAllContacts(this.organizationId, this.isCustomer ? 'Customer' : 'Supplier').subscribe({
      next: (c) => this.contacts.set(c),
    });

    if (this.isCustomer) {
      this.salesService.listInvoices(this.organizationId, 'Approved', 1, 200).subscribe({
        next: (r) =>
          this.targets.set(
            r.items.map((i) => ({
              id: i.id,
              code: i.code,
              contactId: i.contactId,
              currencyCode: i.currencyCode ?? BASE_CURRENCY_CODE,
            })),
          ),
      });
    } else {
      this.purchasingService.listAllPurchaseBills(this.organizationId, 'Approved').subscribe({
        next: (items) =>
          this.targets.set(
            items.map((i) => ({
              id: i.id,
              code: i.code,
              contactId: i.contactId,
              currencyCode: i.currencyCode ?? BASE_CURRENCY_CODE,
            })),
          ),
      });
    }

    this.load();
  }

  /**
   * Phase 36 -- the contact's documents **in this credit's own currency**.
   *
   * <p>A payment can only be allocated to documents in its own currency (phase 28 Decision F): the
   * allocation Amount is a single number with no currency of its own, so settling a USD invoice
   * with an NPR receipt would relieve it by the whole exchange rate. The server has always refused
   * that; this screen used to offer it anyway and surface the refusal as an error on Confirm, which
   * is a choice the user could make and only then be told they could not. Filtering the picker is
   * the cheaper half of the same rule.</p>
   */
  protected targetsFor(item: AllocatablePaymentDto): TargetOption[] {
    const currencyCode = item.currencyCode || BASE_CURRENCY_CODE;
    return this.targets().filter((t) => t.contactId === item.contactId && t.currencyCode === currencyCode);
  }

  /** True for a credit in a currency none of this contact's open documents share. */
  protected hasNoTargets(item: AllocatablePaymentDto): boolean {
    return this.targetsFor(item).length === 0;
  }

  protected isForeign(item: AllocatablePaymentDto): boolean {
    return (item.currencyCode || BASE_CURRENCY_CODE) !== BASE_CURRENCY_CODE;
  }

  protected switchTab(showAllocated: boolean): void {
    this.showAllocated.set(showAllocated);
    this.page.set(1);
    this.load();
  }

  protected onFilterChange(): void {
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

  protected rowKey(item: AllocatablePaymentDto): string {
    return `${item.sourceType}:${item.id}`;
  }

  protected startApply(item: AllocatablePaymentDto): void {
    this.applyRowKey.set(this.rowKey(item));
    this.applyTargetId.set('');
    this.applyAmount.set(item.balance);
  }

  protected cancelApply(): void {
    this.applyRowKey.set(null);
  }

  protected confirmApply(item: AllocatablePaymentDto): void {
    const targetDocumentId = this.applyTargetId();
    const amount = this.applyAmount();
    if (!targetDocumentId || amount <= 0) {
      return;
    }

    this.applying.set(true);
    this.errorMessage.set(null);

    this.paymentsService
      .applyPaymentAllocation(
        this.organizationId,
        item.sourceType,
        item.id,
        item.parentDocumentId,
        this.isCustomer ? 'Invoice' : 'PurchaseBill',
        targetDocumentId,
        amount,
      )
      .subscribe({
        next: () => {
          this.applying.set(false);
          this.applyRowKey.set(null);
          this.successMessage.set(`Applied ${amount.toFixed(2)} from ${item.code}.`);
          this.load();
        },
        error: (err: unknown) => {
          this.applying.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not apply allocation. Please try again.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);
    this.paymentsService
      .listAllocatablePayments(
        this.organizationId,
        this.direction,
        this.showAllocated(),
        this.contactId() || undefined,
        this.page(),
        this.pageSize(),
      )
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load payments.');
        },
      });
  }
}
