import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { OpenPosSessionRequest, PosSession, PosTill, PosTillSummary } from '../../../core/pos/pos.models';
import { PosService } from '../../../core/pos/pos.service';
import { PosLauncherPage } from './pos-launcher-page';

/**
 * Phase 62 -- the till's launcher: the tills the cashier may open, their own open session at each,
 * and a Start Session form that counts the float the way the location asks.
 */
describe('PosLauncherPage', () => {
  const organizationId = 'org-1';

  const summary = (overrides: Partial<PosTillSummary>): PosTillSummary => ({
    locationId: 'loc-1', locationCode: 'HO', locationName: 'Thamel', posMode: 'Retail',
    mySessionId: null, mySessionCode: null, mySessionOpenedAt: null, ...overrides,
  });

  const till = (cashVerificationRequired: boolean): PosTill => ({
    locationId: 'loc-1', locationCode: 'HO', locationName: 'Thamel', posMode: 'Retail', availableTabs: ['Retail'],
    defaultTab: 'Retail', serviceChargeEnabled: false, serviceChargeRate: 0, roundOffEnabled: true,
    cashVerificationRequired, denominations: [1000, 500, 100], printInvoice: true, abbreviatedTaxInvoiceEnabled: false,
    isVatRegistered: true, warehouseId: null, warehouseName: null, walkInCustomer: null, paymentModes: [],
    categories: [], canSellOnCredit: false,
  });

  function page(tills: PosTillSummary[], verification = false) {
    const opened: OpenPosSessionRequest[] = [];
    const service = {
      listTills: (): Observable<PosTillSummary[]> => of(tills),
      getTill: (): Observable<PosTill> => of(till(verification)),
      openSession: (_o: string, request: OpenPosSessionRequest): Observable<PosSession> => {
        opened.push(request);
        return of({ id: 'ses-1' } as PosSession);
      },
    };

    TestBed.configureTestingModule({
      imports: [PosLauncherPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PosService, useValue: service },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: organizationId }) } } },
      ],
    });

    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(PosLauncherPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    return {
      element,
      opened,
      navigate,
      text: () => element.textContent?.replace(/\s+/g, ' ') ?? '',
      press: (label: string) => {
        [...element.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim() === label)!.click();
        fixture.detectChanges();
      },
      type: (id: string, value: string) => {
        const input = element.querySelector<HTMLInputElement>(`#${id}`)!;
        input.value = value;
        input.dispatchEvent(new Event('input'));
        fixture.detectChanges();
      },
      submit: () => {
        element.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
        fixture.detectChanges();
      },
    };
  }

  it('offers the till, its open session, and says why a restaurant till is not offered', () => {
    const p = page([
      summary({ mySessionId: 'ses-1', mySessionCode: 'SES0001', mySessionOpenedAt: '2026-10-01T03:00:00Z' }),
      summary({ locationId: 'loc-2', locationCode: '1002', locationName: 'Ground Floor', posMode: 'Restaurant' }),
    ]);

    expect(p.text()).toContain('Your session SES0001 has been open since');
    expect(p.element.querySelector('a[href="/organizations/org-1/pos/till/loc-1"]')).not.toBeNull();
    expect(p.text()).toContain('The restaurant till (tables, kitchen orders and bill splitting) is not built yet.');
    expect(p.element.querySelectorAll('button').length).toBe(0);
  });

  it('starts a session with an amount where the till does not count notes', () => {
    const p = page([summary({})]);

    p.press('Start Session');
    p.type('pos-open-amount', '1000');
    p.submit();

    expect(p.opened).toEqual([{ locationId: 'loc-1', openingAmount: 1000, denominations: null }]);
    expect(p.navigate).toHaveBeenCalledWith(['/organizations', organizationId, 'pos', 'till', 'loc-1']);
  });

  it('counts the float note by note where the till requires cash verification', () => {
    const p = page([summary({})], true);

    p.press('Start Session');
    expect(p.element.querySelector('#pos-open-amount')).toBeNull();
    p.type('pos-open-loc-1-500', '2');
    expect(p.text()).toContain('Counted: 1,000.00');
    p.submit();

    expect(p.opened).toEqual([{ locationId: 'loc-1', openingAmount: null, denominations: [{ value: 500, count: 2 }] }]);
  });

  it('explains an empty list rather than showing nothing', () => {
    const p = page([]);
    expect(p.text()).toContain('No location runs a till you can open.');
  });

  it('shows a refused load as the refusal alone, naming the key, with no empty-list reason under it', () => {
    TestBed.configureTestingModule({
      imports: [PosLauncherPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: PosService,
          useValue: {
            listTills: () => throwError(() => new HttpErrorResponse({
              status: 403,
              error: { title: 'You do not have permission to perform this action (Pos.Session.Operate).' },
            })),
          },
        },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: organizationId }) } } },
      ],
    });

    const fixture = TestBed.createComponent(PosLauncherPage);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('(Pos.Session.Operate)');
    expect(text).not.toContain('No location runs a till');
  });
});
