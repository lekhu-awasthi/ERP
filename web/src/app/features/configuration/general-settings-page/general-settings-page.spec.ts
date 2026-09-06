import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';

import { GeneralSettings } from '../../../core/organizations/organizations.models';
import { OrganizationsService } from '../../../core/organizations/organizations.service';
import { GeneralSettingsPage } from './general-settings-page';

/**
 * Phase 31 -- Configurations > General.
 *
 * <p>The assertions worth having are the ones that would have caught this phase's own failure mode:
 * that all five settings actually round-trip (four of them had been unreachable since phase 2, so
 * "the field exists" proves nothing), and that Inventory Tracking Mode -- deliberately not offered
 * on the form -- is sent back unchanged rather than reset to a default, which is exactly how a
 * write-only field silently loses its value.</p>
 */
describe('GeneralSettingsPage', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  class OrganizationsServiceStub {
    saved: GeneralSettings | null = null;

    constructor(private readonly current: GeneralSettings) {}

    getGeneralSettings(): Observable<GeneralSettings> {
      return of(this.current);
    }

    updateGeneralSettings(_organizationId: string, request: GeneralSettings): Observable<GeneralSettings> {
      this.saved = request;
      return of(request);
    }
  }

  function page(overrides: Partial<GeneralSettings> = {}) {
    const service = new OrganizationsServiceStub({
      suggestSellingPriceMode: 'RecentSellingPrice',
      productPriceBasis: 'ExclusiveOfVat',
      inventoryTrackingMode: 'AccountingMovement',
      negativeCashBalanceAction: 'Reject',
      negativeStockBalanceAction: 'Warn',
      creditLimitExceedsAction: 'Warn',
      ...overrides,
    });

    TestBed.configureTestingModule({
      imports: [GeneralSettingsPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: OrganizationsService, useValue: service },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => organizationId } } } },
      ],
    });

    const fixture = TestBed.createComponent(GeneralSettingsPage);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    return {
      fixture,
      service,
      element,
      text: () => element.textContent ?? '',
      radio: (id: string) => element.querySelector<HTMLInputElement>(`#${id}`)!,
      save: () => element.querySelector<HTMLButtonElement>('button.btn-primary')!.click(),
    };
  }

  afterEach(() => TestBed.resetTestingModule());

  it('renders all five settings the live page carries', () => {
    const { text } = page();

    expect(text()).toContain('Suggest Selling Price');
    expect(text()).toContain('Product Price Basis');
    expect(text()).toContain('Negative Cash Balance');
    expect(text()).toContain('Negative Item Balance');
    expect(text()).toContain('Credit Limit Exceeds');
  });

  it('checks the radio matching each loaded value', () => {
    const { radio } = page({ productPriceBasis: 'InclusiveOfVat', creditLimitExceedsAction: 'DoNothing' });

    expect(radio('basis-inclusive').checked).toBe(true);
    expect(radio('basis-exclusive').checked).toBe(false);
    expect(radio('credit-DoNothing').checked).toBe(true);
    expect(radio('cash-Reject').checked).toBe(true);
    expect(radio('stock-Warn').checked).toBe(true);
  });

  it('saves every changed setting', () => {
    const { radio, save, service, fixture } = page();

    radio('suggest-fixed').click();
    radio('basis-inclusive').click();
    radio('cash-DoNothing').click();
    radio('stock-Reject').click();
    radio('credit-Reject').click();
    fixture.detectChanges();

    save();

    expect(service.saved).toEqual({
      suggestSellingPriceMode: 'FixedSellingPrice',
      productPriceBasis: 'InclusiveOfVat',
      inventoryTrackingMode: 'AccountingMovement',
      negativeCashBalanceAction: 'DoNothing',
      negativeStockBalanceAction: 'Reject',
      creditLimitExceedsAction: 'Reject',
    });
  });

  /**
   * Inventory Tracking Mode has no control on this form (the reference product removed it from this
   * page), but it is on the command -- so a save must send back what was loaded. Dropping it would
   * silently reset a field the deferred Delivery Note / GRN work depends on.
   */
  it('sends the un-offered Inventory Tracking Mode back unchanged', () => {
    const { save, service } = page({ inventoryTrackingMode: 'PhysicalMovement' });

    save();

    expect(service.saved?.inventoryTrackingMode).toBe('PhysicalMovement');
  });
});
