import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ActivatedRoute } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CrmService } from '../../../core/crm/crm.service';
import { DealRow } from '../../../core/crm/crm.models';
import { TabParent } from '../../../core/contacts/tab-parent';
import { ActivityPanel } from '../../contacts/activity-panel/activity-panel';
import { AttachmentList } from '../../contacts/attachment-list/attachment-list';
import { ContactPersonnelList } from '../../contacts/contact-personnel-list/contact-personnel-list';
import { TaskList } from '../../workflow/task-list/task-list';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { StatusBanner } from '../../../shared/a11y/status-banner';

type DealTab = 'Overview' | 'Contact Personnel' | 'Tasks' | 'Documents' | 'Activity';

/**
 * Phase 43 (39 carried item #1) — the Deal detail page.
 *
 * <b>The tab list is the live one, not a guess.</b> Read on 2026-09-13 and written down in
 * docs/phase-39-status.md's list-and-detail table: a left rail, then Overview / Contact Personnel /
 * Tasks, plus a Documents dropzone and an Activity composer. That is five panes, and every one of
 * them is a component this codebase already had — the Deal was simply not a parent anything could
 * hang off.
 *
 * <b>Contact Personnel is the deal's <i>contact's</i> personnel, not the deal's.</b> A deal has no
 * personnel of its own; the live tab shows the people at the company the deal is with, which is the
 * existing Contact-scoped component pointed at `deal.contactId`. Worth stating because the obvious
 * reading of a tab on a Deal page is that it belongs to the Deal.
 *
 * <b>Why it reads its record through the list query.</b> `listDeals` gained an optional `id` filter
 * rather than this page getting a `GetDealQuery` of its own: the row is assembled from a contact
 * name, a lead-source name, a stage name and colour and one user name per assignee, and a second
 * query would be a second copy of that assembly (phase-26b's shared-reader rule). It also inherits
 * the private-deal visibility rule, which that handler enforces itself rather than through the
 * permission key — so a deal the caller may not see is a 404 here for free.
 */
@Component({
  selector: 'app-deal-detail-page',
  imports: [
    RouterLink,
    TaskList,
    AttachmentList,
    ActivityPanel,
    ContactPersonnelList,
    AmountPipe,
    NepaliDatePipe,
    StatusBanner,
  ],
  templateUrl: './deal-detail-page.html',
})
export class DealDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly crmService = inject(CrmService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly dealId = this.route.snapshot.paramMap.get('dealId')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly deal = signal<DealRow | null>(null);

  protected readonly tabs: readonly DealTab[] = [
    'Overview',
    'Contact Personnel',
    'Tasks',
    'Documents',
    'Activity',
  ];
  protected readonly activeTab = signal<DealTab>('Overview');

  /** The Documents and Activity tabs hang off the Deal itself; Tasks takes the parent enum. */
  protected readonly parent = computed<TabParent>(() => ({ kind: 'Deal', dealId: this.dealId }));

  constructor() {
    this.load();
  }

  protected switchTab(tab: DealTab): void {
    this.activeTab.set(tab);
  }

  private load(): void {
    this.loading.set(true);

    this.crmService.listDeals(this.organizationId, null, null, 1, 1, null, this.dealId).subscribe({
      next: (result) => {
        this.loading.set(false);
        const row = result.rows[0] ?? null;
        this.deal.set(row);

        if (!row) {
          this.errorMessage.set('Deal not found.');
        }
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(error));
      },
    });
  }
}
