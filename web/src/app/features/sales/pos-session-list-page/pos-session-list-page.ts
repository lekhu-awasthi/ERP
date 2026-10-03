import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PosSessionStatus } from '../../../core/pos/pos.models';
import { PosSessionRow } from '../../../core/pos/pos-reports.models';
import { PosReportsService } from '../../../core/pos/pos-reports.service';
import { StatusBanner } from '../../../shared/a11y/status-banner';
import { triggerBlobDownload } from '../../../shared/download-file';
import { AmountPipe } from '../../../shared/formatting/amount-pipe';
import { NepaliDatePipe } from '../../../shared/formatting/nepali-date-pipe';
import { ReportLocationFilter } from '../../../shared/locations/report-location-filter';
import { ListChrome } from '../../../shared/pagination/list-chrome';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { PosPeriod, PosPeriodFilter, lastDays } from '../../../shared/pos/pos-period-filter';

type StatusFilter = PosSessionStatus | 'All';

/**
 * Phase 66 -- Sales > POS Sessions: every drawer opened in a period, whose it was, what it took and
 * whether its count came up short (phase 61 section 5: "phase 66's reports are the natural home for a
 * list"). A row opens the session's own page, its X or Z report. A drawer's own events -- open, cash in
 * and out, close -- write no audit row (phase 61 Decision M); this list and that page are their record.
 *
 * <p>Its period is its own, not the shell's date range: a session list over "all time" has no meaning,
 * and the server asks for a period like every POS report.</p>
 */
@Component({
  selector: 'app-pos-session-list-page',
  imports: [
    RouterLink, StatusBanner, AmountPipe, NepaliDatePipe, ReportLocationFilter, ListChrome, PaginationControl,
    PosPeriodFilter,
  ],
  templateUrl: './pos-session-list-page.html',
})
export class PosSessionListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly reports = inject(PosReportsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;
  protected readonly statuses: StatusFilter[] = ['All', 'Open', 'Closed'];

  protected readonly period = signal<PosPeriod>(lastDays(7));
  protected readonly locationId = signal('');
  protected readonly search = signal('');
  protected readonly statusFilter = signal<StatusFilter>('All');

  protected readonly loading = signal(true);
  protected readonly exporting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly items = signal<PosSessionRow[]>([]);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  constructor() {
    this.load();
  }

  protected onPeriodChange(period: PosPeriod): void {
    this.period.set(period);
    this.reload();
  }

  protected onLocation(locationId: string): void {
    this.locationId.set(locationId);
    this.reload();
  }

  protected onSearch(term: string): void {
    this.search.set(term);
    this.reload();
  }

  protected selectStatus(status: StatusFilter): void {
    this.statusFilter.set(status);
    this.reload();
  }

  protected onPageChange(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected onPageSizeChange(pageSize: number): void {
    this.pageSize.set(pageSize);
    this.reload();
  }

  protected export(): void {
    this.exporting.set(true);
    const period = this.period();
    this.reports
      .exportSessions(this.organizationId, { ...period, locationId: this.locationId() }, this.status(), this.search())
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `PosSessions_${period.fromDate}_${period.toDate}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the sessions.');
        },
      });
  }

  private status(): PosSessionStatus | null {
    const status = this.statusFilter();
    return status === 'All' ? null : status;
  }

  private reload(): void {
    this.page.set(1);
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.reports
      .listSessions(
        this.organizationId, { ...this.period(), locationId: this.locationId() }, this.status(), this.search(),
        this.page(), this.pageSize())
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.errorMessage.set(null);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.items.set([]);
          this.totalCount.set(0);
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the sessions.');
        },
      });
  }
}
