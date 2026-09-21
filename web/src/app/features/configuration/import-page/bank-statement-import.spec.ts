import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { AccountingService } from '../../../core/accounting/accounting.service';
import { BankAccountDto } from '../../../core/accounting/accounting.models';
import { PagedResult } from '../../../core/common/paged-result';
import { ExportJobSummary } from '../../../core/exports/export.models';
import { ExportService } from '../../../core/exports/export.service';
import {
  ACCOUNT_SCOPED_ENTITY_TYPES,
  CREATE_ONLY_ENTITY_TYPES,
  ImportJobSummary,
} from '../../../core/imports/import.models';
import { ImportService } from '../../../core/imports/import.service';
import { ImportPage } from './import-page';

/**
 * Phase 55 -- the bank-account half of Configurations &gt; Import / Export.
 *
 * <p>A bank statement row does not name its own account, so the account is context for the whole
 * run. The server requires it for this upload type and refuses it for the other nine
 * (<c>BankStatementImportSweepGuardTests</c> asserts both directions); this file asserts the
 * client half of the same rule, including the direction that would otherwise rot -- that the
 * control does not appear, and no account is sent, for any other type.</p>
 */
describe('ImportPage (bank statement half)', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';
  const bankAccountId = '22222222-2222-2222-2222-222222222222';

  function page(queryParams: Record<string, string> = {}): {
    fixture: ComponentFixture<ImportPage>;
    element: () => HTMLElement;
    text: () => string;
    importService: ImportServiceStub;
    selectType: (value: string) => void;
    accountSelect: () => HTMLSelectElement | null;
  } {
    const importService = new ImportServiceStub();

    TestBed.configureTestingModule({
      imports: [ImportPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ExportService, useValue: new ExportServiceStub() },
        { provide: ImportService, useValue: importService },
        { provide: AccountingService, useValue: new AccountingServiceStub() },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: { get: () => organizationId },
              queryParamMap: { get: (key: string) => queryParams[key] ?? null },
            },
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(ImportPage);
    fixture.detectChanges();

    const element = () => fixture.nativeElement as HTMLElement;

    return {
      fixture,
      element,
      text: () => element().textContent ?? '',
      importService,
      accountSelect: () => element().querySelector<HTMLSelectElement>('#bankAccountId'),
      selectType: (value: string) => {
        const select = element().querySelector<HTMLSelectElement>('#entityType')!;
        select.value = value;
        select.dispatchEvent(new Event('change'));
        fixture.detectChanges();
      },
    };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('offers Bank Statement as an upload type', () => {
    const { element } = page();

    const options = [...element().querySelectorAll('#entityType option')].map((o) => o.textContent?.trim());
    expect(options).toContain('Bank Statement');
  });

  /** The direction that would otherwise rot: no other upload type may name an account. */
  it('shows no bank account picker for any other upload type', () => {
    const { accountSelect, selectType } = page();

    expect(accountSelect()).toBeNull();

    for (const type of ['Product', 'Customer', 'Account', 'ProductVariant']) {
      selectType(type);
      expect(accountSelect()).toBeNull();
    }
  });

  it('shows the bank account picker once Bank Statement is chosen', () => {
    const { accountSelect, selectType, text } = page();

    selectType('BankStatement');

    expect(accountSelect()).toBeTruthy();
    expect([...accountSelect()!.options].map((o) => o.textContent?.trim())).toContain(
      'Nabil Bank · 0101234567',
    );
    expect(text()).toContain('Nothing is posted to the');
  });

  /** The Statement screen's Import button lands here; the user should not re-pick what they were
   * already looking at. */
  it('preselects the type and the account from the query string', () => {
    const { accountSelect, element } = page({ entityType: 'BankStatement', bankAccountId });

    const typeSelect = element().querySelector<HTMLSelectElement>('#entityType')!;
    expect([...typeSelect.options].find((o) => o.selected)?.value).toBe('BankStatement');
    expect([...accountSelect()!.options].find((o) => o.selected)?.value).toBe(bankAccountId);
  });

  /**
   * Caught on the client as well as the server, because the server's 400 arrives only after the
   * whole file has been uploaded -- a slow way to be told about a select two inches away.
   */
  it('refuses to upload a statement with no account chosen, without sending the file', () => {
    const { element, fixture, selectType, importService, text } = page();

    selectType('BankStatement');

    const file = new File(['x'], 'statement.xlsx');
    const input = element().querySelector<HTMLInputElement>('input[type="file"]')!;
    Object.defineProperty(input, 'files', { value: [file] });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    element()
      .querySelectorAll<HTMLButtonElement>('button')
      .forEach((b) => {
        if (b.textContent?.includes('Start Import')) b.click();
      });
    fixture.detectChanges();

    expect(importService.uploads).toEqual([]);
    expect(text()).toContain('Choose the bank account this statement belongs to.');
  });

  it('sends the chosen account alongside the file', () => {
    const { element, fixture, selectType, importService, accountSelect } = page();

    selectType('BankStatement');

    const select = accountSelect()!;
    select.value = bankAccountId;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const file = new File(['x'], 'statement.xlsx');
    const input = element().querySelector<HTMLInputElement>('input[type="file"]')!;
    Object.defineProperty(input, 'files', { value: [file] });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    element()
      .querySelectorAll<HTMLButtonElement>('button')
      .forEach((b) => {
        if (b.textContent?.includes('Start Import')) b.click();
      });

    expect(importService.uploads).toEqual([{ entityType: 'BankStatement', bankAccountId }]);
  });

  /** A statement line has no business key to update by, so the action picker locks to Create. */
  it('treats Bank Statement as create-only and account-scoped', () => {
    expect(CREATE_ONLY_ENTITY_TYPES).toContain('BankStatement');
    expect(ACCOUNT_SCOPED_ENTITY_TYPES).toEqual(['BankStatement']);
  });
});

class ImportServiceStub {
  readonly uploads: { entityType: string; bankAccountId: string | null }[] = [];

  listImportJobs(): Observable<PagedResult<ImportJobSummary>> {
    return of({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  }

  createImportJob(
    _organizationId: string,
    entityType: string,
    _mode: string,
    _file: File,
    _reviewBeforeApply: boolean,
    bankAccountId: string | null,
  ): Observable<ImportJobSummary> {
    this.uploads.push({ entityType, bankAccountId });
    return of({} as ImportJobSummary);
  }
}

class ExportServiceStub {
  listExportJobs(): Observable<PagedResult<ExportJobSummary>> {
    return of({ items: [], page: 1, pageSize: 25, totalCount: 0 });
  }
}

class AccountingServiceStub {
  listBankAccounts(): Observable<PagedResult<BankAccountDto>> {
    const account: BankAccountDto = {
      id: '22222222-2222-2222-2222-222222222222',
      code: 'BC0001',
      name: 'Nabil Bank',
      kind: 'Bank',
      bankId: null,
      bankName: 'Nabil',
      accountNumber: '0101234567',
      isActive: true,
      balance: 0,
    };
    return of({ items: [account], page: 1, pageSize: 200, totalCount: 1 });
  }
}
