import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { PagedResult } from '../../../core/common/paged-result';
import { ExportJobSummary } from '../../../core/exports/export.models';
import { ExportService } from '../../../core/exports/export.service';
import { ImportJobSummary } from '../../../core/imports/import.models';
import { ImportService } from '../../../core/imports/import.service';
import { ImportPage } from './import-page';

/**
 * Phase 38 -- the import half of the Import / Export screen: the pre-commit review, and the two
 * upload types that can only create.
 *
 * <p>These are template assertions rather than service assertions, and deliberately so: the
 * server's behaviour is proved by the Application suite, while what only a component test can catch
 * is a screen that <b>offers the wrong thing</b> -- a Confirm Upload button on a job nobody is
 * waiting for, an Update Existing option the API will reject with a 400, or a review panel that
 * does not say plainly that nothing has been written yet.</p>
 */
describe('ImportPage (review half)', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  function importJob(overrides: Partial<ImportJobSummary> = {}): ImportJobSummary {
    return {
      id: 'import-1',
      entityType: 'ProductCategory',
      mode: 'CreateNew',
      fileName: 'categories.xlsx',
      status: 'PendingConfirmation',
      failureReason: null,
      totalRowCount: 10,
      processedRowCount: 10,
      succeededRowCount: 0,
      failedRowCount: 2,
      cancellationRequested: false,
      initiatedByUserId: 'user-1',
      initiatedByName: 'Ram Bahadur',
      createdAt: '2026-09-12T07:39:46Z',
      startedAt: '2026-09-12T07:39:50Z',
      completedAt: null,
      reviewBeforeApply: true,
      reviewConfirmedAt: null,
      validatedRowCount: 8,
      ...overrides,
    };
  }

  function page(jobs: ImportJobSummary[]): {
    fixture: ComponentFixture<ImportPage>;
    text: () => string;
    reviewPanel: () => HTMLElement | null;
    confirmButton: () => HTMLButtonElement | null;
    modeSelect: () => HTMLSelectElement;
    importService: ImportServiceStub;
  } {
    const importService = new ImportServiceStub(jobs);

    TestBed.configureTestingModule({
      imports: [ImportPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ImportService, useValue: importService },
        { provide: ExportService, useValue: new ExportServiceStub() },
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
      reviewPanel: () => element().querySelector<HTMLElement>('[data-testid="import-review"]'),
      confirmButton: () => element().querySelector<HTMLButtonElement>('[data-testid="import-confirm"]'),
      modeSelect: () => element().querySelector<HTMLSelectElement>('#importMode')!,
      importService,
    };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('shows the review panel with both counts and says nothing has been imported yet', () => {
    const { reviewPanel, confirmButton } = page([importJob()]);

    const panel = reviewPanel();
    expect(panel).not.toBeNull();

    const panelText = panel?.textContent ?? '';
    expect(panelText).toContain('Nothing has been imported yet');
    expect(panelText).toContain('8 of 10 records validated');
    expect(panelText).toContain('2 records have errors');

    expect(confirmButton()).not.toBeNull();
  });

  it('offers no review panel or Confirm button once the job has been applied', () => {
    const { reviewPanel, confirmButton } = page([
      importJob({
        status: 'Completed',
        succeededRowCount: 8,
        completedAt: '2026-09-12T07:40:10Z',
        reviewConfirmedAt: '2026-09-12T07:40:00Z',
      }),
    ]);

    expect(reviewPanel()).toBeNull();
    expect(confirmButton()).toBeNull();
  });

  it('confirms through the service rather than re-uploading the file', () => {
    const { confirmButton, importService } = page([importJob()]);

    confirmButton()!.click();

    expect(importService.confirmed).toEqual([{ organizationId, id: 'import-1' }]);
  });

  /**
   * The mode picker is locked for the types the server refuses to update, so a user cannot reach a
   * 400 that names no field. Asserted on the default upload type, which is Product -- a type that
   * DOES offer both modes -- and then on a create-only one, because a lock that is always on would
   * pass a one-sided test.
   */
  it('locks the action picker only for the upload types that cannot update', () => {
    const { fixture, modeSelect } = page([]);

    expect(modeSelect().disabled).toBe(false);

    const component = fixture.componentInstance as unknown as {
      entityType: { set: (value: string) => void };
    };
    component.entityType.set('AccountGroup');
    fixture.detectChanges();

    expect(modeSelect().disabled).toBe(true);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('can only be created by import');
  });
});

class ImportServiceStub {
  readonly confirmed: { organizationId: string; id: string }[] = [];

  constructor(private readonly jobs: ImportJobSummary[]) {}

  listImportJobs(): Observable<PagedResult<ImportJobSummary>> {
    return of({ items: this.jobs, page: 1, pageSize: 25, totalCount: this.jobs.length });
  }

  confirmImportJob(organizationId: string, id: string): Observable<ImportJobSummary> {
    this.confirmed.push({ organizationId, id });
    return of(this.jobs[0]);
  }
}

class ExportServiceStub {
  listExportJobs(): Observable<PagedResult<ExportJobSummary>> {
    return of({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  }
}
