import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';

import { Currency } from '../../core/organizations/organizations.models';
import { OrganizationsService } from '../../core/organizations/organizations.service';
import { CurrencyRateFields } from './currency-rate-fields';

/**
 * Phase 28. The behaviour confirmed live on the reference product's Invoice and Customer Payment
 * forms (2026-09-04) -- and the reason no document command needs a feature gate: with one currency
 * on the tenant's list, this control is "NPR, rate 1, read-only" all by itself.
 */
describe('CurrencyRateFields', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  @Component({
    imports: [CurrencyRateFields],
    template: `<app-currency-rate-fields
      [organizationId]="organizationId"
      [(currencyCode)]="currencyCode"
      [(exchangeRate)]="exchangeRate"
    />`,
  })
  class Host {
    organizationId = organizationId;
    currencyCode = signal('NPR');
    exchangeRate = signal(1);
  }

  function currency(code: string, name: string, isActive = true): Currency {
    return {
      id: `id-${code}`,
      organizationId,
      code,
      name,
      symbol: code,
      isActive,
      createdAt: '2026-09-01T00:00:00Z',
    };
  }

  function render(currencies: Observable<Currency[]>) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: OrganizationsService, useValue: { listCurrencies: () => currencies } },
      ],
    });

    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return fixture;
  }

  it('pins the rate to 1 and disables it while the base currency is selected', () => {
    const fixture = render(of([currency('NPR', 'Nepalese Rupee'), currency('USD', 'US Dollar')]));

    const rate: HTMLInputElement = fixture.nativeElement.querySelector('#exchangeRate');

    expect(rate.disabled).toBe(true);
    expect(rate.value).toBe('1');
  });

  it('enables the rate once a foreign currency is chosen', () => {
    const fixture = render(of([currency('NPR', 'Nepalese Rupee'), currency('USD', 'US Dollar')]));

    const select: HTMLSelectElement = fixture.nativeElement.querySelector('#currencyCode');
    select.value = 'USD';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const rate: HTMLInputElement = fixture.nativeElement.querySelector('#exchangeRate');
    expect(rate.disabled).toBe(false);
    expect(fixture.componentInstance.currencyCode()).toBe('USD');
  });

  it('resets the rate to 1 when the base currency is chosen again', () => {
    const fixture = render(of([currency('NPR', 'Nepalese Rupee'), currency('USD', 'US Dollar')]));

    const select: HTMLSelectElement = fixture.nativeElement.querySelector('#currencyCode');
    select.value = 'USD';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const rate: HTMLInputElement = fixture.nativeElement.querySelector('#exchangeRate');
    rate.value = '133';
    rate.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(fixture.componentInstance.exchangeRate()).toBe(133);

    select.value = 'NPR';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(fixture.componentInstance.exchangeRate()).toBe(1);
  });

  it('offers only active currencies', () => {
    const fixture = render(of([
      currency('NPR', 'Nepalese Rupee'),
      currency('USD', 'US Dollar'),
      currency('EUR', 'Euro', false),
    ]));

    const options: HTMLOptionElement[] = Array.from(fixture.nativeElement.querySelectorAll('#currencyCode option'));

    expect(options.map((x) => x.value)).toEqual(['NPR', 'USD']);
  });

  it('still shows a retired currency a document was already raised in', () => {
    // A document must keep displaying the currency it was issued in even after the tenant retires
    // it -- the same reasoning that makes a document store the code rather than a Currency id.
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: OrganizationsService,
          useValue: { listCurrencies: () => of([currency('NPR', 'Nepalese Rupee'), currency('EUR', 'Euro', false)]) },
        },
      ],
    });

    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.currencyCode.set('EUR');
    fixture.detectChanges();

    const options: HTMLOptionElement[] = Array.from(fixture.nativeElement.querySelectorAll('#currencyCode option'));
    expect(options.map((x) => x.value)).toContain('EUR');
  });

  it('renders a working base-currency form when the currency list cannot be read', () => {
    const fixture = render(throwError(() => new Error('403')));

    const rate: HTMLInputElement = fixture.nativeElement.querySelector('#exchangeRate');
    expect(rate.disabled).toBe(true);
    expect(fixture.componentInstance.currencyCode()).toBe('NPR');
  });
});
