import { Component, OnInit, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { DealStage } from '../../../core/configuration/configuration.models';
import { CrmService } from '../../../core/crm/crm.service';
import { DealRow, DealStatus } from '../../../core/crm/crm.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { DealForm } from '../deal-form/deal-form';

/**
 * Shared Deal list component (roadmap Phase 15) -- reused, not duplicated, across its two
 * confirmed live integration points (Contact detail page's Deals tab, Organization dashboard's
 * Deals section), the same "don't duplicate, extract a shared reader" discipline Phase 13's
 * TaskList established. Three status sub-tabs (Pending/Won/Lost, the confirmed live UI shape) plus
 * an inline create form, a per-row inline Stage dropdown, and Won/Lost quick actions.
 *
 * contactId() is optional (unlike TaskList's required parentId) -- when bound (Contact detail
 * page), the create form's Contact is implied and hidden; when omitted (Organization dashboard),
 * the create form shows a Contact picker sourced from Customer/Lead contacts only (Supplier is
 * rejected server-side -- see CrmValidation.EnsureContactCanHaveDealAsync).
 */
@Component({
  selector: 'app-deal-list',
  imports: [RouterLink, PaginationControl, AmountPipe, NepaliDatePipe, StatusBanner, DealForm],
  templateUrl: './deal-list.html',
})
export class DealList implements OnInit {
  private readonly crmService = inject(CrmService);
  private readonly configurationService = inject(ConfigurationService);

  readonly organizationId = input.required<string>();
  readonly contactId = input<string | null>(null);

  protected readonly statuses: DealStatus[] = ['Pending', 'Won', 'Lost'];
  protected readonly activeStatus = signal<DealStatus>('Pending');

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<DealRow[]>([]);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  protected readonly dealStages = signal<DealStage[]>([]);

  protected readonly showCreateForm = signal(false);

  /** Phase 39 -- the search box the live CRM > Deals list carries. Its own signal, written by the
   * input handler: the app is zoneless, so a `computed()` over a FormControl's value caches for
   * ever (phase-17). */
  protected readonly search = signal('');

  /** Resets to page 1 like every sibling filter on this list -- without it, a narrowing search
   * applied on page 3 reads as "this pipeline is empty" (phase-35b). */
  protected onSearchInput(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
    this.page.set(1);
    this.load();
  }

  // Reads required inputs, so this runs from ngOnInit (guaranteed after Angular has bound the
  // inputs), not the constructor (NG8118 -- an input() isn't readable that early).
  //
  // Deal stages stay here and not in app-deal-form: they drive the per-row inline Stage dropdown on
  // the grid, which is a list control rather than a form field (the live Update Deals modal has no
  // Stage either).
  ngOnInit(): void {
    this.configurationService.listDealStages(this.organizationId()).subscribe({
      next: (stages) => this.dealStages.set(stages.sort((a, b) => a.sortOrder - b.sortOrder)),
    });
    this.load();
  }

  protected switchTab(status: DealStatus): void {
    this.activeStatus.set(status);
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

  protected toggleCreateForm(): void {
    this.showCreateForm.set(!this.showCreateForm());
    this.errorMessage.set(null);
  }

  /** Phase 48 -- see TaskList.onCreated; a new deal is always Pending. */
  protected onCreated(): void {
    this.showCreateForm.set(false);
    this.activeStatus.set('Pending');
    this.load();
  }

  protected changeStage(row: DealRow, event: Event): void {
    const dealStageId = (event.target as HTMLSelectElement).value;
    if (!dealStageId) {
      return;
    }
    this.crmService.moveDealToStage(this.organizationId(), row.id, { dealStageId }).subscribe({
      next: () => this.load(),
      error: (err: unknown) => {
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not move the deal. Please try again.');
      },
    });
  }

  protected markWon(row: DealRow): void {
    this.crmService.markDealWon(this.organizationId(), row.id).subscribe({
      next: () => this.load(),
      error: (err: unknown) => {
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not mark the deal Won. Please try again.');
      },
    });
  }

  protected markLost(row: DealRow): void {
    this.crmService.markDealLost(this.organizationId(), row.id).subscribe({
      next: () => this.load(),
      error: (err: unknown) => {
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not mark the deal Lost. Please try again.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);
    this.crmService
      .listDeals(
        this.organizationId(), this.contactId(), this.activeStatus(),
        this.page(), this.pageSize(), this.search().trim() || null)
      .subscribe({
        next: (result) => {
          this.rows.set(result.rows);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load deals.');
        },
      });
  }
}
