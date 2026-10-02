import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { DenominationCount, POS_MODE_LABELS, PosTill, PosTillSummary } from '../../../core/pos/pos.models';
import { PosService } from '../../../core/pos/pos.service';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { DenominationCountInput } from '../denomination-count/denomination-count';

/**
 * Phase 62 -- where a cashier starts: the tills they may open a drawer at
 * (`GET /pos/tills`, already filtered to the locations where they hold `Sales.Invoice.Create`), their
 * own open session at each, and a Start Session form that counts the float the way the location
 * asks -- an amount, or note by note when it requires cash verification.
 *
 * <p>A Restaurant till opens onto its floor (phase 64): tables, orders and kitchen tickets. Billing an
 * order is phase 65's, and the card says so rather than hiding it (phase 49: a chosen gap owes a
 * surface); it no longer offers a drawer, because nothing at a restaurant till takes money yet.</p>
 */
@Component({
  selector: 'app-pos-launcher-page',
  imports: [RouterLink, StatusBanner, NepaliDatePipe, DenominationCountInput],
  templateUrl: './pos-launcher-page.html',
})
export class PosLauncherPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly posService = inject(PosService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly modeLabels = POS_MODE_LABELS;

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly tills = signal<PosTillSummary[]>([]);

  /** The till whose Start Session form is open, with what that form needs to know. */
  protected readonly starting = signal<PosTill | null>(null);
  protected readonly openingAmount = signal<number | null>(null);
  protected readonly openingCount = signal<DenominationCount[]>([]);
  protected readonly submitting = signal(false);

  constructor() {
    this.posService.listTills(this.organizationId).subscribe({
      next: (tills) => {
        this.tills.set(tills);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.loadFailed.set(true);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the tills.');
      },
    });
  }

  protected beginStart(locationId: string): void {
    this.errorMessage.set(null);
    this.openingAmount.set(null);
    this.openingCount.set([]);

    this.posService.getTill(this.organizationId, locationId).subscribe({
      next: (till) => this.starting.set(till),
      error: (err: unknown) => this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the till.'),
    });
  }

  protected cancelStart(): void {
    this.starting.set(null);
  }

  protected onAmountInput(event: Event): void {
    const value = (event.target as HTMLInputElement).valueAsNumber;
    this.openingAmount.set(Number.isFinite(value) ? value : null);
  }

  protected startSession(): void {
    const till = this.starting();
    if (!till) return;

    const byCount = till.cashVerificationRequired;
    if (!byCount && (this.openingAmount() === null || this.openingAmount()! < 0)) {
      this.errorMessage.set('Enter the opening float: the cash already in the drawer, which can be 0.');
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);

    this.posService
      .openSession(this.organizationId, {
        locationId: till.locationId,
        openingAmount: byCount ? null : this.openingAmount(),
        denominations: byCount ? this.openingCount() : null,
      })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          void this.router.navigate(['/organizations', this.organizationId, 'pos', 'till', till.locationId]);
        },
        error: (err: unknown) => {
          this.submitting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not start the session.');
        },
      });
  }
}
