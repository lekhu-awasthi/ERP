import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { SmsCreditLedgerDto, SmsLogListDto } from '../../../core/crm/crm.models';
import { CrmService } from '../../../core/crm/crm.service';
import { SmsShellPage } from './sms-shell-page';

/**
 * Phase 45 (39 carried item #4) -- the SMS History search box.
 *
 * <p>Phase 39 exempted <c>ListSmsLogsQuery</c> from the search sweep and named its re-entry
 * condition; this phase met it. What a component test can prove that a handler test cannot is that
 * the term actually leaves the screen -- phase-23's bug #1 was a DTO carrying fields end to end that
 * no template ever rendered, and the mirror of it is a box whose value never reaches the wire.</p>
 */
describe('SmsShellPage — history search', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  const emptyHistory: SmsLogListDto = { rows: [], page: 1, pageSize: 20, totalCount: 0 };
  const emptyLedger: SmsCreditLedgerDto = { balance: 0, rows: [], page: 1, pageSize: 20, totalCount: 0 };

  let fixture: ComponentFixture<SmsShellPage>;
  let historyCalls: { page: number; search: string | null }[];

  async function render(): Promise<void> {
    historyCalls = [];

    const crm: Partial<CrmService> = {
      listSmsCreditLedger: (): Observable<SmsCreditLedgerDto> => of(emptyLedger),
      listSmsHistory: (
        _org: string,
        page = 1,
        _pageSize = 20,
        search: string | null = null,
      ): Observable<SmsLogListDto> => {
        historyCalls.push({ page, search });
        return of(emptyHistory);
      },
    };

    await TestBed.configureTestingModule({
      imports: [SmsShellPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: CrmService, useValue: crm },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: new Map([['id', organizationId]]) } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SmsShellPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  async function openHistoryTab(): Promise<void> {
    const tab = Array.from(el().querySelectorAll<HTMLButtonElement>('button')).find(
      (b) => b.textContent?.trim() === 'SMS History',
    );
    tab!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function searchBox(): HTMLInputElement {
    return el().querySelector<HTMLInputElement>('input[aria-label="Search SMS history"]')!;
  }

  it('carries the typed term to the service', async () => {
    await render();
    await openHistoryTab();

    const box = searchBox();
    box.value = 'Dashain';
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(historyCalls.at(-1)).toEqual({ page: 1, search: 'Dashain' });
  });

  it('sends no term for a whitespace-only box, which is not a search', async () => {
    await render();
    await openHistoryTab();

    const box = searchBox();
    box.value = '   ';
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(historyCalls.at(-1)?.search).toBeNull();
  });

  it('resets to page one, so a narrowing search does not read as no data', async () => {
    await render();
    await openHistoryTab();

    fixture.componentInstance['historyPage'].set(3);

    const box = searchBox();
    box.value = 'Promo';
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(historyCalls.at(-1)?.page).toBe(1);
  });

  it('tells an empty search apart from an empty history', async () => {
    await render();
    await openHistoryTab();

    expect(el().textContent).toContain('No SMS sent yet.');

    const box = searchBox();
    box.value = 'nothing matches this';
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(el().textContent).toContain('No SMS matches that search.');
  });
});
