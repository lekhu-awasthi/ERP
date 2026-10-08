import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { Account } from '../../../core/accounting/accounting.models';
import { AccountingService } from '../../../core/accounting/accounting.service';
import { PaymentMode } from '../../../core/configuration/configuration.models';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { PosMode } from '../../../core/organizations/organizations.models';
import { PosConfiguration, PosLocationSettings, UpdatePosLocationSettingsRequest } from '../../../core/pos/pos.models';
import { PosService } from '../../../core/pos/pos.service';
import { PosSettingsPage } from './pos-settings-page';

/**
 * Phase 60 -- Configurations > Point of Sale. The assertions are the round trip (every field the
 * screen shows is the field it sends), the entitlement filter on the type picker, and the rule that
 * a payment mode without an account cannot be ticked.
 */
describe('PosSettingsPage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';
  const locationId = '22222222-2222-2222-2222-222222222222';

  const defaults = (posMode: PosMode): PosLocationSettings => ({
    locationId,
    locationCode: 'HO',
    locationName: 'HeadOffice',
    locationIsActive: true,
    posMode,
    isSaved: false,
    serviceChargeEnabled: false,
    serviceChargeRate: 0,
    serviceChargeAccountId: null,
    serviceChargeOnTakeAway: true,
    roundOffEnabled: false,
    roundOffAccountId: null,
    cashVerificationRequired: false,
    denominations: [1000, 500, 100, 50, 20, 10, 5, 2, 1],
    defaultTab: null,
    effectiveDefaultTab: posMode === 'Restaurant' ? 'DineIn' : posMode === 'Retail' ? 'Retail' : null,
    availableTabs: posMode === 'Restaurant' ? ['DineIn', 'TakeAway', 'Delivery'] : posMode === 'Retail' ? ['Retail', 'Delivery'] : [],
    printEstimateBill: true,
    printInvoice: true,
    printCreditNote: true,
    printKot: true,
    abbreviatedTaxInvoiceEnabled: false,
    paymentModeIds: [],
  });

  class PosServiceStub {
    saved: UpdatePosLocationSettingsRequest | null = null;
    linked: string[] | null = null;

    constructor(
      private readonly config: PosConfiguration,
      private current: PosLocationSettings,
    ) {}

    getConfiguration(): Observable<PosConfiguration> {
      return of(this.config);
    }

    getLocationSettings(): Observable<PosLocationSettings> {
      return of(this.current);
    }

    updateLocationSettings(_o: string, _l: string, request: UpdatePosLocationSettingsRequest): Observable<PosLocationSettings> {
      this.saved = request;
      this.current = { ...this.current, ...request, isSaved: true };
      return of(this.current);
    }

    setLocationPaymentModes(_o: string, _l: string, ids: string[]): Observable<PosLocationSettings> {
      this.linked = ids;
      this.current = { ...this.current, paymentModeIds: ids };
      return of(this.current);
    }

    setLocationMode(_o: string, _l: string, posMode: PosMode): Observable<PosLocationSettings> {
      this.current = defaults(posMode);
      return of(this.current);
    }
  }

  const account = (id: string, code: string): Account => ({
    id, organizationId, code, name: `Account ${code}`, rootType: 'Income', groupId: 'g', kind: 'Other',
    bankId: null, accountNumber: null, isActive: true, createdAt: '2026-10-01T00:00:00Z',
  });

  const mode = (id: string, name: string, accountId: string | null): PaymentMode => ({
    id, organizationId, name, isActive: true, requiresChequeDetails: false, kind: 'Cash', accountId,
    createdAt: '2026-10-01T00:00:00Z',
  });

  function page(posMode: PosMode = 'Restaurant', config: Partial<PosConfiguration> = {}) {
    const service = new PosServiceStub(
      {
        posRetailEnabled: false,
        posRestaurantEnabled: true,
        walkInCustomer: { id: 'w', code: 'WALKIN', name: 'Cash Customer' },
        locations: [{
          id: locationId, code: 'HO', name: 'HeadOffice', isActive: true, isHeadOffice: true, posMode,
          hasSettings: false, linkedPaymentModeCount: 0,
        }],
        ...config,
      },
      defaults(posMode),
    );

    TestBed.configureTestingModule({
      imports: [PosSettingsPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PosService, useValue: service },
        { provide: AccountingService, useValue: { listAllAccounts: () => of([account('a1', '4100')]) } },
        {
          provide: ConfigurationService,
          useValue: { listPaymentModes: () => of([mode('m1', 'Cash', 'cash-account'), mode('m2', 'Voucher', null)]) },
        },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => organizationId } } } },
      ],
    });

    const fixture = TestBed.createComponent(PosSettingsPage);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const input = (id: string) => element.querySelector<HTMLInputElement>(`#${id}`)!;
    const button = (label: string) =>
      [...element.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.trim() === label)!;

    return {
      fixture,
      service,
      element,
      input,
      text: () => element.textContent ?? '',
      tick: (id: string) => {
        input(id).click();
        fixture.detectChanges();
      },
      press: (label: string) => {
        button(label).click();
        fixture.detectChanges();
      },
    };
  }

  it('shows the walk-in customer and offers only the POS types the tenant is entitled to', () => {
    const p = page();

    expect(p.text()).toContain('Cash Customer');
    const options = [...p.element.querySelectorAll<HTMLOptionElement>('#pos-settings-mode option')].map((o) => o.value);
    expect(options).toEqual(['None', 'Restaurant']);
  });

  it('sends every setting it shows, and service charge only when it is on', () => {
    const p = page();

    p.tick('pos-settings-service-charge');
    const rate = p.input('pos-settings-service-charge-rate');
    rate.value = '10';
    rate.dispatchEvent(new Event('input'));
    p.tick('pos-settings-round-off');
    p.tick('pos-settings-cash-verification');
    p.tick('pos-settings-print-kot');
    p.tick('pos-settings-abbreviated');
    p.press('Save Settings');

    expect(p.service.saved).toEqual({
      serviceChargeEnabled: true,
      serviceChargeRate: 10,
      serviceChargeAccountId: null,
      serviceChargeOnTakeAway: true,
      roundOffEnabled: true,
      roundOffAccountId: null,
      cashVerificationRequired: true,
      denominations: [1000, 500, 100, 50, 20, 10, 5, 2, 1],
      defaultTab: null,
      printEstimateBill: true,
      printInvoice: true,
      printCreditNote: true,
      printKot: false,
      abbreviatedTaxInvoiceEnabled: true,
    });
    expect(p.text()).toContain('Settings saved for HeadOffice.');
  });

  it('sends service charge on take-away as unticked at a restaurant, and shows it nowhere else', () => {
    const p = page();

    p.tick('pos-settings-service-charge');
    expect(p.input('pos-settings-service-charge-take-away').checked).toBe(true);
    p.tick('pos-settings-service-charge-take-away');
    p.press('Save Settings');
    expect(p.service.saved?.serviceChargeOnTakeAway).toBe(false);

    TestBed.resetTestingModule();
    const retail = page('Retail');
    retail.tick('pos-settings-service-charge');
    expect(retail.element.querySelector('#pos-settings-service-charge-take-away')).toBeNull();
  });

  it('keeps denominations largest first and refuses a duplicate', () => {
    const p = page();

    p.press('Reset to default');
    const add = p.input('pos-settings-new-denomination');
    add.value = '200';
    add.dispatchEvent(new Event('input'));
    p.press('Add');
    add.value = '100';
    add.dispatchEvent(new Event('input'));
    p.press('Add');
    expect(p.text()).toContain('100 is already listed.');

    p.press('Save Settings');
    expect(p.service.saved?.denominations).toEqual([1000, 500, 200, 100, 50, 20, 10, 5, 2, 1]);
  });

  it('hides the KOT toggle at a Retail till', () => {
    const p = page('Retail', { posRetailEnabled: true, posRestaurantEnabled: false });

    expect(p.element.querySelector('#pos-settings-print-kot')).toBeNull();
  });

  it('links only a payment mode that names its account', () => {
    const p = page();

    expect(p.input('pos-settings-mode-m2').disabled).toBe(true);
    expect(p.text()).toContain('no payment account');

    p.tick('pos-settings-mode-m1');
    p.press('Save Payment Modes');

    expect(p.service.linked).toEqual(['m1']);
  });
});
