import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { HistoryService } from './history.service';

/**
 * Phase 33. Every assertion here mirrors something the confirm-live pass observed in the reference
 * product's own `localStorage["history"]` — see `history.service.ts` for the observations.
 */
describe('HistoryService', () => {
  let service: HistoryService;

  beforeEach(() => {
    localStorage.removeItem('erp.history');
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    service = TestBed.inject(HistoryService);
  });

  afterEach(() => localStorage.removeItem('erp.history'));

  it('records a screen only once you have left it', () => {
    service.visit('/organizations/abc/sales/invoices');

    expect(service.entries()).toEqual([]);

    service.visit('/organizations/abc/products');

    expect(service.entries().map((x) => x.name)).toEqual(['Invoices']);
  });

  it('keeps one entry per area, most recent first', () => {
    // Two Configurations screens then two different areas: the second Configurations screen replaces
    // the first, while the other areas coexist. Observed exactly this way on the live tenant.
    service.visit('/organizations/abc/configuration/general');
    service.visit('/organizations/abc/configuration/credit-terms');
    service.visit('/organizations/abc/sales/invoices');
    service.visit('/organizations/abc/products');
    service.visit('/organizations/abc/reports/trial-balance');

    expect(service.entries().map((x) => `${x.area}/${x.name}`)).toEqual([
      'Products/Products',
      'Sales/Invoices',
      'Configuration/Credit Terms',
    ]);
  });

  it('survives a reload through localStorage', () => {
    service.visit('/organizations/abc/sales/invoices');
    service.visit('/organizations/abc/products');

    const reloaded = TestBed.inject(HistoryService);
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideRouter([])] });

    expect(TestBed.inject(HistoryService).entries().map((x) => x.name)).toEqual(['Invoices']);
    expect(reloaded).toBeTruthy();
  });

  it('ignores a stored value written in some other shape', () => {
    localStorage.setItem('erp.history', '{"not":"an array"}');
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideRouter([])] });

    expect(TestBed.inject(HistoryService).entries()).toEqual([]);
  });

  it('clears', () => {
    service.visit('/organizations/abc/sales/invoices');
    service.visit('/organizations/abc/products');
    service.clear();

    expect(service.entries()).toEqual([]);
    expect(localStorage.getItem('erp.history')).toBe('[]');
  });
});
