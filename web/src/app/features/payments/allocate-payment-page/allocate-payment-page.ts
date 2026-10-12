import { DecimalPipe } from '@angular/common';
import { Component, Injector, afterNextRender, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import { PaymentsService } from '../../../core/payments/payments.service';
import { AllocatablePaymentDto, OpenAllocationTarget, PaymentDirection } from '../../../core/payments/payments.models';
import { BASE_CURRENCY_CODE } from '../../../core/organizations/organizations.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { StatusBanner } from '../../../shared/a11y/status-banner';

/**
 * Phase 17 -- Allocate Customer/Supplier Payment (FR-5.12/FR-6.12, docs/phase-17-status.md
 * decision #8). One component parameterized by route data `direction` serves both the Customer and
 * Supplier screens. Lists two credit sources (decision #2): Approved Payments and Approved
 * JournalVouchers' own Contact-tagged lines -- rows are keyed by (sourceType, id), not id alone,
 * since a Payment's Id and a JournalVoucherLine's Id are independent Guid spaces.
 */
@Component({
  selector: 'app-allocate-payment-page',
  imports: [RouterLink, PaginationControl, DecimalPipe, NepaliDatePipe, StatusBanner],
  templateUrl: './allocate-payment-page.html',
})
export class AllocatePaymentPage {
  private readonly route = inject(ActivatedRoute);
  private readonly paymentsService = inject(PaymentsService);
  private readonly contactsService = inject(ContactsService);
  private readonly injector = inject(Injector);

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
  /**
   * Phase 88 -- the open documents of the row being applied: its contact's, in its currency, with something left to
   * settle, oldest due first (`ListOpenAllocationTargetsQuery`). Loaded when Apply opens the form, because a row is one
   * contact and one currency. Until now this was the first 200 approved documents of the whole organization, filtered
   * here, settled ones included.
   */
  protected readonly targets = signal<OpenAllocationTarget[]>([]);
  protected readonly targetsLoading = signal(false);
  private targetsRequest = 0;

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

    this.load();
  }

  /**
   * Phase 36 -- the contact's documents **in this credit's own currency** (phase 88: the server now narrows them, by
   * contact, currency and what is left; this keeps the rule stated where the picker is).
   *
   * <p>A payment can only be allocated to documents in its own currency (phase 28 Decision F): the
   * allocation Amount is a single number with no currency of its own, so settling a USD invoice
   * with an NPR receipt would relieve it by the whole exchange rate. The server has always refused
   * that; this screen used to offer it anyway and surface the refusal as an error on Confirm, which
   * is a choice the user could make and only then be told they could not. Filtering the picker is
   * the cheaper half of the same rule.</p>
   */
  protected targetsFor(item: AllocatablePaymentDto): OpenAllocationTarget[] {
    const currencyCode = item.currencyCode || BASE_CURRENCY_CODE;
    return this.targets().filter((t) => t.currencyCode === currencyCode);
  }

  /** True once loaded, for a credit with no open document of this contact's in its currency. */
  protected hasNoTargets(item: AllocatablePaymentDto): boolean {
    return !this.targetsLoading() && this.targetsFor(item).length === 0;
  }

  /** Phase 88 -- choosing a document fills Amount with as much as both sides allow, as the vendor's This Allocation does. */
  protected onTargetChange(item: AllocatablePaymentDto, targetId: string): void {
    this.applyTargetId.set(targetId);
    const target = this.targets().find((t) => t.id === targetId);
    if (target) {
      this.applyAmount.set(Math.min(item.balance, target.balance));
    }
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

  /** Phase 87 -- Apply gives way to Cancel, so focus goes to the form it opened (the document picker), never to the page. */
  protected startApply(item: AllocatablePaymentDto): void {
    this.applyRowKey.set(this.rowKey(item));
    this.applyTargetId.set('');
    this.applyAmount.set(item.balance);
    this.loadTargets(item);
    this.focusAfterRender('allocate-payment-page-select-2');
  }

  private loadTargets(item: AllocatablePaymentDto): void {
    const request = ++this.targetsRequest;
    this.targets.set([]);
    this.targetsLoading.set(true);
    this.paymentsService
      .listOpenAllocationTargets(this.organizationId, item.contactId, this.direction, item.currencyCode || BASE_CURRENCY_CODE)
      .subscribe({
        next: (targets) => {
          if (request === this.targetsRequest) {
            this.targets.set(targets);
            this.targetsLoading.set(false);
          }
        },
        error: (err: unknown) => {
          if (request === this.targetsRequest) {
            this.targetsLoading.set(false);
            this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the open documents.');
          }
        },
      });
  }

  /** Phase 87 -- Cancel gives way to Apply again: focus goes back to that row's Apply. */
  protected cancelApply(item: AllocatablePaymentDto): void {
    this.applyRowKey.set(null);
    this.focusAfterRender(`allocate-apply-${item.id}`);
  }

  private focusAfterRender(id: string): void {
    afterNextRender(() => document.getElementById(id)?.focus(), { injector: this.injector });
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
          // Phase 87 -- the form and its Confirm are gone and the row may be too once the list reloads: focus goes to the
          // page's heading, with the outcome in the status banner beneath it.
          this.focusAfterRender('allocate-payment-page-heading');
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
