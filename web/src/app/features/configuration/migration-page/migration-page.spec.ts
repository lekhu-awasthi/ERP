import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { PagedResult } from '../../../core/common/paged-result';
import {
  ImportEntityType,
  ImportJobSummary,
  ImportMode,
  MIGRATION_ENTITY_TYPES,
} from '../../../core/imports/import.models';
import { ImportService } from '../../../core/imports/import.service';
import { MigrationPage } from './migration-page';

/**
 * Phase 21c -- Configurations > Organization > Migration.
 *
 * <p>Rendering tests, because the assertions that matter here are about what the screen is allowed
 * to say and offer. Two of them are the feature's whole safety story: the page must state outright
 * that migrated rows never reach the General Ledger, and it must ask the server for <b>only</b> the
 * two migrated upload types -- if it listed every ImportJob, a master-data import would appear in
 * the migration log and the separation Decision B paid for would be cosmetic.</p>
 */
describe('MigrationPage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  function migrationJob(overrides: Partial<ImportJobSummary> = {}): ImportJobSummary {
    return {
      id: 'job-1',
      entityType: 'MigratedSalesRegister',
      mode: 'CreateNew',
      fileName: 'sales-history-2081.xlsx',
      status: 'Completed',
      failureReason: null,
      totalRowCount: 120,
      processedRowCount: 120,
      succeededRowCount: 118,
      failedRowCount: 2,
      cancellationRequested: false,
      initiatedByUserId: 'user-1',
      initiatedByName: 'Ram Bahadur',
      createdAt: '2026-09-01T07:39:46Z',
      startedAt: '2026-09-01T07:39:50Z',
      completedAt: '2026-09-01T07:39:51Z',
      ...overrides,
    };
  }

  function page(jobs: ImportJobSummary[]) {
    const importService = new ImportServiceStub(jobs);

    TestBed.configureTestingModule({
      imports: [MigrationPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ImportService, useValue: importService },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => organizationId } } },
        },
      ],
    });

    const fixture = TestBed.createComponent(MigrationPage);
    fixture.detectChanges();

    const element = () => fixture.nativeElement as HTMLElement;
    return {
      fixture,
      text: () => element().textContent ?? '',
      uploadTypeOptions: () =>
        Array.from(element().querySelectorAll<HTMLOptionElement>('#migrationEntityType option')).map(
          (o) => o.value,
        ),
      importService,
    };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('states outright that migrated rows never reach the General Ledger', () => {
    const { text } = page([]);

    expect(text()).toContain('never posted to the General Ledger');
    expect(text()).toContain('Trial Balance');
  });

  it('offers only the two migrated registers as upload types, and no Create/Update selector', () => {
    const { uploadTypeOptions, fixture } = page([]);

    expect(uploadTypeOptions()).toEqual(['MigratedSalesRegister', 'MigratedPurchaseRegister']);
    expect(fixture.nativeElement.querySelector('#importMode')).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('only be created, never updated');
  });

  it('asks the server for migration jobs only, so a master-data import never appears here', () => {
    const { importService } = page([]);

    expect(importService.requestedEntityTypes).toEqual([[...MIGRATION_ENTITY_TYPES]]);
  });

  it('shows an empty history for an organization that has never migrated', () => {
    const { text } = page([]);
    expect(text()).toContain('No register migrations have been run');
  });

  it('reports a partly-rejected file as Completed and offers its per-row errors', () => {
    const { text, fixture } = page([migrationJob()]);

    expect(text()).toContain('sales-history-2081.xlsx');
    expect(text()).toContain('Completed');
    expect(text()).toContain('118 imported');
    expect(text()).toContain('2 failed');
    expect(
      fixture.nativeElement.querySelector('[data-testid="migration-show-errors"]'),
    ).toBeTruthy();
  });

  it('names the register rather than the raw enum value in the history', () => {
    const { text } = page([migrationJob({ entityType: 'MigratedPurchaseRegister' })]);

    expect(text()).toContain('Purchase Register');
    expect(text()).not.toContain('MigratedPurchaseRegister');
  });

  it('uploads in create mode without asking, since migrated rows cannot be updated', () => {
    const { fixture, importService } = page([]);
    const component = fixture.componentInstance as unknown as {
      selectedFileName: { set(value: string | null): void };
      upload(): void;
    };

    (fixture.componentInstance as unknown as { selectedFile: File | null }).selectedFile = new File(
      ['x'],
      'history.xlsx',
    );
    component.selectedFileName.set('history.xlsx');
    component.upload();

    expect(importService.createdModes).toEqual(['CreateNew']);
  });
});

class ImportServiceStub {
  readonly requestedEntityTypes: (readonly ImportEntityType[])[] = [];
  readonly createdModes: ImportMode[] = [];

  constructor(private readonly jobs: ImportJobSummary[]) {}

  listImportJobs(
    _organizationId: string,
    entityTypes: readonly ImportEntityType[] | null = null,
  ): Observable<PagedResult<ImportJobSummary>> {
    if (entityTypes) {
      this.requestedEntityTypes.push(entityTypes);
    }

    return of({ items: this.jobs, page: 1, pageSize: 25, totalCount: this.jobs.length });
  }

  createImportJob(
    _organizationId: string,
    _entityType: ImportEntityType,
    mode: ImportMode,
  ): Observable<ImportJobSummary> {
    this.createdModes.push(mode);
    return of(this.jobs[0]);
  }

  cancelImportJob(): Observable<void> {
    return of(undefined);
  }
}
