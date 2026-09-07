import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { SalesService } from '../../../core/sales/sales.service';
import { Invoice, InvoiceStatus } from '../../../core/sales/sales.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { BillingLocation } from '../../../core/organizations/organizations.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';

type StatusFilter = InvoiceStatus | 'All';

@Component({
  selector: 'app-invoice-list-page',
  imports: [RouterLink, PaginationControl, NepaliDatePipe],
  templateUrl: './invoice-list-page.html',
})
export class InvoiceListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly salesService = inject(SalesService);
  private readonly organizationsService = inject(OrganizationsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<Invoice[]>([]);
  protected readonly statusFilter = signal<StatusFilter>('All');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  protected readonly statuses: StatusFilter[] = ['All', 'Draft', 'Approved'];

  /**
   * Phase 32 -- the LOCATION column the live invoice grid carries between CUSTOMER and INVOICE NO
   * (confirmed 2026-09-07), plus the filter behind it.
   *
   * The list response already carries each invoice's `locationId`, so the name is resolved from the
   * tenant's own location list rather than by widening the query's projection -- the same list the
   * document form's picker is populated from, fetched once here.
   */
  protected readonly billingLocations = signal<BillingLocation[]>([]);
  protected readonly locationFilter = signal<string>('');

  constructor() {
    this.organizationsService.listBillingLocations(this.organizationId).subscribe({
      next: (locations) => this.billingLocations.set(locations),
      error: () => this.billingLocations.set([]),
    });

    this.load();
  }

  protected locationName(locationId: string | null): string | null {
    if (!locationId) {
      return null;
    }

    return this.billingLocations().find((x) => x.id === locationId)?.name ?? null;
  }

  protected selectLocation(locationId: string): void {
    this.locationFilter.set(locationId);
    this.page.set(1);
    this.load();
  }

  protected selectStatus(status: StatusFilter): void {
    this.statusFilter.set(status);
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

  private load(): void {
    this.loading.set(true);
    const status = this.statusFilter();
    this.salesService
      .listInvoices(
        this.organizationId,
        status === 'All' ? undefined : status,
        this.page(),
        this.pageSize(),
        this.locationFilter() || undefined,
      )
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load invoices.');
        },
      });
  }
}
