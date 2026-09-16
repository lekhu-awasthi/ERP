import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { DealRow } from '../../../core/crm/crm.models';
import { CrmService } from '../../../core/crm/crm.service';
import { DealForm } from '../deal-form/deal-form';
import { StatusBanner } from '../../../shared/a11y/status-banner';

/**
 * Phase 48 (43 carried item #4) — the route that makes `UpdateDealCommand` reachable.
 * See {@link TaskEditPage} for why this is a route and not a modal.
 *
 * <p>Reading through `listDeals`' `id` filter also inherits the private-deal visibility rule that
 * handler enforces itself, so a deal the caller may not see is a 404 here for free — the same
 * property the detail page relies on.</p>
 */
@Component({
  selector: 'app-deal-edit-page',
  imports: [RouterLink, DealForm, StatusBanner],
  templateUrl: './deal-edit-page.html',
})
export class DealEditPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly crmService = inject(CrmService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly dealId = this.route.snapshot.paramMap.get('dealId')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly deal = signal<DealRow | null>(null);

  constructor() {
    this.load();
  }

  protected back(): void {
    void this.router.navigate(['/organizations', this.organizationId, 'crm', 'deals', this.dealId]);
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
