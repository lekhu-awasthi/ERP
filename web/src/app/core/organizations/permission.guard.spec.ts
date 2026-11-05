import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, UrlTree, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, firstValueFrom } from 'rxjs';

import { environment } from '../../../environments/environment';
import { permissionGuard, permissionKeysOf } from './permission.guard';

const ORGANIZATION_ID = '22222222-2222-2222-2222-222222222222';

function routeSnapshot(): ActivatedRouteSnapshot {
  const parent = { paramMap: convertToParamMap({ id: ORGANIZATION_ID }), parent: null };
  return { paramMap: convertToParamMap({}), parent } as unknown as ActivatedRouteSnapshot;
}

describe('permissionGuard', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function run(...keys: string[]): Promise<boolean | UrlTree> {
    const result = TestBed.runInInjectionContext(() =>
      permissionGuard(...keys)(routeSnapshot(), {} as never),
    );
    return firstValueFrom(result as Observable<boolean | UrlTree>);
  }

  function expectGrantsRequest() {
    return httpMock.expectOne(`${environment.apiBaseUrl}/api/organizations/${ORGANIZATION_ID}/my-permissions`);
  }

  it('opens the route for a key held organization-wide', async () => {
    const outcome = run('Sales.Invoice.View');
    expectGrantsRequest().flush({ organizationWide: ['Sales.Invoice.View'], atSomeLocation: [] });

    expect(await outcome).toBe(true);
  });

  /** Phase 32b: a list lets through a key held at one branch and narrows itself to that branch. */
  it('opens the route for a key held only at some location', async () => {
    const outcome = run('Sales.Invoice.View');
    expectGrantsRequest().flush({ organizationWide: [], atSomeLocation: ['Sales.Invoice.View'] });

    expect(await outcome).toBe(true);
  });

  it('opens the route when any one of its keys is held', async () => {
    const outcome = run('Sales.Invoice.View', 'Sales.Invoice.Create');
    expectGrantsRequest().flush({ organizationWide: ['Sales.Invoice.Create'], atSomeLocation: [] });

    expect(await outcome).toBe(true);
  });

  it('redirects to Home when no key is held', async () => {
    const outcome = run('Reports.TrialBalance.View');
    expectGrantsRequest().flush({ organizationWide: ['Sales.Invoice.View'], atSomeLocation: [] });

    const result = await outcome;
    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe(`/organizations/${ORGANIZATION_ID}/home`);
  });

  /**
   * The opposite of featureGuard, on purpose: hiding a screen is usability, the server still refuses
   * what was not granted, and a role whose own grants cannot be read must not lose every screen.
   */
  it('fails open when the grants cannot be read', async () => {
    const outcome = run('Sales.Invoice.View');
    expectGrantsRequest().flush(null, { status: 403, statusText: 'Forbidden' });

    expect(await outcome).toBe(true);
  });

  it('reads the grants once per organization, not once per guarded route', async () => {
    const first = run('Sales.Invoice.View');
    expectGrantsRequest().flush({ organizationWide: ['Sales.Invoice.View'], atSomeLocation: [] });
    await first;

    // No second request: HttpTestingController.verify() in afterEach would fail on one.
    expect(await run('Sales.Invoice.View')).toBe(true);
  });

  it('declares its keys, which is what the nav reads', () => {
    expect(permissionKeysOf(permissionGuard('Contacts.Contact.View'))).toEqual(['Contacts.Contact.View']);
    expect(permissionKeysOf(() => true)).toBeNull();
  });
});
