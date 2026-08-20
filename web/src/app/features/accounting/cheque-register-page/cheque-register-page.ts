import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { Contact } from '../../../core/contacts/contacts.models';
import { PaymentsService } from '../../../core/payments/payments.service';
import { ChequeDashboardSummaryDto, ChequeDto, ChequeStatus, PaymentDirection } from '../../../core/payments/payments.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';

type ChequeTab = 'dashboard' | 'received' | 'issued';

const NEXT_STATUSES: Record<ChequeStatus, ChequeStatus[]> = {
  Pending: ['Deposited', 'Cleared', 'Bounced', 'Cancelled'],
  Deposited: ['Cleared', 'Bounced', 'Cancelled'],
  Cleared: [],
  Bounced: [],
  Cancelled: [],
};

/** Phase 17 -- Dashboard (period + contact filter, Received/Issued counters, combined list) +
 * Cheque Received / Cheque Issued tabs (docs/phase-17-status.md decisions #4/#5). */
@Component({
  selector: 'app-cheque-register-page',
  imports: [RouterLink, PaginationControl, DatePipe, DecimalPipe],
  templateUrl: './cheque-register-page.html',
})
export class ChequeRegisterPage {
  private readonly route = inject(ActivatedRoute);
  private readonly paymentsService = inject(PaymentsService);
  private readonly contactsService = inject(ContactsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly nextStatuses = NEXT_STATUSES;

  protected readonly activeTab = signal<ChequeTab>('dashboard');
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly contacts = signal<Contact[]>([]);
  protected readonly contactId = signal('');
  protected readonly fromDate = signal('');
  protected readonly toDate = signal('');

  protected readonly summary = signal<ChequeDashboardSummaryDto>({ receivedCount: 0, issuedCount: 0 });
  protected readonly items = signal<ChequeDto[]>([]);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  constructor() {
    this.contactsService.listAllContacts(this.organizationId).subscribe({ next: (c) => this.contacts.set(c) });
    this.load();
  }

  protected switchTab(tab: ChequeTab): void {
    this.activeTab.set(tab);
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

  protected transition(cheque: ChequeDto, newStatus: ChequeStatus): void {
    this.paymentsService.transitionChequeStatus(this.organizationId, cheque.id, newStatus).subscribe({
      next: () => this.load(),
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not update cheque status.'),
    });
  }

  private directionForTab(): PaymentDirection | undefined {
    if (this.activeTab() === 'received') return 'Received';
    if (this.activeTab() === 'issued') return 'Paid';
    return undefined;
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);
    const contactId = this.contactId() || undefined;
    const fromDate = this.fromDate() || undefined;
    const toDate = this.toDate() || undefined;

    this.paymentsService
      .listCheques(this.organizationId, this.directionForTab(), undefined, contactId, fromDate, toDate, this.page(), this.pageSize())
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load cheques.');
        },
      });

    if (this.activeTab() === 'dashboard') {
      this.paymentsService.chequeDashboardSummary(this.organizationId, fromDate, toDate, contactId).subscribe({
        next: (summary) => this.summary.set(summary),
      });
    }
  }
}
