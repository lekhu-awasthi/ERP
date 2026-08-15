import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ContactsService } from '../../../core/contacts/contacts.service';
import { ContactAgeingSummaryRowDto, ContactGroup } from '../../../core/contacts/contacts.models';

/**
 * Read-only report screen -- roadmap Phase 9's ContactAgeingSummaryQuery (ContactType=Customer).
 * Confirmed live shape (architecture-spec.md line 276 / erp-module-scan.md): Account Name, Contact
 * Group, 1-30/31-60/61-90/91+ Days, Total, as of a single date -- no From/To range, unlike Statement.
 * "Credit Term" is omitted -- no Contact or document anywhere in this codebase carries a CreditTermId
 * (see ContactAgeingSummaryQuery's own doc comment).
 */
@Component({
  selector: 'app-customer-ageing-summary-page',
  imports: [RouterLink],
  templateUrl: './customer-ageing-summary-page.html',
})
export class CustomerAgeingSummaryPage {
  private readonly route = inject(ActivatedRoute);
  private readonly contactsService = inject(ContactsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<ContactAgeingSummaryRowDto[]>([]);
  protected readonly contactGroups = signal<ContactGroup[]>([]);

  protected readonly asOfDate = signal(this.today());
  protected readonly contactGroupId = signal('');

  constructor() {
    this.contactsService.listContactGroups(this.organizationId).subscribe({ next: (g) => this.contactGroups.set(g) });
    this.load();
  }

  protected onAsOfDateChange(event: Event): void {
    this.asOfDate.set((event.target as HTMLInputElement).value);
    this.load();
  }

  protected onContactGroupChange(event: Event): void {
    this.contactGroupId.set((event.target as HTMLSelectElement).value);
    this.load();
  }

  protected totals(): { days1To30: number; days31To60: number; days61To90: number; days91Plus: number; total: number } {
    return this.rows().reduce(
      (sum, r) => ({
        days1To30: sum.days1To30 + r.days1To30,
        days31To60: sum.days31To60 + r.days31To60,
        days61To90: sum.days61To90 + r.days61To90,
        days91Plus: sum.days91Plus + r.days91Plus,
        total: sum.total + r.total,
      }),
      { days1To30: 0, days31To60: 0, days61To90: 0, days91Plus: 0, total: 0 },
    );
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.contactsService.getCustomerAgeingSummary(this.organizationId, this.asOfDate(), this.contactGroupId() || null).subscribe({
      next: (report) => {
        this.rows.set(report.rows);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the Customer Ageing Summary.');
      },
    });
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }
}
