import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { CatalogueReportsService } from '../../../core/reports/catalogue-reports.service';
import { UserLogRowDto } from '../../../core/reports/catalogue-reports.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { OrganizationMember } from '../../../core/organizations/organizations.models';
import { DEFAULT_PAGE_SIZE } from '../../../core/common/paged-result';
import { PaginationControl } from '../../../shared/pagination/pagination-control';
import { triggerBlobDownload } from '../../../shared/download-file';
import { BsDateInput } from '../../../shared/formatting/bs-date-input';

/**
 * Phase 26c -- the login/logout/failed-login event log. Admin-only, because it discloses per-person
 * IP addresses, devices and the addresses that failed to sign in.
 *
 * The timestamp is rendered with Angular's own `DatePipe`, not `NepaliDatePipe`: this is a
 * to-the-second audit trail, and the seconds matter more here than the calendar does.
 */
@Component({
  selector: 'app-user-log-page',
  imports: [RouterLink, DatePipe, PaginationControl, BsDateInput],
  templateUrl: './user-log-page.html',
})
export class UserLogPage {
  private readonly route = inject(ActivatedRoute);
  private readonly reports = inject(CatalogueReportsService);
  private readonly organizationsService = inject(OrganizationsService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly rows = signal<UserLogRowDto[]>([]);
  protected readonly members = signal<OrganizationMember[]>([]);

  protected readonly fromDate = signal(firstOfMonth());
  protected readonly toDate = signal(today());
  protected readonly userId = signal('');

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly totalCount = signal(0);

  protected readonly exporting = signal(false);

  constructor() {
    this.organizationsService.listMembers(this.organizationId).subscribe({
      next: (members) => this.members.set(members),
    });
    this.load();
  }

  protected onFromDateChange(value: string): void {
    this.fromDate.set(value);
    this.reload();
  }

  protected onToDateChange(value: string): void {
    this.toDate.set(value);
    this.reload();
  }

  protected onUserChange(event: Event): void {
    this.userId.set((event.target as HTMLSelectElement).value);
    this.reload();
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

  protected exportCurrentView(): void {
    this.runExport(false, this.page(), this.pageSize());
  }

  protected exportFullDataset(): void {
    this.runExport(true, 1, this.pageSize());
  }

  /** Colours the Description cell so a run of failures stands out from routine sign-ins. */
  protected descriptionClass(outcome: string): string {
    switch (outcome) {
      case 'LoginFailed':
        return 'bg-danger-subtle text-danger';
      case 'LogoutSucceeded':
        return 'bg-secondary-subtle text-secondary';
      default:
        return 'bg-success-subtle text-success';
    }
  }

  private reload(): void {
    this.page.set(1);
    this.load();
  }

  private runExport(full: boolean, page: number, pageSize: number): void {
    this.exporting.set(true);
    this.reports
      .exportUserLog(
        this.organizationId, this.fromDate(), this.toDate(), this.userId() || null, full, page, pageSize,
      )
      .subscribe({
        next: (blob) => {
          this.exporting.set(false);
          triggerBlobDownload(blob, `UserLog_${this.fromDate()}_${this.toDate()}.xlsx`);
        },
        error: (err: unknown) => {
          this.exporting.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not export the User Log.');
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.reports
      .getUserLog(
        this.organizationId, this.fromDate(), this.toDate(), this.userId() || null,
        this.page(), this.pageSize(),
      )
      .subscribe({
        next: (report) => {
          this.rows.set(report.items);
          this.totalCount.set(report.totalCount);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the User Log.');
        },
      });
  }
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

function firstOfMonth(): string {
  const now = new Date();
  return new Date(Date.UTC(now.getFullYear(), now.getMonth(), 1)).toISOString().slice(0, 10);
}
