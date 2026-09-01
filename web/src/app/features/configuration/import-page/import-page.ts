import { Component, OnDestroy, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { extractErrorMessage } from '../../../core/auth/api-error';
import { ExportJobStatus, ExportJobSummary } from '../../../core/exports/export.models';
import { ExportService } from '../../../core/exports/export.service';
import {
  ImportEntityType,
  ImportJobRow,
  ImportJobStatus,
  ImportJobSummary,
  ImportMode,
} from '../../../core/imports/import.models';
import { ImportService } from '../../../core/imports/import.service';
import { triggerBlobDownload } from '../../../shared/download-file';

/**
 * Roadmap Phase 21a / FR-2.9 -- Configurations > Import / Export.
 *
 * <p>The reference product's wizard (confirmed live) is four steps and <b>synchronous</b>: pick an
 * upload type and action, upload, review a server-side dry run ("N records validated / N records
 * have errors"), then press Confirm Upload -- with a 20-minute client timeout and a "do not refresh
 * this page" warning. NFR-4.3 requires the opposite, so this screen keeps the same two-choice entry
 * (Upload Type + Create New / Update Existing, matching the product's own labels) and replaces the
 * blocking review with a job history that shows live progress and the same per-row errors after the
 * fact. See CreateImportJobCommand for the full comparison and what a pre-commit review step would
 * additively cost.</p>
 *
 * <p>Polling, not a socket: a job's status is a cheap indexed read, and adding a push channel for
 * one screen would be a deployment concern in exchange for a few seconds of latency.</p>
 *
 * <p><b>Phase 21b added the Export half to this same screen</b> rather than giving it its own. The
 * reference product files both directions under one "Import / Export" nav entry, and it has no
 * backup screen at all (confirmed live during Phase 21a), so a separate page would have invented a
 * navigation item the product does not have. The word on the button is <b>Export</b>, not Backup:
 * there is no restore path in this product, and see FR-2.8 / Decision A in
 * docs/phase-21b-status.md for why saying so plainly was the whole first decision of the phase.</p>
 */
@Component({
  selector: 'app-import-page',
  imports: [RouterLink],
  templateUrl: './import-page.html',
})
export class ImportPage implements OnDestroy {
  private static readonly PollIntervalMs = 2000;

  private readonly route = inject(ActivatedRoute);
  private readonly importService = inject(ImportService);
  private readonly exportService = inject(ExportService);

  protected readonly organizationId = this.route.snapshot.paramMap.get('id')!;

  protected readonly loading = signal(true);
  protected readonly uploading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly jobs = signal<ImportJobSummary[]>([]);

  // Plain signals written by the (change) handlers rather than a FormGroup read inside computed():
  // this app is zoneless, and a computed() over a FormControl caches its first value forever
  // (phase-17's bug). These drive both the template download and the upload, so a stale read here
  // would import the wrong entity type.
  protected readonly entityType = signal<ImportEntityType>('Product');
  protected readonly mode = signal<ImportMode>('CreateNew');
  protected readonly selectedFileName = signal<string | null>(null);

  protected readonly exportJobs = signal<ExportJobSummary[]>([]);
  protected readonly exportStarting = signal(false);
  protected readonly downloadingExportId = signal<string | null>(null);

  protected readonly expandedJobId = signal<string | null>(null);
  protected readonly expandedRows = signal<ImportJobRow[]>([]);
  protected readonly expandedRowsLoading = signal(false);

  protected readonly entityTypes: readonly { value: ImportEntityType; label: string }[] = [
    { value: 'Product', label: 'Product' },
    { value: 'Customer', label: 'Customer' },
    { value: 'Supplier', label: 'Supplier' },
  ];

  protected readonly modes: readonly { value: ImportMode; label: string }[] = [
    { value: 'CreateNew', label: 'Create New Records' },
    { value: 'UpdateExisting', label: 'Update Existing Records' },
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

  protected onModeChange(value: string): void {
    this.mode.set(value as ImportMode);
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
      next: (blob) => triggerBlobDownload(blob, `${entityType}ImportTemplate.xlsx`),
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
      .createImportJob(this.organizationId, this.entityType(), this.mode(), this.selectedFile)
      .subscribe({
        next: () => {
          this.uploading.set(false);
          this.selectedFile = null;
          this.selectedFileName.set(null);
          this.load();
        },
        error: (err: unknown) => {
          this.uploading.set(false);
          this.errorMessage.set(extractErrorMessage(err) ?? 'Could not start the import.');
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

  /** Both job kinds use the same five status names, so the two badges share these helpers. */
  protected isActive(status: ImportJobStatus | ExportJobStatus): boolean {
    return status === 'Queued' || status === 'Running';
  }

  protected statusClass(status: ImportJobStatus | ExportJobStatus): string {
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

  /** Percentage of rows processed. Zero until the runner has read the file and knows the total, so
   * a Queued job shows an empty bar rather than a misleading full one. */
  protected progressPercent(job: ImportJobSummary): number {
    if (job.totalRowCount <= 0) {
      return 0;
    }

    return Math.round((job.processedRowCount / job.totalRowCount) * 100);
  }

  protected startExport(): void {
    this.exportStarting.set(true);
    this.errorMessage.set(null);

    this.exportService.createExportJob(this.organizationId).subscribe({
      next: () => {
        this.exportStarting.set(false);
        this.load();
      },
      error: (err: unknown) => {
        this.exportStarting.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not start the export.');
      },
    });
  }

  protected cancelExport(job: ExportJobSummary): void {
    this.exportService.cancelExportJob(this.organizationId, job.id).subscribe({
      next: () => this.load(),
      error: (err: unknown) =>
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not cancel the export.'),
    });
  }

  /** Fetched as a Blob so the request carries the auth cookie and a 403 or an expired-file 404
   * surfaces as a message rather than a broken download -- see ExportService.downloadExport. */
  protected downloadExport(job: ExportJobSummary): void {
    this.downloadingExportId.set(job.id);
    this.errorMessage.set(null);

    this.exportService.downloadExport(this.organizationId, job.id).subscribe({
      next: (blob) => {
        this.downloadingExportId.set(null);
        triggerBlobDownload(blob, job.fileName ?? 'DataExport.xlsx');
      },
      error: (err: unknown) => {
        this.downloadingExportId.set(null);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not download the export.');
        // The file may have just expired, so re-read the history rather than leaving a stale
        // Download button on screen.
        this.load();
      },
    });
  }

  protected exportProgressPercent(job: ExportJobSummary): number {
    if (job.totalCategoryCount <= 0) {
      return 0;
    }

    return Math.round((job.processedCategoryCount / job.totalCategoryCount) * 100);
  }

  protected fileSizeLabel(bytes: number | null): string {
    if (bytes === null) {
      return '';
    }

    return bytes < 1024 * 1024
      ? `${Math.max(1, Math.round(bytes / 1024))} KB`
      : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  protected modeLabel(mode: ImportMode): string {
    return this.modes.find((option) => option.value === mode)?.label ?? mode;
  }

  private load(): void {
    this.importService.listImportJobs(this.organizationId).subscribe({
      next: (result) => {
        this.jobs.set(result.items);
        this.loading.set(false);
        this.syncPolling();
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load import history.');
      },
    });

    // Two independent calls rather than one combined endpoint: the two features have separate
    // permission keys, so a user granted one and not the other still gets the half they may see
    // instead of a whole page that 403s.
    this.exportService.listExportJobs(this.organizationId).subscribe({
      next: (result) => {
        this.exportJobs.set(result.items);
        this.syncPolling();
      },
      error: (err: unknown) =>
        this.errorMessage.set(extractErrorMessage(err) ?? 'Could not load export history.'),
    });
  }

  /** Polls only while something is actually running, and stops the moment nothing is -- an idle
   * Configurations tab must not sit hitting the API forever. */
  private syncPolling(): void {
    const anyActive =
      this.jobs().some((job) => this.isActive(job.status)) ||
      this.exportJobs().some((job) => this.isActive(job.status));

    if (anyActive && this.pollHandle === null) {
      this.pollHandle = setInterval(() => this.load(), ImportPage.PollIntervalMs);
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
