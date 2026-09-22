import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { AccountingService } from '../../../core/accounting/accounting.service';
import {
  BankBalanceHistoryDto,
  BankBalanceHistoryPoint,
  BankReconciliationReportDto,
} from '../../../core/accounting/accounting.models';
import { extractErrorMessage } from '../../../core/auth/api-error';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { triggerBlobDownload } from '../../../shared/download-file';

/**
 * Phase 56 — the Reconciliation Report, read live from the reference product on 2026-09-21:
 *
 * <pre>
 *   Balance In TIGG App        As of 21-09-2026   NPR 791
 *   Balance in Cash In Hand    As of 21-09-2026   NPR 1,582
 *   Difference                                    NPR -791
 *   > Unrecognized Transaction in Tigg App        NPR 0
 *   > Unrecognized Transaction in Bank            NPR 791
 * </pre>
 *
 * <p><b>One as-of date, not a range</b> — its filter bar carries a single Date control, which is the
 * right shape for the subject: a reconciliation report answers "do the two records agree today",
 * and a balance is cumulative rather than a period figure. The date goes through
 * `app-bs-date-input` like every date a user types in this app (phase 23, enforced by
 * `sweep-guard.spec.ts`).</p>
 *
 * <p><b>The two balances come from different date fields, deliberately.</b> The bank side cuts off
 * on the statement line's value date and the book side on the posting date, because they are two
 * records kept by two different people — which is the whole premise of reconciling them. Each row
 * says which date it is using.</p>
 *
 * <p><b>Phase 57 added the export and the Balance History chart.</b> The chart is the reference
 * product's own `/balance-history/:id`, which it draws on the account Overview — a screen this app
 * does not have, so it lives here, beside the two figures it is the history of. Both series come
 * from the same two records this report sums, so the chart's last point <i>is</i> the figure printed
 * above it (phase 26b's shared-reader rule).</p>
 */
@Component({
  selector: 'app-bank-reconciliation-report-page',
  imports: [RouterLink, StatusBanner, BsDateInput, AmountPipe, NepaliDatePipe],
  templateUrl: './bank-reconciliation-report-page.html',
})
export class BankReconciliationReportPage {
  private readonly route = inject(ActivatedRoute);
  private readonly accountingService = inject(AccountingService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly bankAccountId = this.route.snapshot.paramMap.get('accountId')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly report = signal<BankReconciliationReportDto | null>(null);

  /** Null lets the server answer "as of today", in Nepal's wall clock rather than the browser's. */
  protected readonly asOfDate = signal<string | null>(null);

  /** The two sections start collapsed, exactly as the reference product's do. */
  protected readonly bookExpanded = signal(false);
  protected readonly bankExpanded = signal(false);

  /** Phase 57 -- the chart's own series and state. */
  protected readonly history = signal<BankBalanceHistoryDto | null>(null);
  protected readonly exporting = signal(false);

  /** The reference product's window. Not offered as a control: it does not offer one either, and a
   * range on a screen that already carries an as-of date would be two date controls saying
   * different things. */
  protected readonly historyDays = 30;

  constructor() {
    this.load();
  }

  protected exportReport(): void {
    this.exporting.set(true);
    this.errorMessage.set(null);

    this.accountingService
      .exportBankReconciliationReport(this.organizationId, this.bankAccountId, this.asOfDate())
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, 'BankReconciliationReport.xlsx');
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the reconciliation report.');
        },
      });
  }

  // --- the chart ---
  //
  // Hand-drawn SVG rather than a charting library: this is the first and only chart in the app, and
  // a dependency for two polylines would be a bundle cost with nothing else to amortise it against
  // (phase 42's measured budget). The viewBox is a fixed 100x40 user-space grid that the container
  // scales, so nothing here needs to know the rendered width.

  protected readonly chartWidth = 100;
  protected readonly chartHeight = 40;

  protected points(): readonly BankBalanceHistoryPoint[] {
    return this.history()?.points ?? [];
  }

  protected bookPath(): string {
    return this.path((p) => p.bookBalance);
  }

  protected bankPath(): string {
    return this.path((p) => p.bankBalance);
  }

  /** The value at either end of the y axis, so the template can label what the lines are drawn
   * against rather than leaving the reader to guess the scale. */
  protected chartMax(): number {
    return this.bounds().max;
  }

  protected chartMin(): number {
    return this.bounds().min;
  }

  /**
   * One sentence describing the chart, for the `aria-label` on it. The picture is `aria-hidden` and
   * the numbers are also rendered as a visually-hidden table below it -- a line chart cannot be read
   * out, and a label alone would be a summary standing in for data the sighted reader can see
   * (phase 40).
   */
  protected chartSummary(): string {
    const points = this.points();

    if (points.length === 0) {
      return 'No balance history for this account.';
    }

    const last = points[points.length - 1];

    return (
      `Balance history over ${points.length} days. ` +
      `This app ends at ${last.bookBalance.toFixed(2)}, the bank statement at ` +
      `${last.bankBalance.toFixed(2)}.`
    );
  }

  private bounds(): { min: number; max: number } {
    const values = this.points().flatMap((p) => [p.bookBalance, p.bankBalance]);

    if (values.length === 0) {
      return { min: 0, max: 0 };
    }

    // Zero is always on the scale: a chart of two balances that both happen to sit near 900 would
    // otherwise magnify the gap between them into the whole height and read as a crisis.
    const min = Math.min(0, ...values);
    const max = Math.max(0, ...values);

    return { min, max: max === min ? min + 1 : max };
  }

  private path(value: (point: BankBalanceHistoryPoint) => number): string {
    const points = this.points();

    if (points.length === 0) {
      return '';
    }

    const { min, max } = this.bounds();
    const step = points.length === 1 ? 0 : this.chartWidth / (points.length - 1);

    return points
      .map((point, index) => {
        const x = (index * step).toFixed(2);
        // SVG's y axis grows downward, so the value is subtracted from the height.
        const y = (this.chartHeight - ((value(point) - min) / (max - min)) * this.chartHeight).toFixed(2);
        return `${x},${y}`;
      })
      .join(' ');
  }


  protected onAsOfDateChange(value: string | null): void {
    this.asOfDate.set(value);
    this.load();
  }

  protected toggleBook(): void {
    this.bookExpanded.update((x) => !x);
  }

  protected toggleBank(): void {
    this.bankExpanded.update((x) => !x);
  }

  private load(): void {
    this.loading.set(true);
    this.loadHistory();

    this.accountingService
      .getBankReconciliationReport(this.organizationId, this.bankAccountId, this.asOfDate())
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the reconciliation report.');
        },
      });
  }

  /**
   * The chart's own request, and a failure here does not fail the page: the four figures above are
   * the report, and a missing chart is a missing illustration rather than a broken screen.
   */
  private loadHistory(): void {
    this.accountingService
      .getBankBalanceHistory(this.organizationId, this.bankAccountId, this.asOfDate(), this.historyDays)
      .subscribe({
        next: (history) => this.history.set(history),
        error: () => this.history.set(null),
      });
  }
}
