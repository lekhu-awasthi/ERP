import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { OrganizationsService } from './organizations.service';

/**
 * Phase 32 (FR-2.3/FR-3.3) -- the client half of Billing Locations.
 *
 * These pin the two wire contracts a screen cannot check for itself:
 * <ol>
 *   <li>the list defaults to <b>active locations only</b>, so a document picker never offers a
 *       branch the tenant has closed (the server refuses one too, so the two agree);</li>
 *   <li>the Advanced panel's settings round-trip under the names the server binds, and the
 *       <c>locationBearingDocumentTypes</c> it returns is what a document form reads to decide
 *       whether to render a picker at all -- so no screen re-derives the scope rule.</li>
 * </ol>
 */
describe('OrganizationsService billing locations (Phase 32)', () => {
  const organizationId = '11111111-1111-1111-1111-111111111111';

  let service: OrganizationsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(OrganizationsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('asks for active locations only by default, so a picker cannot offer a closed branch', () => {
    service.listBillingLocations(organizationId).subscribe();

    const request = http.expectOne((r) => r.url.endsWith('/billing-locations'));
    expect(request.request.params.get('includeInactive')).toBe('false');
    request.flush([]);
  });

  it('asks for inactive locations only when the Show Inactive toggle is on', () => {
    service.listBillingLocations(organizationId, true).subscribe();

    const request = http.expectOne((r) => r.url.endsWith('/billing-locations'));
    expect(request.request.params.get('includeInactive')).toBe('true');
    request.flush([]);
  });

  it('reads the Advanced panel settings, including the document types that carry a location', () => {
    let received: string[] = [];
    service.getBillingLocationSettings(organizationId).subscribe((s) => {
      received = s.locationBearingDocumentTypes;
    });

    const request = http.expectOne((r) => r.url.endsWith('/billing-location-settings'));
    expect(request.request.method).toBe('GET');
    request.flush({
      locationScopeMode: 'SalesTransactionsOnly',
      locationWiseReportPermission: false,
      multipleLocationsEnabled: true,
      locationBearingDocumentTypes: ['CreditNote', 'Invoice', 'SalesOrder'],
    });

    // The default scope is the live one: the three sales-side types, and not PurchaseBill.
    expect(received).toContain('Invoice');
    expect(received).not.toContain('PurchaseBill');
  });

  it('writes the scope mode and the report-permission toggle together', () => {
    service
      .updateBillingLocationSettings(organizationId, {
        locationScopeMode: 'AllTransactions',
        locationWiseReportPermission: true,
      })
      .subscribe();

    const request = http.expectOne((r) => r.url.endsWith('/billing-location-settings'));
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      locationScopeMode: 'AllTransactions',
      locationWiseReportPermission: true,
    });
    request.flush({
      locationScopeMode: 'AllTransactions',
      locationWiseReportPermission: true,
      multipleLocationsEnabled: true,
      locationBearingDocumentTypes: ['Invoice', 'PurchaseBill'],
    });
  });

  it('creates a location without sending a location type -- the server assigns it', () => {
    service
      .createBillingLocation(organizationId, { code: 'BR1', name: 'Branch One', address: 'Pokhara' })
      .subscribe();

    const request = http.expectOne((r) => r.url.endsWith('/billing-locations') && r.method === 'POST');
    expect(request.request.body).not.toHaveProperty('locationType');
    request.flush({ id: 'x', code: 'BR1', name: 'Branch One' });
  });
});
