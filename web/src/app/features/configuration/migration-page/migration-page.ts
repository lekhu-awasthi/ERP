import { Component, OnDestroy, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import {
  ImportEntityType,
  ImportJobRow,
  ImportJobStatus,
  ImportJobSummary,
  MIGRATION_ENTITY_TYPES,
} from '../../../core/imports/import.models';
import { ImportService } from '../../../core/imports/import.service';
import { triggerBlobDownload } from '../../../shared/download-file';

/**
 * Phase 21c / FR-2.10 -- Configurations > Organization > Migration.
 *
 * <p><b>Its own screen, not a fourth entry on the Import / Export page's Upload Type dropdown</b>
 * (Decision B). The reference product files migrated tax-register import under Organization >
 * Migration -- a "Migrated Reports" panel listing Sales Register and Purchase Register with an
 * IMPORT button -- entirely separately from Import / Export, and this is one place its separation
 * is worth copying rather than simplifying away: master-data import edits records the user can see
 * and fix, while this one seeds numbers that go straight into a statutory return and can never be
 * reconciled against a document. Behind the screen they are the same ImportJob and the same runner
 * (Decision C); the `entityTypes` filter is what keeps each screen's history to its own uploads.</p>
 *
 * <p>There is no Create/Update selector, because migrated rows are create-only (see ImportMode) --
 * offering a control with one valid setting would only invite the question.</p>
 *
 * <p>Polling, not a socket, and only while something is running -- copied from ImportPage for the
 * same reasons.</p>
 */
@Component({
  selector: 'app-migration-page',
  imports: [RouterLink],
  templateUrl: './migration-page.html',
})
export class MigrationPage implements OnDestroy {
  private static readonly PollIntervalMs = 2000;

  private readonly route = inject(ActivatedRoute);
  private readonly importService = inject(ImportService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly uploading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly jobs = signal<ImportJobSummary[]>([]);

  // A plain signal written by the (change) handler rather than a FormControl read inside a
  // computed(): this app is zoneless, and such a computed caches its first value forever
  // (phase-17's bug). A stale read here would import a sales file as a purchase register.
  protected readonly entityType = signal<ImportEntityType>('MigratedSalesRegister');
  protected readonly selectedFileName = signal<string | null>(null);

  protected readonly expandedJobId = signal<string | null>(null);
  protected readonly expandedRows = signal<ImportJobRow[]>([]);
  protected readonly expandedRowsLoading = signal(false);

  protected readonly entityTypes: readonly { value: ImportEntityType; label: string; report: string }[] = [
    { value: 'MigratedSalesRegister', label: 'Sales Register', report: 'migrated-sales-register' },
    { value: 'MigratedPurchaseRegister', label: 'Purchase Register', report: 'migrated-purchase-register' },
  ];

  private selectedFile: File | null = null;
  private pollHandle: ReturnType<typeof setInterval> | null = null;

  constructor() {
    this.load();
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  protected onEntityTypeChange(value: string): void {
    this.entityType.set(value as ImportEntityType);
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files?.[0] ?? null;
    this.selectedFileName.set(this.selectedFile?.name ?? null);
  }

  protected downloadTemplate(): void {
    const entityType = this.entityType();
    this.errorMessage.set(null);
    this.importService.downloadTemplate(this.organizationId, entityType).subscribe({
      next: (blob) => triggerBlobDownload(blob, `${entityType}Template.xlsx`),
      error: (err: unknown) =>
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not download the template.'),
    });
  }

  protected upload(): void {
    if (!this.selectedFile) {
      this.errorMessage.set('Choose a .xlsx file to import.');
      return;
    }

    this.uploading.set(true);
    this.errorMessage.set(null);

    this.importService
      .createImportJob(this.organizationId, this.entityType(), 'CreateNew', this.selectedFile)
      .subscribe({
        next: () => {
          this.uploading.set(false);
          this.selectedFile = null;
          this.selectedFileName.set(null);
          this.load();
        },
        error: (err: unknown) => {
          this.uploading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not start the migration import.');
        },
      });
  }

  protected cancel(job: ImportJobSummary): void {
    this.importService.cancelImportJob(this.organizationId, job.id).subscribe({
      next: () => this.load(),
      error: (err: unknown) =>
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not cancel the import.'),
    });
  }

  protected toggleRows(job: ImportJobSummary): void {
    if (this.expandedJobId() === job.id) {
      this.expandedJobId.set(null);
      this.expandedRows.set([]);
      return;
    }

    this.expandedJobId.set(job.id);
    this.expandedRowsLoading.set(true);
    this.importService.getImportJob(this.organizationId, job.id).subscribe({
      next: (detail) => {
        this.expandedRows.set(detail.rows.items);
        this.expandedRowsLoading.set(false);
      },
      error: (err: unknown) => {
        this.expandedRowsLoading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load the row results.');
      },
    });
  }

  protected isActive(status: ImportJobStatus): boolean {
    return status === 'Queued' || status === 'Running';
  }

  protected statusClass(status: ImportJobStatus): string {
    switch (status) {
      case 'Completed':
        return 'text-bg-success';
      case 'Failed':
        return 'text-bg-danger';
      case 'Cancelled':
        return 'text-bg-secondary';
      default:
        return 'text-bg-info';
    }
  }

  protected progressPercent(job: ImportJobSummary): number {
    if (job.totalRowCount <= 0) {
      return 0;
    }

    return Math.round((job.processedRowCount / job.totalRowCount) * 100);
  }

  protected entityTypeLabel(entityType: ImportEntityType): string {
    return this.entityTypes.find((option) => option.value === entityType)?.label ?? entityType;
  }

  private load(): void {
    this.importService.listImportJobs(this.organizationId, MIGRATION_ENTITY_TYPES).subscribe({
      next: (result) => {
        this.jobs.set(result.items);
        this.loading.set(false);
        this.syncPolling();
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load migration history.');
      },
    });
  }

  private syncPolling(): void {
    const anyActive = this.jobs().some((job) => this.isActive(job.status));

    if (anyActive && this.pollHandle === null) {
      this.pollHandle = setInterval(() => this.load(), MigrationPage.PollIntervalMs);
    } else if (!anyActive) {
      this.stopPolling();
    }
  }

  private stopPolling(): void {
    if (this.pollHandle !== null) {
      clearInterval(this.pollHandle);
      this.pollHandle = null;
    }
  }
}
