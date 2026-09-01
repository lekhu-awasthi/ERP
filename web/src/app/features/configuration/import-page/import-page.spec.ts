import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { PagedResult } from '../../../core/common/paged-result';
import { ExportJobSummary } from '../../../core/exports/export.models';
import { ExportService } from '../../../core/exports/export.service';
import { ImportJobSummary } from '../../../core/imports/import.models';
import { ImportService } from '../../../core/imports/import.service';
import { ImportPage } from './import-page';

/**
 * Phase 21b's Export half of Configurations > Import / Export.
 *
 * <p>This is a rendering test rather than a browser pass because the assertions that matter are
 * about <b>what the template is allowed to offer</b>. In particular the Download button keys off
 * <code>hasArtifact</code> and not <code>status</code>: a Completed export whose file retention has
 * since deleted must not be offered as a download. Getting that wrong would be a security-adjacent
 * UI bug that a screenshot would not catch.</p>
 */
describe('ImportPage (export half)', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  function exportJob(overrides: Partial<ExportJobSummary> = {}): ExportJobSummary {
    return {
      id: 'job-1',
      status: 'Completed',
      failureReason: null,
      fileName: 'DataExport_Acme_2026-09-01_1324.xlsx',
      fileSizeBytes: 13391,
      totalCategoryCount: 5,
      processedCategoryCount: 5,
      totalRowCount: 18,
      truncationNotice: null,
      cancellationRequested: false,
      hasArtifact: true,
      initiatedByUserId: 'user-1',
      initiatedByName: 'Ram Bahadur',
      createdAt: '2026-09-01T07:39:46Z',
      startedAt: '2026-09-01T07:39:50Z',
      completedAt: '2026-09-01T07:39:51Z',
      expiresAt: '2026-09-08T07:39:51Z',
      artifactPurgedAt: null,
      ...overrides,
    };
  }

  function page(jobs: ExportJobSummary[]): {
    fixture: ComponentFixture<ImportPage>;
    text: () => string;
    /** Text of the export-history card only. The import half of this page has its own
     * "Download ... Template" button, so a whole-page search for "Download" proves nothing. */
    historyText: () => string;
    downloadButton: () => HTMLButtonElement | null;
    exportService: ExportServiceStub;
  } {
    const exportService = new ExportServiceStub(jobs);

    TestBed.configureTestingModule({
      imports: [ImportPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ExportService, useValue: exportService },
        { provide: ImportService, useValue: new ImportServiceStub() },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => organizationId } } },
        },
      ],
    });

    const fixture = TestBed.createComponent(ImportPage);
    fixture.detectChanges();

    const element = () => fixture.nativeElement as HTMLElement;
    return {
      fixture,
      text: () => element().textContent ?? '',
      historyText: () =>
        element().querySelector('[data-testid="export-history"]')?.textContent ?? '',
      downloadButton: () =>
        element().querySelector<HTMLButtonElement>('[data-testid="export-download"]'),
      exportService,
    };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('offers an Export action and says plainly that the file is not a restorable backup', () => {
    const { text } = page([]);

    expect(text()).toContain('Start Export');
    expect(text()).toContain('not a restorable backup');
    expect(text()).toContain('products, contacts, chart of');
    // Never the word "backup" as a label for the action itself -- see Decision A.
    expect(text()).not.toContain('Start Backup');
  });

  it('shows an empty export history for an organization that has never exported', () => {
    const { historyText } = page([]);
    expect(historyText()).toContain('No exports have been generated');
  });

  it('offers Download for a completed export whose file still exists', () => {
    const { historyText, downloadButton } = page([exportJob()]);

    expect(historyText()).toContain('DataExport_Acme_2026-09-01_1324.xlsx');
    expect(historyText()).toContain('13 KB');
    expect(downloadButton()).toBeTruthy();
  });

  it('does NOT offer Download once retention has deleted the file', () => {
    const { historyText, downloadButton } = page([
      exportJob({ hasArtifact: false, artifactPurgedAt: '2026-09-08T08:00:00Z' }),
    ]);

    // Still listed, still Completed, still named -- but no dead download link.
    expect(historyText()).toContain('DataExport_Acme_2026-09-01_1324.xlsx');
    expect(historyText()).toContain('expired');
    expect(downloadButton()).toBeNull();
  });

  it('discloses truncation without calling the export a failure', () => {
    const { historyText, downloadButton } = page([
      exportJob({ truncationNotice: 'Ledger Transactions (25,000 of 41,233 rows)' }),
    ]);

    expect(historyText()).toContain('Ledger Transactions (25,000 of 41,233 rows)');
    expect(historyText()).toContain('still complete and downloadable');
    expect(downloadButton()).toBeTruthy();
  });

  it('offers Cancel while an export is running, and no Download yet', () => {
    const { historyText, downloadButton } = page([
      exportJob({
        status: 'Running',
        hasArtifact: false,
        fileName: null,
        fileSizeBytes: null,
        totalRowCount: 0,
        processedCategoryCount: 2,
      }),
    ]);

    expect(historyText()).toContain('Cancel');
    expect(historyText()).toContain('2/5 sheets');
    expect(downloadButton()).toBeNull();
  });

  it('downloads through the service rather than a raw link, so the request carries auth', () => {
    const { downloadButton, exportService } = page([exportJob()]);

    downloadButton()?.click();

    expect(exportService.downloadedIds).toEqual(['job-1']);
  });
});

class ExportServiceStub {
  readonly downloadedIds: string[] = [];

  constructor(private readonly jobs: ExportJobSummary[]) {}

  listExportJobs(): Observable<PagedResult<ExportJobSummary>> {
    return of({ items: this.jobs, page: 1, pageSize: 25, totalCount: this.jobs.length });
  }

  createExportJob(): Observable<ExportJobSummary> {
    return of(this.jobs[0]);
  }

  cancelExportJob(): Observable<void> {
    return of(undefined);
  }

  downloadExport(_organizationId: string, id: string): Observable<Blob> {
    this.downloadedIds.push(id);
    return of(new Blob(['x']));
  }
}

class ImportServiceStub {
  listImportJobs(): Observable<PagedResult<ImportJobSummary>> {
    return of({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  }
}
