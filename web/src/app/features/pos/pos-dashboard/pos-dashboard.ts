import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { PosDashboard, PosSalesPoint } from '../../../core/pos/pos-reports.models';
import { PosReportsService } from '../../../core/pos/pos-reports.service';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { ReportLocationFilter } from '../../../shared/locations/report-location-filter';
import { PosPeriod, PosPeriodFilter, lastDays } from '../../../shared/pos/pos-period-filter';

/** One bar of the chart: where it sits, in percent of the plot, and what it says. */
interface Bar {
  readonly point: PosSalesPoint;
  readonly label: string;
  readonly showLabel: boolean;
  readonly top: number;
  readonly height: number;
  readonly negative: boolean;
}

/**
 * Phase 66 -- the POS home's overview, under the tills on the launcher (the vendor's POS home is its
 * location cards over an overview, read live 2026-10-02). Every figure is the server's
 * GetPosDashboardQuery, which reads the readers the Day Report and Sales by Item read, so the tiles and
 * the reports they link to cannot disagree -- the vendor's said 611 over a products panel of 610.20.
 *
 * <p><b>The panels add up to the tiles, on screen.</b> The products panel ends with the round-off and
 * its total is Net Sales; the payments panel ends with change and credit and its total is Net Sales.
 * The vendor's "Other Products" row clamped a negative difference to zero; ours prints a row for the
 * round-off and computes nothing in the browser.</p>
 *
 * <p><b>The chart draws its points.</b> One bar per hour (one day) or per day, from zero, never a
 * smoothing spline -- the vendor's basis curve drew a 611 day peaking near 400. The bars are
 * aria-hidden and the series is also a visually-hidden table (phase 57's pattern).</p>
 *
 * <p>It reads Pos.Session.ViewAll, the Day Report's key: a cashier without it gets no overview at all,
 * rather than a panel of refusals on the screen they start their shift from.</p>
 */
@Component({
  selector: 'app-pos-dashboard',
  imports: [RouterLink, AmountPipe, NepaliDatePipe, ReportLocationFilter, PosPeriodFilter, StatusBanner],
  templateUrl: './pos-dashboard.html',
})
export class PosDashboardPanel implements OnInit {
  private readonly reports = inject(PosReportsService);

  readonly organizationId = input.required<string>();

  protected readonly period = signal<PosPeriod>(lastDays(1));
  protected readonly locationId = signal('');
  protected readonly loading = signal(true);
  /** Set when the caller does not hold the dashboard's key: the overview is then not shown at all. */
  protected readonly refused = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly dashboard = signal<PosDashboard | null>(null);

  protected readonly bars = computed<Bar[]>(() => {
    const d = this.dashboard();
    if (!d) return [];

    const max = Math.max(0, ...d.series.map((x) => x.net));
    const min = Math.min(0, ...d.series.map((x) => x.net));
    const span = max - min || 1;
    const zero = (max / span) * 100;
    const step = Math.max(1, Math.ceil(d.series.length / 12));

    return d.series.map((point, index) => {
      const height = (Math.abs(point.net) / span) * 100;
      const negative = point.net < 0;
      return {
        point,
        label: point.hour !== null ? `${String(point.hour).padStart(2, '0')}:00` : point.date,
        showLabel: index % step === 0,
        top: negative ? zero : zero - height,
        height,
        negative,
      };
    });
  });

  protected readonly chartMax = computed(() => Math.max(0, ...(this.dashboard()?.series.map((x) => x.net) ?? [0])));

  ngOnInit(): void {
    this.load();
  }

  protected onPeriodChange(period: PosPeriod): void {
    this.period.set(period);
    this.load();
  }

  protected onLocationChange(value: string): void {
    this.locationId.set(value);
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.reports
      .getDashboard(this.organizationId(), { ...this.period(), locationId: this.locationId() })
      .subscribe({
        next: (dashboard) => {
          this.dashboard.set(dashboard);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          if (err instanceof HttpErrorResponse && err.status === 403) {
            this.refused.set(true);
            return;
          }
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the overview.');
        },
      });
  }
}
